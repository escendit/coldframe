using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Sensors;

namespace Coldframe.Server.Sensors;

/// <summary>
/// The evaluation streak of a Sensor (Story 6.1).
/// </summary>
/// <param name="Open">The side of the Sensor's open Alert, or <see langword="null"/> when it has none.</param>
/// <param name="Position">Where the Readings of the running streak are; <see cref="ThresholdPosition.Within"/> when there is none.</param>
/// <param name="Count">How many consecutive Readings the streak has; 0 when there is none.</param>
public readonly record struct StreakState(ThresholdSide? Open, ThresholdPosition Position, int Count);

/// <summary>
/// What one evaluated Reading does to a streak.
/// </summary>
/// <param name="Next">The streak after the Reading.</param>
/// <param name="Closes">Whether the open Alert closes, as recovered.</param>
/// <param name="Opens">The side an Alert opens on, or <see langword="null"/>. With <paramref name="Closes"/>, the next episode follows the closed one.</param>
public readonly record struct StreakStep(StreakState Next, bool Closes, ThresholdSide? Opens);

/// <summary>
/// The streak rule of Threshold evaluation (Story 6.1), pure and the only place that decides when an Alert opens
/// or closes. An Alert opens after <see cref="Required"/> consecutive Readings beyond the same Threshold and
/// closes after as many back within; a Reading off the running side ends that streak and starts its own.
/// </summary>
/// <remarks>
/// While an Alert is open, Readings above the high are not "within" either: three of them in a row close a low
/// Alert as recovered and open the next episode on the high side in the same step (and the other way round), so
/// a Lot that went from too dry to too wet does not keep reading "needs water".
/// </remarks>
public static class ThresholdStreakRule
{
    /// <summary>
    /// How many consecutive Readings open or close an Alert: about 30 minutes at the 15-minute interval.
    /// </summary>
    public const int Required = 3;

    /// <summary>
    /// Places a value against the Thresholds: beyond only strictly, and never above an empty high.
    /// </summary>
    /// <param name="value">The Reading as it is compared: percent for a calibrating Sensor, else the Specification's unit.</param>
    /// <param name="low">The effective low Threshold.</param>
    /// <param name="high">The effective high Threshold, or <see langword="null"/>.</param>
    public static ThresholdPosition Classify(long value, long low, long? high)
    {
        if (value < low)
        {
            return ThresholdPosition.BelowLow;
        }

        return high is { } limit && value > limit ? ThresholdPosition.AboveHigh : ThresholdPosition.Within;
    }

    /// <summary>
    /// Ends the running streak, as changed Thresholds and a new evaluation epoch do. An open Alert stays open.
    /// </summary>
    /// <param name="current">The streak so far.</param>
    public static StreakState Reset(StreakState current) => new(current.Open, ThresholdPosition.Within, 0);

    /// <summary>
    /// Applies one evaluated Reading to the streak.
    /// </summary>
    /// <param name="current">The streak so far.</param>
    /// <param name="reading">Where the Reading is against the Thresholds.</param>
    public static StreakStep Advance(StreakState current, ThresholdPosition reading)
    {
        // Where the Sensor stays without anything changing: within, or beyond the side of its open Alert.
        var steady = current.Open switch
        {
            ThresholdSide.Low => ThresholdPosition.BelowLow,
            ThresholdSide.High => ThresholdPosition.AboveHigh,
            _ => ThresholdPosition.Within,
        };

        if (reading == steady)
        {
            return new StreakStep(Reset(current), false, null);
        }

        var count = current.Count > 0 && current.Position == reading ? current.Count + 1 : 1;

        if (count < Required)
        {
            return new StreakStep(new StreakState(current.Open, reading, count), false, null);
        }

        ThresholdSide? opens = reading switch
        {
            ThresholdPosition.BelowLow => ThresholdSide.Low,
            ThresholdPosition.AboveHigh => ThresholdSide.High,
            _ => null,
        };

        return new StreakStep(new StreakState(opens, ThresholdPosition.Within, 0), current.Open is not null, opens);
    }
}
