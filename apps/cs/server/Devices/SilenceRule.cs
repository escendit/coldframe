using Coldframe.Contracts.Devices;

namespace Coldframe.Server.Devices;

/// <summary>
/// The Silence Window of a Device (Story 7.1, AD-6): its defaults, its bounds and when it expires. Pure, and the
/// only place that does this arithmetic; the Device grain supplies the journaled clock start and the time.
/// </summary>
public static class SilenceRule
{
    /// <summary>
    /// The Silence Window of a Node without an override: a Node reports every 15 minutes or so.
    /// </summary>
    public static readonly TimeSpan NodeWindow = TimeSpan.FromHours(6);

    /// <summary>
    /// The Silence Window of a Hub without an override: a Hub heartbeats every 30 to 60 seconds.
    /// </summary>
    public static readonly TimeSpan HubWindow = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The shortest window a Device can be given: the period of the reminder that wakes the grain.
    /// </summary>
    public static readonly TimeSpan MinWindow = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The longest window a Device can be given.
    /// </summary>
    public static readonly TimeSpan MaxWindow = TimeSpan.FromDays(30);

    /// <summary>
    /// How often the Device grain is woken to look at its expiry. Only a wake-up: the expiry is journaled state.
    /// </summary>
    public static readonly TimeSpan WakePeriod = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The Silence Window of a Device of <paramref name="kind"/> that has no override.
    /// </summary>
    public static TimeSpan DefaultWindow(DeviceKind kind) => kind switch
    {
        DeviceKind.Hub => HubWindow,
        DeviceKind.Node => NodeWindow,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown Device kind."),
    };

    /// <summary>
    /// Whether <paramref name="window"/> can be a Device's override: 1 min to 30 days, in whole seconds.
    /// </summary>
    public static bool IsValid(TimeSpan window) =>
        window >= MinWindow && window <= MaxWindow && window.Ticks % TimeSpan.TicksPerSecond == 0;

    /// <summary>
    /// The Silence Window in force: the Device's override, else the default of its kind.
    /// </summary>
    /// <param name="kind">Hub or Node.</param>
    /// <param name="overridden">The Device's own window, if it has one.</param>
    public static TimeSpan Window(DeviceKind kind, TimeSpan? overridden) => overridden ?? DefaultWindow(kind);

    /// <summary>
    /// When the window expires: the clock start plus the window.
    /// </summary>
    /// <param name="clockStart">Where the window counts from.</param>
    /// <param name="window">The Silence Window in force.</param>
    public static DateTimeOffset DueAt(DateTimeOffset clockStart, TimeSpan window) => clockStart + window;

    /// <summary>
    /// Whether the window has expired: the Device is silent from <paramref name="dueAt"/> on, not before.
    /// </summary>
    /// <param name="dueAt">When the window expires.</param>
    /// <param name="now">The current time.</param>
    public static bool IsOverdue(DateTimeOffset dueAt, DateTimeOffset now) => now >= dueAt;

    /// <summary>
    /// The later of the clock start a Device holds and a time something restarted it at: the clock never runs
    /// backwards, so an old report or an old Server start changes nothing.
    /// </summary>
    /// <param name="clockStart">The clock start so far, or <see langword="null"/> before the first.</param>
    /// <param name="candidate">A time the clock may restart at.</param>
    public static DateTimeOffset Latest(DateTimeOffset? clockStart, DateTimeOffset candidate) =>
        clockStart is { } current && current > candidate ? current : candidate;
}
