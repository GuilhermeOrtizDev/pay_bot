using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using PayBot.Configuration;
using PayBot.Data;
using PayBot.Domain;
using PayBot.Evolution;
using PayBot.Menu;
using PayBot.Summaries;
using PayBot.Time;

namespace PayBot.Webhooks;

public sealed class InboundProcessor(
    PayBotDbContext db,
    EvolutionClient evolution,
    SummaryService summaries,
    InboundGate gate,
    IOptions<PayBotOptions> options,
    ILogger<InboundProcessor> logger)
{
    private static readonly JsonSerializerOptions DraftOptions = new(JsonSerializerDefaults.Web);

    public async Task<WebhookResult> HandleAsync(string body, string? headerApiKey, CancellationToken cancellationToken)
    {
        ParsedWebhook parsed;
        try
        {
            parsed = InboundMessageParser.Parse(body);
        }
        catch (JsonException)
        {
            logger.LogWarning("Webhook da Evolution com JSON invalido.");
            return WebhookResult.Ok;
        }

        var settings = options.Value;
        if (!InboundMessageParser.ApiKeyMatches(parsed.ApiKey, headerApiKey, settings.EvolutionApiKey))
        {
            // Evolution puts the instance token in the body, not the global API key, and sends no header.
            logger.LogInformation("Webhook aceito sem a chave global da Evolution.");
        }

        if (!parsed.AcceptEvent || parsed.Messages.Count == 0)
            return WebhookResult.Ok;

        await gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var message in parsed.Messages)
                await ProcessAsync(message, settings, cancellationToken);
        }
        finally
        {
            gate.Release();
        }

        return WebhookResult.Ok;
    }

    private async Task ProcessAsync(CandidateMessage message, PayBotOptions settings, CancellationToken cancellationToken)
    {
        if (!InboundMessageParser.IsAllowedSender(message.RemoteJid, settings.GroupJid))
        {
            logger.LogInformation("Mensagem ignorada. Chat {RemoteJid}.", message.RemoteJid);
            return;
        }

        if (string.IsNullOrWhiteSpace(message.Text) || BotEcho.IsEcho(message.Text))
            return;

        if (await db.ProcessedMessages.AnyAsync(item => item.MessageId == message.MessageId, cancellationToken))
            return;

        var groupJid = settings.GroupJid.Trim();
        var session = await db.Sessions.SingleOrDefaultAsync(item => item.GroupJid == groupJid, cancellationToken);
        if (session is null)
        {
            session = new ConversationSession
            {
                Id = Guid.NewGuid(),
                GroupJid = groupJid,
                Step = MenuStep.Main,
                DraftJson = "{}",
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.Sessions.Add(session);
        }

        var cards = await db.Cards
            .OrderByDescending(card => card.LastUsedAt)
            .Select(card => new KnownCard(card.Id, card.Name, card.Kind))
            .ToListAsync(cancellationToken);

        var turn = MenuEngine.Handle(
            message.Text,
            session.Step,
            DeserializeDraft(session.DraftJson),
            cards,
            SaoPauloClock.Today());

        var now = DateTimeOffset.UtcNow;
        if (turn.Expense is not null)
            await AddExpenseAsync(turn.Expense, message.MessageId, now, cancellationToken);
        if (turn.Income is not null)
            AddIncome(turn.Income, message.MessageId, now);

        var reply = turn.Reply;
        var nextStep = turn.NextStep;
        var draft = turn.Draft;
        if (turn.Summary is not null)
        {
            var report = await summaries.BuildAsync(turn.Summary, cancellationToken);
            reply = SummaryFormatter.Format(report) + "\n\n" + MenuTexts.Summary;
        }

        if (turn.DeleteList is not null)
        {
            var entries = await LoadDeleteCandidatesAsync(turn.DeleteList, cancellationToken);
            if (entries.Count == 0)
            {
                reply = MenuTexts.NoRecords + "\n\n" + MenuTexts.Remove;
                nextStep = MenuStep.RemoveMenu;
                draft = new SessionDraft();
            }
            else
            {
                reply = DeleteListFormatter.Format(entries);
                draft.DeleteIds = entries.Select(entry => entry.Id).ToList();
            }
        }

        if (turn.DeleteId is Guid deleteId)
        {
            var entry = await db.Transactions
                .Include(item => item.Card)
                .FirstOrDefaultAsync(item => item.Id == deleteId, cancellationToken);
            if (entry is null)
                reply = MenuTexts.RecordNotFound + "\n\n" + MenuTexts.Remove;
            else
            {
                reply = $"Registro removido: {DeleteListFormatter.Line(entry)}\n\n{MenuTexts.Remove}";
                db.Transactions.Remove(entry);
            }
        }

        session.Step = nextStep;
        session.DraftJson = JsonSerializer.Serialize(draft, DraftOptions);
        session.UpdatedAt = now;
        db.ProcessedMessages.Add(new ProcessedMessage
        {
            MessageId = message.MessageId,
            ProcessedAt = now
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicate(exception))
        {
            logger.LogInformation("Mensagem {MessageId} ja processada.", message.MessageId);
            db.ChangeTracker.Clear();
            return;
        }

        if (!string.IsNullOrWhiteSpace(reply))
            await evolution.SendAsync(reply, cancellationToken);

        logger.LogInformation("Mensagem {MessageId} levou o menu para {Step}.", message.MessageId, turn.NextStep);
    }

    private async Task AddExpenseAsync(PendingExpense expense, string messageId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Guid? cardId = expense.CardId;
        if (expense.Method is PaymentMethod.CreditCard or PaymentMethod.DebitCard)
        {
            var kind = expense.Method == PaymentMethod.CreditCard ? CardKind.Credit : CardKind.Debit;
            if (expense.CreateCard && !string.IsNullOrWhiteSpace(expense.CardName))
            {
                var normalized = Normalize(expense.CardName);
                var card = await db.Cards.FirstOrDefaultAsync(
                    item => item.Kind == kind && item.NormalizedName == normalized,
                    cancellationToken);
                if (card is null)
                {
                    card = new Card
                    {
                        Id = Guid.NewGuid(),
                        Name = expense.CardName.Trim(),
                        NormalizedName = normalized,
                        Kind = kind,
                        CreatedAt = now,
                        LastUsedAt = now
                    };
                    db.Cards.Add(card);
                }
                else
                    card.LastUsedAt = now;

                cardId = card.Id;
            }
            else if (cardId is Guid existingId)
            {
                var card = await db.Cards.FirstOrDefaultAsync(item => item.Id == existingId, cancellationToken);
                if (card is not null)
                    card.LastUsedAt = now;
            }
        }

        db.Transactions.Add(new LedgerEntry
        {
            Id = Guid.NewGuid(),
            OccurredAt = now,
            LocalDate = SaoPauloClock.LocalDate(now),
            Direction = Direction.Expense,
            Amount = expense.Amount,
            PaymentMethod = expense.Method,
            Description = expense.Description,
            Installments = expense.Installments,
            IsFixed = expense.IsFixed,
            CardId = cardId,
            WhatsAppMessageId = messageId
        });
    }

    private async Task<List<LedgerEntry>> LoadDeleteCandidatesAsync(DeleteListRequest request, CancellationToken cancellationToken)
    {
        var query = db.Transactions.Include(entry => entry.Card).AsQueryable();
        if (request.From is DateOnly from && request.To is DateOnly to)
            query = query.Where(entry => entry.LocalDate >= from && entry.LocalDate <= to);

        return await query
            .OrderByDescending(entry => entry.LocalDate)
            .ThenByDescending(entry => entry.OccurredAt)
            .Take(request.Take)
            .ToListAsync(cancellationToken);
    }

    private void AddIncome(PendingIncome income, string messageId, DateTimeOffset now)
    {
        db.Transactions.Add(new LedgerEntry
        {
            Id = Guid.NewGuid(),
            OccurredAt = now,
            LocalDate = SaoPauloClock.LocalDate(now),
            Direction = Direction.Income,
            Amount = income.Amount,
            Description = income.Description,
            WhatsAppMessageId = messageId
        });
    }

    private static SessionDraft DeserializeDraft(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new SessionDraft();

        try
        {
            return JsonSerializer.Deserialize<SessionDraft>(json, DraftOptions) ?? new SessionDraft();
        }
        catch (JsonException)
        {
            return new SessionDraft();
        }
    }

    private static string Normalize(string name) => name.Trim().ToLowerInvariant();

    private static bool IsDuplicate(DbUpdateException exception)
    {
        if (exception.InnerException is not PostgresException postgres
            || postgres.SqlState != PostgresErrorCodes.UniqueViolation)
            return false;

        var constraint = postgres.ConstraintName ?? "";
        return constraint.Contains("WhatsAppMessageId", StringComparison.OrdinalIgnoreCase)
            || constraint.Contains("processed_messages", StringComparison.OrdinalIgnoreCase);
    }
}
