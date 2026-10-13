using System.Text.Json;

namespace PayBot.Webhooks;

public sealed record CandidateMessage(
    string MessageId,
    string RemoteJid,
    bool FromMe,
    string? Participant,
    string? Text);

public sealed class ParsedWebhook
{
    public string? ApiKey { get; init; }
    public bool AcceptEvent { get; init; }
    public IReadOnlyList<CandidateMessage> Messages { get; init; } = [];
}

public static class BotEcho
{
    public const char Marker = '\u200B';

    private static readonly string[] Prefixes =
    [
        "Gasto registrado:",
        "Entrada registrada:",
        "Resumo de ",
        "Resumo total",
        "Resumo\n",
        "Como enviar o resumo",
        "Nao consegui enviar o PDF",
        "Registrar gasto",
        "1 - Registrar gasto",
        "Escolha o cartao",
        "Informe o valor",
        "Informe a descricao",
        "Informe o nome do cartao",
        "Informe o periodo",
        "A partir de qual mes",
        "Mes invalido",
        "Nao consegui registrar",
        "Numero de parcelas",
        "Valor invalido",
        "Numero de parcelas invalido",
        "Periodo invalido",
        "Nome do cartao invalido",
        "Descricao invalida",
        "Cartao credito",
        "Cartao debito",
        "Remover",
        "Selecione o id",
        "Registro removido:",
        "Nenhum registro",
        "Id invalido",
        "Registro nao encontrado"
    ];

    public static bool IsEcho(string text)
    {
        if (text.Contains(Marker))
            return true;

        var trimmed = text.Trim();
        return Prefixes.Any(prefix => trimmed.StartsWith(prefix, StringComparison.Ordinal));
    }
}

public static class InboundMessageParser
{
    public static bool ApiKeyMatches(string? bodyKey, string? headerKey, string expected)
    {
        if (string.IsNullOrEmpty(expected))
            return false;

        return string.Equals(bodyKey, expected, StringComparison.Ordinal)
            || string.Equals(headerKey, expected, StringComparison.Ordinal);
    }

    public static bool IsAllowedSender(string remoteJid, string groupJid)
    {
        if (string.IsNullOrWhiteSpace(groupJid) || string.IsNullOrWhiteSpace(remoteJid))
            return false;

        return string.Equals(remoteJid.Trim(), groupJid.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static ParsedWebhook Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var accept = IsUpsert(ReadString(root, "event"));
        var messages = new List<CandidateMessage>();
        if (accept && TryProp(root, "data", out var data))
            Collect(data, messages);

        return new ParsedWebhook
        {
            ApiKey = ReadString(root, "apikey"),
            AcceptEvent = accept,
            Messages = messages
        };
    }

    private static void Collect(JsonElement data, List<CandidateMessage> messages)
    {
        if (data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
                AddMessage(item, messages);
            return;
        }

        if (data.ValueKind != JsonValueKind.Object)
            return;

        if (TryProp(data, "messages", out var nested) && nested.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in nested.EnumerateArray())
                AddMessage(item, messages);
            return;
        }

        AddMessage(data, messages);
    }

    private static void AddMessage(JsonElement item, List<CandidateMessage> messages)
    {
        if (item.ValueKind != JsonValueKind.Object || !TryProp(item, "key", out var key))
            return;

        var id = ReadString(key, "id");
        if (string.IsNullOrWhiteSpace(id))
            return;

        string? text = null;
        if (TryProp(item, "message", out var message))
            text = ReadText(Unwrap(message));

        messages.Add(new CandidateMessage(
            id,
            GroupChatJid(key),
            ReadBool(key, "fromMe"),
            ReadString(key, "participant"),
            text));
    }

    private static string GroupChatJid(JsonElement key)
    {
        var remote = ReadString(key, "remoteJid") ?? "";
        var alternate = ReadString(key, "remoteJidAlt");
        if (remote.EndsWith("@g.us", StringComparison.OrdinalIgnoreCase))
            return remote;
        if (alternate is not null && alternate.EndsWith("@g.us", StringComparison.OrdinalIgnoreCase))
            return alternate;
        return remote;
    }

    private static JsonElement Unwrap(JsonElement message)
    {
        string[] wrappers = ["ephemeralMessage", "viewOnceMessage", "viewOnceMessageV2", "documentWithCaptionMessage", "editedMessage"];
        foreach (var name in wrappers)
        {
            if (TryProp(message, name, out var wrapper)
                && wrapper.ValueKind == JsonValueKind.Object
                && TryProp(wrapper, "message", out var inner)
                && inner.ValueKind == JsonValueKind.Object)
                return Unwrap(inner);
        }

        return message;
    }

    private static string? ReadText(JsonElement message)
    {
        var conversation = ReadString(message, "conversation");
        if (!string.IsNullOrWhiteSpace(conversation))
            return conversation;

        if (TryProp(message, "extendedTextMessage", out var extended))
        {
            var text = ReadString(extended, "text");
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        return null;
    }

    private static bool IsUpsert(string? eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            return true;

        var normalized = eventName.Replace("_", ".", StringComparison.Ordinal).Replace("-", ".", StringComparison.Ordinal);
        return normalized.Contains("messages.upsert", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!TryProp(element, name, out var value))
            return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static bool ReadBool(JsonElement element, string name)
    {
        if (!TryProp(element, name, out var value))
            return false;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => string.Equals(value.GetString(), "true", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static bool TryProp(JsonElement element, string name, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object)
            return false;
        if (element.TryGetProperty(name, out value))
            return true;

        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                continue;
            value = property.Value;
            return true;
        }

        return false;
    }
}
