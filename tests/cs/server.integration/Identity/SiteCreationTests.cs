using Coldframe.Contracts.Sites;
using Coldframe.Server.Identity;

namespace Coldframe.Server.IntegrationTests.Identity;

/// <summary>
/// Create Site through the User and Site grains on a TestCluster, with a fake Phase Two and hints off
/// (AD-1, AD-3, AD-4, AD-21).
/// </summary>
public sealed class SiteCreationTests(IdentityCluster identity) : IClassFixture<IdentityCluster>
{
    [Fact]
    public async Task CreatesATaggedOrganizationWithTheCallerAsOwnerAndJournalsBothEvents()
    {
        var userId = NewUserId();

        var result = await identity.User(userId).CreateSite("k1", "Home", Ct);

        Assert.Equal(SiteCreationOutcome.Created, result.Outcome);
        var site = Assert.IsType<SiteSummary>(result.Site);
        Assert.Equal("Home", site.Name);
        Assert.Equal(SiteRole.Owner, site.Role);
        Assert.Equal(7, Guid.Parse(site.Id).Version);

        var organization = Assert.Single(identity.PhaseTwo.Tagged($"{userId}:k1"));
        Assert.Equal(site.Id, organization.Id);
        Assert.Equal(site.Id, organization.Name);
        Assert.Equal("Home", organization.DisplayName);
        Assert.All(SiteGrain.OrganizationRoles.Values, role => Assert.True(identity.PhaseTwo.HasRole(site.Id, role), $"No role '{role}'."));
        Assert.True(identity.PhaseTwo.IsMember(site.Id, userId));
        Assert.True(identity.PhaseTwo.HasGrant(site.Id, "owner", userId));

        Assert.Equal(["site.created", "site.membership-granted"], await identity.AliasesAsync($"site/{site.Id}"));
        Assert.Equal(
            ["user.site-creation-requested", "user.site-creation-completed", "user.site-membership-changed", "user.site-alerts-pulled"],
            await identity.AliasesAsync($"user/{userId}"));
        Assert.Equal(new Dictionary<string, SiteRole> { [site.Id] = SiteRole.Owner }, await identity.UserSitesAsync(userId));
    }

    [Fact]
    public async Task TheProjectionHoldsTheSiteAndTheOwnerWhenCreateSiteReturns()
    {
        var userId = NewUserId();

        var result = await identity.User(userId).CreateSite("k1", "Home", Ct);
        var siteId = result.Site!.Id;

        // No wait: hints are off and the next poll is 10 minutes of fake time away.
        var view = await identity.ReadModel.FindSiteAsync(siteId, userId, TestContext.Current.CancellationToken);

        Assert.NotNull(view);
        Assert.Equal("Home", view.Name);
        Assert.Equal(SiteLifecycle.Active, view.Lifecycle);
        Assert.Equal(SiteRole.Owner, view.CallerRole);

        var stranger = await identity.ReadModel.FindSiteAsync(siteId, NewUserId(), TestContext.Current.CancellationToken);
        Assert.NotNull(stranger);
        Assert.Null(stranger.CallerRole);
    }

    [Fact]
    public async Task ARetryAfterAFailureFollowingOrganizationCreationReturnsTheSameSiteWithOneOrganization()
    {
        var userId = NewUserId();
        identity.PhaseTwo.FailOnceAfterCreate();

        var failed = await identity.User(userId).CreateSite("k1", "Home", Ct);
        Assert.Equal(SiteCreationOutcome.IdentityProviderUnavailable, failed.Outcome);

        var pending = Assert.Single(identity.PhaseTwo.Tagged($"{userId}:k1"));

        var retried = await identity.User(userId).CreateSite("k1", "Home", Ct);
        var again = await identity.User(userId).CreateSite("k1", "Home", Ct);

        Assert.Equal(SiteCreationOutcome.Created, retried.Outcome);
        Assert.Equal(pending.Id, retried.Site!.Id);
        Assert.Equal(retried, again);
        Assert.Single(identity.PhaseTwo.Tagged($"{userId}:k1"));
        Assert.Equal(["site.created", "site.membership-granted"], await identity.AliasesAsync($"site/{pending.Id}"));
    }

