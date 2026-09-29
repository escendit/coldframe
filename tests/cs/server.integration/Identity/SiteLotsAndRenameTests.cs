using Coldframe.Contracts.Lots;
using Coldframe.Contracts.Sites;
using Coldframe.Server.Identity;
using Coldframe.Server.Lots;

namespace Coldframe.Server.IntegrationTests.Identity;

/// <summary>
/// Site rename, Lot creation through the Site grain, and the Lot grain on a TestCluster with a fake Phase
/// Two and hints off (Story 1.9; AD-2, AD-3, AD-18, AD-20). Only read-your-writes can bring the projections
/// up to date here.
/// </summary>
public sealed class SiteLotsAndRenameTests(IdentityCluster identity) : IClassFixture<IdentityCluster>
{
    [Fact]
    public async Task ARenameSetsTheDisplayNameThenJournalsAndProjectsIt()
    {
        var (userId, siteId) = await CreateSiteAsync();

        var result = await identity.Site(siteId).Rename("Home garden", Ct);

        Assert.Equal(new SiteRenameResult(SiteRenameOutcome.Renamed, "Home garden"), result);
        Assert.Equal("Home garden", identity.PhaseTwo.Find(siteId)?.DisplayName);
        Assert.Equal(["site.created", "site.membership-granted", "site.renamed"], await identity.AliasesAsync($"site/{siteId}"));
        Assert.Equal("Home garden", (await identity.ReadModel.FindSiteAsync(siteId, userId, Ct))?.Name);

        // Keycloak now matches, so a reconciliation finds nothing to revert.
        var reconciled = await identity.Site(siteId).Reconcile(null, acceptUnconfirmed: true, Ct);
        Assert.Equal(SiteReconciliationOutcome.Unchanged, reconciled.Outcome);
    }

    [Fact]
    public async Task TheCurrentNameCallsNothingAndJournalsNothing()
    {
        var (_, siteId) = await CreateSiteAsync();
        var writes = identity.PhaseTwo.Writes;

        var result = await identity.Site(siteId).Rename("Home", Ct);

        Assert.Equal(new SiteRenameResult(SiteRenameOutcome.Unchanged, "Home"), result);
        Assert.Equal(writes, identity.PhaseTwo.Writes);
        Assert.Equal(["site.created", "site.membership-granted"], await identity.AliasesAsync($"site/{siteId}"));
    }

    [Fact]
    public async Task WithKeycloakDownNothingIsJournaledAndTheNameStays()
    {
        var (userId, siteId) = await CreateSiteAsync();
        identity.PhaseTwo.FailOnceOn(SiteWrite.UpdateDisplayName);

        var result = await identity.Site(siteId).Rename("Home garden", Ct);

        Assert.Equal(SiteRenameOutcome.IdentityProviderUnavailable, result.Outcome);
        Assert.Equal("Home", identity.PhaseTwo.Find(siteId)?.DisplayName);
        Assert.Equal(["site.created", "site.membership-granted"], await identity.AliasesAsync($"site/{siteId}"));
        Assert.Equal("Home", (await identity.ReadModel.FindSiteAsync(siteId, userId, Ct))?.Name);

        // Trying again succeeds.
        Assert.Equal(SiteRenameOutcome.Renamed, (await identity.Site(siteId).Rename("Home garden", Ct)).Outcome);
    }

    [Fact]
    public async Task AnUncreatedSiteNeitherRenamesNorCreatesLots()
    {
        var site = identity.Site(Guid.CreateVersion7().ToString());

        Assert.Equal(SiteRenameOutcome.NotFound, (await site.Rename("Home", Ct)).Outcome);
        Assert.Equal(LotCreationOutcome.NotFound, (await site.CreateLot(NewUserId(), "k1", "Tomatoes", Ct)).Outcome);
    }

    [Fact]
    public async Task CreateLotIsIdempotentPerCallerAndKeyAndProjectsTheLotAtOnce()
    {
        var (userId, siteId) = await CreateSiteAsync();
        var site = identity.Site(siteId);

        var created = await site.CreateLot(userId, "k1", "Tomatoes", Ct);
        var retried = await site.CreateLot(userId, "k1", "Tomatoes", Ct);
        var reused = await site.CreateLot(userId, "k1", "Beans", Ct);

        // The fake clock stands still; a later creation time makes the list order deterministic.
        identity.Time.Advance(TimeSpan.FromSeconds(1));
        var otherCaller = await site.CreateLot(NewUserId(), "k1", "Beans", Ct);

        Assert.Equal(LotCreationOutcome.Created, created.Outcome);
        var lot = Assert.IsType<LotSummary>(created.Lot);
        Assert.Equal(7, Guid.Parse(lot.Id).Version);
        Assert.Equal(new LotSummary(lot.Id, siteId, "Tomatoes", null, false), lot);
        Assert.Equal(created, retried);
        Assert.Equal(LotCreationOutcome.IdempotencyKeyReused, reused.Outcome);
        Assert.Equal(LotCreationOutcome.Created, otherCaller.Outcome);
        Assert.NotEqual(lot.Id, otherCaller.Lot!.Id);

        Assert.Equal(["lot.created"], await identity.AliasesAsync($"lot/{lot.Id}"));
        Assert.Equal(
            ["site.created", "site.membership-granted", "site.lot-creation-requested", "site.lot-creation-completed", "site.lot-creation-requested", "site.lot-creation-completed"],
            await identity.AliasesAsync($"site/{siteId}"));
        Assert.Equal(
            [new LotView(lot.Id, "Tomatoes", "noNode", false), new LotView(otherCaller.Lot.Id, "Beans", "noNode", false)],
            await identity.Lots.ListLotsAsync(siteId, Ct));
    }

