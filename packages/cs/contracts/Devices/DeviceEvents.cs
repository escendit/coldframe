using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Events;

namespace Coldframe.Contracts.Devices;

/// <summary>
/// <c>K_dev</c> encrypted at rest under the Server's key-encryption key (AD-12). Only the Server can unwrap
/// it; the plaintext never crosses a grain boundary and is never journaled.
/// </summary>
/// <param name="KekId">Names the key-encryption key: the first 16 hex digits of its SHA-256.</param>
/// <param name="Nonce">The 12-byte ChaCha20-Poly1305 nonce, random per wrap.</param>
/// <param name="Sealed">The sealed <c>K_dev</c>: 32 bytes of ciphertext and the 16-byte tag.</param>
[GenerateSerializer]
[Alias("coldframe.wrapped-device-key")]
public sealed record WrappedDeviceKey(
    [property: Id(0)] string KekId,
    [property: Id(1)] byte[] Nonce,
    [property: Id(2)] byte[] Sealed);

/// <summary>
/// The Device was enrolled on a Site: the Site's roster holds it and the Server holds its <c>K_dev</c>,
/// wrapped. The first event of every <c>device/{id}</c> stream.
/// </summary>
/// <param name="SiteId">The Site the Device belongs to.</param>
/// <param name="Kind">Hub or Node.</param>
/// <param name="WrappedKey">The Device's <c>K_dev</c>, wrapped under the key-encryption key.</param>
/// <param name="EnrolledAt">When the Server enrolled the Device.</param>
[EventType("device.enrolled")]
[GenerateSerializer]
[Alias("coldframe.device-enrolled")]
public sealed record DeviceEnrolled(
    [property: Id(0)] string SiteId,
    [property: Id(1)] DeviceKind Kind,
    [property: Id(2)] WrappedDeviceKey WrappedKey,
    [property: Id(3)] DateTimeOffset EnrolledAt);

/// <summary>
/// The Node was assigned to a Lot, after the Lot granted it the claim (<c>ILotGrain.Claim</c>, AD-18). The
/// Device grain is the only source of which Lot the Node is on.
/// </summary>
/// <param name="SiteId">The Site of the Node and the Lot.</param>
/// <param name="LotId">The Lot the Node is on from now on.</param>
/// <param name="AssignedAt">When the Server assigned the Node.</param>
[EventType("device.assigned")]
[GenerateSerializer]
[Alias("coldframe.device-assigned")]
public sealed record DeviceAssigned(
    [property: Id(0)] string SiteId,
    [property: Id(1)] string LotId,
    [property: Id(2)] DateTimeOffset AssignedAt);

/// <summary>
/// The Node was moved to another Lot, after the new Lot granted it the claim (<c>ILotGrain.Claim</c>, AD-18).
/// The old Lot is released afterwards from the Device's persisted pending state, until it succeeds. Readings
/// stay with the Node.
/// </summary>
/// <param name="SiteId">The Site of the Node and both Lots.</param>
/// <param name="FromLotId">The Lot the Node leaves.</param>
/// <param name="ToLotId">The Lot the Node is on from now on.</param>
/// <param name="MovedAt">When the Server moved the Node.</param>
[EventType("device.moved")]
[GenerateSerializer]
[Alias("coldframe.device-moved")]
public sealed record DeviceMoved(
    [property: Id(0)] string SiteId,
    [property: Id(1)] string FromLotId,
    [property: Id(2)] string ToLotId,
    [property: Id(3)] DateTimeOffset MovedAt);

/// <summary>
/// The Node was unassigned from its Lot. The Lot is released afterwards from the Device's persisted pending
/// state, until it succeeds. Later Readings are stored but not evaluated.
/// </summary>
/// <param name="SiteId">The Site of the Node and the Lot.</param>
/// <param name="FromLotId">The Lot the Node leaves.</param>
/// <param name="UnassignedAt">When the Server unassigned the Node.</param>
[EventType("device.unassigned")]
[GenerateSerializer]
[Alias("coldframe.device-unassigned")]
public sealed record DeviceUnassigned(
    [property: Id(0)] string SiteId,
    [property: Id(1)] string FromLotId,
    [property: Id(2)] DateTimeOffset UnassignedAt);

