using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Sensors;

namespace Coldframe.Server.Sensors;

/// <summary>
/// The open Threshold Alert of a Sensor: at most one at a time (Story 6.1).
/// </summary>
/// <param name="AlertId">The Alert ID, derived from the Sensor and the episode.</param>
/// <param name="Episode">The episode the Alert belongs to.</param>
/// <param name="Side">The Threshold that was crossed.</param>
[GenerateSerializer]
[Alias("coldframe.sensor-open-alert")]
public sealed record SensorOpenAlert(
    [property: Id(0)] Guid AlertId,
    [property: Id(1)] int Episode,
    [property: Id(2)] ThresholdSide Side);

/// <summary>
/// An open or a close of the Sensor's Alert that the Alert grain has not acknowledged yet (Story 6.1), with
/// everything the call needs, so it is delivered again from the Sensor's own stream.
/// </summary>
/// <param name="AlertId">The Alert.</param>
/// <param name="Change"><see cref="AlertLifecycle.Open"/> for an open, <see cref="AlertLifecycle.Closed"/> for a close.</param>
/// <param name="Episode">The episode of the Alert.</param>
/// <param name="Side">The Threshold that was crossed.</param>
/// <param name="SiteId">The Site of the Node when the episode opened; <see langword="null"/> for a close, which the Alert grain places itself.</param>
/// <param name="LotId">The Lot of the Node when the episode opened; <see langword="null"/> for a close.</param>
/// <param name="Reason">Why the Alert closes; <see langword="null"/> for an open.</param>
/// <param name="At">When the Alert opened or closed.</param>
[GenerateSerializer]
[Alias("coldframe.sensor-pending-alert-delivery")]
public sealed record PendingAlertDelivery(
    [property: Id(0)] Guid AlertId,
    [property: Id(1)] AlertLifecycle Change,
    [property: Id(2)] int Episode,
    [property: Id(3)] ThresholdSide Side,
    [property: Id(4)] string? SiteId,
    [property: Id(5)] string? LotId,
    [property: Id(6)] AlertCloseReason? Reason,
    [property: Id(7)] DateTimeOffset At);

