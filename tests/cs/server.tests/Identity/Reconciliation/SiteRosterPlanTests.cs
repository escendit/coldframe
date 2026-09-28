using Coldframe.Contracts.Sites;
using Coldframe.Server.Identity;
using Coldframe.Server.Identity.Reconciliation;

namespace Coldframe.Server.Tests.Identity.Reconciliation;

/// <summary>
/// How a pulled roster turns into Membership changes, including the AD-3 break-glass rule.
/// </summary>
public sealed class SiteRosterPlanTests
{
    private const string Owner = "owner-1";
    private const string U = "user-u";
    private const string V = "user-v";

    [Fact]
    public void AMemberAddedWithAdministratorIsGrantedAdministrator()
    {
        var plan = Plan(Members((Owner, SiteRole.Owner)), Roster((Owner, ["owner"]), (U, ["administrator"])));

        Assert.Equal([new MembershipGranted(U, SiteRole.Administrator)], plan.Grants);
        Assert.Empty(plan.Revocations);
        Assert.False(plan.Ownerless);
    }

    [Fact]
    public void TheHighestRoleHeldWins()
    {
        var plan = Plan(
            Members((Owner, SiteRole.Owner), (U, SiteRole.Member)),
            Roster((Owner, ["owner"]), (U, ["member", "owner"])));

        Assert.Equal([new MembershipGranted(U, SiteRole.Owner)], plan.Grants);
    }

    [Fact]
    public void ARemovedMemberIsRevoked()
    {
        var plan = Plan(Members((Owner, SiteRole.Owner), (U, SiteRole.Member)), Roster((Owner, ["owner"])));

        Assert.Empty(plan.Grants);
        Assert.Equal([new MembershipRevoked(U)], plan.Revocations);
    }

    [Fact]
    public void AMemberHoldingNoneOfTheThreeRolesHasNoMembership()
    {
        var plan = Plan(Members((Owner, SiteRole.Owner), (U, SiteRole.Member)), Roster((Owner, ["owner"]), (U, [])));

        Assert.Equal([new MembershipRevoked(U)], plan.Revocations);
    }

    [Fact]
    public void ARoleHeldByANonMemberIsIgnored()
    {
        var roster = new PhaseTwoRoster(
            "Home",
            Set(Owner),
            new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
            {
                ["owner"] = Set(Owner),
                ["administrator"] = Set(U),
            });

        var plan = Plan(Members((Owner, SiteRole.Owner)), roster);

        Assert.False(plan.ChangesMemberships);
    }

    [Fact]
    public void AnEchoOfTheSitesOwnWriteChangesNothing()
    {
        var plan = Plan(Members((Owner, SiteRole.Owner)), Roster((Owner, ["owner"])));

        Assert.False(plan.ChangesMemberships);
        Assert.False(plan.Ownerless);
        Assert.Empty(plan.KeptOwners);
    }

    [Fact]
    public void RevokingOwnerFromTheOnlyOwnerKeepsItOwner()
    {
        var plan = Plan(Members((Owner, SiteRole.Owner)), Roster((Owner, ["member"])));

        Assert.True(plan.Ownerless);
        Assert.Equal([Owner], plan.KeptOwners);
        Assert.False(plan.ChangesMemberships);
    }

    [Fact]
    public void RemovingTheOnlyOwnerKeepsItOwnerAndStillAppliesTheOtherDifferences()
    {
        var plan = Plan(
            Members((Owner, SiteRole.Owner), (U, SiteRole.Member), (V, SiteRole.Member)),
            Roster((U, ["administrator"])));

        Assert.True(plan.Ownerless);
        Assert.Equal([Owner], plan.KeptOwners);
        Assert.Equal([new MembershipGranted(U, SiteRole.Administrator)], plan.Grants);
        Assert.Equal([new MembershipRevoked(V)], plan.Revocations);
    }

    [Fact]
    public void AnOwnerlessRosterKeepsEveryCurrentOwner()
    {
        var plan = Plan(Members((V, SiteRole.Owner), (Owner, SiteRole.Owner)), Roster((Owner, ["member"])));

        Assert.Equal([Owner, V], plan.KeptOwners);
        Assert.False(plan.ChangesMemberships);
    }

    [Fact]
    public void ARosterWithAnotherOwnerIsAppliedInFull()
    {
        var plan = Plan(Members((Owner, SiteRole.Owner)), Roster((U, ["owner"])));

        Assert.False(plan.Ownerless);
        Assert.Equal([new MembershipGranted(U, SiteRole.Owner)], plan.Grants);
        Assert.Equal([new MembershipRevoked(Owner)], plan.Revocations);
    }

    [Fact]
    public void ChangesAreOrderedByUserId()
    {
        var plan = Plan(
            Members((Owner, SiteRole.Owner), ("c", SiteRole.Member), ("a", SiteRole.Member)),
            Roster((Owner, ["owner"]), ("z", ["member"]), ("b", ["member"])));

        Assert.Equal(["b", "z"], plan.Grants.Select(grant => grant.UserId));
        Assert.Equal(["a", "c"], plan.Revocations.Select(revocation => revocation.UserId));
    }

    private static SiteRosterPlan Plan(Dictionary<string, SiteRole> members, PhaseTwoRoster roster) =>
        SiteRosterPlan.Plan(
            members,
            members.Where(pair => pair.Value == SiteRole.Owner).Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal),
            roster);

    private static Dictionary<string, SiteRole> Members(params (string UserId, SiteRole Role)[] members) =>
        members.ToDictionary(member => member.UserId, member => member.Role, StringComparer.Ordinal);

    private static PhaseTwoRoster Roster(params (string UserId, string[] Roles)[] members)
    {
        var holders = SiteGrain.OrganizationRoles.Values.ToDictionary(
            role => role,
            role => (IReadOnlySet<string>)members.Where(member => member.Roles.Contains(role)).Select(member => member.UserId).ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);

        return new PhaseTwoRoster("Home", Set([.. members.Select(member => member.UserId)]), holders);
    }

    private static HashSet<string> Set(params string[] ids) => new(ids, StringComparer.Ordinal);
}
