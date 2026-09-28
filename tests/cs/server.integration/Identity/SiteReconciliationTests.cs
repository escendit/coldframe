using Coldframe.Contracts.Sites;
using Coldframe.Server.Identity;
using Microsoft.Extensions.Logging;

namespace Coldframe.Server.IntegrationTests.Identity;

/// <summary>
/// <see cref="ISiteGrain.Reconcile"/> on a TestCluster with a fake Phase Two whose console methods play
/// the Keycloak administrator. Hints are off, so the projection is current only through read-your-writes
/// (AD-1, AD-3, AD-4, AD-20).
/// </summary>
public sealed class SiteReconciliationTests(IdentityCluster identity) : IClassFixture<IdentityCluster>
{
    private static readonly string[] Created = ["site.created", "site.membership-granted"];

    [Fact]
    public async Task AMemberAddedInKeycloakIsGrantedItsRoleAndProjected()
    {
        var (siteId, _) = await CreateSiteAsync();
        var u = NewUserId();
        identity.PhaseTwo.ConsoleAddMember(siteId, u, "administrator");

        var result = await identity.Site(siteId).Reconcile(MemberPresent(u), acceptUnconfirmed: false, Ct);

        Assert.Equal(SiteReconciliationOutcome.Changed, result.Outcome);
        Assert.Equal(SiteLifecycle.Active, result.Lifecycle);
        Assert.Equal(SiteRole.Administrator, result.Members[u]);
        Assert.Equal([.. Created, "site.membership-granted"], await identity.AliasesAsync($"site/{siteId}"));
        Assert.Equal(SiteRole.Administrator, (await View(siteId, u))!.CallerRole);
    }

    [Fact]
    public async Task ARoleAddedInKeycloakRaisesTheMembersRole()
    {
        var (siteId, _) = await CreateSiteAsync();
        var u = NewUserId();
        identity.PhaseTwo.ConsoleAddMember(siteId, u, "member");
        await identity.Site(siteId).Reconcile(null, acceptUnconfirmed: false, Ct);

        identity.PhaseTwo.ConsoleGrantRole(siteId, "owner", u);
        var result = await identity.Site(siteId).Reconcile(RoleHeld(u, SiteRole.Owner), acceptUnconfirmed: false, Ct);

        Assert.Equal(SiteReconciliationOutcome.Changed, result.Outcome);
        Assert.Equal(SiteRole.Owner, result.Members[u]);
        var events = await identity.Store.ReadStreamAsync($"site/{siteId}", Ct);
        Assert.Equal(new MembershipGranted(u, SiteRole.Owner), events[^1].Data);
        Assert.Equal(SiteRole.Owner, (await View(siteId, u))!.CallerRole);
    }

    [Fact]
    public async Task AMemberRemovedInKeycloakIsRevokedAndLeavesTheProjection()
    {
        var (siteId, _) = await CreateSiteAsync();
        var u = NewUserId();
        identity.PhaseTwo.ConsoleAddMember(siteId, u, "member");
        await identity.Site(siteId).Reconcile(MemberPresent(u), acceptUnconfirmed: false, Ct);

        identity.PhaseTwo.ConsoleRemoveMember(siteId, u);
        var result = await identity.Site(siteId).Reconcile(
            new RosterExpectation(RosterExpectationKind.MemberAbsent, u),
            acceptUnconfirmed: false,
            Ct);

        Assert.Equal(SiteReconciliationOutcome.Changed, result.Outcome);
        Assert.DoesNotContain(u, result.Members.Keys);
        Assert.Equal([u], result.FormerMembers);
        Assert.Equal("site.membership-revoked", (await identity.AliasesAsync($"site/{siteId}"))[^1]);
        Assert.Null((await View(siteId, u))!.CallerRole);
    }

    [Fact]
    public async Task AnEchoOfTheSitesOwnWriteChangesNothingAndCallsNoKeycloakWrite()
    {
        var (siteId, ownerId) = await CreateSiteAsync();
        var writes = identity.PhaseTwo.Writes;

        var result = await identity.Site(siteId).Reconcile(RoleHeld(ownerId, SiteRole.Owner), acceptUnconfirmed: false, Ct);

        Assert.Equal(SiteReconciliationOutcome.Unchanged, result.Outcome);
        Assert.Equal(Created, await identity.AliasesAsync($"site/{siteId}"));
        Assert.Equal(writes, identity.PhaseTwo.Writes);
    }