/// <summary>
/// A Lot the Node left was released (<c>ILotGrain.Release</c> answered released or unchanged): it is no longer
/// pending for release.
/// </summary>
/// <param name="LotId">The Lot that is free of this Node now.</param>
/// <param name="ReleasedAt">When the Server saw the release succeed.</param>
[EventType("device.lot-released")]
[GenerateSerializer]
[Alias("coldframe.device-lot-released")]
public sealed record DeviceLotReleased(
    [property: Id(0)] string LotId,
    [property: Id(1)] DateTimeOffset ReleasedAt);

/// <summary>
/// The Device sent an authentic, new heartbeat (FR-13): its last-seen time. Every accepted heartbeat is
/// journaled, so the Devices projection (Story 3.7) and Silence evaluation (Epic 7) read real events.
/// </summary>
/// <param name="SeenAt">When the Server accepted the heartbeat, from its clock.</param>
/// <param name="DeviceTimestampMs">The heartbeat's signed timestamp, Unix milliseconds; the next one must be above it.</param>
/// <param name="UptimeMs">The Device's uptime from the body, when sent.</param>
[EventType("device.seen")]
[GenerateSerializer]
[Alias("coldframe.device-seen")]
public sealed record DeviceSeen(
    [property: Id(0)] DateTimeOffset SeenAt,
    [property: Id(1)] long DeviceTimestampMs,
    [property: Id(2)] long? UptimeMs);

/// <summary>
/// Where a Pause of a Device comes from (AD-8). A Device is paused if and only if it has at least one source.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.device-pause-source")]
public enum DevicePauseSource
{
    /// <summary>
    /// The Device itself was paused.
    /// </summary>
    Device = 0,

    /// <summary>
    /// The Device's Site is paused; the Site grain propagates it to its roster.
    /// </summary>
    Site = 1,
}

/// <summary>
/// A Node's frame was accepted through another Hub than the one recorded (AD-18): the Node's last relay Hub.
/// Any enrolled Hub may relay any Node; the relay Hub only serves display and Silence suppression (AD-7).
/// Journaled only when the Hub changes, never per frame.
/// </summary>
/// <param name="HubId">The Device ID of the Hub that relayed the frame.</param>
/// <param name="ChangedAt">When the Server accepted the frame.</param>
[EventType("device.relay-changed")]
[GenerateSerializer]
[Alias("coldframe.device-relay-changed")]
public sealed record DeviceRelayChanged(
    [property: Id(0)] string HubId,
    [property: Id(1)] DateTimeOffset ChangedAt);

/// <summary>
/// The Device got a Pause source, or the end date of one changed (AD-8). While it has any source, its
/// Readings are acknowledged and discarded.
/// </summary>
/// <param name="Source">Where the Pause comes from.</param>
/// <param name="EndsAt">When this source's Pause ends, if it has an end.</param>
/// <param name="PausedAt">When the Pause was set.</param>
[EventType("device.paused")]
[GenerateSerializer]
[Alias("coldframe.device-paused")]
public sealed record DevicePaused(
    [property: Id(0)] DevicePauseSource Source,
    [property: Id(1)] DateTimeOffset? EndsAt,
    [property: Id(2)] DateTimeOffset PausedAt);

/// <summary>
/// One Pause source of the Device cleared (AD-8). The Device resumes only when its last source clears.
/// </summary>
/// <param name="Source">The source that cleared.</param>
/// <param name="ResumedAt">When it cleared.</param>
[EventType("device.resumed")]
[GenerateSerializer]
[Alias("coldframe.device-resumed")]
public sealed record DeviceResumed(
    [property: Id(0)] DevicePauseSource Source,
    [property: Id(1)] DateTimeOffset ResumedAt);

