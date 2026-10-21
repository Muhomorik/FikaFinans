namespace FikaFinans.Application.Schedules;

/// <summary>
/// The next occurrence of a time of day — what an in-process daily timer re-arms to after
/// each firing. Pure, so the clock and the zone come in as arguments.
/// </summary>
public static class DailyTime
{
    /// <summary>
    /// The first moment strictly after <paramref name="now"/> at which the wall clock in
    /// <paramref name="zone"/> reads <paramref name="at"/>.
    /// </summary>
    /// <remarks>
    /// Strictly after, so a timer re-armed the instant it fires waits for tomorrow. Worked in
    /// the zone's local time, so a daylight-saving change keeps the wall-clock time rather
    /// than drifting an hour.
    /// </remarks>
    public static DateTimeOffset NextAfter(DateTimeOffset now, TimeOnly at, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var localNow = TimeZoneInfo.ConvertTime(now, zone);
        var date = DateOnly.FromDateTime(localNow.DateTime);

        var candidate = AtLocal(date, at, zone);
        return candidate > now ? candidate : AtLocal(date.AddDays(1), at, zone);
    }

    private static DateTimeOffset AtLocal(DateOnly date, TimeOnly at, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(at, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
}
