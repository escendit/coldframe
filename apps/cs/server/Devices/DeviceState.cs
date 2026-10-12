using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Devices;

namespace Coldframe.Server.Devices;

/// <summary>
/// The state of the Device grain: its Site, kind and wrapped <c>K_dev</c>, once enrolled, a Node's Lot, when
/// it was last seen, its Pause sources (AD-8), a Node's last relay Hub (AD-18), and the hash and Sensors of
/// the last Specification set the Server accepted from a Node (AD-19), and the Calibration in force per Sensor
/// as the Sensor grain set it (Story 5.1), and the evaluation epoch (Story 6.1). For silence (Story 7.1) it holds
/// the clock start, the Device's own Silence Window, the episode counter, the open <c>silent</c> Alert, the last
/// recovery and the deliveries the Alert grain has not acknowledged; the expiry is derived from them. The replay
/// window and the downlink counter are not journaled: they live in <c>device_replay</c> and commit with the
/// Readings (AD-9).
/// </summary>
[GenerateSerializer]
[Alias("coldframe.device-state")]
public sealed class DeviceState
{
    [Id(7)]
    private readonly Dictionary<DevicePauseSource, DateTimeOffset?> _pausedBy = [];

    [Id(10)]
    private List<DeclaredSensor> _sensors = [];

    [Id(11)]
    private List<string> _pendingReleases = [];

    [Id(12)]
    private Dictionary<Guid, CalibrationInForce> _calibrations = [];

    [Id(20)]
    private readonly List<PendingDeviceAlertDelivery> _pendingAlertDeliveries = [];

    /// <summary>
    /// The Site the Device is enrolled on, or <see langword="null"/> before enrolment.
    /// </summary>
    [Id(0)]
    public string? SiteId { get; private set; }

    /// <summary>
    /// Hub or Node, once enrolled.
    /// </summary>
    [Id(1)]
    public DeviceKind Kind { get; private set; }

    /// <summary>
    /// The wrapped <c>K_dev</c>, once enrolled.
    /// </summary>
    [Id(2)]
    public WrappedDeviceKey? WrappedKey { get; private set; }

    /// <summary>
    /// When the Device was enrolled.
    /// </summary>
    [Id(3)]
    public DateTimeOffset? EnrolledAt { get; private set; }

    /// <summary>
    /// When the Server last accepted a heartbeat, or <see langword="null"/> before the first.
    /// </summary>
    [Id(4)]
    public DateTimeOffset? LastSeenAt { get; private set; }

    /// <summary>
    /// The signed timestamp of the last accepted heartbeat, Unix milliseconds; 0 before the first. A
    /// heartbeat at or below it is a replay, even after the grain was reactivated.
    /// </summary>
    [Id(5)]
    public long LastHeartbeatTimestampMs { get; private set; }

    /// <summary>
    /// The Lot a Node is assigned to (AD-18), or <see langword="null"/>. The Device grain is the only source
    /// of which Lot a Node is on.
    /// </summary>
    [Id(6)]
    public string? LotId { get; private set; }

    /// <summary>
    /// The Node's evaluation epoch (Story 6.1): it counts the assignment and Pause events the Device journaled
    /// (<see cref="DeviceAssigned"/>, <see cref="DeviceMoved"/>, <see cref="DeviceUnassigned"/>,
    /// <see cref="DevicePaused"/>, <see cref="DeviceResumed"/>), so it is derived from the stream and needs no
    /// event of its own. The Device grain sends it with every Reading it hands a Sensor grain, and a Sensor's
    /// streak never runs across two epochs.
    /// </summary>
    [Id(13)]
    public long EvaluationEpoch { get; private set; }

    /// <summary>
    /// The newest <c>measured_at</c> the Server accepted from a Node (Story 7.1), or <see langword="null"/> before
    /// the first: the Node's liveness. Never the arrival time, and never backwards.
    /// </summary>
    [Id(14)]
    public DateTimeOffset? LastReportedAt { get; private set; }