    [Fact]
    public async Task TheSameEventTwiceChangesNothingTheSecondTime()
    {
        var (siteId, _) = await CreateSiteAsync();
        var u = NewUserId();
        identity.PhaseTwo.ConsoleAddMember(siteId, u, "member");

        var first = await identity.Site(siteId).Reconcile(MemberPresent(u), acceptUnconfirmed: false, Ct);
        var aliases = await identity.AliasesAsync($"site/{siteId}");
        var second = await identity.Site(siteId).Reconcile(MemberPresent(u), acceptUnconfirmed: false, Ct);

        Assert.Equal(SiteReconciliationOutcome.Changed, first.Outcome);
        Assert.Equal(SiteReconciliationOutcome.Unchanged, second.Outcome);
        Assert.Equal(aliases, await identity.AliasesAsync($"site/{siteId}"));
    }

    [Fact]
    public async Task RevokingOwnerFromTheOnlyOwnerKeepsItOwnerAndJournalsOneRefusalPerEpisode()
    {
        var (siteId, ownerId) = await CreateSiteAsync();
        var writes = identity.PhaseTwo.Writes;
        identity.PhaseTwo.ConsoleRevokeRole(siteId, "owner", ownerId);

        var first = await identity.Site(siteId).Reconcile(RoleNotHeld(ownerId, SiteRole.Owner), acceptUnconfirmed: false, Ct);
        var second = await identity.Site(siteId).Reconcile(null, acceptUnconfirmed: false, Ct);

        Assert.Equal(SiteReconciliationOutcome.Changed, first.Outcome);
        Assert.Equal(SiteReconciliationOutcome.Unchanged, second.Outcome);
        Assert.Equal(SiteRole.Owner, second.Members[ownerId]);

        // The operator-visible error: one per ownerless reconcile, including the one that journals nothing.
        var errors = identity.Logs.Records
            .Where(record => record.EventId.Id == SiteGrain.OwnerlessEditRefusedEventId && record.Message.Contains(siteId, StringComparison.Ordinal))
            .ToList();
        Assert.Equal(2, errors.Count);
        Assert.All(errors, record => Assert.Equal(LogLevel.Error, record.Level));
        Assert.Equal([.. Created, "site.ownerless-edit-refused"], await identity.AliasesAsync($"site/{siteId}"));
        var refused = Assert.IsType<SiteOwnerlessEditRefused>((await identity.Store.ReadStreamAsync($"site/{siteId}", Ct))[^1].Data);
        Assert.Equal([ownerId], refused.KeptOwners);
        Assert.Equal(SiteRole.Owner, (await View(siteId, ownerId))!.CallerRole);

        // Nothing is written back to Keycloak: the Owner stays without the role there.
        Assert.Equal(writes, identity.PhaseTwo.Writes);
        Assert.False(identity.PhaseTwo.HasGrant(siteId, "owner", ownerId));
    }

    [Fact]
    public async Task RemovingTheOnlyOwnerKeepsItOwnerButAppliesTheOtherDifferences()
    {
        var (siteId, ownerId) = await CreateSiteAsync();
        var u = NewUserId();
        identity.PhaseTwo.ConsoleRemoveMember(siteId, ownerId);
        identity.PhaseTwo.ConsoleAddMember(siteId, u, "administrator");

        var result = await identity.Site(siteId).Reconcile(
            new RosterExpectation(RosterExpectationKind.MemberAbsent, ownerId),
            acceptUnconfirmed: false,
            Ct);

        Assert.Equal(SiteReconciliationOutcome.Changed, result.Outcome);
        Assert.Equal(SiteRole.Owner, result.Members[ownerId]);
        Assert.Equal(SiteRole.Administrator, result.Members[u]);
        Assert.Equal(
            [.. Created, "site.ownerless-edit-refused", "site.membership-granted"],
            await identity.AliasesAsync($"site/{siteId}"));
        Assert.Equal(SiteRole.Owner, (await View(siteId, ownerId))!.CallerRole);
        Assert.Equal(SiteRole.Administrator, (await View(siteId, u))!.CallerRole);
    }

