namespace Coldframe.Server.Devices;

/// <summary>
/// What one device report says about a Node's battery (Story 7.3).
/// </summary>
public enum BatteryReport
{
    /// <summary>
    /// Neither low nor recovered: the battery could not be read while the Node was not charging, or the charger
    /// status is unknown below the threshold or with an unreadable battery. It ends either streak and starts none.
    /// </summary>
    Neither = 0,

    /// <summary>
    /// The battery is known and below <see cref="BatteryRule.LowBelowPercent"/> while the Node is not charging.
    /// </summary>
    Low = 1,

    /// <summary>
    /// The Node is charging, or its battery is known and at <see cref="BatteryRule.LowBelowPercent"/> or above.
    /// </summary>
    Recovered = 2,
}

/// <summary>
/// The battery streaks of a Node (Story 7.3).
/// </summary>
/// <param name="Open">Whether the Node's battery Alert is open.</param>
/// <param name="Low">The consecutive low reports so far; counted only while no Alert is open.</param>
/// <param name="Recovered">The consecutive recovered reports so far; counted only while an Alert is open.</param>
public readonly record struct BatteryStreaks(bool Open, int Low, int Recovered);

/// <summary>
/// What one counted device report does to the streaks.
/// </summary>
/// <param name="Next">The streaks after the report.</param>
/// <param name="Opens">Whether the report is the one that opens the Alert.</param>
/// <param name="Closes">Whether the report is the one that closes the Alert, as recovered.</param>
public readonly record struct BatteryStep(BatteryStreaks Next, bool Opens, bool Closes);

/// <summary>
/// The low-battery rule (Story 7.3), pure and the only place that decides when a Node's battery Alert opens or
/// closes: it opens with the third consecutive low report and closes with the third consecutive recovered one. A
/// report that is not low ends a low streak; a report that is not recovered ends a recovery streak.
/// </summary>
/// <remarks>
/// Only the streak that can change something is counted: low reports while no Alert is open, recovered reports
/// while one is. A healthy Node's report and a low report of a Node whose Alert is open therefore leave the
/// streaks as they are, and the Device grain journals nothing for them.
/// </remarks>
public static class BatteryRule
{
    /// <summary>
    /// A battery is low below this percentage; at it, it is not.
    /// </summary>
    public const int LowBelowPercent = 20;

    /// <summary>
    /// How many consecutive reports open or close the Alert.
    /// </summary>
    public const int Required = 3;

    /// <summary>
    /// The charger status token of a Node that is charging, as <c>device_reports.charging</c> stores it.
    /// </summary>
    public const string Charging = "charging";

    /// <summary>
    /// The charger status token of a Node that is not charging.
    /// </summary>
    public const string NotCharging = "not_charging";

    /// <summary>
    /// Classifies a device report.
    /// </summary>
    /// <param name="batteryPercent">The battery level, or <see langword="null"/> when it was not read.</param>
    /// <param name="charging">The charger status token: <c>charging</c>, <c>not_charging</c> or <c>unknown</c>.</param>
    public static BatteryReport Classify(int? batteryPercent, string? charging)
    {
        if (string.Equals(charging, Charging, StringComparison.Ordinal) || batteryPercent >= LowBelowPercent)
        {
            return BatteryReport.Recovered;
        }

        return batteryPercent is not null && string.Equals(charging, NotCharging, StringComparison.Ordinal)
            ? BatteryReport.Low
            : BatteryReport.Neither;
    }

    /// <summary>
    /// Applies one counted device report to the streaks.
    /// </summary>
    /// <param name="current">The streaks so far.</param>
    /// <param name="report">What the report says.</param>
    public static BatteryStep Advance(BatteryStreaks current, BatteryReport report)
    {
        if (current.Open)
        {
            if (report != BatteryReport.Recovered)
            {
                return new BatteryStep(new BatteryStreaks(true, 0, 0), false, false);
            }

            return current.Recovered + 1 >= Required
                ? new BatteryStep(new BatteryStreaks(false, 0, 0), false, true)
                : new BatteryStep(new BatteryStreaks(true, 0, current.Recovered + 1), false, false);
        }

        if (report != BatteryReport.Low)
        {
            return new BatteryStep(new BatteryStreaks(false, 0, 0), false, false);
        }

        return current.Low + 1 >= Required
            ? new BatteryStep(new BatteryStreaks(true, 0, 0), true, false)
            : new BatteryStep(new BatteryStreaks(false, current.Low + 1, 0), false, false);
    }
}