/// <summary>
/// One Sensor of a Node's accepted Specification set (AD-19).
/// </summary>
/// <param name="Slot">The Sensor's slot: its index in the set.</param>
/// <param name="Quantity">What the Sensor measures: its Sensor ID token, such as <c>soil_moisture</c>.</param>
/// <param name="SensorId">The Sensor ID derived from the Device ID, the slot and the quantity.</param>
[GenerateSerializer]
[Alias("coldframe.declared-sensor")]
public sealed record DeclaredSensor(
    [property: Id(0)] int Slot,
    [property: Id(1)] string Quantity,
    [property: Id(2)] Guid SensorId);

/// <summary>
/// The Server accepted a Specification set of the Node (AD-19), after every Sensor of the set was declared
/// to its Sensor grain. <paramref name="SpecHash"/> is the Node's known hash from now on, and
/// <paramref name="Sensors"/> replaces the Node's Sensor list: a Reading of another slot or quantity has no
/// declared Sensor.
/// </summary>
/// <param name="SpecHash">The <c>spec_hash</c> of the frame that carried the set, 1 to 32 bytes.</param>
/// <param name="Sensors">The Sensors of the set, in slot order.</param>
/// <param name="DeclaredAt">When the Server accepted the set.</param>
[EventType("device.specifications-declared")]
[GenerateSerializer]
[Alias("coldframe.device-specifications-declared")]
public sealed record DeviceSpecificationsDeclared(
    [property: Id(0)] byte[] SpecHash,
    [property: Id(1)] IReadOnlyList<DeclaredSensor> Sensors,
    [property: Id(2)] DateTimeOffset DeclaredAt);

/// <summary>
/// The Sensor grain set a Calibration in force for one of the Node's Sensors (Story 5.1, AD-9). The Device grain
/// only caches it: Readings stored from now on carry <paramref name="CalibrationId"/> in <c>calibration_id</c>,
/// and the Calibration itself belongs to the Sensor grain. Setting the same or an older revision journals nothing.
/// </summary>
/// <param name="SensorId">The Sensor the Calibration belongs to.</param>
/// <param name="CalibrationId">The Calibration ID in force.</param>
/// <param name="Revision">The Sensor's Calibration count; the cache keeps the highest.</param>
/// <param name="SetAt">When the Device grain cached it.</param>
[EventType("device.calibration-set")]
[GenerateSerializer]
[Alias("coldframe.device-calibration-set")]
public sealed record DeviceCalibrationSet(
    [property: Id(0)] Guid SensorId,
    [property: Id(1)] Guid CalibrationId,
    [property: Id(2)] int Revision,
    [property: Id(3)] DateTimeOffset SetAt);

/// <summary>
/// The Server accepted a frame of the Node whose <c>measured_at</c> is newer than any it accepted before (Story
/// 7.1): the Node's liveness. A Node's liveness is the newest <c>measured_at</c>, never the arrival time, so a
/// backlog of old Readings does not make a silent Node look alive. Journaled once per such frame, so the Silence
/// Window's expiry is derived from the Device's own stream; a paused Node journals none.
/// </summary>
/// <param name="MeasuredAt">The frame's <c>measured_at</c>, rebased for a Node without synced time.</param>
/// <param name="ReportedAt">When the Server accepted the frame.</param>
[EventType("device.reported")]
[GenerateSerializer]
[Alias("coldframe.device-reported")]
public sealed record DeviceReported(
    [property: Id(0)] DateTimeOffset MeasuredAt,
    [property: Id(1)] DateTimeOffset ReportedAt);

/// <summary>
/// The Device's own Silence Window was set or cleared (Story 7.1). An override replaces the default of the
/// Device's kind; without one the default is in force again. The expiry moves with it and needs no other event.
/// </summary>
/// <param name="WindowSeconds">The override in whole seconds, or <see langword="null"/> when it was cleared.</param>
/// <param name="SetAt">When the window was set.</param>
[EventType("device.silence-window-set")]
[GenerateSerializer]
[Alias("coldframe.device-silence-window-set")]
public sealed record DeviceSilenceWindowSet(
    [property: Id(0)] long? WindowSeconds,
    [property: Id(1)] DateTimeOffset SetAt);

