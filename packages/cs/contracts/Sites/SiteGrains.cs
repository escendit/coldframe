using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Lots;

namespace Coldframe.Contracts.Sites;

/// <summary>
/// A User, keyed by the OIDC <c>sub</c>. Creates Sites on the User's behalf, idempotently per key.
/// </summary>
[Alias("coldframe.user")]
public interface IUserGrain : IGrainWithStringKey
{
    /// <summary>
    /// Creates a Site named <paramref name="name"/> with the User as its Owner. A repeated call with the
    /// same <paramref name="idempotencyKey"/> within 24 h returns the original result.
    /// </summary>
    /// <param name="idempotencyKey">The request's <c>Idempotency-Key</c>, already validated.</param>
    /// <param name="name">The Site name, already trimmed and validated.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("create-site")]
    Task<SiteCreationResult> CreateSite(string idempotencyKey, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the User's Role on a Site as the Site grain reports it. Idempotent: journals only when the
    /// Role differs from the one the User grain holds.
    /// </summary>
    /// <param name="siteId">The Site ID.</param>
    /// <param name="role">The Role; <see langword="null"/> when the User is no member or the Site is deleted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("sync-site-membership")]
    Task SyncSiteMembership(string siteId, SiteRole? role, CancellationToken cancellationToken = default);
}

/// <summary>
/// A Site, keyed by its Site ID (the Phase Two Organization ID). The only writer of the Site's
/// Memberships and Roles, in its journal and in Phase Two.
/// </summary>
[Alias("coldframe.site")]
public interface ISiteGrain : IGrainWithStringKey
{
    /// <summary>
    /// Makes the Site <see cref="SiteLifecycle.Active"/> with <paramref name="ownerId"/> as its Owner, and
    /// brings the identity projection up to date before it returns. Idempotent for the same Owner.
    /// </summary>
    /// <param name="name">The Site name.</param>
    /// <param name="ownerId">The User ID of the Owner.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("initialize")]
    Task<SiteInitializationResult> Initialize(string name, string ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the Organization's current roster from Keycloak and journals only the differences to the
    /// Site's own state (AD-3, AD-20). It only reads Keycloak. An <see cref="SiteLifecycle.Active"/> Site
    /// keeps its Owners when Keycloak shows none, and becomes <see cref="SiteLifecycle.Deleted"/> when the
    /// Organization is gone. Any other Site ignores the call. Brings the identity projection up to date
    /// before it returns.
    /// </summary>
    /// <param name="expectation">What the Keycloak event says the roster now shows, or <see langword="null"/>.</param>
    /// <param name="acceptUnconfirmed">
    /// <see langword="true"/> applies the pulled roster even when it contradicts <paramref name="expectation"/>.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("reconcile")]
    Task<SiteReconciliationResult> Reconcile(
        RosterExpectation? expectation,
        bool acceptUnconfirmed,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames an <see cref="SiteLifecycle.Active"/> Site: sets the Organization's <c>displayName</c> in
    /// Keycloak, then journals the rename and brings the identity projection up to date (AD-3). The current
    /// name changes nothing and calls nothing.
    /// </summary>
    /// <param name="name">The Site name, already trimmed and validated.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("rename")]
    Task<SiteRenameResult> Rename(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a Lot on an <see cref="SiteLifecycle.Active"/> Site, idempotently per caller and key for
    /// 24 h: the request is journaled with its new Lot ID before the Lot grain is called, so a retry
    /// resumes with the same Lot.
    /// </summary>
    /// <param name="callerId">The caller's User ID (the OIDC <c>sub</c>).</param>
    /// <param name="idempotencyKey">The request's <c>Idempotency-Key</c>, already validated.</param>
    /// <param name="name">The Lot name, already trimmed and validated.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("create-lot")]
    Task<LotCreationResult> CreateLot(string callerId, string idempotencyKey, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a Device to the roster of an <see cref="SiteLifecycle.Active"/> Site (AD-18). Only the Device
    /// grain calls it, before it journals its enrolment. A key used within 24 h for another Device answers
    /// <see cref="DeviceRegistrationOutcome.IdempotencyKeyReused"/>; a Device already on the roster journals
    /// nothing. Nothing is persisted on a refusal. The reply carries the Site's Pause (AD-8).
    /// </summary>
    /// <param name="deviceId">The Device ID, 16 lowercase hex digits.</param>
    /// <param name="kind">Hub or Node.</param>
    /// <param name="idempotencyKey">
    /// The request's idempotency key, scoped to its caller: <c>{sub}:{Idempotency-Key}</c>, as for Lots.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("register-device")]
    Task<DeviceRegistrationResult> RegisterDevice(
        string deviceId,
        DeviceKind kind,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// How a Site creation ended.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.site-creation-outcome")]
public enum SiteCreationOutcome
{
    /// <summary>
    /// The Site exists; <see cref="SiteCreationResult.Site"/> describes it.
    /// </summary>
    Created = 0,

    /// <summary>
    /// The key was used for a request with a different name within 24 h. Nothing was created.
    /// </summary>
    IdempotencyKeyReused = 1,

    /// <summary>
    /// Keycloak could not be reached. The request stays pending; a retry with the same key resumes it.
    /// </summary>
    IdentityProviderUnavailable = 2,
}

/// <summary>
/// The result of <see cref="IUserGrain.CreateSite"/>.
/// </summary>
/// <param name="Outcome">How the creation ended.</param>
/// <param name="Site">The Site, when <paramref name="Outcome"/> is <see cref="SiteCreationOutcome.Created"/>.</param>
[GenerateSerializer]
[Alias("coldframe.site-creation-result")]
public sealed record SiteCreationResult([property: Id(0)] SiteCreationOutcome Outcome, [property: Id(1)] SiteSummary? Site = null);

/// <summary>
/// A Site as its caller sees it.
/// </summary>
/// <param name="Id">The Site ID.</param>
/// <param name="Name">The Site name.</param>
/// <param name="Role">The caller's Role on the Site.</param>
[GenerateSerializer]
[Alias("coldframe.site-summary")]
public sealed record SiteSummary([property: Id(0)] string Id, [property: Id(1)] string Name, [property: Id(2)] SiteRole Role);

/// <summary>
/// How a Site initialization ended.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.site-initialization-outcome")]
public enum SiteInitializationOutcome
{
    /// <summary>
    /// The Site is active with the requested Owner, now or already.
    /// </summary>
    Initialized = 0,

    /// <summary>
    /// Keycloak could not be reached. Nothing was journaled; calling again resumes.
    /// </summary>
    IdentityProviderUnavailable = 1,

    /// <summary>
    /// The Site is already active with another Owner, or deleted. Nothing changed.
    /// </summary>
    Conflict = 2,
}

/// <summary>
/// The result of <see cref="ISiteGrain.Initialize"/>.
/// </summary>
/// <param name="Outcome">How the initialization ended.</param>
/// <param name="Name">The Site name, when <paramref name="Outcome"/> is <see cref="SiteInitializationOutcome.Initialized"/>.</param>
[GenerateSerializer]
[Alias("coldframe.site-initialization-result")]
public sealed record SiteInitializationResult([property: Id(0)] SiteInitializationOutcome Outcome, [property: Id(1)] string? Name = null);

/// <summary>
/// How a Site reconciliation ended.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.site-reconciliation-outcome")]
public enum SiteReconciliationOutcome
{
    /// <summary>
    /// The Site is not <see cref="SiteLifecycle.Active"/>. Keycloak was not called; nothing changed.
    /// </summary>
    Ignored = 0,

    /// <summary>
    /// The Site already matches Keycloak. Nothing was journaled.
    /// </summary>
    Unchanged = 1,

    /// <summary>
    /// The differences were journaled.
    /// </summary>
    Changed = 2,

    /// <summary>
    /// Keycloak does not show yet what the event says. Nothing was journaled; calling again later resumes.
    /// </summary>
    NotYetVisible = 3,

    /// <summary>
    /// Keycloak could not be reached. Nothing was journaled; calling again resumes.
    /// </summary>
    IdentityProviderUnavailable = 4,
}

/// <summary>
/// The result of <see cref="ISiteGrain.Reconcile"/>: the outcome and the Site's state afterwards.
/// </summary>
/// <param name="Outcome">How the reconciliation ended.</param>
/// <param name="Lifecycle">The Site's lifecycle.</param>
/// <param name="Members">Each member's Role, by User ID.</param>
/// <param name="FormerMembers">The User IDs that held a Role on the Site and hold none now.</param>
[GenerateSerializer]
[Alias("coldframe.site-reconciliation-result")]
public sealed record SiteReconciliationResult(
    [property: Id(0)] SiteReconciliationOutcome Outcome,
    [property: Id(1)] SiteLifecycle Lifecycle,
    [property: Id(2)] IReadOnlyDictionary<string, SiteRole> Members,
    [property: Id(3)] IReadOnlyList<string> FormerMembers);

/// <summary>
/// What a Keycloak event says about an Organization.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.roster-expectation-kind")]
public enum RosterExpectationKind
{
    /// <summary>
    /// The Organization no longer exists.
    /// </summary>
    OrganizationAbsent = 0,

    /// <summary>
    /// <see cref="RosterExpectation.UserId"/> is a member.
    /// </summary>
    MemberPresent = 1,

    /// <summary>
    /// <see cref="RosterExpectation.UserId"/> is no member.
    /// </summary>
    MemberAbsent = 2,

    /// <summary>
    /// <see cref="RosterExpectation.UserId"/> holds the Organization role of <see cref="RosterExpectation.Role"/>.
    /// </summary>
    RoleHeld = 3,

    /// <summary>
    /// <see cref="RosterExpectation.UserId"/> does not hold the Organization role of <see cref="RosterExpectation.Role"/>.
    /// </summary>
    RoleNotHeld = 4,
}

/// <summary>
/// What a Keycloak event says the Organization's roster now shows. It only tells a reconciliation whether
/// its pull ran too early; the pulled roster is the truth.
/// </summary>
/// <param name="Kind">What is expected.</param>
/// <param name="UserId">The User the expectation is about, if any.</param>
/// <param name="Role">The Role whose Organization role the expectation is about, if any.</param>
[GenerateSerializer]
[Alias("coldframe.roster-expectation")]
public sealed record RosterExpectation(
    [property: Id(0)] RosterExpectationKind Kind,
    [property: Id(1)] string? UserId = null,
    [property: Id(2)] SiteRole? Role = null);

/// <summary>
/// How a Site rename ended.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.site-rename-outcome")]
public enum SiteRenameOutcome
{
    /// <summary>
    /// The Site has the new name, in Keycloak and in its journal.
    /// </summary>
    Renamed = 0,

    /// <summary>
    /// The Site already had the name. Keycloak was not called; nothing was journaled.
    /// </summary>
    Unchanged = 1,

    /// <summary>
    /// The Site is not <see cref="SiteLifecycle.Active"/>. Nothing changed.
    /// </summary>
    NotFound = 2,

    /// <summary>
    /// Keycloak could not be reached. Nothing was journaled; the Site keeps its name.
    /// </summary>
    IdentityProviderUnavailable = 3,
}

/// <summary>
/// The result of <see cref="ISiteGrain.Rename"/>.
/// </summary>
/// <param name="Outcome">How the rename ended.</param>
/// <param name="Name">The Site name afterwards, unless <paramref name="Outcome"/> is <see cref="SiteRenameOutcome.NotFound"/>.</param>
[GenerateSerializer]
[Alias("coldframe.site-rename-result")]
public sealed record SiteRenameResult([property: Id(0)] SiteRenameOutcome Outcome, [property: Id(1)] string? Name = null);

/// <summary>
/// How a Lot creation ended.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.lot-creation-outcome")]
public enum LotCreationOutcome
{
    /// <summary>
    /// The Lot exists; <see cref="LotCreationResult.Lot"/> describes it.
    /// </summary>
    Created = 0,

    /// <summary>
    /// The key was used by the same caller for a different name within 24 h. Nothing was created.
    /// </summary>
    IdempotencyKeyReused = 1,

    /// <summary>
    /// The Site is not <see cref="SiteLifecycle.Active"/>. Nothing was created.
    /// </summary>
    NotFound = 2,
}

/// <summary>
/// The result of <see cref="ISiteGrain.CreateLot"/>.
/// </summary>
/// <param name="Outcome">How the creation ended.</param>
/// <param name="Lot">The Lot, when <paramref name="Outcome"/> is <see cref="LotCreationOutcome.Created"/>.</param>
[GenerateSerializer]
[Alias("coldframe.lot-creation-result")]
public sealed record LotCreationResult([property: Id(0)] LotCreationOutcome Outcome, [property: Id(1)] LotSummary? Lot = null);

/// <summary>
/// How a Device registration ended.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.device-registration-outcome")]
public enum DeviceRegistrationOutcome
{
    /// <summary>
    /// The Device is on the Site's roster, now or already.
    /// </summary>
    Registered = 0,

    /// <summary>
    /// The Site is not <see cref="SiteLifecycle.Active"/>. Nothing was journaled.
    /// </summary>
    NotFound = 1,

    /// <summary>
    /// The caller used the key within 24 h for another Device. Nothing was journaled.
    /// </summary>
    IdempotencyKeyReused = 2,
}

/// <summary>
/// The Site's Pause as a joining Device receives it (AD-8). Site Pause arrives in Epic 8; until then a Site
/// is never paused.
/// </summary>
/// <param name="Paused">Whether the Site is paused.</param>
/// <param name="EndsAt">When the Pause ends, if it has an end.</param>
[GenerateSerializer]
[Alias("coldframe.site-pause")]
public sealed record SitePause([property: Id(0)] bool Paused, [property: Id(1)] DateTimeOffset? EndsAt = null)
{
    /// <summary>
    /// A Site that is not paused.
    /// </summary>
    public static SitePause NotPaused { get; } = new(false);
}

/// <summary>
/// The result of <see cref="ISiteGrain.RegisterDevice"/>.
/// </summary>
/// <param name="Outcome">How the registration ended.</param>
/// <param name="Pause">The Site's Pause, when <paramref name="Outcome"/> is <see cref="DeviceRegistrationOutcome.Registered"/>.</param>
[GenerateSerializer]
[Alias("coldframe.device-registration-result")]
public sealed record DeviceRegistrationResult(
    [property: Id(0)] DeviceRegistrationOutcome Outcome,
    [property: Id(1)] SitePause? Pause = null);
