using Coldframe.Contracts.Sites;
using Coldframe.Server.Identity;
using Microsoft.Extensions.Logging;

namespace Coldframe.Server.IntegrationTests.Identity;

/// <summary>
/// <see cref="ISiteGrain.ChangeRole"/> and <see cref="ISiteGrain.RemoveMember"/> on a TestCluster with a fake Phase
/// Two (Story 9.2; AD-3): the Site grain checks the last-Owner rule against its own Owners, writes Phase Two first
/// (the new role before the old ones go), journals only once every call succeeded, and brings the identity
/// projection up to date before it answers.
/// </summary>
public sealed class SiteMembershipChangeTests(IdentityCluster identity) : IClassFixture<IdentityCluster>
{
    [Fact]
    public async Task ARoleChangeGrantsTheNewRoleFirstThenRevokesTheOthersAndIsProjected()
    {
        var (siteId, _) = await CreateSiteAsync();
        var ana = await AddMemberAsync(siteId, "member");
        var before = await identity.AliasesAsync($"site/{siteId}");

        var result = await identity.Site(siteId).ChangeRole(ana, SiteRole.Administrator, Ct);

        Assert.Equal(new SiteMembershipChangeResult(SiteMembershipChangeOutcome.Changed, SiteRole.Administrator), result);
        Assert.Equal([$"grant administrator {ana}", $"revoke owner {ana}", $"revoke member {ana}"], CallsFor(ana));
        Assert.True(identity.PhaseTwo.HasGrant(siteId, "administrator", ana));
        Assert.False(identity.PhaseTwo.HasGrant(siteId, "member", ana));
        Assert.Equal([.. before, "site.membership-granted"], await identity.AliasesAsync($"site/{siteId}"));
        Assert.Equal(new MembershipGranted(ana, SiteRole.Administrator), (await identity.Store.ReadStreamAsync($"site/{siteId}", Ct))[^1].Data);

        // Read-your-writes: the next request is authorized with the new Role.
        Assert.Equal(SiteRole.Administrator, (await View(siteId, ana))!.CallerRole);
    }

    [Fact]
    public async Task TheSameRoleAgainCallsNothingAndJournalsNothing()
    {
        var (siteId, _) = await CreateSiteAsync();
        var ana = await AddMemberAsync(siteId, "member");
        var before = await identity.AliasesAsync($"site/{siteId}");
        var writes = identity.PhaseTwo.Writes;

        var result = await identity.Site(siteId).ChangeRole(ana, SiteRole.Member, Ct);

        Assert.Equal(SiteMembershipChangeOutcome.Unchanged, result.Outcome);
        Assert.Equal(writes, identity.PhaseTwo.Writes);
        Assert.Equal(before, await identity.AliasesAsync($"site/{siteId}"));
    }

    [Fact]
    public async Task ARemovalRevokesEveryRoleThenTheMembershipAndTheNextRequestIsRejected()
    {
        var (siteId, _) = await CreateSiteAsync();
        var ana = await AddMemberAsync(siteId, "administrator");
        var before = await identity.AliasesAsync($"site/{siteId}");

        var result = await identity.Site(siteId).RemoveMember(ana, Ct);

        Assert.Equal(new SiteMembershipChangeResult(SiteMembershipChangeOutcome.Changed), result);
        Assert.Equal([$"revoke owner {ana}", $"revoke administrator {ana}", $"revoke member {ana}", $"remove {ana}"], CallsFor(ana));
        Assert.False(identity.PhaseTwo.IsMember(siteId, ana));
        Assert.Equal([.. before, "site.membership-revoked"], await identity.AliasesAsync($"site/{siteId}"));

        // Read-your-writes: the projection no longer gives the removed person a Role on the Site.
        Assert.Null((await View(siteId, ana))!.CallerRole);
    }

    [Fact]
    public async Task AnOwnerIsRemovedOrLoweredWhileAnotherOwnerRemains()
    {
        var (siteId, simon) = await CreateSiteAsync();
        var bea = await AddMemberAsync(siteId, "owner");

        // Lowering yourself is allowed while Bea remains Owner.
        Assert.Equal(SiteMembershipChangeOutcome.Changed, (await identity.Site(siteId).ChangeRole(simon, SiteRole.Member, Ct)).Outcome);
        Assert.Equal(SiteRole.Member, (await View(siteId, simon))!.CallerRole);

        // Bea is now the only Owner.
        Assert.Equal(SiteMembershipChangeOutcome.LastOwner, (await identity.Site(siteId).RemoveMember(bea, Ct)).Outcome);
    }