    /// <summary>
    /// The Device's own Silence Window, or <see langword="null"/> while the default of its kind is in force.
    /// </summary>
    [Id(15)]
    public TimeSpan? SilenceWindowOverride { get; private set; }

    /// <summary>
    /// Where the Silence Window counts from (Story 7.1), or <see langword="null"/> before enrolment: the latest
    /// of enrolment, the last accepted report (a Hub's heartbeat, a Node's newest <c>measured_at</c>), the last
    /// Server start, the last resume and, for a Node, the recovery of its last relay Hub. It never moves backwards.
    /// </summary>
    [Id(16)]
    public DateTimeOffset? SilenceFrom { get; private set; }

    /// <summary>
    /// The Device's silence episode counter: 0 before its first silence. Journaled before the Alert grain is
    /// called, so the Alert ID it derives never repeats and never changes on a retry.
    /// </summary>
    [Id(17)]
    public int SilenceEpisode { get; private set; }

    /// <summary>
    /// The Device's open <c>silent</c> Alert, or <see langword="null"/>. At most one at a time.
    /// </summary>
    [Id(18)]
    public DeviceSilentAlert? OpenSilentAlert { get; private set; }

    /// <summary>
    /// When the Device's last silence episode ended, or <see langword="null"/> before the first. A Node whose
    /// relay Hub recovered after the Node's clock start counts its window from here.
    /// </summary>
    [Id(19)]
    public DateTimeOffset? LastRecoveredAt { get; private set; }

    /// <summary>
    /// The Silence Window in force: the Device's override, else the default of its kind.
    /// </summary>
    public TimeSpan SilenceWindow => SilenceRule.Window(Kind, SilenceWindowOverride);

    /// <summary>
    /// When the Silence Window expires (AD-6): <see cref="SilenceFrom"/> plus <see cref="SilenceWindow"/>, or
    /// <see langword="null"/> before enrolment. Derived, so it survives a restart and a changed window moves it
    /// without an event of its own.
    /// </summary>
    public DateTimeOffset? SilenceDueAt => SilenceFrom is { } from ? SilenceRule.DueAt(from, SilenceWindow) : null;

    /// <summary>
    /// The opens and closes of the Device's Alerts that the Alert grain has not acknowledged, oldest first; they
    /// are delivered in this order, so a close never overtakes its open.
    /// </summary>
    public IReadOnlyList<PendingDeviceAlertDelivery> PendingAlertDeliveries => _pendingAlertDeliveries;

    /// <summary>
    /// The Pause sources of the Device, each with its optional end date (AD-8).
    /// </summary>
    public IReadOnlyDictionary<DevicePauseSource, DateTimeOffset?> PausedBy => _pausedBy;

    /// <summary>
    /// Whether the Device is paused: if and only if it has at least one Pause source.
    /// </summary>
    public bool IsPaused => _pausedBy.Count > 0;

    /// <summary>
    /// The Hub that relayed the Node's last accepted frame, or <see langword="null"/> before the first.
    /// </summary>
    [Id(8)]
    public string? LastRelayHubId { get; private set; }

    /// <summary>
    /// The Lots the Node left whose release is not confirmed yet (AD-18), oldest first. Journaled state: a
    /// crash between the move and the release still frees the old Lot, because the Device keeps releasing
    /// them until each answers released or unchanged.
    /// </summary>
    public IReadOnlyList<string> PendingReleases => _pendingReleases;

    /// <summary>
    /// The Node's known hash (AD-19): the <c>spec_hash</c> of the last Specification set the Server accepted
    /// from it, or <see langword="null"/> before the first.
    /// </summary>
    [Id(9)]
    public byte[]? SpecHash { get; private set; }

    /// <summary>
    /// The Sensors of that set, in slot order. A Reading of another slot or quantity has no declared Sensor.
    /// </summary>
    public IReadOnlyList<DeclaredSensor> Sensors => _sensors;