    [Fact]
    public async Task WhileKeycloakIsDownTheRequestStaysPendingAndARetryResumesWithTheSameSiteId()
    {
        var userId = NewUserId();
        var user = identity.User(userId);

        // One silo, one fake: the switch is off again before any other test could see it.
        identity.PhaseTwo.Unavailable = true;
        SiteCreationResult down;
        try
        {
            down = await user.CreateSite("k1", "Home", Ct);
        }
        finally
        {
            identity.PhaseTwo.Unavailable = false;
        }

        Assert.Equal(SiteCreationOutcome.IdentityProviderUnavailable, down.Outcome);
        Assert.Equal(["user.site-creation-requested"], await identity.AliasesAsync($"user/{userId}"));
        Assert.Empty(identity.PhaseTwo.Tagged($"{userId}:k1"));

        var requested = Assert.IsType<SiteCreationRequested>(
            Assert.Single(await identity.Store.ReadStreamAsync($"user/{userId}", TestContext.Current.CancellationToken)).Data);

        var resumed = await user.CreateSite("k1", "Home", Ct);

        Assert.Equal(SiteCreationOutcome.Created, resumed.Outcome);
        Assert.Equal(requested.SiteId, resumed.Site!.Id);
    }

    [Theory]
    [InlineData(SiteWrite.EnsureRole)]
    [InlineData(SiteWrite.AddMember)]
    [InlineData(SiteWrite.GrantRole)]
    public async Task KeycloakFailingWhileTheSiteIsInitializedJournalsNoSiteEventsAndARetryCompletes(SiteWrite write)
    {
        var userId = NewUserId();
        var user = identity.User(userId);
        identity.PhaseTwo.FailOnceOn(write);

        var failed = await user.CreateSite("k1", "Home", Ct);

        Assert.Equal(SiteCreationOutcome.IdentityProviderUnavailable, failed.Outcome);
        var organization = Assert.Single(identity.PhaseTwo.Tagged($"{userId}:k1"));
        Assert.Empty(await identity.AliasesAsync($"site/{organization.Id}"));
        Assert.Equal(["user.site-creation-requested"], await identity.AliasesAsync($"user/{userId}"));

        var retried = await user.CreateSite("k1", "Home", Ct);

        Assert.Equal(SiteCreationOutcome.Created, retried.Outcome);
        Assert.Equal(organization.Id, retried.Site!.Id);
        Assert.Single(identity.PhaseTwo.Tagged($"{userId}:k1"));
        Assert.True(identity.PhaseTwo.HasGrant(organization.Id, "owner", userId));
        Assert.Equal(["site.created", "site.membership-granted"], await identity.AliasesAsync($"site/{organization.Id}"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(SiteWrite.EnsureRole)]
    [InlineData(SiteWrite.GrantRole)]
    public async Task AKeycloakThatNeverAnswersEndsInUnavailableWhenTheBudgetRunsOutAndARetryResumes(SiteWrite? stalledCall)
    {
        var userId = NewUserId();
        var user = identity.User(userId);
        var stalled = identity.PhaseTwo.StallOnce(stalledCall);

        var creating = user.CreateSite("k1", "Home", Ct);
        await stalled.WaitAsync(Ct);
        identity.Time.Advance(new KeycloakOptions().OperationBudget);
        var timedOut = await creating.WaitAsync(Ct);

        Assert.Equal(SiteCreationOutcome.IdentityProviderUnavailable, timedOut.Outcome);
        Assert.Equal(["user.site-creation-requested"], await identity.AliasesAsync($"user/{userId}"));
        var requested = Assert.IsType<SiteCreationRequested>(
            Assert.Single(await identity.Store.ReadStreamAsync($"user/{userId}", Ct)).Data);
        Assert.Empty(await identity.AliasesAsync($"site/{requested.SiteId}"));

        var retried = await user.CreateSite("k1", "Home", Ct);

        Assert.Equal(SiteCreationOutcome.Created, retried.Outcome);
        Assert.Equal(requested.SiteId, retried.Site!.Id);
        Assert.True(identity.PhaseTwo.HasGrant(requested.SiteId, "owner", userId));
        Assert.Equal(["site.created", "site.membership-granted"], await identity.AliasesAsync($"site/{requested.SiteId}"));
    }

    [Fact]
    public async Task ASiteThatWasNeverCreatedIsNotInTheProjection()
    {
        // The policy turns a missing Site into 404 site-not-found.
        var userId = NewUserId();
        await identity.User(userId).CreateSite("k1", "Home", Ct);

        Assert.Null(await identity.ReadModel.FindSiteAsync(Guid.CreateVersion7().ToString(), userId, Ct));
    }

    [Fact]
    public async Task AKeyReusedWithADifferentNameIsRefusedAndCreatesNothing()
    {
        var userId = NewUserId();
        var user = identity.User(userId);
        await user.CreateSite("k1", "Home", Ct);
        var writes = identity.PhaseTwo.Writes;

        var reused = await user.CreateSite("k1", "Other", Ct);

        Assert.Equal(SiteCreationOutcome.IdempotencyKeyReused, reused.Outcome);
        Assert.Null(reused.Site);
        Assert.Equal(writes, identity.PhaseTwo.Writes);
        Assert.Single(identity.PhaseTwo.Tagged($"{userId}:k1"));
        Assert.Equal(4, (await identity.AliasesAsync($"user/{userId}")).Count);
    }

    [Fact]
    public async Task AKeyIsKeptFor24HoursAndThenTreatedAsNew()
    {
        var userId = NewUserId();
        var user = identity.User(userId);

        var first = await user.CreateSite("k1", "Home", Ct);

        identity.Time.Advance(TimeSpan.FromHours(24) - TimeSpan.FromSeconds(1));
        Assert.Equal(first, await user.CreateSite("k1", "Home", Ct));

        identity.Time.Advance(TimeSpan.FromSeconds(1));
        var second = await user.CreateSite("k1", "Home", Ct);

        Assert.Equal(SiteCreationOutcome.Created, second.Outcome);
        Assert.NotEqual(first.Site!.Id, second.Site!.Id);
        Assert.Equal(2, identity.PhaseTwo.Tagged($"{userId}:k1").Count);
    }

    [Fact]
    public async Task TheSameKeyFromAnotherUserCreatesAnIndependentSite()
    {
        var a = await identity.User(NewUserId()).CreateSite("shared-key", "Home", Ct);
        var b = await identity.User(NewUserId()).CreateSite("shared-key", "Home", Ct);

        Assert.Equal(SiteCreationOutcome.Created, b.Outcome);
        Assert.NotEqual(a.Site!.Id, b.Site!.Id);
    }

    [Fact]
    public async Task InitializeIsIdempotentForTheSameOwnerAndRefusesAnother()
    {
        var userId = NewUserId();
        var created = await identity.User(userId).CreateSite("k1", "Home", Ct);
        var siteId = created.Site!.Id;
        var writes = identity.PhaseTwo.Writes;

        var repeated = await identity.Site(siteId).Initialize("Home", userId, Ct);
        var other = await identity.Site(siteId).Initialize("Home", NewUserId(), Ct);

        Assert.Equal(new SiteInitializationResult(SiteInitializationOutcome.Initialized, "Home"), repeated);
        Assert.Equal(SiteInitializationOutcome.Conflict, other.Outcome);
        Assert.Equal(writes, identity.PhaseTwo.Writes);
        Assert.Equal(["site.created", "site.membership-granted"], await identity.AliasesAsync($"site/{siteId}"));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewUserId() => Guid.NewGuid().ToString();
}
