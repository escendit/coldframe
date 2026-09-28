using Coldframe.Contracts.Sites;

namespace Coldframe.Server.Identity.Reconciliation;

/// <summary>
/// Where a Keycloak admin event goes: the Site whose roster it touches and what the roster should now show.
/// </summary>
/// <param name="SiteId">The Site ID, which is the Organization ID.</param>
/// <param name="Expectation">What the event says the roster now shows, or <see langword="null"/>.</param>
public sealed record AdminEventRoute(string SiteId, RosterExpectation? Expectation)
{
    /// <summary>
    /// Phase Two's resource type of Organizations.
    /// </summary>
    public const string OrganizationResource = "ORGANIZATION";

    /// <summary>
    /// Phase Two's resource type of Organization memberships.
    /// </summary>
    public const string MembershipResource = "ORGANIZATION_MEMBERSHIP";

    /// <summary>
    /// Phase Two's resource type of Organization role grants.
    /// </summary>
    public const string RoleMappingResource = "ORGANIZATION_ROLE_MAPPING";

    private const string Create = "CREATE";
    private const string Delete = "DELETE";

    /// <summary>
    /// Routes an admin event. Pure.
    /// </summary>
    /// <remarks>
    /// Paths are relative to <c>/realms/{realm}/</c>:
    /// <list type="bullet">
    /// <item><c>ORGANIZATION</c> <c>orgs/{org}</c> (create, delete) and <c>orgs/{org}/{org}</c> (update);</item>
    /// <item><c>ORGANIZATION_MEMBERSHIP</c> <c>orgs/{org}/members/{user}</c>;</item>
    /// <item><c>ORGANIZATION_ROLE_MAPPING</c> <c>orgs/{org}/roles/{role}/users/{user}</c>, or the bulk
    /// <c>users/{user}/orgs/{org}/roles/…</c>, which gives no expectation.</item>
    /// </list>
    /// </remarks>
    /// <param name="adminEvent">The admin event.</param>
    /// <param name="realmId">The ID of the Coldframe realm.</param>
    /// <returns>
    /// The route, or <see langword="null"/> when the event is ignored: another realm, a failed operation, a
    /// resource type that does not affect the roster, a path that cannot be parsed, or a role other than
    /// <c>owner</c>, <c>administrator</c> and <c>member</c>.
    /// </returns>
    public static AdminEventRoute? From(KeycloakAdminEvent adminEvent, string realmId)
    {
        ArgumentNullException.ThrowIfNull(adminEvent);
        ArgumentException.ThrowIfNullOrWhiteSpace(realmId);

        if (!string.Equals(adminEvent.RealmId, realmId, StringComparison.Ordinal)
            || adminEvent.Error is not null
            || Segments(adminEvent.ResourcePath) is not { } path)
        {
            return null;
        }

        var operation = adminEvent.OperationType;

        return adminEvent.ResourceType switch
        {
            OrganizationResource => FromOrganization(path, operation),
            MembershipResource => FromMembership(path, operation),
            RoleMappingResource => FromRoleMapping(path, operation),
            _ => null,
        };
    }

    private static AdminEventRoute? FromOrganization(string[] path, string? operation) =>
        path switch
        {
            ["orgs", var org] => new AdminEventRoute(
                org,
                operation == Delete ? new RosterExpectation(RosterExpectationKind.OrganizationAbsent) : null),
            ["orgs", var org, _] => new AdminEventRoute(org, null),
            _ => null,
        };

    private static AdminEventRoute? FromMembership(string[] path, string? operation) =>
        path switch
        {
            ["orgs", var org, "members", var user] => new AdminEventRoute(
                org,
                operation switch
                {
                    Create => new RosterExpectation(RosterExpectationKind.MemberPresent, user),
                    Delete => new RosterExpectation(RosterExpectationKind.MemberAbsent, user),
                    _ => null,
                }),
            _ => null,
        };

    private static AdminEventRoute? FromRoleMapping(string[] path, string? operation)
    {
        switch (path)
        {
            case ["orgs", var org, "roles", var organizationRole, "users", var user]:
                if (RoleOf(organizationRole) is not { } role)
                {
                    return null;
                }

                return new AdminEventRoute(
                    org,
                    operation switch
                    {
                        Create => new RosterExpectation(RosterExpectationKind.RoleHeld, user, role),
                        Delete => new RosterExpectation(RosterExpectationKind.RoleNotHeld, user, role),
                        _ => null,
                    });
            case ["users", _, "orgs", var org, "roles", ..]:
                return new AdminEventRoute(org, null);
            default:
                return null;
        }
    }

    private static SiteRole? RoleOf(string organizationRole)
    {
        foreach (var (role, name) in SiteGrain.OrganizationRoles)
        {
            if (string.Equals(name, organizationRole, StringComparison.Ordinal))
            {
                return role;
            }
        }

        return null;
    }

    // Splits a resource path into unescaped segments; null when it is empty or has an empty segment.
    private static string[]? Segments(string? resourcePath)
    {
        if (string.IsNullOrWhiteSpace(resourcePath))
        {
            return null;
        }

        var segments = resourcePath.TrimStart('/').Split('/');

        if (segments.Any(string.IsNullOrWhiteSpace))
        {
            return null;
        }

        return [.. segments.Select(Uri.UnescapeDataString)];
    }
}
