using Coldframe.Contracts.Sites;
using Coldframe.Server.Identity.Reconciliation;

namespace Coldframe.Server.Tests.Identity.Reconciliation;

/// <summary>
/// Which user event reaches a Site (Story 9.1): only an accepted Phase Two invitation, which expects the
/// accepting User on the Organization's roster. Every other user event is ignored.
/// </summary>
public sealed class UserEventRouteTests
{
    private const string Realm = "coldframe";
    private const string Org = "0192f3a4-7c1e-7d2b-9a51-3f7e2c9b1d00";
    private const string User = "5b0c7c1e-2a4d-4f1b-8e3a-9d7f6c5b4a31";

    [Fact]
    public void AnAcceptedInvitationReconcilesTheSiteExpectingTheNewMember()
    {
        var route = UserEventRoute.From(Event(), Realm);

        Assert.NotNull(route);
        Assert.Equal(Org, route.SiteId);
        Assert.Equal(new RosterExpectation(RosterExpectationKind.MemberPresent, User), route.Expectation);
    }

    [Fact]
    public void ARejectedInvitationIsIgnored()
    {
        Assert.Null(UserEventRoute.From(Event() with { Error = "User invitation revoked." }, Realm));
    }

    [Theory]
    [InlineData("LOGIN")]
    [InlineData("REGISTER")]
    [InlineData("UPDATE_PROFILE")]
    [InlineData("custom_required_action")]
    [InlineData(null)]
    public void AnotherEventTypeIsIgnored(string? type)
    {
        Assert.Null(UserEventRoute.From(Event() with { Type = type }, Realm));
    }

    [Theory]
    [InlineData("master")]
    [InlineData(null)]
    public void AnotherRealmIsIgnored(string? realm)
    {
        Assert.Null(UserEventRoute.From(Event() with { RealmId = realm }, Realm));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AnEventWithoutAUserIsIgnored(string? user)
    {
        Assert.Null(UserEventRoute.From(Event() with { UserId = user }, Realm));
    }

    [Fact]
    public void AnEventWithoutAnOrganizationIsIgnored()
    {
        Assert.Null(UserEventRoute.From(Event() with { Details = null }, Realm));
        Assert.Null(UserEventRoute.From(Event() with { Details = new Dictionary<string, string?> { ["invitation_id"] = "i1" } }, Realm));
        Assert.Null(UserEventRoute.From(Event() with { Details = new Dictionary<string, string?> { ["org_id"] = null } }, Realm));
        Assert.Null(UserEventRoute.From(Event() with { Details = new Dictionary<string, string?> { ["org_id"] = string.Empty } }, Realm));
    }

    // As Phase Two's InvitationRequiredAction emits it on accept: details org_id and invitation_id.
    private static KeycloakUserEvent Event() => new()
    {
        Id = "e1",
        Type = "CUSTOM_REQUIRED_ACTION",
        RealmId = Realm,
        UserId = User,
        Details = new Dictionary<string, string?> { ["org_id"] = Org, ["invitation_id"] = "i1" },
    };
}