    [Fact]
    public async Task AnOwnerInKeycloakAgainEndsTheEpisodeAndTheRosterAppliesNormally()
    {
        var (siteId, ownerId) = await CreateSiteAsync();
        var u = NewUserId();
        identity.PhaseTwo.ConsoleRevokeRole(siteId, "owner", ownerId);
        await identity.Site(siteId).Reconcile(null, acceptUnconfirmed: false, Ct);

        // Another User becomes Owner; the former Owner stays a plain member in Keycloak.
        identity.PhaseTwo.ConsoleAddMember(siteId, u, "owner");
        identity.PhaseTwo.ConsoleGrantRole(siteId, "member", ownerId);
        var restored = await identity.Site(siteId).Reconcile(RoleHeld(u, SiteRole.Owner), acceptUnconfirmed: false, Ct);

        Assert.Equal(SiteReconciliationOutcome.Changed, restored.Outcome);
        Assert.Equal(SiteRole.Owner, restored.Members[u]);
        Assert.Equal(SiteRole.Member, restored.Members[ownerId]);
        Assert.Equal(
            [.. Created, "site.ownerless-edit-refused", "site.ownerless-edit-resolved", "site.membership-granted", "site.membership-granted"],
            await identity.AliasesAsync($"site/{siteId}"));

        // A second episode is refused again, once.
        identity.PhaseTwo.ConsoleRemoveMember(siteId, u);
        identity.PhaseTwo.ConsoleRevokeRole(siteId, "member", ownerId);
        await identity.Site(siteId).Reconcile(null, acceptUnconfirmed: false, Ct);
        await identity.Site(siteId).Reconcile(null, acceptUnconfirmed: false, Ct);

        var aliases = await identity.AliasesAsync($"site/{siteId}");
        Assert.Equal(2, aliases.Count(alias => alias == "site.ownerless-edit-refused"));
        Assert.Equal(1, aliases.Count(alias => alias == "site.ownerless-edit-resolved"));
    }

    [Fact]
    public async Task AnOrganizationDeletedInKeycloakDeletesTheSiteAndKeepsItsRows()
    {
        var (siteId, ownerId) = await CreateSiteAsync();
        identity.PhaseTwo.ConsoleDelete(siteId);

        var result = await identity.Site(siteId).Reconcile(
            new RosterExpectation(RosterExpectationKind.OrganizationAbsent),
            acceptUnconfirmed: false,
            Ct);

        Assert.Equal(SiteReconciliationOutcome.Changed, result.Outcome);
        Assert.Equal(SiteLifecycle.Deleted, result.Lifecycle);
        Assert.Equal(SiteRole.Owner, result.Members[ownerId]);
        Assert.Equal([.. Created, "site.deleted"], await identity.AliasesAsync($"site/{siteId}"));

        // The policy answers 404 for a Site that is not Active; the rows stay.
        var view = await View(siteId, ownerId);
        Assert.NotNull(view);
        Assert.Equal(SiteLifecycle.Deleted, view.Lifecycle);
        Assert.Equal(SiteRole.Owner, view.CallerRole);

        // Deletion is terminal: later events are ignored without asking Keycloak.
        var reads = identity.PhaseTwo.RosterReads;
        var later = await identity.Site(siteId).Reconcile(null, acceptUnconfirmed: true, Ct);
        Assert.Equal(SiteReconciliationOutcome.Ignored, later.Outcome);
        Assert.Equal(SiteLifecycle.Deleted, later.Lifecycle);
        Assert.Equal(reads, identity.PhaseTwo.RosterReads);
        Assert.Equal(SiteInitializationOutcome.Conflict, (await identity.Site(siteId).Initialize("Home", ownerId, Ct)).Outcome);
    }

    [Fact]
    public async Task ARenameInKeycloakRenamesTheSite()
    {
        var (siteId, ownerId) = await CreateSiteAsync();
        identity.PhaseTwo.ConsoleRename(siteId, "Allotment");

        var result = await identity.Site(siteId).Reconcile(null, acceptUnconfirmed: false, Ct);

        Assert.Equal(SiteReconciliationOutcome.Changed, result.Outcome);
        Assert.Equal([.. Created, "site.renamed"], await identity.AliasesAsync($"site/{siteId}"));
        Assert.Equal("Allotment", (await View(siteId, ownerId))!.Name);
    }

    [Fact]
    public async Task APullThatDoesNotShowTheEventYetJournalsNothingUntilItIsAccepted()
    {
        var (siteId, _) = await CreateSiteAsync();
        var u = NewUserId();

        // The listener fired before Keycloak committed: the pull does not show U.
        var early = await identity.Site(siteId).Reconcile(MemberPresent(u), acceptUnconfirmed: false, Ct);

        Assert.Equal(SiteReconciliationOutcome.NotYetVisible, early.Outcome);
        Assert.Equal(Created, await identity.AliasesAsync($"site/{siteId}"));

        // From attempt 5 the pull is the truth; here it shows nothing new.
        var accepted = await identity.Site(siteId).Reconcile(MemberPresent(u), acceptUnconfirmed: true, Ct);

        Assert.Equal(SiteReconciliationOutcome.Unchanged, accepted.Outcome);
        Assert.Equal(Created, await identity.AliasesAsync($"site/{siteId}"));
    }

