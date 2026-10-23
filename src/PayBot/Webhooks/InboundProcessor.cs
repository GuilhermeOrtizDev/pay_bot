using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using PayBot.Ai;
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
    OpenAiClient ai,
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
        var groupJid = WhatsAppGroup.NormalizeJid(message.RemoteJid);
        var tenantId = groupJid.Length == 0
            ? null
            : await db.Groups.AsNoTracking()
                .Where(group => group.Jid == groupJid)
                .Select(group => group.TenantId)
                .FirstOrDefaultAsync(cancellationToken);
        if (tenantId is not Guid tenant)
        {
            logger.LogInformation("Mensagem ignorada. Chat {RemoteJid} sem tenant.", message.RemoteJid);
            return;
        }

        if (string.IsNullOrWhiteSpace(message.Text) && !message.IsAudio)
            return;
        if (BotEcho.HasMarker(message.Text))
            return;

        if (await db.ProcessedMessages.AnyAsync(item => item.MessageId == message.MessageId, cancellationToken))
            return;

        var session = await db.Sessions.SingleOrDefaultAsync(item => item.GroupJid == groupJid, cancellationToken);
        var aiActive = settings.AiAvailable && session is not null && AiEngine.IsAiStep(session.Step);
        if (message.IsAudio && !aiActive)
            return;
        // In AI mode free text like "Remover ..." or "Cartao credito ..." must not be mistaken for a bot echo.
        if (!aiActive && BotEcho.IsEcho(message.Text!))
            return;

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
            .Where(card => card.TenantId == tenant)
            .OrderByDescending(card => card.LastUsedAt)
            .Select(card => new KnownCard(card.Id, card.Name, card.Kind))
            .ToListAsync(cancellationToken);

        var adminActive = !aiActive
            && session.Step != MenuStep.Paused
            && (AdminEngine.IsAdminStep(session.Step) || AdminEngine.IsCommand(message.Text));
        MenuTurn turn;
        if (aiActive)
            turn = await AiTurnAsync(message, session.Step, DeserializeDraft(session.DraftJson), cards, settings, cancellationToken);
        else if (adminActive)
            turn = await AdminTurnAsync(message.Text, session.Step, DeserializeDraft(session.DraftJson), settings, cancellationToken);
        else
            turn = MenuEngine.Handle(
                message.Text,
                session.Step,
                DeserializeDraft(session.DraftJson),
                cards,
                SaoPauloClock.Today(),
                settings.AiAvailable);
        var aiTurn = AiEngine.IsAiStep(turn.NextStep);

        var now = DateTimeOffset.UtcNow;
        if (turn.Expense is not null)
            await AddExpenseAsync(tenant, turn.Expense, message.MessageId, now, cancellationToken);
        if (turn.Income is not null)
            AddIncome(tenant, turn.Income, message.MessageId, now);
        if (turn.Admin is not null)
            await ApplyAdminAsync(turn.Admin, now, cancellationToken);

        var reply = turn.Reply;
        var nextStep = turn.NextStep;
        var draft = turn.Draft;
        var previousStep = session.Step;
        if (turn.Summary is not null)
        {
            var report = await summaries.BuildAsync(tenant, turn.Summary, cancellationToken);
            if (turn.AsPdf)
            {
                var sent = await evolution.SendDocumentAsync(
                    groupJid,
                    SummaryPdf.Render(report),
                    "resumo.pdf",
                    report.Title,
                    cancellationToken);
                if (sent)
                    reply = aiTurn ? "" : MenuTexts.Summary;
                else if (aiTurn)
                    reply = MenuTexts.PdfFailed + "\n\n" + SummaryFormatter.Format(report);
                else
                {
                    reply = MenuTexts.PdfFailed + "\n\n" + MenuTexts.SummaryOutput;
                    nextStep = MenuStep.AwaitingSummaryOutput;
                    draft = new SessionDraft
                    {
                        SummaryFrom = turn.Summary.From,
                        SummaryTo = turn.Summary.To,
                        SummaryTitle = turn.Summary.Title,
                        SummaryMethod = turn.Summary.Method,
                        SummaryCardId = turn.Summary.CardId
                    };
                }
            }
            else
                reply = aiTurn
                    ? SummaryFormatter.Format(report)
                    : SummaryFormatter.Format(report) + "\n\n" + MenuTexts.Summary;
        }

        if (turn.DeleteList is not null)
        {
            var entries = await LoadDeleteCandidatesAsync(tenant, turn.DeleteList, cancellationToken);
            if (entries.Count == 0)
            {
                reply = aiTurn ? MenuTexts.NoRecords : MenuTexts.NoRecords + "\n\n" + MenuTexts.Remove;
                nextStep = aiTurn ? MenuStep.AiMode : MenuStep.RemoveMenu;
                draft = new SessionDraft();
            }
            else
            {
                reply = aiTurn
                    ? DeleteListFormatter.FormatChoices(entries) + "\n\n" + MenuTexts.AiDeleteChoice
                    : DeleteListFormatter.Format(entries);
                draft.DeleteIds = entries.Select(entry => entry.Id).ToList();
            }
        }

        if (turn.DeleteId is Guid deleteId)
        {
            var entry = await db.Transactions
                .Include(item => item.Card)
                .FirstOrDefaultAsync(item => item.Id == deleteId && item.TenantId == tenant, cancellationToken);
            var menu = aiTurn ? "" : "\n\n" + MenuTexts.Remove;
            if (entry is null)
                reply = MenuTexts.RecordNotFound + menu;
            else
            {
                reply = $"Registro removido: {DeleteListFormatter.Line(entry)}{menu}";
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
        catch (Exception exception)
        {
            logger.LogError(exception, "Falha ao gravar a mensagem {MessageId}.", message.MessageId);
            db.ChangeTracker.Clear();
            await evolution.SendAsync(groupJid, FailureReply(previousStep), cancellationToken);
            return;
        }

        if (turn.Expense is not null || turn.Income is not null)
            reply = await AppendMonthBalanceAsync(tenant, reply, turn, now, cancellationToken);

        if (!string.IsNullOrWhiteSpace(reply))
            await evolution.SendAsync(groupJid, reply, cancellationToken);

        logger.LogInformation("Mensagem {MessageId} levou o menu para {Step}.", message.MessageId, turn.NextStep);
    }

    private async Task<MenuTurn> AiTurnAsync(
        CandidateMessage message,
        MenuStep step,
        SessionDraft draft,
        IReadOnlyList<KnownCard> cards,
        PayBotOptions settings,
        CancellationToken cancellationToken)
    {
        var text = message.Text;
        if (message.IsAudio)
        {
            if (!settings.TranscribeAvailable)
                return AiEngine.Stay(step, draft, MenuTexts.AiTranscribeOff);

            text = await TranscribeAsync(message, cancellationToken);
            if (string.IsNullOrWhiteSpace(text))
                return AiEngine.Stay(step, draft, MenuTexts.AiAudioFailed);
            logger.LogInformation("Audio {MessageId} transcrito.", message.MessageId);
        }

        var direct = AiEngine.Direct(text, step, draft, cards);
        if (direct is not null)
            return direct;

        var intent = await ai.InterpretAsync(text!, new AiContext(SaoPauloClock.Today(), cards, draft, step), cancellationToken);
        if (intent is null)
            return AiEngine.Stay(step, draft, MenuTexts.AiFailed);

        return AiEngine.Interpret(intent, step, draft, cards);
    }

    private async Task<MenuTurn> AdminTurnAsync(
        string? text,
        MenuStep step,
        SessionDraft draft,
        PayBotOptions settings,
        CancellationToken cancellationToken)
    {
        var tenants = await db.Tenants.AsNoTracking()
            .OrderBy(tenant => tenant.Name)
            .Select(tenant => new AdminTenant(tenant.Id, tenant.Name))
            .ToListAsync(cancellationToken);
        var groups = await db.Groups.AsNoTracking()
            .OrderBy(group => group.CreatedAt)
            .Select(group => new AdminGroup(group.Id, group.Jid, group.Tenant != null ? group.Tenant.Name : null))
            .ToListAsync(cancellationToken);

        return AdminEngine.Handle(text, step, draft, tenants, groups, settings.AiAvailable);
    }

    private async Task ApplyAdminAsync(AdminChange change, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(change.NewTenantName))
        {
            db.Tenants.Add(new Tenant
            {
                Id = Guid.NewGuid(),
                Name = change.NewTenantName.Trim(),
                NormalizedName = Normalize(change.NewTenantName),
                CreatedAt = now
            });
        }

        if (!string.IsNullOrWhiteSpace(change.NewGroupJid))
        {
            db.Groups.Add(new WhatsAppGroup
            {
                Id = Guid.NewGuid(),
                Jid = WhatsAppGroup.NormalizeJid(change.NewGroupJid),
                CreatedAt = now
            });
        }

        if (change.LinkGroupId is Guid groupId && change.LinkTenantId is Guid tenantId)
        {
            var group = await db.Groups.FirstOrDefaultAsync(item => item.Id == groupId, cancellationToken);
            if (group is not null)
                group.TenantId = tenantId;
        }
    }

    private async Task<string?> TranscribeAsync(CandidateMessage message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message.RawJson))
            return null;

        var media = await evolution.GetMediaBase64Async(message.RawJson, cancellationToken);
        if (media is not { } audio || audio.Content.Length == 0)
            return null;

        return await ai.TranscribeAsync(audio.Content, audio.MimeType, cancellationToken);
    }

    private static string FailureReply(MenuStep step) => step switch
    {
        MenuStep.AwaitingChargeMonth or MenuStep.AwaitingIncomeMonth => MenuTexts.SaveFailed + "\n\n" + MenuTexts.AskChargeMonth,
        MenuStep.AwaitingDateRange => MenuTexts.SaveFailed + "\n\n" + MenuTexts.AskMonthPeriod,
        MenuStep.AwaitingDeleteDateRange => MenuTexts.SaveFailed + "\n\n" + MenuTexts.AskPeriod,
        MenuStep.AwaitingAmount or MenuStep.AwaitingIncomeAmount => MenuTexts.SaveFailed + "\n\n" + MenuTexts.AskAmount,
        MenuStep.AwaitingExpenseDescription or MenuStep.AwaitingIncomeDescription => MenuTexts.SaveFailed + "\n\n" + MenuTexts.AskDescription,
        MenuStep.AwaitingInstallments => MenuTexts.SaveFailed + "\n\n" + MenuTexts.AskInstallments,
        MenuStep.AwaitingInstallmentAmountKind => MenuTexts.SaveFailed + "\n\n" + MenuTexts.InstallmentAmountKindMenu,
        MenuStep.AwaitingNewCardName => MenuTexts.SaveFailed + "\n\n" + MenuTexts.AskCardName,
        MenuStep.AdminAwaitingTenantName => MenuTexts.SaveFailed + "\n\n" + MenuTexts.AskTenantName,
        MenuStep.AdminAwaitingGroupJid => MenuTexts.SaveFailed + "\n\n" + MenuTexts.AskGroupJid,
        _ => MenuTexts.SaveFailed
    };

    private async Task AddExpenseAsync(Guid tenantId, PendingExpense expense, string messageId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Guid? cardId = expense.CardId;
        if (expense.Method is PaymentMethod.CreditCard or PaymentMethod.DebitCard)
        {
            var kind = expense.Method == PaymentMethod.CreditCard ? CardKind.Credit : CardKind.Debit;
            if (expense.CreateCard && !string.IsNullOrWhiteSpace(expense.CardName))
            {
                var normalized = Normalize(expense.CardName);
                var card = await db.Cards.FirstOrDefaultAsync(
                    item => item.TenantId == tenantId && item.Kind == kind && item.NormalizedName == normalized,
                    cancellationToken);
                if (card is null)
                {
                    card = new Card
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
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
                var card = await db.Cards.FirstOrDefaultAsync(
                    item => item.Id == existingId && item.TenantId == tenantId,
                    cancellationToken);
                if (card is not null)
                    card.LastUsedAt = now;
                else
                    cardId = null;
            }
        }

        var local = SaoPauloClock.LocalDate(now);
        var start = expense.ChargeMonth ?? new DateOnly(local.Year, local.Month, 1);
        if (expense.Installments is int count
            && expense.AmountKind is InstallmentAmountKind amountKind
            && InstallmentSchedule.TryBuild(expense.Amount, count, amountKind, start, out var months))
        {
            for (var index = 0; index < months.Count; index++)
            {
                var month = months[index];
                db.Transactions.Add(Entry(
                    tenantId,
                    expense,
                    month.Amount,
                    month.Date,
                    SaoPauloClock.AtNoon(month.Date),
                    count,
                    cardId,
                    InstallmentSchedule.EntryMessageId(messageId, index + 1),
                    now));
            }

            return;
        }

        var chargeDate = expense.ChargeMonth ?? local;
        db.Transactions.Add(Entry(
            tenantId,
            expense,
            expense.Amount,
            chargeDate,
            expense.ChargeMonth is null ? now : SaoPauloClock.AtNoon(chargeDate),
            expense.Installments,
            cardId,
            messageId,
            now));
    }

    private static LedgerEntry Entry(
        Guid tenantId,
        PendingExpense expense,
        decimal amount,
        DateOnly date,
        DateTimeOffset occurredAt,
        int? installments,
        Guid? cardId,
        string messageId,
        DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        CreatedAt = now,
        OccurredAt = occurredAt,
        LocalDate = date,
        Direction = Direction.Expense,
        Amount = amount,
        PaymentMethod = expense.Method,
        Description = expense.Description,
        Installments = installments,
        IsFixed = expense.IsFixed,
        CardId = cardId,
        WhatsAppMessageId = messageId
    };

    private async Task<List<LedgerEntry>> LoadDeleteCandidatesAsync(Guid tenantId, DeleteListRequest request, CancellationToken cancellationToken)
    {
        var query = db.Transactions.Include(entry => entry.Card).Where(entry => entry.TenantId == tenantId);
        if (request.From is DateOnly from && request.To is DateOnly to)
            query = query.Where(entry => entry.LocalDate >= from && entry.LocalDate <= to);

        var entries = await query.ToListAsync(cancellationToken);
        return OrderDeleteCandidates(entries, request);
    }

    internal static List<LedgerEntry> OrderDeleteCandidates(IEnumerable<LedgerEntry> entries, DeleteListRequest request)
    {
        if (request.From is DateOnly from && request.To is DateOnly to)
        {
            return entries
                .Where(entry => entry.LocalDate >= from && entry.LocalDate <= to)
                .OrderByDescending(entry => entry.LocalDate)
                .ThenByDescending(entry => entry.OccurredAt)
                .Take(request.Take)
                .ToList();
        }

        return entries
            .OrderByDescending(entry => entry.CreatedAt)
            .ThenByDescending(entry => entry.Id)
            .Take(request.Take)
            .ToList();
    }

    private async Task<string> AppendMonthBalanceAsync(Guid tenantId, string reply, MenuTurn turn, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var month = turn.Expense?.ChargeMonth ?? turn.Income?.ChargeMonth ?? SaoPauloClock.LocalDate(now);
        try
        {
            var balance = await summaries.MonthBalanceAsync(tenantId, month, cancellationToken);
            return MenuTexts.WithMonthBalance(reply, month, balance);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Falha ao calcular o saldo de {Month}.", month);
            return reply;
        }
    }

    private void AddIncome(Guid tenantId, PendingIncome income, string messageId, DateTimeOffset now)
    {
        db.Transactions.Add(new LedgerEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CreatedAt = now,
            OccurredAt = SaoPauloClock.AtNoon(income.ChargeMonth),
            LocalDate = income.ChargeMonth,
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
