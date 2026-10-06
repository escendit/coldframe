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