    /// <summary>
    /// The Calibration ID in force for a Sensor (Story 5.1), or <see langword="null"/> while it has none. A cache
    /// the Sensor grain fills: the Device grain stamps it on the Sensor's Readings and never validates it.
    /// </summary>
    /// <param name="sensorId">The Sensor ID.</param>
    public Guid? CalibrationOf(Guid sensorId) =>
        _calibrations.TryGetValue(sensorId, out var held) ? held.CalibrationId : null;

    /// <summary>
    /// Whether the cache holds the Sensor's Calibration of <paramref name="revision"/> or a newer one.
    /// </summary>
    /// <param name="sensorId">The Sensor ID.</param>
    /// <param name="revision">The revision to look for.</param>
    public bool HoldsCalibration(Guid sensorId, int revision) =>
        _calibrations.TryGetValue(sensorId, out var held) && held.Revision >= revision;

    /// <summary>
    /// Whether <paramref name="specHash"/> is the Node's known hash. An empty hash is never known.
    /// </summary>
    public bool Knows(ReadOnlySpan<byte> specHash) =>
        !specHash.IsEmpty && SpecHash is { } known && specHash.SequenceEqual(known);

    public void Apply(DeviceEnrolled @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        SiteId = @event.SiteId;
        Kind = @event.Kind;
        WrappedKey = @event.WrappedKey;
        EnrolledAt = @event.EnrolledAt;

        // A Device that never reports is silent one window after its enrolment.
        SilenceFrom = SilenceRule.Latest(SilenceFrom, @event.EnrolledAt);
    }

    public void Apply(DeviceAssigned @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        LotId = @event.LotId;
        _pendingReleases.Remove(@event.LotId);
        EvaluationEpoch++;
    }

    public void Apply(DeviceMoved @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        LotId = @event.ToLotId;

        // Moving back to a Lot still pending release makes it the Node's again: it must not be released.
        _pendingReleases.Remove(@event.ToLotId);
        QueueRelease(@event.FromLotId);
        EvaluationEpoch++;
    }

    public void Apply(DeviceUnassigned @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        LotId = null;
        QueueRelease(@event.FromLotId);
        EvaluationEpoch++;
    }

    public void Apply(DeviceLotReleased @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        _pendingReleases.Remove(@event.LotId);
    }

    public void Apply(DeviceSeen @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        LastSeenAt = @event.SeenAt;
        LastHeartbeatTimestampMs = Math.Max(LastHeartbeatTimestampMs, @event.DeviceTimestampMs);

        // A Hub's liveness is its last accepted heartbeat.
        SilenceFrom = SilenceRule.Latest(SilenceFrom, @event.SeenAt);
    }

    public void Apply(DeviceRelayChanged @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        LastRelayHubId = @event.HubId;
    }

    public void Apply(DevicePaused @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        _pausedBy[@event.Source] = @event.EndsAt;
        EvaluationEpoch++;
    }

    public void Apply(DeviceResumed @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        _pausedBy.Remove(@event.Source);
        EvaluationEpoch++;

        // The Silence Window restarts at the moment of resume: when the last source cleared.
        if (_pausedBy.Count == 0)
        {
            SilenceFrom = SilenceRule.Latest(SilenceFrom, @event.ResumedAt);
        }
    }

    public void Apply(DeviceSpecificationsDeclared @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        SpecHash = @event.SpecHash;
        _sensors = [.. @event.Sensors];
    }

    public void Apply(DeviceCalibrationSet @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        // Idempotent by revision: a redelivery, or one that arrives late, never replaces a newer Calibration.
        if (!HoldsCalibration(@event.SensorId, @event.Revision))
        {
            _calibrations[@event.SensorId] = new CalibrationInForce(@event.CalibrationId, @event.Revision);
        }
    }

    public void Apply(DeviceReported @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        // A Node's liveness is the newest measured_at accepted from it, never the arrival time.
        if (LastReportedAt is not { } last || @event.MeasuredAt > last)
        {
            LastReportedAt = @event.MeasuredAt;
        }

        SilenceFrom = SilenceRule.Latest(SilenceFrom, @event.MeasuredAt);
    }

