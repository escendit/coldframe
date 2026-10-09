using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Sites;

namespace Coldframe.Server.Notifications;

/// <summary>
/// How often a User is reminded of an open Alert, and when (Story 6.4): pure, and the only place that decides.
/// A Reminder is due one interval after the previous due-at, never after the time something was sent.
/// </summary>
public static class ReminderIntervalRule
{
    /// <summary>
    /// The shortest interval between Reminders of a Health Alert, whatever the cadence.
    /// </summary>
    public static readonly TimeSpan HealthFloor = TimeSpan.FromHours(24);

    /// <summary>
    /// The interval of <paramref name="cadence"/>: 24 h for daily, 48 h for every 2 days.
    /// </summary>
    public static TimeSpan Of(ReminderCadence cadence) => cadence switch
    {
        ReminderCadence.Daily => TimeSpan.FromHours(24),
        ReminderCadence.Every2Days => TimeSpan.FromHours(48),
        _ => throw new ArgumentOutOfRangeException(nameof(cadence), cadence, "Unknown Reminder cadence."),
    };

    /// <summary>
    /// The cadence a User is reminded at: their own, else the Site's as their grain holds it, else daily.
    /// </summary>
    /// <param name="own">The User's own cadence for the Site, if set.</param>
    /// <param name="site">The Site's cadence as the User grain last heard it, if ever.</param>
    public static ReminderCadence Resolve(ReminderCadence? own, ReminderCadence? site) =>
        own ?? site ?? ReminderCadence.Daily;

    /// <summary>
    /// Whether <paramref name="kind"/> is a Health Alert: every kind that is not a Threshold Alert. None exists
    /// before Epic 7.
    /// </summary>
    public static bool IsHealth(AlertKind kind) => kind != AlertKind.Threshold;

    /// <summary>
    /// The interval between Reminders of an Alert of <paramref name="kind"/>: <paramref name="resolved"/> for a
    /// Threshold Alert, and <c>max(resolved, 24 h)</c> for a Health Alert.
    /// </summary>
    /// <param name="kind">What the Alert is about.</param>
    /// <param name="resolved">The interval of the resolved cadence.</param>
    public static TimeSpan Interval(AlertKind kind, TimeSpan resolved)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(resolved, TimeSpan.Zero);

        return IsHealth(kind) && resolved < HealthFloor ? HealthFloor : resolved;
    }

    /// <summary>
    /// The interval between Reminders of an Alert of <paramref name="kind"/> for a User with these cadences.
    /// </summary>
    /// <param name="kind">What the Alert is about.</param>
    /// <param name="own">The User's own cadence for the Site, if set.</param>
    /// <param name="site">The Site's cadence as the User grain last heard it, if ever.</param>
    public static TimeSpan Interval(AlertKind kind, ReminderCadence? own, ReminderCadence? site) =>
        Interval(kind, Of(Resolve(own, site)));

    /// <summary>
    /// The first <c>origin + n × interval</c> (n at least 1) after <paramref name="now"/>: when the first Reminder
    /// of an Alert is due that the User grain learned of without being told.
    /// </summary>
    /// <param name="origin">When the Alert opened.</param>
    /// <param name="interval">The interval between Reminders.</param>
    /// <param name="now">The current time.</param>
    public static DateTimeOffset FirstAfter(DateTimeOffset origin, TimeSpan interval, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);

        return now < origin
            ? origin + interval
            : origin + (interval * (((now - origin).Ticks / interval.Ticks) + 1));
    }

    /// <summary>
    /// The last <c>dueAt + n × interval</c> (n at least 0) not after <paramref name="now"/>: overdue Reminders
    /// collapse into this one, and the next is due one interval later, after <paramref name="now"/>.
    /// A <paramref name="dueAt"/> that is still ahead is returned as it is.
    /// </summary>
    /// <param name="dueAt">The due-at of the oldest Reminder that is overdue.</param>
    /// <param name="interval">The interval between Reminders.</param>
    /// <param name="now">The current time.</param>
    public static DateTimeOffset LastAtOrBefore(DateTimeOffset dueAt, TimeSpan interval, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);

        return now < dueAt
            ? dueAt
            : dueAt + (interval * ((now - dueAt).Ticks / interval.Ticks));
    }
}
