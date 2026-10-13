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
        Assert.False(InboundMessageParser.IsAllowedSender(message.RemoteJid, Group));
    }

    [Fact]
    public void Accepts_anyone_in_the_group_including_lid_addressing()
    {
        var member = Payload("conversation", "1", fromMe: false, participant: "208271755505899@lid");
        var message = Assert.Single(InboundMessageParser.Parse(member).Messages);
        Assert.True(InboundMessageParser.IsAllowedSender(message.RemoteJid, Group));

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
        Assert.False(BotEcho.IsEcho("1"));
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
