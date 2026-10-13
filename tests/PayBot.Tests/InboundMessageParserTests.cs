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
    public void Ignores_other_events_other_groups_and_other_people()
    {
        var update = """
            {"event":"messages.update","apikey":"key","data":{"key":{"id":"1","remoteJid":"120363000000000000@g.us","fromMe":true},"message":{"conversation":"1"}}}
            """;
        Assert.False(InboundMessageParser.Parse(update).AcceptEvent);

        var otherGroup = Payload("conversation", "1", fromMe: true, remoteJid: "999@g.us");
        var message = Assert.Single(InboundMessageParser.Parse(otherGroup).Messages);
        Assert.False(InboundMessageParser.IsAllowedSender(message.RemoteJid, message.FromMe, message.Participant, Group, "5511999999999"));

        var otherPerson = Payload("conversation", "1", fromMe: false, participant: "5511888888888@s.whatsapp.net");
        var stranger = Assert.Single(InboundMessageParser.Parse(otherPerson).Messages);
        Assert.False(InboundMessageParser.IsAllowedSender(stranger.RemoteJid, stranger.FromMe, stranger.Participant, Group, "5511999999999"));
    }

    [Fact]
    public void Accepts_the_owner_and_rejects_bot_echo()
    {
        var owner = Payload("conversation", "1", fromMe: false, participant: "5511999999999:12@s.whatsapp.net");
        var message = Assert.Single(InboundMessageParser.Parse(owner).Messages);
        Assert.True(InboundMessageParser.IsAllowedSender(message.RemoteJid, message.FromMe, message.Participant, Group, "5511999999999"));
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

    private static string Payload(string kind, string text, bool fromMe, string? remoteJid = null, string? participant = null)
    {
        var body = kind == "extended"
            ? "{\"extendedTextMessage\":{\"text\":\"" + text + "\"}}"
            : "{\"conversation\":\"" + text + "\"}";
        var who = participant is null ? "" : ",\"participant\":\"" + participant + "\"";
        var jid = remoteJid ?? Group;
        var mine = fromMe ? "true" : "false";
        return "{\"event\":\"messages.upsert\",\"apikey\":\"secret\",\"data\":{\"key\":{\"id\":\"abc\",\"remoteJid\":\""
            + jid + "\",\"fromMe\":" + mine + who + "},\"message\":" + body + "}}";
    }
}
