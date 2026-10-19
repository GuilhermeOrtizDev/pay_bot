using PayBot.Webhooks;

namespace PayBot.Tests;

public class InboundMessageParserTests
{
    private const string Group = "120363000000000000@g.us";

    [Fact]
    public void Reads_group_text_and_extended_text()
    {
        var conversation = Payload("conversation", "1", fromMe: true);
        var extended = Payload("extended", "2", fromMe: true);

        var first = InboundMessageParser.Parse(conversation);
        var second = InboundMessageParser.Parse(extended);

        Assert.Equal("1", Assert.Single(first.Messages).Text);
        Assert.Equal("2", Assert.Single(second.Messages).Text);
        Assert.True(first.AcceptEvent);
    }

    [Fact]
    public void Ignores_other_events_and_other_groups()
    {
        var update = """
            {"event":"messages.update","apikey":"key","data":{"key":{"id":"1","remoteJid":"120363000000000000@g.us","fromMe":true},"message":{"conversation":"1"}}}
            """;
        Assert.False(InboundMessageParser.Parse(update).AcceptEvent);

        var otherGroup = Payload("conversation", "1", fromMe: true, remoteJid: "999@g.us");
        var message = Assert.Single(InboundMessageParser.Parse(otherGroup).Messages);
        Assert.Equal("999@g.us", message.RemoteJid);
    }

    [Fact]
    public void Accepts_anyone_in_the_group_including_lid_addressing()
    {
        var member = Payload("conversation", "1", fromMe: false, participant: "208271755505899@lid");
        var message = Assert.Single(InboundMessageParser.Parse(member).Messages);
        Assert.Equal(Group, message.RemoteJid);

        var lid = Payload(
            "conversation",
            "1",
            fromMe: false,
            remoteJid: "208271755505899@lid",
            remoteJidAlt: Group);
        var addressed = Assert.Single(InboundMessageParser.Parse(lid).Messages);
        Assert.Equal(Group, addressed.RemoteJid);
        Assert.True(BotEcho.IsEcho("\u200B1 - Registrar gasto"));
        Assert.True(BotEcho.IsEcho("Gasto registrado: Pix R$ 10,00"));
        Assert.True(BotEcho.IsEcho("Registrar em texto\nEnvie o texto do lancamento."));
        Assert.True(BotEcho.IsEcho("Exemplo\ngasto\ntipo: pix"));
        Assert.True(BotEcho.IsEcho("Exemplos\n0 - Voltar Menu Inicial"));
        Assert.True(BotEcho.IsEcho("Faltou ou esta invalido:\n- mes (ex: 10/2026)"));
        Assert.False(BotEcho.IsEcho("1"));
        Assert.False(BotEcho.IsEcho("gasto\ntipo: pix\nvalor: 32,50\ndescricao: mercado"));
    }

    [Fact]
    public void Flags_voice_notes_as_audio_and_keeps_the_raw_message()
    {
        var plain = """
            {"event":"messages.upsert","data":{"key":{"id":"a1","remoteJid":"120363000000000000@g.us","fromMe":true},"message":{"audioMessage":{"mimetype":"audio/ogg; codecs=opus","ptt":true}}}}
            """;
        var ephemeral = """
            {"event":"messages.upsert","data":{"key":{"id":"a2","remoteJid":"120363000000000000@g.us","fromMe":true},"message":{"ephemeralMessage":{"message":{"audioMessage":{"ptt":true}}}}}}
            """;

        var first = Assert.Single(InboundMessageParser.Parse(plain).Messages);
        var second = Assert.Single(InboundMessageParser.Parse(ephemeral).Messages);

        Assert.True(first.IsAudio);
        Assert.Null(first.Text);
        Assert.Contains("audioMessage", first.RawJson);
        Assert.Contains("\"a1\"", first.RawJson);
        Assert.True(second.IsAudio);

        var text = Assert.Single(InboundMessageParser.Parse(Payload("conversation", "1", fromMe: true)).Messages);
        Assert.False(text.IsAudio);
        Assert.Null(text.RawJson);
    }

    [Fact]
    public void Api_key_must_match_body_or_header()
    {
        Assert.True(InboundMessageParser.ApiKeyMatches("secret", null, "secret"));
        Assert.True(InboundMessageParser.ApiKeyMatches(null, "secret", "secret"));
        Assert.False(InboundMessageParser.ApiKeyMatches("nope", "nope", "secret"));
        Assert.False(InboundMessageParser.ApiKeyMatches("secret", "secret", ""));
    }

    private static string Payload(
        string kind,
        string text,
        bool fromMe,
        string? remoteJid = null,
        string? participant = null,
        string? remoteJidAlt = null)
    {
        var body = kind == "extended"
            ? "{\"extendedTextMessage\":{\"text\":\"" + text + "\"}}"
            : "{\"conversation\":\"" + text + "\"}";
        var who = participant is null ? "" : ",\"participant\":\"" + participant + "\"";
        var alt = remoteJidAlt is null ? "" : ",\"remoteJidAlt\":\"" + remoteJidAlt + "\"";
        var jid = remoteJid ?? Group;
        var mine = fromMe ? "true" : "false";
        return "{\"event\":\"messages.upsert\",\"apikey\":\"secret\",\"data\":{\"key\":{\"id\":\"abc\",\"remoteJid\":\""
            + jid + "\",\"fromMe\":" + mine + who + alt + "},\"message\":" + body + "}}";
    }
}
