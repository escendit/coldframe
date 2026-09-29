using Coldframe.Contracts.Sites;
using Coldframe.Server.Identity.Reconciliation;

namespace Coldframe.Server.Tests.Identity.Reconciliation;

/// <summary>
/// Which Site a Phase Two admin event touches and what it says the roster now shows (AD-3).
/// </summary>
public sealed class AdminEventRouteTests
{
    private const string Realm = "coldframe";
    private const string Org = "0192f3a4-7c1e-7d2b-9a51-3f7e2c9b1d00";
    private const string User = "5b0c7c1e-2a4d-4f1b-8e3a-9d7f6c5b4a31";

    public static TheoryData<string, string, string, RosterExpectation?> Routed() => new()
    {
        { "ORGANIZATION", "CREATE", $"orgs/{Org}", null },
        { "ORGANIZATION", "UPDATE", $"orgs/{Org}/{Org}", null },
        { "ORGANIZATION", "DELETE", $"orgs/{Org}", new RosterExpectation(RosterExpectationKind.OrganizationAbsent) },
        { "ORGANIZATION_MEMBERSHIP", "CREATE", $"orgs/{Org}/members/{User}", new RosterExpectation(RosterExpectationKind.MemberPresent, User) },
        { "ORGANIZATION_MEMBERSHIP", "DELETE", $"orgs/{Org}/members/{User}", new RosterExpectation(RosterExpectationKind.MemberAbsent, User) },
        { "ORGANIZATION_ROLE_MAPPING", "CREATE", $"orgs/{Org}/roles/owner/users/{User}", new RosterExpectation(RosterExpectationKind.RoleHeld, User, SiteRole.Owner) },
        { "ORGANIZATION_ROLE_MAPPING", "CREATE", $"orgs/{Org}/roles/administrator/users/{User}", new RosterExpectation(RosterExpectationKind.RoleHeld, User, SiteRole.Administrator) },
        { "ORGANIZATION_ROLE_MAPPING", "DELETE", $"orgs/{Org}/roles/member/users/{User}", new RosterExpectation(RosterExpectationKind.RoleNotHeld, User, SiteRole.Member) },
        { "ORGANIZATION_ROLE_MAPPING", "CREATE", $"users/{User}/orgs/{Org}/roles", null },
        { "ORGANIZATION_ROLE_MAPPING", "DELETE", $"users/{User}/orgs/{Org}/roles/owner", null },
        { "ORGANIZATION_MEMBERSHIP", "CREATE", $"/orgs/{Org}/members/{User}", new RosterExpectation(RosterExpectationKind.MemberPresent, User) },
    };

    public static TheoryData<string, string, string> Ignored() => new()
    {
        // Resource types that do not affect the roster.
        { "ORGANIZATION_ROLE", "CREATE", $"orgs/{Org}/roles/owner" },
        { "USER", "CREATE", $"users/{User}" },
        { "TEAM", "CREATE", $"orgs/{Org}/teams/t1" },
        { "INVITATION", "CREATE", $"orgs/{Org}/invitations/i1" },
        { "DOMAIN", "UPDATE", $"orgs/{Org}/domains/example.org" },

        // Unparseable paths.
        { "ORGANIZATION", "CREATE", string.Empty },
        { "ORGANIZATION", "CREATE", "orgs" },
        { "ORGANIZATION", "CREATE", $"orgs//{Org}" },
        { "ORGANIZATION_MEMBERSHIP", "CREATE", $"orgs/{Org}/members" },
        { "ORGANIZATION_MEMBERSHIP", "CREATE", $"orgs/{Org}/teams/{User}" },
        { "ORGANIZATION_ROLE_MAPPING", "CREATE", $"orgs/{Org}/roles/owner/users" },

        // A role other than owner, administrator and member.
        { "ORGANIZATION_ROLE_MAPPING", "CREATE", $"orgs/{Org}/roles/billing/users/{User}" },
        { "ORGANIZATION_ROLE_MAPPING", "CREATE", $"orgs/{Org}/roles/Owner/users/{User}" },
    };

    [Theory]
    [MemberData(nameof(Routed))]
    public void RoutesEveryRosterEventToItsSite(string resourceType, string operation, string path, RosterExpectation? expected)
    {
        var route = AdminEventRoute.From(Event(resourceType, operation, path), Realm);

        Assert.NotNull(route);
        Assert.Equal(Org, route.SiteId);
        Assert.Equal(expected, route.Expectation);
    }

    [Theory]
    [MemberData(nameof(Ignored))]
    public void IgnoresEventsThatDoNotTouchARoster(string resourceType, string operation, string path)
    {
        Assert.Null(AdminEventRoute.From(Event(resourceType, operation, path), Realm));
    }

    [Fact]
    public void IgnoresEventsOfAnotherRealm()
    {
        var master = Event("ORGANIZATION_MEMBERSHIP", "CREATE", $"orgs/{Org}/members/{User}") with { RealmId = "a1b2c3" };

        Assert.Null(AdminEventRoute.From(master, Realm));
    }

    [Fact]
    public void IgnoresFailedOperations()
    {
        var failed = Event("ORGANIZATION_MEMBERSHIP", "CREATE", $"orgs/{Org}/members/{User}") with { Error = "unknown_error" };

        Assert.Null(AdminEventRoute.From(failed, Realm));
    }

    [Fact]
    public void NeverReadsTheRepresentation()
    {
        var withRepresentation = Event("ORGANIZATION", "UPDATE", $"orgs/{Org}/{Org}") with
        {
            Representation = """{"id":"another-org","displayName":"Other"}""",
        };

        Assert.Equal(new AdminEventRoute(Org, null), AdminEventRoute.From(withRepresentation, Realm));
    }

    private static KeycloakAdminEvent Event(string resourceType, string operation, string path) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Time = 1_790_000_000_000,
        RealmId = Realm,
        ResourceType = resourceType,
        OperationType = operation,
        ResourcePath = path,
    };
}
