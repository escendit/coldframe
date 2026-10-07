using Coldframe.Contracts.Sensors;

namespace Coldframe.Server.Sensors;

/// <summary>
/// The state of the Sensor grain: the Node and slot it belongs to, the Specification in force, and each
/// Threshold side as <c>Default | Override(value) | Cleared</c> (AD-19). A side stores only its kind and an
/// override's value; a side in <see cref="ThresholdKind.Default"/> reads the Specification's default. It also
/// holds the Calibration in force, the reference point kept while the other one is missing, and which
/// Calibration the Device grain acknowledged (Story 5.1).
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
    /// The Calibration in force, or <see langword="null"/> while the Sensor is uncalibrated.
    /// </summary>
    [Id(5)]
    public SensorCalibration? Calibration { get; private set; }

    /// <summary>
    /// The raw value of a dry point kept while the wet one is missing.
    /// </summary>
    [Id(6)]
    public long? PendingDryRaw { get; private set; }

    /// <summary>
    /// The raw value of a wet point kept while the dry one is missing.
    /// </summary>
    [Id(7)]
    public long? PendingWetRaw { get; private set; }

    /// <summary>
    /// The newest Calibration the Device grain acknowledged, or <see langword="null"/> before the first.
    /// </summary>
    [Id(8)]
    public Guid? DeliveredCalibrationId { get; private set; }

    /// <summary>
    /// Whether the Sensor has a Calibration in force.
    /// </summary>
    public bool Calibrated => Calibration is not null;

    /// <summary>
    /// Whether the Calibration in force still has to reach the Device grain.
    /// </summary>
    public bool DeliveryPending => Calibration is { } calibration && DeliveredCalibrationId != calibration.Id;

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

    public void Apply(SensorCalibrationPointRecorded @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        // The Calibration in force is not touched: a recalibration only replaces it when its second point is in.
        if (@event.Point == CalibrationPoint.Dry)
        {
            PendingDryRaw = @event.RawValue;
        }
        else
        {
            PendingWetRaw = @event.RawValue;
        }
    }

    public void Apply(SensorCalibrated @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        Calibration = new SensorCalibration(@event.CalibrationId, (Calibration?.Revision ?? 0) + 1, @event.DryRaw, @event.WetRaw, @event.CalibratedAt);
        PendingDryRaw = null;
        PendingWetRaw = null;
    }

    public void Apply(SensorCalibrationDelivered @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        // Only the newest Calibration counts as delivered: an acknowledgement of an older one is history.
        if (Calibration?.Id == @event.CalibrationId)
        {
            DeliveredCalibrationId = @event.CalibrationId;
        }
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