/// <summary>
/// Why a Device's silence clock was started again without a report of the Device (Story 7.1).
/// </summary>
[GenerateSerializer]
[Alias("coldframe.silence-clock-restart-cause")]
public enum SilenceClockRestartCause
{
    /// <summary>
    /// The Server started: while it was down it could accept nothing, and that is no silence of the Device.
    /// </summary>
    ServerStarted = 0,

    /// <summary>
    /// The Node's last relay Hub recovered from its own silence: until then the Node could not have reported.
    /// </summary>
    RelayHubRecovered = 1,
}

/// <summary>
/// The Device's silence clock was started again by something else than its own report (Story 7.1): the Silence
/// Window now counts from <paramref name="From"/>. Never journaled for an earlier time than the clock holds.
/// </summary>
/// <param name="From">The new clock start: the Server's start, or when the relay Hub recovered.</param>
/// <param name="Cause">Why the clock was restarted.</param>
/// <param name="HubId">The relay Hub that recovered; <see langword="null"/> for a Server start.</param>
/// <param name="RestartedAt">When the Device grain restarted the clock.</param>
[EventType("device.silence-clock-restarted")]
[GenerateSerializer]
[Alias("coldframe.device-silence-clock-restarted")]
public sealed record DeviceSilenceClockRestarted(
    [property: Id(0)] DateTimeOffset From,
    [property: Id(1)] SilenceClockRestartCause Cause,
    [property: Id(2)] string? HubId,
    [property: Id(3)] DateTimeOffset RestartedAt);

/// <summary>
/// The Device's Silence Window expired (Story 7.1): its next silence episode, journaled before its Health Alert
/// of kind <c>silent</c> is opened, so a retry opens the same Alert. A Device has at most one open at a time.
/// </summary>
/// <param name="Episode">The Device's silence episode, from 1.</param>
/// <param name="AlertId">The Alert ID: the UUIDv5 of <c>device:{deviceId}:silent:{episode}</c>.</param>
/// <param name="SiteId">The Site of the Device.</param>
/// <param name="LotId">The Lot of a Node when the episode started; <see langword="null"/> for a Hub or an unassigned Node.</param>
/// <param name="DueAt">The expiry that passed: the clock start plus the Silence Window.</param>
/// <param name="StartedAt">When the Device grain found the expiry overdue.</param>
/// <param name="SilentSince">
/// When the Device was last heard (Story 7.2): a Node's newest accepted <c>measured_at</c>, a Hub's last accepted
/// heartbeat, or the enrolment time of a Device that never reported. Not the clock start, which a Server start or
/// a Hub's recovery moves. <see langword="null"/> in an event journaled before that story.
/// </param>
[EventType("device.silence-started")]
[GenerateSerializer]
[Alias("coldframe.device-silence-started")]
public sealed record DeviceSilenceStarted(
    [property: Id(0)] int Episode,
    [property: Id(1)] Guid AlertId,
    [property: Id(2)] string SiteId,
    [property: Id(3)] string? LotId,
    [property: Id(4)] DateTimeOffset DueAt,
    [property: Id(5)] DateTimeOffset StartedAt,
    [property: Id(6)] DateTimeOffset? SilentSince = null);

/// <summary>
/// The Device's silence episode ended (Story 7.1): its Alert is closed with <paramref name="Reason"/>. The next
/// silence is another episode and another Alert.
/// </summary>
/// <param name="Episode">The episode that ended.</param>
/// <param name="AlertId">Its Alert.</param>
/// <param name="Reason">Why it ended; a Device that reports again ends it as <c>Recovered</c>.</param>
/// <param name="EndedAt">When the Device grain ended it: for a recovery, when the report was accepted.</param>
[EventType("device.silence-ended")]
[GenerateSerializer]
[Alias("coldframe.device-silence-ended")]
public sealed record DeviceSilenceEnded(
    [property: Id(0)] int Episode,
    [property: Id(1)] Guid AlertId,
    [property: Id(2)] AlertCloseReason Reason,
    [property: Id(3)] DateTimeOffset EndedAt);

