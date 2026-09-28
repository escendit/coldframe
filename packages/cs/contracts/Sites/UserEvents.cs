using Coldframe.Contracts.Events;

namespace Coldframe.Contracts.Sites;

/// <summary>
/// The User asked to create a Site. Persisted before any call to Keycloak, so a retry with the same
/// idempotency key resumes with the same Site ID.
/// </summary>
/// <param name="IdempotencyKey">The <c>Idempotency-Key</c> of the request.</param>
/// <param name="SiteId">The Site ID (the Phase Two Organization ID) chosen for the request, a UUIDv7.</param>
/// <param name="Name">The requested Site name.</param>
/// <param name="RequestedAt">When the request was received; the key expires 24 h later.</param>
[EventType("user.site-creation-requested")]
[GenerateSerializer]
[Alias("coldframe.user-site-creation-requested")]
public sealed record SiteCreationRequested(
    [property: Id(0)] string IdempotencyKey,
    [property: Id(1)] string SiteId,
    [property: Id(2)] string Name,
    [property: Id(3)] DateTimeOffset RequestedAt);

/// <summary>
/// The Site requested under <paramref name="IdempotencyKey"/> exists and the User is its Owner.
/// </summary>
/// <param name="IdempotencyKey">The <c>Idempotency-Key</c> of the request.</param>
/// <param name="SiteId">The Site ID.</param>
[EventType("user.site-creation-completed")]
[GenerateSerializer]
[Alias("coldframe.user-site-creation-completed")]
public sealed record SiteCreationCompleted([property: Id(0)] string IdempotencyKey, [property: Id(1)] string SiteId);
