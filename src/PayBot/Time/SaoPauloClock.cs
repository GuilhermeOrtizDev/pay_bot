namespace PayBot.Time;

public static class SaoPauloClock
{
    private static readonly TimeZoneInfo Zone = Resolve();

    public static DateOnly Today() => LocalDate(DateTimeOffset.UtcNow);

    public static DateOnly LocalDate(DateTimeOffset utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, Zone).DateTime);

    public static DateTimeOffset AtNoon(DateOnly date)
    {
        var local = date.ToDateTime(new TimeOnly(12, 0));
        return new DateTimeOffset(local, Zone.GetUtcOffset(local));
    }

    private static TimeZoneInfo Resolve()
    {
        if (TimeZoneInfo.TryFindSystemTimeZoneById("America/Sao_Paulo", out var zone))
            return zone;
        return TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
    }
}