    [Theory]
    [InlineData(RosterExpectationKind.RoleHeld)]
    [InlineData(RosterExpectationKind.RoleNotHeld)]
    [InlineData(RosterExpectationKind.MemberAbsent)]
    [InlineData(RosterExpectationKind.OrganizationAbsent)]
    public async Task EveryExpectationThePullDoesNotShowYetJournalsNothing(RosterExpectationKind kind)
    {
        var (siteId, ownerId) = await CreateSiteAsync();

        // Each expectation runs ahead of the console change it announces: the Owner does not hold
        // administrator yet, still holds owner, is still a member, and the Organization still exists.
        var expectation = kind switch
        {
            RosterExpectationKind.RoleHeld => RoleHeld(ownerId, SiteRole.Administrator),
            RosterExpectationKind.RoleNotHeld => RoleNotHeld(ownerId, SiteRole.Owner),
            RosterExpectationKind.MemberAbsent => new RosterExpectation(RosterExpectationKind.MemberAbsent, ownerId),
            _ => new RosterExpectation(RosterExpectationKind.OrganizationAbsent),
        };

        var result = await identity.Site(siteId).Reconcile(expectation, acceptUnconfirmed: false, Ct);

        Assert.Equal(SiteReconciliationOutcome.NotYetVisible, result.Outcome);
        Assert.Equal(Created, await identity.AliasesAsync($"site/{siteId}"));
    }

    [Fact]
    public async Task AnUnavailableKeycloakJournalsNothing()
    {
        var (siteId, _) = await CreateSiteAsync();
        identity.PhaseTwo.ConsoleRename(siteId, "Allotment");

        identity.PhaseTwo.Unavailable = true;
        SiteReconciliationResult down;
        try
        {
            down = await identity.Site(siteId).Reconcile(null, acceptUnconfirmed: true, Ct);
        }
        finally
        {
            identity.PhaseTwo.Unavailable = false;
        }

        Assert.Equal(SiteReconciliationOutcome.IdentityProviderUnavailable, down.Outcome);
        Assert.Equal(Created, await identity.AliasesAsync($"site/{siteId}"));

        var resumed = await identity.Site(siteId).Reconcile(null, acceptUnconfirmed: false, Ct);
        Assert.Equal(SiteReconciliationOutcome.Changed, resumed.Outcome);
    }

    [Fact]
    public async Task AnUncreatedSiteIgnoresReconciliationWithoutAskingKeycloak()
    {
        var siteId = Guid.CreateVersion7().ToString();
        var reads = identity.PhaseTwo.RosterReads;

        var result = await identity.Site(siteId).Reconcile(null, acceptUnconfirmed: true, Ct);

        Assert.Equal(SiteReconciliationOutcome.Ignored, result.Outcome);
        Assert.Equal(SiteLifecycle.Uncreated, result.Lifecycle);
        Assert.Equal(reads, identity.PhaseTwo.RosterReads);
        Assert.Empty(await identity.AliasesAsync($"site/{siteId}"));
        Assert.Null(await View(siteId, NewUserId()));
    }

    [Fact]
    public async Task SyncSiteMembershipJournalsOnlyChanges()
    {
        var userId = NewUserId();
        var user = identity.User(userId);

        await user.SyncSiteMembership("site-a", SiteRole.Member, Ct);
        await user.SyncSiteMembership("site-a", SiteRole.Member, Ct);
        await user.SyncSiteMembership("site-b", null, Ct);

        Assert.Equal(new Dictionary<string, SiteRole> { ["site-a"] = SiteRole.Member }, await identity.UserSitesAsync(userId));
        Assert.Equal(["user.site-membership-changed"], await identity.AliasesAsync($"user/{userId}"));

        await user.SyncSiteMembership("site-a", SiteRole.Administrator, Ct);
        await user.SyncSiteMembership("site-a", null, Ct);

        Assert.Empty(await identity.UserSitesAsync(userId));
        Assert.Equal(3, (await identity.AliasesAsync($"user/{userId}")).Count);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewUserId() => Guid.NewGuid().ToString();

    private static RosterExpectation MemberPresent(string userId) => new(RosterExpectationKind.MemberPresent, userId);

    private static RosterExpectation RoleHeld(string userId, SiteRole role) => new(RosterExpectationKind.RoleHeld, userId, role);

    private static RosterExpectation RoleNotHeld(string userId, SiteRole role) => new(RosterExpectationKind.RoleNotHeld, userId, role);

    private async Task<(string SiteId, string OwnerId)> CreateSiteAsync()
    {
        var ownerId = NewUserId();
        var created = await identity.User(ownerId).CreateSite("k1", "Home", Ct);
        Assert.Equal(SiteCreationOutcome.Created, created.Outcome);
        return (created.Site!.Id, ownerId);
    }

    private Task<SiteAccessView?> View(string siteId, string userId) => identity.ReadModel.FindSiteAsync(siteId, userId, Ct);
}
