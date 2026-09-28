namespace Coldframe.Server.Identity;

/// <summary>
/// An Organization as the Phase Two API returns it.
/// </summary>
/// <param name="Id">The Organization ID, which is the Site ID.</param>
/// <param name="Name">The unique name, which is also the Site ID.</param>
/// <param name="DisplayName">The Site name.</param>
/// <param name="Attributes">The attributes, each with its values.</param>
public sealed record PhaseTwoOrganization(
    string Id,
    string Name,
    string? DisplayName,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Attributes);

/// <summary>
/// An Organization's roster as Phase Two shows it right now.
/// </summary>
/// <param name="DisplayName">The Site name.</param>
/// <param name="MemberIds">The User IDs of the members.</param>
/// <param name="RoleHolders">
/// For each Organization role of <see cref="SiteGrain.OrganizationRoles"/>, the User IDs that hold it.
/// A missing role has no holders.
/// </param>
public sealed record PhaseTwoRoster(
    string? DisplayName,
    IReadOnlySet<string> MemberIds,
    IReadOnlyDictionary<string, IReadOnlySet<string>> RoleHolders)
{
    /// <summary>
    /// Whether <paramref name="userId"/> holds the Organization role <paramref name="role"/>.
    /// </summary>
    public bool Holds(string role, string userId) =>
        RoleHolders.TryGetValue(role, out var holders) && holders.Contains(userId);
}

/// <summary>
/// The Phase Two Organizations API (<c>{keycloak}/realms/{realm}/orgs</c>), as far as Coldframe uses it.
/// </summary>
/// <remarks>
/// Every method throws <see cref="IdentityProviderUnavailableException"/> when Keycloak cannot be reached,
/// times out, or answers with a server error, and <see cref="InvalidOperationException"/> on any other
/// unexpected answer. Only the User grain creates Organizations; only the Site grain writes Memberships,
/// Roles and the display name (AD-1).
/// </remarks>
public interface IPhaseTwoOrganizations
{
    /// <summary>
    /// The attribute that tags an Organization with the request that created it: <c>"{sub}:{key}"</c> (AD-3).
    /// </summary>
    public const string IdempotencyKeyAttribute = "coldframe.idempotencyKey";

    /// <summary>
    /// Returns the Organizations whose attribute <paramref name="attribute"/> is exactly <paramref name="value"/>.
    /// </summary>
    Task<IReadOnlyList<PhaseTwoOrganization>> FindByAttributeAsync(string attribute, string value, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the Organization, or <see langword="null"/> when there is none with that ID.
    /// </summary>
    Task<PhaseTwoOrganization?> GetAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// Creates an Organization with the given ID.
    /// </summary>
    /// <returns><see langword="true"/> when created; <see langword="false"/> when the ID or name exists already (409).</returns>
    Task<bool> CreateAsync(PhaseTwoOrganization organization, CancellationToken cancellationToken);

    /// <summary>
    /// Creates an Organization role unless it exists.
    /// </summary>
    Task EnsureRoleAsync(string organizationId, string role, CancellationToken cancellationToken);

    /// <summary>
    /// Makes a User a member of the Organization. Idempotent.
    /// </summary>
    Task AddMemberAsync(string organizationId, string userId, CancellationToken cancellationToken);

    /// <summary>
    /// Grants a member an Organization role. Idempotent; the User must be a member already.
    /// </summary>
    Task GrantRoleAsync(string organizationId, string role, string userId, CancellationToken cancellationToken);

    /// <summary>
    /// Sets the Organization's display name, which is the Site name, and keeps everything else. Only the
    /// Site grain calls it.
    /// </summary>
    Task UpdateDisplayNameAsync(string organizationId, string displayName, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the Organization's display name, members and the holders of the roles in
    /// <see cref="SiteGrain.OrganizationRoles"/>, page by page. Only reads.
    /// </summary>
    /// <returns>The roster, or <see langword="null"/> when there is no Organization with that ID.</returns>
    Task<PhaseTwoRoster?> GetRosterAsync(string organizationId, CancellationToken cancellationToken);
}
