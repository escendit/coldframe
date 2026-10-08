using Coldframe.Contracts.Sensors;

namespace Coldframe.Server.Sensors;

/// <summary>
/// What <see cref="ThresholdRules.Evaluate"/> decided.
/// </summary>
/// <param name="Outcome">
/// <see cref="SensorThresholdsOutcome.Changed"/> or <see cref="SensorThresholdsOutcome.Unchanged"/> when the
/// request is valid, otherwise the reason it is refused.
/// </param>
/// <param name="Low">The low side to store (kind, and the value of an override only).</param>
/// <param name="High">The high side to store.</param>
public sealed record ThresholdChange(SensorThresholdsOutcome Outcome, ThresholdSetting Low, ThresholdSetting High);

/// <summary>
/// The rules of a Sensor's Thresholds (Story 5.3, AD-19), pure so they can be tested without a grain. A Sensor
/// is alerting exactly when its effective low exists.
/// </summary>
public static class ThresholdRules
{
    /// <summary>
    /// The share of the range, in percent, above its minimum at which the Server proposes a low.
    /// </summary>
    public const long ProposedLowPercent = 20;

    /// <summary>
    /// The lowest and highest value a Threshold can take: whole percent 0 to 100 for a calibrating Sensor
    /// (whatever its Calibration), otherwise the Specification's range, in its unit.
    /// </summary>
    /// <param name="specification">The Specification in force.</param>
    public static (long Min, long Max) Limits(SensorSpecification specification)
    {
        ArgumentNullException.ThrowIfNull(specification);

        return specification.Calibration ? (0, 100) : (specification.RangeMin, specification.RangeMax);
    }

    /// <summary>
    /// The low the Server proposes: <c>Min + 20 % x (Max - Min)</c> of the Sensor's range (20 percent for a
    /// calibrating Sensor) in integer arithmetic, when the Specification has no default low; otherwise
    /// <see langword="null"/>. There is never a proposed high.
    /// </summary>
    /// <param name="specification">The Specification in force.</param>
    public static long? ProposedLow(SensorSpecification specification)
    {
        ArgumentNullException.ThrowIfNull(specification);

        if (specification.DefaultLow is not null)
        {
            return null;
        }

        var (min, max) = Limits(specification);
        return min + (ProposedLowPercent * (max - min) / 100);
    }

    /// <summary>
    /// Validates a request against the sides in force and says what to store. A side that is
    /// <see langword="null"/> stays as it is; the rules apply to the effective values after the change.
    /// </summary>
    /// <param name="specification">The Specification in force.</param>
    /// <param name="currentLow">The stored low side.</param>
    /// <param name="currentHigh">The stored high side.</param>
    /// <param name="requestedLow">The requested low side, or <see langword="null"/> to keep it.</param>
    /// <param name="requestedHigh">The requested high side, or <see langword="null"/> to keep it.</param>
    public static ThresholdChange Evaluate(
        SensorSpecification specification,
        ThresholdSetting currentLow,
        ThresholdSetting currentHigh,
        ThresholdSetting? requestedLow,
        ThresholdSetting? requestedHigh)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(currentLow);
        ArgumentNullException.ThrowIfNull(currentHigh);

        var limits = Limits(specification);
        var low = requestedLow is null ? currentLow : Normalized(requestedLow);
        var high = requestedHigh is null ? currentHigh : Normalized(requestedHigh);

        foreach (var side in new[] { requestedLow, requestedHigh })
        {
            if (side is not null && !WellFormed(side))
            {
                return Refused(SensorThresholdsOutcome.MalformedSide);
            }

            if (side is { Kind: ThresholdKind.Override, Value: { } value } && (value < limits.Min || value > limits.Max))
            {
                return Refused(SensorThresholdsOutcome.OutOfRange);
            }
        }

        var effectiveLow = Effective(low, specification.DefaultLow);
        var effectiveHigh = Effective(high, specification.DefaultHigh);

        if (effectiveHigh is not null && effectiveLow is null)
        {
            return Refused(SensorThresholdsOutcome.LowRequired);
        }

        if (effectiveLow is { } lowValue && effectiveHigh is { } highValue && lowValue >= highValue)
        {
            return Refused(SensorThresholdsOutcome.LowNotBelowHigh);
        }

        var changed = low != currentLow || high != currentHigh;
        return new ThresholdChange(changed ? SensorThresholdsOutcome.Changed : SensorThresholdsOutcome.Unchanged, low, high);

        ThresholdChange Refused(SensorThresholdsOutcome outcome) => new(outcome, currentLow, currentHigh);
    }

    /// <summary>
    /// The effective value of a stored side given the Specification's default.
    /// </summary>
    /// <param name="side">The stored side.</param>
    /// <param name="specificationDefault">The Specification's default for that side, or <see langword="null"/>.</param>
    public static long? Effective(ThresholdSetting side, long? specificationDefault)
    {
        ArgumentNullException.ThrowIfNull(side);

        return side.Kind switch
        {
            ThresholdKind.Default => specificationDefault,
            ThresholdKind.Override => side.Value,
            _ => null,
        };
    }

    private static bool WellFormed(ThresholdSetting side) => side.Kind switch
    {
        ThresholdKind.Override => side.Value is not null,
        ThresholdKind.Default or ThresholdKind.Cleared => side.Value is null,
        _ => false,
    };

    // Only an override has a value of its own.
    private static ThresholdSetting Normalized(ThresholdSetting side) =>
        side.Kind == ThresholdKind.Override ? side : new ThresholdSetting(side.Kind);
}
