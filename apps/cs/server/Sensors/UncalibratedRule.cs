using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Sensors;

namespace Coldframe.Server.Sensors;

/// <summary>
/// What the Sensor's state asks of its uncalibrated Alert (Story 7.4).
/// </summary>
/// <param name="Closes">Why the open Alert closes, or <see langword="null"/> when it stays as it is or none is open.</param>
/// <param name="Opens">Whether the Sensor's next uncalibrated episode opens, for the Lot of the context.</param>
public readonly record struct UncalibratedStep(AlertCloseReason? Closes, bool Opens)
{
    /// <summary>
    /// Nothing changes.
    /// </summary>
    public static UncalibratedStep None { get; }
}

/// <summary>
/// The uncalibrated rule (Story 7.4): pure, and the only place that decides when a Sensor's Health Alert of kind
/// <c>uncalibrated</c> opens and closes. A Sensor needs calibration when its Specification says
/// <c>calibration: true</c> and it has no Calibration in force. The Alert is open exactly while the Sensor needs
/// calibration and its newest context is on a Lot and not paused, and it names that Lot.
/// </summary>
public static class UncalibratedRule
{
    /// <summary>
    /// Whether a Sensor needs calibration: its Specification requires one and none is in force.
    /// </summary>
    /// <param name="specification">The Specification in force, or <see langword="null"/> before the first declaration.</param>
    /// <param name="calibrated">Whether the Sensor has a Calibration in force.</param>
    public static bool NeedsCalibration(SensorSpecification? specification, bool calibrated) =>
        specification is { Calibration: true } && !calibrated;

    /// <summary>
    /// Decides what happens to the Sensor's uncalibrated Alert, from the state alone.
    /// </summary>
    /// <param name="context">The newest context the Sensor holds, or <see langword="null"/> before the first.</param>
    /// <param name="specification">The Specification in force, or <see langword="null"/> before the first declaration.</param>
    /// <param name="calibrated">Whether the Sensor has a Calibration in force.</param>
    /// <param name="openLotId">The Lot the open uncalibrated Alert names, or <see langword="null"/> when none is open.</param>
    public static UncalibratedStep Decide(SensorContext? context, SensorSpecification? specification, bool calibrated, string? openLotId)
    {
        var needs = NeedsCalibration(specification, calibrated);
        var wanted = needs && context is { Evaluated: true };

        if (openLotId is null)
        {
            return new UncalibratedStep(Closes: null, Opens: wanted);
        }

        if (!needs)
        {
            // A Specification that stopped requiring calibration refuses a Calibration: nothing else could end
            // the episode, and "removed" is the nearest of the five reasons.
            return new UncalibratedStep(specification is { Calibration: true } ? AlertCloseReason.Calibrated : AlertCloseReason.Removed, Opens: false);
        }

        if (context is null)
        {
            return UncalibratedStep.None;
        }

        // The Alert leads to the Lot it names: once the Node is on no Lot, or on another one, it is closed, and a
        // move opens the next episode for the new Lot.
        if (!string.Equals(context.LotId, openLotId, StringComparison.Ordinal))
        {
            return new UncalibratedStep(AlertCloseReason.Unassigned, Opens: wanted);
        }

        return context.Paused ? new UncalibratedStep(AlertCloseReason.Paused, Opens: false) : UncalibratedStep.None;
    }
}