/// <summary>
/// The Alert grain holds an open or a close of one of the Device's Alerts and its Site knows it (Story 7.1): the
/// Device grain stops delivering it.
/// </summary>
/// <param name="AlertId">The Alert.</param>
/// <param name="Change">
/// What was delivered: <see cref="AlertLifecycle.Open"/> the open,
/// <see cref="AlertLifecycle.Closed"/> the close.
/// </param>
/// <param name="DeliveredAt">When the Alert grain acknowledged it.</param>
[EventType("device.alert-delivered")]
[GenerateSerializer]
[Alias("coldframe.device-alert-delivered")]
public sealed record DeviceAlertDelivered(
    [property: Id(0)] Guid AlertId,
    [property: Id(1)] AlertLifecycle Change,
    [property: Id(2)] DateTimeOffset DeliveredAt);

/// <summary>
/// A device report of the Node was counted and moved a battery streak (Story 7.3): the count of consecutive
/// reports that are low (battery known and below 20 % while not charging) or recovered (charging, or battery
/// known and at 20 % or above). Never journaled for a report that leaves both streaks as they are, so a healthy
/// Node adds no event per frame.
/// </summary>
/// <param name="Low">The consecutive low reports so far; counted only while no battery Alert is open.</param>
/// <param name="Recovered">The consecutive recovered reports so far; counted only while a battery Alert is open.</param>
/// <param name="MeasuredAt">The <c>measured_at</c> stored with the report's key at its first delivery: the last report counted.</param>
[EventType("device.battery-streak-changed")]
[GenerateSerializer]
[Alias("coldframe.device-battery-streak-changed")]
public sealed record DeviceBatteryStreakChanged(
    [property: Id(0)] int Low,
    [property: Id(1)] int Recovered,
    [property: Id(2)] DateTimeOffset MeasuredAt);

/// <summary>
/// The Node's third consecutive low device report was counted (Story 7.3): its next low-battery episode,
/// journaled before its Health Alert of kind <c>battery</c> is opened, so a retry opens the same Alert. A Node has
/// at most one open at a time. Both streaks start over.
/// </summary>
/// <param name="Episode">The Node's low-battery episode, from 1.</param>
/// <param name="AlertId">The Alert ID: the UUIDv5 of <c>device:{deviceId}:battery:{episode}</c>.</param>
/// <param name="SiteId">The Site of the Node.</param>
/// <param name="LotId">The Lot of the Node when the episode started; <see langword="null"/> for an unassigned Node.</param>
/// <param name="BatteryPercent">The battery level of the report that started the episode.</param>
/// <param name="MeasuredAt">The stored <c>measured_at</c> of that report: the last report counted.</param>
/// <param name="StartedAt">When the Device grain counted it.</param>
[EventType("device.battery-low-started")]
[GenerateSerializer]
[Alias("coldframe.device-battery-low-started")]
public sealed record DeviceBatteryLowStarted(
    [property: Id(0)] int Episode,
    [property: Id(1)] Guid AlertId,
    [property: Id(2)] string SiteId,
    [property: Id(3)] string? LotId,
    [property: Id(4)] int BatteryPercent,
    [property: Id(5)] DateTimeOffset MeasuredAt,
    [property: Id(6)] DateTimeOffset StartedAt);

/// <summary>
/// The Node's low-battery episode ended (Story 7.3): its Alert is closed with <paramref name="Reason"/>. The next
/// one is another episode and another Alert. Both streaks start over.
/// </summary>
/// <param name="Episode">The episode that ended.</param>
/// <param name="AlertId">Its Alert.</param>
/// <param name="Reason">Why it ended; the third consecutive recovered report ends it as <c>Recovered</c>.</param>
/// <param name="MeasuredAt">The stored <c>measured_at</c> of the report that ended it; <see langword="null"/> when no report did.</param>
/// <param name="EndedAt">When the Device grain ended it.</param>
[EventType("device.battery-low-ended")]
[GenerateSerializer]
[Alias("coldframe.device-battery-low-ended")]
public sealed record DeviceBatteryLowEnded(
    [property: Id(0)] int Episode,
    [property: Id(1)] Guid AlertId,
    [property: Id(2)] AlertCloseReason Reason,
    [property: Id(3)] DateTimeOffset? MeasuredAt,
    [property: Id(4)] DateTimeOffset EndedAt);