    public void Apply(DeviceSilenceWindowSet @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        SilenceWindowOverride = @event.WindowSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null;
    }

    public void Apply(DeviceSilenceClockRestarted @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        SilenceFrom = SilenceRule.Latest(SilenceFrom, @event.From);
    }

    public void Apply(DeviceSilenceStarted @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        // At most one open silent Alert: an episode that is not the next one is not a new silence.
        if (OpenSilentAlert is not null || @event.Episode <= SilenceEpisode)
        {
            return;
        }

        SilenceEpisode = @event.Episode;
        OpenSilentAlert = new DeviceSilentAlert(@event.AlertId, @event.Episode, @event.StartedAt);
        _pendingAlertDeliveries.Add(new PendingDeviceAlertDelivery(
            @event.AlertId,
            AlertLifecycle.Open,
            AlertKind.Silent,
            @event.Episode,
            @event.SiteId,
            @event.LotId,
            Reason: null,
            @event.StartedAt));
    }

    public void Apply(DeviceSilenceEnded @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        if (OpenSilentAlert is not { } open || open.AlertId != @event.AlertId)
        {
            return;
        }

        _pendingAlertDeliveries.Add(new PendingDeviceAlertDelivery(
            open.AlertId,
            AlertLifecycle.Closed,
            AlertKind.Silent,
            open.Episode,
            SiteId: null,
            LotId: null,
            @event.Reason,
            @event.EndedAt));
        OpenSilentAlert = null;
        LastRecoveredAt = @event.EndedAt;
    }

    public void Apply(DeviceAlertDelivered @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        _pendingAlertDeliveries.RemoveAll(delivery => delivery.AlertId == @event.AlertId && delivery.Change == @event.Change);
    }

    private void QueueRelease(string lotId)
    {
        if (!_pendingReleases.Contains(lotId, StringComparer.Ordinal))
        {
            _pendingReleases.Add(lotId);
        }
    }
}

/// <summary>
/// The Calibration the Device grain stamps on a Sensor's Readings (Story 5.1).
/// </summary>
/// <param name="CalibrationId">The Calibration ID in force.</param>
/// <param name="Revision">The Sensor's Calibration count when it was set.</param>
[GenerateSerializer]
[Alias("coldframe.calibration-in-force")]
public sealed record CalibrationInForce(
    [property: Id(0)] Guid CalibrationId,
    [property: Id(1)] int Revision);

/// <summary>
/// An open or a close of one of the Device's Alerts that the Alert grain has not acknowledged yet (Story 7.1),
/// with everything the call needs, so it is delivered again from the Device's own stream.
/// </summary>
/// <param name="AlertId">The Alert.</param>
/// <param name="Change"><see cref="AlertLifecycle.Open"/> for an open, <see cref="AlertLifecycle.Closed"/> for a close.</param>
/// <param name="Kind">What the Alert is about.</param>
/// <param name="Episode">The Device's episode of the Alert.</param>
/// <param name="SiteId">The Site of the Device when the episode started; <see langword="null"/> for a close, which the Alert grain places itself.</param>
/// <param name="LotId">The Lot of a Node when the episode started; <see langword="null"/> without one and for a close.</param>
/// <param name="Reason">Why the Alert closes; <see langword="null"/> for an open.</param>
/// <param name="At">When the Alert opened or closed.</param>
[GenerateSerializer]
[Alias("coldframe.device-pending-alert-delivery")]
public sealed record PendingDeviceAlertDelivery(
    [property: Id(0)] Guid AlertId,
    [property: Id(1)] AlertLifecycle Change,
    [property: Id(2)] AlertKind Kind,
    [property: Id(3)] int Episode,
    [property: Id(4)] string? SiteId,
    [property: Id(5)] string? LotId,
    [property: Id(6)] AlertCloseReason? Reason,
    [property: Id(7)] DateTimeOffset At);
