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
}

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