/// <summary>
/// The state of the Sensor grain: the Node and slot it belongs to, the Specification in force, and each
/// Threshold side as <c>Default | Override(value) | Cleared</c> (AD-19). A side stores only its kind and an
/// override's value; a side in <see cref="ThresholdKind.Default"/> reads the Specification's default. It also
/// holds the Calibration in force, the reference point kept while the other one is missing, and which
/// Calibration the Device grain acknowledged (Story 5.1). For evaluation (Story 6.1) it holds the running streak,
/// the episode counter, the open Alert and the deliveries the Alert grain has not acknowledged.
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

    [Id(15)]
    private readonly List<PendingAlertDelivery> _pendingAlertDeliveries = [];

    /// <summary>
    /// Where the Readings of the running streak are; <see cref="ThresholdPosition.Within"/> when there is none.
    /// </summary>
    [Id(9)]
    public ThresholdPosition StreakPosition { get; private set; }

    /// <summary>
    /// How many consecutive Readings the running streak has: 0, 1 or 2.
    /// </summary>
    [Id(10)]
    public int StreakCount { get; private set; }

    /// <summary>
    /// The evaluation epoch of the Node when the streak last changed. A Reading of another epoch starts over.
    /// </summary>
    [Id(11)]
    public long EvaluationEpoch { get; private set; }

    /// <summary>
    /// When the last Reading that changed the streak was taken, or <see langword="null"/> before the first. A
    /// steady Reading journals nothing (the journal replays in full), so after a reactivation this can lag
    /// behind the Reading the grain evaluated last.
    /// </summary>
    [Id(12)]
    public DateTimeOffset? LastEvaluatedAt { get; private set; }

    /// <summary>
    /// The Sensor's episode counter: 0 before its first Alert. Journaled before the Alert grain is called, so
    /// the Alert ID it derives never repeats and never changes on a retry.
    /// </summary>
    [Id(13)]
    public int Episode { get; private set; }

    /// <summary>
    /// The Sensor's open Threshold Alert, or <see langword="null"/>. At most one at a time.
    /// </summary>
    [Id(14)]
    public SensorOpenAlert? OpenAlert { get; private set; }

    /// <summary>
    /// The opens and closes the Alert grain has not acknowledged, oldest first; they are delivered in this order.
    /// </summary>
    public IReadOnlyList<PendingAlertDelivery> PendingAlertDeliveries => _pendingAlertDeliveries;

    /// <summary>
    /// The running streak with the side of the open Alert, as <see cref="ThresholdStreakRule"/> reads it.
    /// </summary>
    public StreakState Streak => new(OpenAlert?.Side, StreakPosition, StreakCount);

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

        // Other defaults or another unit: the streak starts over. An open Alert stays open (Story 6.1).
        StreakPosition = ThresholdPosition.Within;
        StreakCount = 0;
    }

    public void Apply(SensorThresholdsChanged @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        Low = Stored(@event.Low);
        High = Stored(@event.High);

        // Other Thresholds, another question: the streak starts over. An open Alert stays open (Story 6.1).
        StreakPosition = ThresholdPosition.Within;
        StreakCount = 0;
    }

    public void Apply(SensorStreakChanged @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        StreakPosition = @event.Count == 0 ? ThresholdPosition.Within : @event.Position;
        StreakCount = @event.Count;
        EvaluationEpoch = @event.Epoch;
        Evaluated(@event.MeasuredAt);
    }

    public void Apply(SensorThresholdEpisodeOpened @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        Episode = @event.Episode;
        OpenAlert = new SensorOpenAlert(@event.AlertId, @event.Episode, @event.Side);
        StreakPosition = ThresholdPosition.Within;
        StreakCount = 0;
        EvaluationEpoch = @event.Epoch;
        Evaluated(@event.MeasuredAt);
        _pendingAlertDeliveries.Add(new PendingAlertDelivery(
            @event.AlertId,
            AlertLifecycle.Open,
            @event.Episode,
            @event.Side,
            @event.SiteId,
            @event.LotId,
            Reason: null,
            @event.OpenedAt));
    }

    public void Apply(SensorThresholdEpisodeClosed @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        if (OpenAlert is not { } open || open.AlertId != @event.AlertId)
        {
            return;
        }

        _pendingAlertDeliveries.Add(new PendingAlertDelivery(
            open.AlertId,
            AlertLifecycle.Closed,
            open.Episode,
            open.Side,
            SiteId: null,
            LotId: null,
            @event.Reason,
            @event.ClosedAt));
        OpenAlert = null;
        StreakPosition = ThresholdPosition.Within;
        StreakCount = 0;
        EvaluationEpoch = @event.Epoch;

        if (@event.MeasuredAt is { } measuredAt)
        {
            Evaluated(measuredAt);
        }
    }

    public void Apply(SensorAlertDelivered @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        _pendingAlertDeliveries.RemoveAll(delivery => delivery.AlertId == @event.AlertId && delivery.Change == @event.Change);
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

        // Another Calibration reads the same raw value differently: the streak starts over. An open Alert
        // stays open (Story 6.1).
        StreakPosition = ThresholdPosition.Within;
        StreakCount = 0;
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

    // Never backwards: evaluation is monotonic in measured_at.
    private void Evaluated(DateTimeOffset measuredAt)
    {
        if (LastEvaluatedAt is not { } last || measuredAt > last)
        {
            LastEvaluatedAt = measuredAt;
        }
    }

    // Only an override has a value of its own.
    private static ThresholdSetting Stored(ThresholdSetting side) =>
        side.Kind == ThresholdKind.Override ? side : new ThresholdSetting(side.Kind);

    private static long? Effective(ThresholdSetting side, long? specificationDefault) =>
        ThresholdRules.Effective(side, specificationDefault);
}
