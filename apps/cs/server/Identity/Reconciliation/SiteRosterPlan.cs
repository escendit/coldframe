using Coldframe.Contracts.Sites;

namespace Coldframe.Server.Identity.Reconciliation;

/// <summary>
/// The differences between a Site's Memberships and the roster Keycloak shows (AD-3).
/// </summary>
/// <param name="Grants">The Memberships to grant or change, ordered by User ID.</param>
/// <param name="Revocations">The Memberships to revoke, ordered by User ID.</param>
/// <param name="Ownerless">
/// Whether Keycloak shows no Owner. The current Owners then keep <see cref="SiteRole.Owner"/>.
/// </param>
/// <param name="KeptOwners">The Owners kept despite Keycloak, ordered; empty unless <paramref name="Ownerless"/>.</param>
public sealed record SiteRosterPlan(
    IReadOnlyList<MembershipGranted> Grants,
    IReadOnlyList<MembershipRevoked> Revocations,
    bool Ownerless,
    IReadOnlyList<string> KeptOwners)
{
    /// <summary>
    /// Whether the plan changes any Membership.
    /// </summary>
    public bool ChangesMemberships => Grants.Count > 0 || Revocations.Count > 0;

    /// <summary>
    /// Plans the Membership changes that make a Site match <paramref name="pulled"/>. Pure.
    /// </summary>
    /// <remarks>
    /// A member's Role is the highest of the Organization roles <c>owner</c>, <c>administrator</c> and
    /// <c>member</c> it holds; a member holding none has no Membership, and a role held by a non-member
    /// counts for nothing. When the roster has no Owner, every current Owner keeps Owner and all other
    /// differences still apply.
    /// </remarks>
    /// <param name="currentMembers">The Site's Memberships.</param>
    /// <param name="currentOwners">The Site's Owners.</param>
    /// <param name="pulled">The roster Keycloak shows.</param>
    public static SiteRosterPlan Plan(
        IReadOnlyDictionary<string, SiteRole> currentMembers,
        IReadOnlySet<string> currentOwners,
        PhaseTwoRoster pulled)
    {
        ArgumentNullException.ThrowIfNull(currentMembers);
        ArgumentNullException.ThrowIfNull(currentOwners);
        ArgumentNullException.ThrowIfNull(pulled);

        var desired = new Dictionary<string, SiteRole>(StringComparer.Ordinal);

        foreach (var memberId in pulled.MemberIds)
        {
            if (HighestRole(pulled, memberId) is { } role)
            {
                desired[memberId] = role;
            }
        }

        var ownerless = !desired.ContainsValue(SiteRole.Owner);
        IReadOnlyList<string> keptOwners = [];

        if (ownerless)
        {
            keptOwners = [.. currentOwners.Order(StringComparer.Ordinal)];

            foreach (var owner in keptOwners)
            {
                desired[owner] = SiteRole.Owner;
            }
        }

        var grants = desired
            .Where(pair => !currentMembers.TryGetValue(pair.Key, out var current) || current != pair.Value)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new MembershipGranted(pair.Key, pair.Value))
            .ToList();

        var revocations = currentMembers.Keys
            .Where(userId => !desired.ContainsKey(userId))
            .Order(StringComparer.Ordinal)
            .Select(userId => new MembershipRevoked(userId))
            .ToList();

        return new SiteRosterPlan(grants, revocations, ownerless, keptOwners);
    }

    private static SiteRole? HighestRole(PhaseTwoRoster roster, string userId)
    {
        SiteRole? highest = null;

        foreach (var (role, organizationRole) in SiteGrain.OrganizationRoles)
        {
            if (roster.Holds(organizationRole, userId) && (highest is null || role > highest))
            {
                highest = role;
            }
        }

        return highest;
    }
}