    [Fact]
    public async Task APendingLotCreationResumesWithTheSameLotAfterAnyDelay()
    {
        var userId = NewUserId();
        var siteId = Guid.CreateVersion7().ToString();
        var lotId = Guid.CreateVersion7().ToString();

        // The request was journaled, then the Lot grain call failed: only the Requested event exists.
        Assert.True(await identity.Store.AppendAsync(
            $"site/{siteId}",
            0,
            [
                new SiteCreated("Home", userId),
                new MembershipGranted(userId, SiteRole.Owner),
                new LotCreationRequested($"{userId}:k1", lotId, "Tomatoes", identity.Time.GetUtcNow()),
            ],
            Ct));

        // A pending key never expires: its Lot may exist.
        identity.Time.Advance(TimeSpan.FromDays(3));

        var reused = await identity.Site(siteId).CreateLot(userId, "k1", "Beans", Ct);
        var resumed = await identity.Site(siteId).CreateLot(userId, "k1", "Tomatoes", Ct);

        Assert.Equal(LotCreationOutcome.IdempotencyKeyReused, reused.Outcome);
        Assert.Equal(new LotCreationResult(LotCreationOutcome.Created, new LotSummary(lotId, siteId, "Tomatoes", null, false)), resumed);
        Assert.Equal(["lot.created"], await identity.AliasesAsync($"lot/{lotId}"));
        Assert.Equal(
            ["site.created", "site.membership-granted", "site.lot-creation-requested", "site.lot-creation-completed"],
            await identity.AliasesAsync($"site/{siteId}"));

        // Completed now: the key holds for 24 h after the request, which is long past.
        var fresh = await identity.Site(siteId).CreateLot(userId, "k1", "Beans", Ct);
        Assert.Equal(LotCreationOutcome.Created, fresh.Outcome);
        Assert.NotEqual(lotId, fresh.Lot!.Id);
    }

    [Fact]
    public async Task AClaimedLotRefusesRemovalAndAFreeOneIsTombstoned()
    {
        var siteId = Guid.CreateVersion7().ToString();
        var claimedId = Guid.CreateVersion7().ToString();
        var freeId = Guid.CreateVersion7().ToString();

        // Fixture claims: Node assignment arrives in Epic 4.
        Assert.True(await identity.Store.AppendAsync($"lot/{claimedId}", 0, [new LotCreated(siteId, "Beans"), new LotClaimed("7C19")], Ct));
        Assert.True(await identity.Store.AppendAsync($"lot/{freeId}", 0, [new LotCreated(siteId, "Herbs")], Ct));

        var refused = await identity.Lot(claimedId).Remove(siteId, Ct);
        Assert.Equal(LotOutcome.Claimed, refused.Outcome);
        Assert.Equal(["lot.created", "lot.claimed"], await identity.AliasesAsync($"lot/{claimedId}"));

        Assert.Equal(LotOutcome.NotFound, (await identity.Lot(freeId).Remove(Guid.CreateVersion7().ToString(), Ct)).Outcome);
        Assert.Equal(LotOutcome.Removed, (await identity.Lot(freeId).Remove(siteId, Ct)).Outcome);
        Assert.Equal(LotOutcome.AlreadyRemoved, (await identity.Lot(freeId).Remove(siteId, Ct)).Outcome);
        Assert.Equal(LotOutcome.NotFound, (await identity.Lot(freeId).Rename(siteId, "Herbs again", Ct)).Outcome);
        Assert.Equal(["lot.created", "lot.removed"], await identity.AliasesAsync($"lot/{freeId}"));

        var described = await identity.Lot(freeId).Describe(siteId, Ct);
        Assert.Equal(new LotResult(LotOutcome.Found, new LotSummary(freeId, siteId, "Herbs", null, true)), described);

        // The removal caught the projection up; the claimed Lot is listed, the removed one only resolvable.
        Assert.Equal([new LotView(claimedId, "Beans", "unknown", false)], await identity.Lots.ListLotsAsync(siteId, Ct));
        Assert.Equal(new LotView(freeId, "Herbs", "noNode", true), await identity.Lots.FindLotAsync(siteId, freeId, Ct));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewUserId() => Guid.NewGuid().ToString();

    private async Task<(string UserId, string SiteId)> CreateSiteAsync()
    {
        var userId = NewUserId();
        var created = await identity.User(userId).CreateSite(Guid.NewGuid().ToString(), "Home", Ct);
        Assert.Equal(SiteCreationOutcome.Created, created.Outcome);
        return (userId, created.Site!.Id);
    }
}
