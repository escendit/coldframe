using Coldframe.Contracts.Sites;

namespace Coldframe.Server.Notifications;

/// <summary>
/// When a Notification Window is open (Story 6.4): pure, and the only place that decides. A window is wall-clock
/// time in the User's time zone, so its instants move with daylight saving. A start that does not exist on a
/// spring-forward day opens at the first valid instant after it; a start that occurs twice opens at its first
/// occurrence.
/// </summary>
public static class NotificationWindowRule
{
    // An opening is at most a day and a daylight-saving change away from any instant.
    private const int DaysAhead = 3;

    /// <summary>
    /// The zone a window is evaluated in: <paramref name="timeZone"/> when the Server's tz database knows it,
    /// else UTC. A User without a chosen or detected zone therefore has a window in UTC.
    /// </summary>
    /// <param name="timeZone">The User's IANA time zone ID, if any.</param>
    public static TimeZoneInfo ZoneOf(string? timeZone) =>
        !string.IsNullOrEmpty(timeZone) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var zone)
            ? zone
            : TimeZoneInfo.Utc;

    /// <summary>
    /// Whether <paramref name="instant"/> lies inside <paramref name="window"/> in <paramref name="zone"/>: at or
    /// after its start and before its end.
    /// </summary>
    public static bool Contains(NotificationWindow window, TimeZoneInfo zone, DateTimeOffset instant)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(zone);

        var time = TimeZoneInfo.ConvertTime(instant, zone).TimeOfDay;

        return time >= TimeSpan.FromMinutes(window.FromMinutes) && time < TimeSpan.FromMinutes(window.ToMinutes);
    }

    /// <summary>
    /// The first instant (UTC) at or after <paramref name="instant"/> at which <paramref name="window"/> opens in
    /// <paramref name="zone"/>.
    /// </summary>
    public static DateTimeOffset NextOpening(NotificationWindow window, TimeZoneInfo zone, DateTimeOffset instant)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(zone);

        var date = TimeZoneInfo.ConvertTime(instant, zone).Date;

        for (var day = 0; day <= DaysAhead; day++)
        {
            foreach (var opening in OpeningsOn(window, zone, date.AddDays(day)))
            {
                if (opening >= instant)
                {
                    return opening;
                }
            }
        }

        throw new InvalidOperationException($"The window does not open within {DaysAhead} days of {instant:O} in {zone.Id}.");
    }

    /// <summary>
    /// The last instant (UTC) at or before <paramref name="instant"/> at which <paramref name="window"/> opened
    /// in <paramref name="zone"/>.
    /// </summary>
    public static DateTimeOffset LastOpening(NotificationWindow window, TimeZoneInfo zone, DateTimeOffset instant)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(zone);

        var date = TimeZoneInfo.ConvertTime(instant, zone).Date;

        for (var day = 0; day <= DaysAhead; day++)
        {
            foreach (var opening in OpeningsOn(window, zone, date.AddDays(-day)).Reverse())
            {
                if (opening <= instant)
                {
                    return opening;
                }
            }
        }

        throw new InvalidOperationException($"The window did not open within {DaysAhead} days before {instant:O} in {zone.Id}.");
    }

    // The instants at which the window opens on one local date, earliest first: one, or two when the start occurs
    // twice because the clocks go back over it.
    private static IEnumerable<DateTimeOffset> OpeningsOn(NotificationWindow window, TimeZoneInfo zone, DateTime date)
    {
        var start = DateTime.SpecifyKind(date.AddMinutes(window.FromMinutes), DateTimeKind.Unspecified);

        if (zone.IsInvalidTime(start))
        {
            // The clocks go forward over the start: the window opens at the first wall-clock minute that exists.
            do
            {
                start = start.AddMinutes(1);
            }
            while (zone.IsInvalidTime(start));

            yield return new DateTimeOffset(start, zone.GetUtcOffset(start)).ToUniversalTime();
        }
        else if (zone.IsAmbiguousTime(start))
        {
            // The larger offset is the earlier instant: the first occurrence.
            foreach (var offset in zone.GetAmbiguousTimeOffsets(start).OrderDescending())
            {
                yield return new DateTimeOffset(start, offset).ToUniversalTime();
            }
        }
        else
        {
            yield return new DateTimeOffset(start, zone.GetUtcOffset(start)).ToUniversalTime();
        }
    }
}
