namespace PayBot.Time;

public static class SaoPauloClock
{
    private static readonly TimeZoneInfo Zone = Resolve();

    public static DateOnly Today() => LocalDate(DateTimeOffset.UtcNow);

    public static DateOnly LocalDate(DateTimeOffset utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, Zone).DateTime);

    private static TimeZoneInfo Resolve()
    {
        if (TimeZoneInfo.TryFindSystemTimeZoneById("America/Sao_Paulo", out var zone))
            return zone;
        return TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
    }
}