    [Theory]
    [InlineData(SiteRole.Administrator)]
    [InlineData(SiteRole.Member)]
    public async Task TheLastOwnerIsNeitherLoweredNorRemovedAndKeycloakIsNotCalled(SiteRole lower)
    {
        var (siteId, simon) = await CreateSiteAsync();
        await AddMemberAsync(siteId, "administrator");
        var before = await identity.AliasesAsync($"site/{siteId}");
        var writes = identity.PhaseTwo.Writes;
        var calls = CallsFor(simon);

        Assert.Equal(new SiteMembershipChangeResult(SiteMembershipChangeOutcome.LastOwner), await identity.Site(siteId).ChangeRole(simon, lower, Ct));
        Assert.Equal(new SiteMembershipChangeResult(SiteMembershipChangeOutcome.LastOwner), await identity.Site(siteId).RemoveMember(simon, Ct));

        Assert.Equal(writes, identity.PhaseTwo.Writes);
        Assert.Equal(calls, CallsFor(simon));
        Assert.Equal(before, await identity.AliasesAsync($"site/{siteId}"));
        Assert.Equal(SiteRole.Owner, (await View(siteId, simon))!.CallerRole);
    }

    [Fact]
    public async Task TwoOwnersLoweringEachOtherAtOnceLeaveOneOwner()
    {
        var (siteId, simon) = await CreateSiteAsync();
        var bea = await AddMemberAsync(siteId, "owner");

        var results = await Task.WhenAll(
            identity.Site(siteId).ChangeRole(bea, SiteRole.Member, Ct),
            identity.Site(siteId).ChangeRole(simon, SiteRole.Member, Ct));

        Assert.Single(results, result => result.Outcome == SiteMembershipChangeOutcome.Changed);
        Assert.Single(results, result => result.Outcome == SiteMembershipChangeOutcome.LastOwner);

        var owners = new[] { simon, bea }.Where(user => identity.PhaseTwo.HasGrant(siteId, "owner", user)).ToList();
        Assert.Single(owners);
        Assert.Equal(SiteRole.Owner, (await View(siteId, owners[0]))!.CallerRole);
    }

    [Fact]
    public async Task TwoOwnersRemovingEachOtherAtOnceLeaveOneOwner()
    {
        var (siteId, simon) = await CreateSiteAsync();
        var bea = await AddMemberAsync(siteId, "owner");

        var results = await Task.WhenAll(
            identity.Site(siteId).RemoveMember(bea, Ct),
            identity.Site(siteId).RemoveMember(simon, Ct));

        Assert.Single(results, result => result.Outcome == SiteMembershipChangeOutcome.Changed);
        Assert.Single(results, result => result.Outcome == SiteMembershipChangeOutcome.LastOwner);
        Assert.Single(new[] { simon, bea }, user => identity.PhaseTwo.IsMember(siteId, user));
    }

    [Fact]
    public async Task SomeoneWhoIsNoMemberIsNotFoundAndNothingIsCalled()
    {
        var (siteId, _) = await CreateSiteAsync();
        var stranger = NewUserId();
        var writes = identity.PhaseTwo.Writes;

        Assert.Equal(SiteMembershipChangeOutcome.MemberNotFound, (await identity.Site(siteId).ChangeRole(stranger, SiteRole.Member, Ct)).Outcome);
        Assert.Equal(SiteMembershipChangeOutcome.MemberNotFound, (await identity.Site(siteId).RemoveMember(stranger, Ct)).Outcome);
        Assert.Equal(writes, identity.PhaseTwo.Writes);
    }

    [Theory]
    [InlineData(SiteWrite.GrantRole, false)]
    [InlineData(SiteWrite.RevokeRole, false)]
    [InlineData(SiteWrite.RevokeRole, true)]
    [InlineData(SiteWrite.RemoveMember, true)]
    public async Task WithKeycloakDownNothingIsJournaledAndTryingAgainSucceeds(SiteWrite failing, bool removal)
    {
        var (siteId, _) = await CreateSiteAsync();
        var ana = await AddMemberAsync(siteId, "member");
        var before = await identity.AliasesAsync($"site/{siteId}");
        identity.PhaseTwo.FailOnceOn(failing);

        var failed = removal
            ? await identity.Site(siteId).RemoveMember(ana, Ct)
            : await identity.Site(siteId).ChangeRole(ana, SiteRole.Administrator, Ct);

        Assert.Equal(new SiteMembershipChangeResult(SiteMembershipChangeOutcome.IdentityProviderUnavailable), failed);
        Assert.Equal(before, await identity.AliasesAsync($"site/{siteId}"));
        Assert.Equal(SiteRole.Member, (await View(siteId, ana))!.CallerRole);

        var again = removal
            ? await identity.Site(siteId).RemoveMember(ana, Ct)
            : await identity.Site(siteId).ChangeRole(ana, SiteRole.Administrator, Ct);

        Assert.Equal(SiteMembershipChangeOutcome.Changed, again.Outcome);
        Assert.Equal(removal ? null : SiteRole.Administrator, (await View(siteId, ana))!.CallerRole);
    }

