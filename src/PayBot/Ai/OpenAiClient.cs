using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using PayBot.Configuration;
using PayBot.Domain;
using PayBot.Menu;

namespace PayBot.Ai;

public sealed class OpenAiClient(
    HttpClient http,
    IOptions<PayBotOptions> options,
    ILogger<OpenAiClient> logger)
{
    private static readonly string[] FieldNames = ["tipo", "cobranca", "parcelas", "valor", "valor_tipo", "cartao", "descricao", "mes"];

    private const string Schema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["acao", "campos", "de", "ate", "formato", "resposta"],
          "properties": {
            "acao": { "type": "string", "enum": ["gasto", "entrada", "resumo", "remover", "outro"] },
            "campos": {
              "type": "object",
              "additionalProperties": false,
              "required": ["tipo", "cobranca", "parcelas", "valor", "valor_tipo", "cartao", "descricao", "mes"],
              "properties": {
                "tipo": { "type": ["string", "null"] },
                "cobranca": { "type": ["string", "null"] },
                "parcelas": { "type": ["integer", "null"] },
                "valor": { "type": ["string", "null"] },
                "valor_tipo": { "type": ["string", "null"] },
                "cartao": { "type": ["string", "null"] },
                "descricao": { "type": ["string", "null"] },
                "mes": { "type": ["string", "null"] }
              }
            },
            "de": { "type": ["string", "null"] },
            "ate": { "type": ["string", "null"] },
            "formato": { "type": "string", "enum": ["texto", "pdf"] },
            "resposta": { "type": ["string", "null"] }
          }
        }
        """;

    private const string Instructions = """
        Voce interpreta mensagens de um bot de financas pessoais no WhatsApp, em portugues do Brasil, e responde so com o JSON do schema.

        acao:
        - gasto: registrar uma despesa.
        - entrada: registrar um recebimento (salario, pix recebido, reembolso, venda).
        - resumo: consultar resumo, saldo, extrato ou quanto gastou.
        - remover: apagar, excluir ou desfazer um registro ja salvo.
        - outro: nada disso, mensagem ambigua, ou mais de um lancamento na mesma mensagem. Em resposta, escreva uma frase curta pedindo o que falta ou pedindo um lancamento por vez.

        campos (so para gasto e entrada; nos outros casos, todos null):
        - Preencha apenas o que a mensagem ou o rascunho informam. Nunca invente valor, cartao, parcelas ou descricao. Use null para o que nao souber.
        - tipo (so gasto): exatamente um de: pix, cartao credito, cartao debito, retirada, despesa fixa, outros. "credito" = cartao credito; "debito" = cartao debito; saque ou dinheiro retirado do caixa = retirada; conta mensal sem cartao (aluguel, condominio, internet) = despesa fixa; dinheiro, boleto ou outra forma = outros. Em entrada, tipo e null.
        - cobranca (so cartao credito ou cartao debito): exatamente um de: parcelas, a vista, fixo. Assinatura ou cobranca mensal recorrente no cartao = fixo. Cartao debito nunca e parcelas. Em outros tipos, null.
        - parcelas: numero inteiro, so quando cobranca = parcelas.
        - valor: o valor que o usuario disse, no formato 1234,56. Nao converta valor de parcela em total nem o contrario.
        - valor_tipo: exatamente um de: parcela, total. So quando cobranca = parcelas; nos outros casos, null. "10x de 120" ou "parcela de 120" = parcela. "1200 em 10x" ou valor total = total. Nao divida nem multiplique o valor. Se nao der para saber, null.
        - cartao: nome do cartao. Se corresponder a um cartao da lista, use o nome exato da lista.
        - descricao: curta, em minusculas, ex: mercado, uber, salario, notebook.
        - mes: MM/yyyy do mes de cobranca. Use so em cartao credito, despesa fixa, outros e entrada. Se o usuario nao disser o mes, use o mes de hoje; "proximo mes" = mes seguinte. Em pix, retirada e cartao debito, null.

        Rascunho: se houver um rascunho, a mensagem provavelmente completa ou corrige esse lancamento. Mantenha a mesma acao e devolva os campos novos ou corrigidos. Se a mensagem claramente pede outra coisa (resumo, remover, outro lancamento), siga a mensagem.

        de e ate (resumo e remover), no formato yyyy-MM-dd:
        - resumo: "do mes" ou sem periodo = primeiro ao ultimo dia do mes de hoje; "proximo mes" = mes seguinte inteiro; "do ano" = 01/01 a 31/12 do ano de hoje; "total" ou "geral" = de e ate null; um mes ou intervalo citado = do primeiro dia ao ultimo dia citado.
        - remover: o periodo onde procurar o registro ("ontem" = de e ate iguais a ontem; "esse mes" = mes de hoje). Sem periodo, de e ate null.
        - Outras acoes: null.

        formato: pdf se o usuario pedir pdf ou arquivo; caso contrario, texto.
        resposta: so para acao outro; nos outros casos, null.
        """;

    private readonly PayBotOptions _options = options.Value;

    public async Task<AiIntent?> InterpretAsync(string text, AiContext context, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["model"] = _options.OpenAiModel,
            ["temperature"] = 0,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = Instructions },
                new JsonObject { ["role"] = "user", ["content"] = UserContent(text, context) }
            },
            ["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject
                {
                    ["name"] = "paybot_intent",
                    ["strict"] = true,
                    ["schema"] = JsonNode.Parse(Schema)
                }
            }
        };

        try
        {
            using var request = Request("/v1/chat/completions");
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("OpenAI recusou a interpretacao com status {Status}.", (int)response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var content = JsonNode.Parse(json)?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(content) ? null : ParseIntent(content);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            logger.LogWarning(exception, "Falha ao interpretar a mensagem na OpenAI.");
            return null;
        }
    }

    public async Task<string?> TranscribeAsync(byte[] audio, string? mimeType, CancellationToken cancellationToken)
    {
        var contentType = string.IsNullOrWhiteSpace(mimeType) ? "audio/ogg" : mimeType.Split(';')[0].Trim();
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(audio);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", "audio" + Extension(contentType));
        form.Add(new StringContent(_options.OpenAiTranscribeModel), "model");
        form.Add(new StringContent("pt"), "language");

        try
        {
            using var request = Request("/v1/audio/transcriptions");
            request.Content = form;
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("OpenAI recusou a transcricao com status {Status}.", (int)response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var text = JsonNode.Parse(json)?["text"]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            logger.LogWarning(exception, "Falha ao transcrever o audio na OpenAI.");
            return null;
        }
    }

    public static AiIntent? ParseIntent(string content)
    {
        var root = JsonNode.Parse(content);
        if (root is null)
            return null;

        var action = Read(root, "acao") switch
        {
            "gasto" => AiAction.Expense,
            "entrada" => AiAction.Income,
            "resumo" => AiAction.Summary,
            "remover" => AiAction.Remove,
            _ => AiAction.Other
        };

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root["campos"] is JsonObject campos)
        {
            foreach (var name in FieldNames)
            {
                var value = campos[name] switch
                {
                    JsonValue node when node.TryGetValue<string>(out var s) => s,
                    JsonValue node when node.TryGetValue<int>(out var i) => i.ToString(CultureInfo.InvariantCulture),
                    _ => null
                };
                if (!string.IsNullOrWhiteSpace(value))
                    fields[name] = value.Trim();
            }
        }

        return new AiIntent(
            action,
            fields,
            ReadDate(root, "de"),
            ReadDate(root, "ate"),
            Read(root, "formato") == "pdf",
            Read(root, "resposta"));
    }

    private static string UserContent(string text, AiContext context)
    {
        var cards = new JsonArray();
        foreach (var card in context.Cards)
        {
            cards.Add(new JsonObject
            {
                ["nome"] = card.Name,
                ["tipo"] = card.Kind == CardKind.Credit ? "cartao credito" : "cartao debito"
            });
        }

        JsonNode? draft = null;
        if (AiEngine.HasPendingEntry(context.Step, context.Draft))
        {
            var fields = new JsonObject();
            foreach (var (key, value) in AiEngine.DraftFields(context.Draft))
                fields[key] = value;
            draft = new JsonObject
            {
                ["acao"] = context.Draft.IsBulkIncome ? "entrada" : "gasto",
                ["campos"] = fields
            };
        }

        var today = context.Today;
        var payload = new JsonObject
        {
            ["hoje"] = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["dia_da_semana"] = today.ToString("dddd", MenuTexts.PtBr),
            ["cartoes"] = cards,
            ["rascunho"] = draft,
            ["mensagem"] = text
        };
        return payload.ToJsonString();
    }

    private HttpRequestMessage Request(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _options.OpenAiBaseUrl.TrimEnd('/') + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.OpenAiApiKey);
        return request;
    }

    private static string? Read(JsonNode root, string name) =>
        root[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static DateOnly? ReadDate(JsonNode root, string name) =>
        DateOnly.TryParseExact(Read(root, name), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    private static string Extension(string contentType) => contentType switch
    {
        "audio/mpeg" or "audio/mp3" => ".mp3",
        "audio/mp4" or "audio/m4a" or "audio/x-m4a" => ".m4a",
        "audio/wav" or "audio/x-wav" => ".wav",
        "audio/webm" => ".webm",
        _ => ".ogg"
    };
}
