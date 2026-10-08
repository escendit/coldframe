using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Events;

namespace Coldframe.Contracts.Sites;

/// <summary>
/// The Site was created by <paramref name="CreatedBy"/>, a User ID (the OIDC <c>sub</c>).
/// </summary>
/// <param name="Name">The Site name.</param>
/// <param name="CreatedBy">The User who created the Site.</param>
[EventType("site.created")]
[GenerateSerializer]
[Alias("coldframe.site-created")]
public sealed record SiteCreated([property: Id(0)] string Name, [property: Id(1)] string CreatedBy);

/// <summary>
/// A User was given a Role on the Site.
/// </summary>
/// <param name="UserId">The User ID (the OIDC <c>sub</c>).</param>
/// <param name="Role">The Role the User holds from now on.</param>
[EventType("site.membership-granted")]
[GenerateSerializer]
[Alias("coldframe.site-membership-granted")]
public sealed record MembershipGranted([property: Id(0)] string UserId, [property: Id(1)] SiteRole Role);

/// <summary>
/// A User no longer holds any Role on the Site. Keycloak no longer lists the User as a member with one
/// of the Organization roles <c>owner</c>, <c>administrator</c> or <c>member</c>.
/// </summary>
/// <param name="UserId">The User ID (the OIDC <c>sub</c>).</param>
[EventType("site.membership-revoked")]
[GenerateSerializer]
[Alias("coldframe.site-membership-revoked")]
public sealed record MembershipRevoked([property: Id(0)] string UserId);

/// <summary>
/// The Site was renamed, in Keycloak (the Organization's <c>displayName</c>).
/// </summary>
/// <param name="Name">The Site name from now on.</param>
[EventType("site.renamed")]
[GenerateSerializer]
[Alias("coldframe.site-renamed")]
public sealed record SiteRenamed([property: Id(0)] string Name);

/// <summary>
/// The Site was deleted: its Organization is gone from Keycloak. Deletion is terminal; the Site's
/// stream, read-model rows and Members stay.
/// </summary>
[EventType("site.deleted")]
[GenerateSerializer]
[Alias("coldframe.site-deleted")]
public sealed record SiteDeleted;

/// <summary>
/// Keycloak shows the Site without an Owner (a break-glass edit). The Site keeps its previous Owners and
/// raises an operator-visible error; nothing is written back to Keycloak. Journaled once per episode.
/// </summary>
/// <param name="KeptOwners">The User IDs that keep the Role Owner.</param>
[EventType("site.ownerless-edit-refused")]
[GenerateSerializer]
[Alias("coldframe.site-ownerless-edit-refused")]
public sealed record SiteOwnerlessEditRefused([property: Id(0)] IReadOnlyList<string> KeptOwners);

/// <summary>
/// Keycloak shows an Owner again, which ends the episode that <see cref="SiteOwnerlessEditRefused"/> began.
/// </summary>
[EventType("site.ownerless-edit-resolved")]
[GenerateSerializer]
[Alias("coldframe.site-ownerless-edit-resolved")]
public sealed record SiteOwnerlessEditResolved;

/// <summary>
/// A caller asked the Site to create a Lot. Persisted before the Lot grain is called, so a retry with the
/// same key resumes with the same Lot ID.
/// </summary>
/// <param name="Key">The idempotency key, scoped to its caller: <c>{sub}:{Idempotency-Key}</c>.</param>
/// <param name="LotId">The Lot ID chosen for the request, a UUIDv7.</param>
/// <param name="Name">The requested Lot name.</param>
/// <param name="RequestedAt">When the request was received; the key expires 24 h later once completed.</param>
[EventType("site.lot-creation-requested")]
[GenerateSerializer]
[Alias("coldframe.site-lot-creation-requested")]
public sealed record LotCreationRequested(
    [property: Id(0)] string Key,
    [property: Id(1)] string LotId,
    [property: Id(2)] string Name,
    [property: Id(3)] DateTimeOffset RequestedAt);

/// <summary>
/// The Lot requested under <paramref name="Key"/> exists.
/// </summary>
/// <param name="Key">The idempotency key, scoped to its caller: <c>{sub}:{Idempotency-Key}</c>.</param>
[EventType("site.lot-creation-completed")]
[GenerateSerializer]
[Alias("coldframe.site-lot-creation-completed")]
public sealed record LotCreationCompleted([property: Id(0)] string Key);

/// <summary>
/// A Device joined the Site's roster (AD-18). Journaled once per Device, before the Device journals its
/// enrolment.
/// </summary>
/// <param name="DeviceId">The Device ID, 16 lowercase hex digits.</param>
/// <param name="Kind">Hub or Node.</param>
/// <param name="IdempotencyKey">The idempotency key, scoped to its caller: <c>{sub}:{Idempotency-Key}</c>.</param>
/// <param name="RegisteredAt">When the Device was registered; the key expires 24 h later.</param>
[EventType("site.device-registered")]
[GenerateSerializer]
[Alias("coldframe.site-device-registered")]
public sealed record DeviceRegistered(
    [property: Id(0)] string DeviceId,
    [property: Id(1)] DeviceKind Kind,
    [property: Id(2)] string IdempotencyKey,
    [property: Id(3)] DateTimeOffset RegisteredAt);

/// <summary>
/// An Alert opened on the Site (Story 6.1): the Alert grain reported it, and the Site lists it among its open
/// Alerts until <see cref="SiteAlertClosed"/>. Journaled once per Alert.
/// </summary>
/// <param name="Alert">The Alert as the Site lists it.</param>
[EventType("site.alert-opened")]
[GenerateSerializer]
[Alias("coldframe.site-alert-opened")]
public sealed record SiteAlertOpened([property: Id(0)] SiteAlert Alert);

/// <summary>
/// An Alert the Site listed closed (Story 6.1): it leaves the Site's open Alerts.
/// </summary>
/// <param name="AlertId">The Alert that closed.</param>
/// <param name="Reason">Why it closed.</param>
/// <param name="ClosedAt">When it closed.</param>
[EventType("site.alert-closed")]
[GenerateSerializer]
[Alias("coldframe.site-alert-closed")]
public sealed record SiteAlertClosed(
    [property: Id(0)] Guid AlertId,
    [property: Id(1)] AlertCloseReason Reason,
    [property: Id(2)] DateTimeOffset ClosedAt);