    [Fact]
    public async Task AKeycloakThatHangsEndsWithinTheBudgetAsUnavailable()
    {
        var (siteId, _) = await CreateSiteAsync();
        var ana = await AddMemberAsync(siteId, "member");
        var before = await identity.AliasesAsync($"site/{siteId}");
        var stalled = identity.PhaseTwo.StallOnce(SiteWrite.GrantRole);

        var change = identity.Site(siteId).ChangeRole(ana, SiteRole.Owner, Ct);
        await stalled.WaitAsync(Ct);
        identity.Time.Advance(new KeycloakOptions().OperationBudget);

        Assert.Equal(SiteMembershipChangeOutcome.IdentityProviderUnavailable, (await change.WaitAsync(Ct)).Outcome);
        Assert.Equal(before, await identity.AliasesAsync($"site/{siteId}"));
    }

    [Fact]
    public async Task AnUncreatedOrDeletedSiteChangesNothing()
    {
        var uncreated = Guid.CreateVersion7().ToString();
        Assert.Equal(SiteMembershipChangeOutcome.NotFound, (await identity.Site(uncreated).ChangeRole(NewUserId(), SiteRole.Member, Ct)).Outcome);
        Assert.Equal(SiteMembershipChangeOutcome.NotFound, (await identity.Site(uncreated).RemoveMember(NewUserId(), Ct)).Outcome);

        var (siteId, simon) = await CreateSiteAsync();
        var ana = await AddMemberAsync(siteId, "member");
        identity.PhaseTwo.ConsoleDelete(siteId);
        await identity.Site(siteId).Reconcile(new RosterExpectation(RosterExpectationKind.OrganizationAbsent), acceptUnconfirmed: false, Ct);

        Assert.Equal(SiteMembershipChangeOutcome.NotFound, (await identity.Site(siteId).ChangeRole(ana, SiteRole.Owner, Ct)).Outcome);
        Assert.Equal(SiteMembershipChangeOutcome.NotFound, (await identity.Site(siteId).RemoveMember(simon, Ct)).Outcome);
    }

    [Fact]
    public async Task AfterAnOwnerlessBreakGlassEditTheKeptOwnerIsTheLastOwnerAndStillChangesRoles()
    {
        var (siteId, simon) = await CreateSiteAsync();
        var ana = await AddMemberAsync(siteId, "member");

        // Break-glass: the only owner role is revoked in the Keycloak console. Reconciliation keeps Simon.
        identity.PhaseTwo.ConsoleRevokeRole(siteId, "owner", simon);
        await identity.Site(siteId).Reconcile(
            new RosterExpectation(RosterExpectationKind.RoleNotHeld, simon, SiteRole.Owner),
            acceptUnconfirmed: false,
            Ct);

        Assert.Contains(
            identity.Logs.Records,
            record => record.EventId.Id == SiteGrain.OwnerlessEditRefusedEventId
                && record.Level == LogLevel.Error
                && record.Message.Contains(siteId, StringComparison.Ordinal));
        Assert.Equal("site.ownerless-edit-refused", (await identity.AliasesAsync($"site/{siteId}"))[^1]);
        Assert.Equal(SiteRole.Owner, (await View(siteId, simon))!.CallerRole);

        // The kept Owner is the last valid Owner: never lowered or removed in the app.
        Assert.Equal(SiteMembershipChangeOutcome.LastOwner, (await identity.Site(siteId).ChangeRole(simon, SiteRole.Member, Ct)).Outcome);

        // And still changes Roles: making Ana Owner gives Keycloak an owner again.
        Assert.Equal(SiteMembershipChangeOutcome.Changed, (await identity.Site(siteId).ChangeRole(ana, SiteRole.Owner, Ct)).Outcome);
        Assert.True(identity.PhaseTwo.HasGrant(siteId, "owner", ana));
        Assert.Equal(SiteRole.Owner, (await View(siteId, ana))!.CallerRole);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewUserId() => Guid.NewGuid().ToString();

    private IReadOnlyList<string> CallsFor(string userId) =>
        [.. identity.PhaseTwo.MembershipCalls.Where(call => call.EndsWith($" {userId}", StringComparison.Ordinal))];

    private async Task<(string SiteId, string OwnerId)> CreateSiteAsync()
    {
        var ownerId = NewUserId();
        var created = await identity.User(ownerId).CreateSite(Guid.NewGuid().ToString(), "Home", Ct);
        Assert.Equal(SiteCreationOutcome.Created, created.Outcome);
        return (created.Site!.Id, ownerId);
    }

    // A member who joined in Keycloak, reconciled into the Site as accepting an invitation is.
    private async Task<string> AddMemberAsync(string siteId, string organizationRole)
    {
        var userId = NewUserId();
        identity.PhaseTwo.ConsoleAddMember(siteId, userId, organizationRole);
        var reconciled = await identity.Site(siteId).Reconcile(
            new RosterExpectation(RosterExpectationKind.MemberPresent, userId),
            acceptUnconfirmed: false,
            Ct);
        Assert.Contains(userId, reconciled.Members.Keys);
        return userId;
    }

    private Task<SiteAccessView?> View(string siteId, string userId) => identity.ReadModel.FindSiteAsync(siteId, userId, Ct);
}
