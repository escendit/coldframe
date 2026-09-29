namespace Coldframe.Contracts.Devices;

/// <summary>
/// A Device, keyed by its Device ID (16 lowercase hex digits). The only writer of the Device's state and
/// the owner of its <c>siteId</c> (AD-1, AD-18).
/// </summary>
[Alias("coldframe.device")]
public interface IDeviceGrain : IGrainWithStringKey
{
    /// <summary>
    /// Enrols the Device on a Site. A Device enrolled on another Site is refused first. Then the Site grain
    /// registers the Device (<c>ISiteGrain.RegisterDevice</c>), and only after it has, the Device journals
    /// <see cref="DeviceEnrolled"/>. Enrolling again on the same Site journals nothing and answers the same.
    /// Nothing is persisted on a refusal.
    /// </summary>
    /// <param name="request">What to enrol, with the wrapped key only.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("enrol")]
    Task<DeviceEnrolmentResult> Enrol(EnrolDevice request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies a Hub's signed heartbeat (AD-12, FR-13) and, when it holds, journals <see cref="DeviceSeen"/>.
    /// The grain verifies inside itself, so the plaintext <c>K_dev</c> never crosses a grain boundary: it
    /// unwraps <c>K_dev</c>, derives the <c>hub-auth/v1</c> key, checks the signature and zeroes both. It
    /// refuses an unenrolled Device, a bad signature, a timestamp more than
    /// <c>HeartbeatMaxSkewMs</c> off its clock, a nonce it has seen, and a timestamp not above the last
    /// one it accepted. Nothing is journaled on a refusal.
    /// </summary>
    /// <param name="request">The request as the Hub signed it.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("heartbeat")]
    Task<DeviceHeartbeatResult> Heartbeat(DeviceHeartbeat request, CancellationToken cancellationToken = default);
}

/// <summary>
/// A Hub's heartbeat as the Edge API hands it to the Device grain: the parts of the signed request, parsed
/// but not yet verified.
/// </summary>
/// <param name="Method">The HTTP method, uppercase.</param>
/// <param name="Path">The request path, exactly as sent.</param>
/// <param name="Body">The exact body bytes (at most 4 KiB).</param>
/// <param name="TimestampMs">The <c>X-Coldframe-Timestamp</c>, Unix milliseconds.</param>
/// <param name="Nonce">The <c>X-Coldframe-Nonce</c>, 16 bytes.</param>
/// <param name="Signature">The <c>X-Coldframe-Signature</c>, 32 bytes.</param>
/// <param name="UptimeMs">The body's <c>uptimeMs</c>, when sent.</param>
[GenerateSerializer]
[Alias("coldframe.device-heartbeat")]
public sealed record DeviceHeartbeat(
    [property: Id(0)] string Method,
    [property: Id(1)] string Path,
    [property: Id(2)] byte[] Body,
    [property: Id(3)] long TimestampMs,
    [property: Id(4)] byte[] Nonce,
    [property: Id(5)] byte[] Signature,
    [property: Id(6)] long? UptimeMs);

/// <summary>
/// How a heartbeat ended.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.device-heartbeat-outcome")]
public enum DeviceHeartbeatOutcome
{
    /// <summary>
    /// The heartbeat is authentic and new; <see cref="DeviceSeen"/> was journaled.
    /// </summary>
    Accepted = 0,

    /// <summary>
    /// The Device authentication failed: unknown Device, bad signature, skew, a replayed nonce or an old
    /// timestamp. Nothing was journaled; the reason is not told to the caller.
    /// </summary>
    Unauthorized = 1,
}

/// <summary>
/// The result of <see cref="IDeviceGrain.Heartbeat"/>.
/// </summary>
/// <param name="Outcome">How the heartbeat ended.</param>
[GenerateSerializer]
[Alias("coldframe.device-heartbeat-result")]
public sealed record DeviceHeartbeatResult([property: Id(0)] DeviceHeartbeatOutcome Outcome);

/// <summary>
/// An enrolment request as the Edge API hands it to the Device grain, after it opened and wrapped <c>K_dev</c>.
/// </summary>
/// <param name="SiteId">The canonical Site ID.</param>
/// <param name="Kind">Hub or Node.</param>
/// <param name="WrappedKey">The Device's <c>K_dev</c>, wrapped under the key-encryption key.</param>
/// <param name="CallerId">The caller's User ID (the OIDC <c>sub</c>).</param>
/// <param name="IdempotencyKey">The request's <c>Idempotency-Key</c>, already validated.</param>
[GenerateSerializer]
[Alias("coldframe.enrol-device")]
public sealed record EnrolDevice(
    [property: Id(0)] string SiteId,
    [property: Id(1)] DeviceKind Kind,
    [property: Id(2)] WrappedDeviceKey WrappedKey,
    [property: Id(3)] string CallerId,
    [property: Id(4)] string IdempotencyKey);

/// <summary>
/// How an enrolment ended.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.device-enrolment-outcome")]
public enum DeviceEnrolmentOutcome
{
    /// <summary>
    /// The Device is enrolled on the Site, now or already; <see cref="DeviceEnrolmentResult.Device"/> describes it.
    /// </summary>
    Enrolled = 0,

    /// <summary>
    /// The Site is not active. Nothing was persisted.
    /// </summary>
    SiteNotFound = 1,

    /// <summary>
    /// The Device is enrolled on another Site. Nothing was persisted.
    /// </summary>
    OnAnotherSite = 2,

    /// <summary>
    /// The caller used the key within 24 h to enrol another Device on the Site. Nothing was persisted.
    /// </summary>
    IdempotencyKeyReused = 3,
}

/// <summary>
/// A Device as the grain holds it, without its key.
/// </summary>
/// <param name="Id">The Device ID.</param>
/// <param name="Kind">Hub or Node.</param>
/// <param name="SiteId">The Site the Device is enrolled on.</param>
[GenerateSerializer]
[Alias("coldframe.device-summary")]
public sealed record DeviceSummary(
    [property: Id(0)] string Id,
    [property: Id(1)] DeviceKind Kind,
    [property: Id(2)] string SiteId);

/// <summary>
/// The result of <see cref="IDeviceGrain.Enrol"/>.
/// </summary>
/// <param name="Outcome">How the enrolment ended.</param>
/// <param name="Device">The Device, when <paramref name="Outcome"/> is <see cref="DeviceEnrolmentOutcome.Enrolled"/>.</param>
[GenerateSerializer]
[Alias("coldframe.device-enrolment-result")]
public sealed record DeviceEnrolmentResult(
    [property: Id(0)] DeviceEnrolmentOutcome Outcome,
    [property: Id(1)] DeviceSummary? Device = null);
