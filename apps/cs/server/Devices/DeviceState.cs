using Coldframe.Contracts.Devices;

namespace Coldframe.Server.Devices;

/// <summary>
/// The state of the Device grain: its Site, kind and wrapped <c>K_dev</c>, once enrolled, a Node's Lot, when
/// it was last seen, its Pause sources (AD-8), a Node's last relay Hub (AD-18), and the hash and Sensors of
/// the last Specification set the Server accepted from a Node (AD-19). The replay window and the
/// downlink counter are not journaled: they live in <c>device_replay</c> and commit with the Readings (AD-9).
/// </summary>
[GenerateSerializer]
[Alias("coldframe.device-state")]
public sealed class DeviceState
{
    [Id(7)]
    private readonly Dictionary<DevicePauseSource, DateTimeOffset?> _pausedBy = [];

    [Id(10)]
    private List<DeclaredSensor> _sensors = [];

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
    }

    public void Apply(DeviceAssigned @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        LotId = @event.LotId;
    }

    public void Apply(DeviceSeen @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        LastSeenAt = @event.SeenAt;
        LastHeartbeatTimestampMs = Math.Max(LastHeartbeatTimestampMs, @event.DeviceTimestampMs);
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
    }

    public void Apply(DeviceResumed @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        _pausedBy.Remove(@event.Source);
    }

    public void Apply(DeviceSpecificationsDeclared @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        SpecHash = @event.SpecHash;
        _sensors = [.. @event.Sensors];
    }
}
