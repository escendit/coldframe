using Coldframe.Contracts.Sensors;

namespace Coldframe.Server.Sensors;

/// <summary>
/// The state of the Sensor grain: the Node and slot it belongs to, the Specification in force, and each
/// Threshold side as <c>Default | Override(value) | Cleared</c> (AD-19). A side stores only its kind and an
/// override's value; a side in <see cref="ThresholdKind.Default"/> reads the Specification's default.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.sensor-state")]
public sealed class SensorState
{
    /// <summary>
    /// The Device ID of the Sensor's Node, once declared.
    /// </summary>
    [Id(0)]
    public string? DeviceId { get; private set; }

    /// <summary>
    /// The Sensor's slot, once declared.
    /// </summary>
    [Id(1)]
    public int Slot { get; private set; }

    /// <summary>
    /// The Specification in force, or <see langword="null"/> before the first declaration.
    /// </summary>
    [Id(2)]
    public SensorSpecification? Specification { get; private set; }

    /// <summary>
    /// The low side: its kind, and its value only when it is an override.
    /// </summary>
    [Id(3)]
    public ThresholdSetting Low { get; private set; } = ThresholdSetting.Default;

    /// <summary>
    /// The high side: its kind, and its value only when it is an override.
    /// </summary>
    [Id(4)]
    public ThresholdSetting High { get; private set; } = ThresholdSetting.Default;

    /// <summary>
    /// Whether the Sensor was declared.
    /// </summary>
    public bool Declared => Specification is not null;

    /// <summary>
    /// The low Threshold in force, or <see langword="null"/> when the side has none.
    /// </summary>
    public long? EffectiveLow => Effective(Low, Specification?.DefaultLow);

    /// <summary>
    /// The high Threshold in force, or <see langword="null"/> when the side has none.
    /// </summary>
    public long? EffectiveHigh => Effective(High, Specification?.DefaultHigh);

    public void Apply(SensorDeclared @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        DeviceId = @event.DeviceId;
        Slot = @event.Slot;
        Specification = @event.Specification;
    }

    public void Apply(SensorSpecificationChanged @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        // The Specification only: a side's kind and an override's value are not touched (AD-19).
        Specification = @event.Specification;
    }

    public void Apply(SensorThresholdsChanged @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        Low = Stored(@event.Low);
        High = Stored(@event.High);
    }

    // Only an override has a value of its own.
    private static ThresholdSetting Stored(ThresholdSetting side) =>
        side.Kind == ThresholdKind.Override ? side : new ThresholdSetting(side.Kind);

    private static long? Effective(ThresholdSetting side, long? specificationDefault) => side.Kind switch
    {
        ThresholdKind.Default => specificationDefault,
        ThresholdKind.Override => side.Value,
        _ => null,
    };
}
