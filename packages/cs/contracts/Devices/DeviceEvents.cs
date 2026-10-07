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
