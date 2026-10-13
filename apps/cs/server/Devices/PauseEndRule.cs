using System.Globalization;
using Coldframe.Server.Notifications;

namespace Coldframe.Server.Devices;

/// <summary>
/// When a Pause with an end date ends (Story 8.1, AD-6): pure, and the only place that decides. An end date is a
/// calendar date. It is 00:00 on that date in the IANA time zone of the User who set the Pause, UTC when the User
/// has none or the Server's tz database does not know it (<see cref="NotificationWindowRule.ZoneOf"/>). A midnight
/// the clocks skip is the first instant of that date that exists; a midnight that occurs twice is its first
/// occurrence. The result is the UTC due-at the owning grain persists.
/// </summary>
public static class PauseEndRule
{
    /// <summary>
    /// How an end date is written in the contract.
    /// </summary>
    public const string DateFormat = "yyyy-MM-dd";

    /// <summary>
    /// Reads an end date as the contract writes it: exactly <see cref="DateFormat"/>, a date that exists.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="date">The date, when it is one.</param>
    public static bool TryParseDate(string? text, out DateOnly date) =>
        DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    /// <summary>
    /// The first instant (UTC) of <paramref name="date"/> in the User's zone.
    /// </summary>
    /// <param name="date">The end date.</param>
    /// <param name="timeZone">The User's IANA time zone ID, if any.</param>
    /// <exception cref="ArgumentOutOfRangeException">No instant holds that midnight.</exception>
    public static DateTimeOffset Resolve(DateOnly date, string? timeZone)
    {
        var zone = NotificationWindowRule.ZoneOf(timeZone);
        var start = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        // The clocks go forward over midnight: the date begins at the first wall-clock minute that exists.
        while (zone.IsInvalidTime(start))
        {
            start = start.AddMinutes(1);
        }

        // The larger offset is the earlier instant: the first occurrence of a midnight that occurs twice.
        var offset = zone.IsAmbiguousTime(start) ? zone.GetAmbiguousTimeOffsets(start).Max() : zone.GetUtcOffset(start);

        return new DateTimeOffset(start, offset).ToUniversalTime();
    }

    /// <summary>
    /// Resolves <paramref name="date"/> like <see cref="Resolve"/> and accepts it only when it is after
    /// <paramref name="now"/>: a Pause never ends in the past, nor at the instant it is set.
    /// </summary>
    /// <param name="date">The end date.</param>
    /// <param name="timeZone">The User's IANA time zone ID, if any.</param>
    /// <param name="now">The clock of whoever asks.</param>
    /// <param name="dueAt">The UTC due-at, when the date is accepted.</param>
    public static bool TryResolve(DateOnly date, string? timeZone, DateTimeOffset now, out DateTimeOffset dueAt)
    {
        try
        {
            dueAt = Resolve(date, timeZone);
        }
        catch (ArgumentOutOfRangeException)
        {
            // The first or the last date there is, in a zone whose midnight falls outside what an instant holds.
            dueAt = default;
            return false;
        }

        return dueAt > now;
    }
}
