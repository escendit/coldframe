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
