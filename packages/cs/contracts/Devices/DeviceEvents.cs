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
