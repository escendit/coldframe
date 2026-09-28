using Coldframe.Contracts.Sites;
using Coldframe.Server.Identity.Reconciliation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Temporalio.Exceptions;
using Temporalio.Testing;

namespace Coldframe.Server.IntegrationTests.Identity;

/// <summary>
/// The activity of the <c>IdentityAdminEvent</c> workflow in Temporal's <see cref="ActivityEnvironment"/>,
/// against the TestCluster's Site and User grains and the fake Phase Two (AD-3).
/// </summary>
public sealed class IdentityReconciliationActivitiesTests(IdentityCluster identity) : IClassFixture<IdentityCluster>
{
    private const string Realm = "coldframe";

    [Fact]
    public async Task AnEventKeycloakDoesNotShowYetFailsRetryablyUntilAttemptFive()
    {
        var (siteId, _) = await CreateSiteAsync();
        var adminEvent = MembershipEvent("CREATE", siteId, NewUserId());

        for (var attempt = 1; attempt < IdentityReconciliationActivities.AcceptUnconfirmedFromAttempt; attempt++)
        {
            var failure = await Assert.ThrowsAsync<ApplicationFailureException>(() => RunAsync(adminEvent, attempt));

            Assert.Equal(IdentityReconciliationActivities.NotYetVisibleFailure, failure.ErrorType);
            Assert.False(failure.NonRetryable);
        }

        await RunAsync(adminEvent, IdentityReconciliationActivities.AcceptUnconfirmedFromAttempt);

        Assert.Equal(["site.created", "site.membership-granted"], await identity.AliasesAsync($"site/{siteId}"));
    }

    [Fact]
    public async Task AnUnavailableKeycloakFailsRetryably()
    {
        var (siteId, _) = await CreateSiteAsync();

        identity.PhaseTwo.Unavailable = true;
        ApplicationFailureException failure;
        try
        {
            failure = await Assert.ThrowsAsync<ApplicationFailureException>(
                () => RunAsync(OrganizationEvent("UPDATE", $"orgs/{siteId}/{siteId}"), attempt: 7));
        }
        finally
        {
            identity.PhaseTwo.Unavailable = false;
        }

        Assert.Equal(IdentityReconciliationActivities.IdentityProviderUnavailableFailure, failure.ErrorType);
        Assert.False(failure.NonRetryable);
    }

    [Fact]
    public async Task AMemberAddedAndRemovedInKeycloakReachesTheirUserGrain()
    {
        var (siteId, ownerId) = await CreateSiteAsync();
        var u = NewUserId();

        identity.PhaseTwo.ConsoleAddMember(siteId, u, "administrator");
        await RunAsync(MembershipEvent("CREATE", siteId, u));

        Assert.Equal(new Dictionary<string, SiteRole> { [siteId] = SiteRole.Administrator }, await identity.UserSitesAsync(u));
        Assert.Equal(new Dictionary<string, SiteRole> { [siteId] = SiteRole.Owner }, await identity.UserSitesAsync(ownerId));

        identity.PhaseTwo.ConsoleRemoveMember(siteId, u);
        await RunAsync(MembershipEvent("DELETE", siteId, u));

        Assert.Empty(await identity.UserSitesAsync(u));

        // Added again: no longer a former member, so the fan-out gives the Site back.
        identity.PhaseTwo.ConsoleAddMember(siteId, u, "member");
        await RunAsync(MembershipEvent("CREATE", siteId, u));

        var result = await identity.Site(siteId).Reconcile(null, acceptUnconfirmed: false, Ct);
        Assert.DoesNotContain(u, result.FormerMembers);
        Assert.Equal(SiteRole.Member, result.Members[u]);
        Assert.Equal(new Dictionary<string, SiteRole> { [siteId] = SiteRole.Member }, await identity.UserSitesAsync(u));
    }

    [Fact]
    public async Task EveryRunFansOutEvenWhenTheSiteIsUnchanged()
    {
        var (siteId, _) = await CreateSiteAsync();
        var u = NewUserId();

        // The Site grain applied the change, but the fan-out never ran (a cut-short attempt).
        identity.PhaseTwo.ConsoleAddMember(siteId, u, "member");
        var applied = await identity.Site(siteId).Reconcile(null, acceptUnconfirmed: false, Ct);
        Assert.Equal(SiteReconciliationOutcome.Changed, applied.Outcome);
        Assert.Empty(await identity.UserSitesAsync(u));

        // A duplicate delivery finds the Site unchanged and still completes the fan-out.
        await RunAsync(MembershipEvent("CREATE", siteId, u));

        Assert.Equal(new Dictionary<string, SiteRole> { [siteId] = SiteRole.Member }, await identity.UserSitesAsync(u));
    }

    [Fact]
    public async Task ADeletedSiteLeavesEveryMembersSiteSet()
    {
        var (siteId, ownerId) = await CreateSiteAsync();
        var u = NewUserId();
        identity.PhaseTwo.ConsoleAddMember(siteId, u, "member");
        await RunAsync(MembershipEvent("CREATE", siteId, u));

        identity.PhaseTwo.ConsoleDelete(siteId);
        await RunAsync(OrganizationEvent("DELETE", $"orgs/{siteId}"));

        Assert.Equal("site.deleted", (await identity.AliasesAsync($"site/{siteId}"))[^1]);
        Assert.Empty(await identity.UserSitesAsync(ownerId));
        Assert.Empty(await identity.UserSitesAsync(u));

        // A later event on the deleted Site completes without a change.
        await RunAsync(OrganizationEvent("DELETE", $"orgs/{siteId}"));
        Assert.Equal(1, (await identity.AliasesAsync($"site/{siteId}")).Count(alias => alias == "site.deleted"));
    }

    [Fact]
    public async Task IgnoredEventsCompleteWithoutAnyChange()
    {
        var (siteId, ownerId) = await CreateSiteAsync();
        identity.PhaseTwo.ConsoleRename(siteId, "Allotment");
        var reads = identity.PhaseTwo.RosterReads;

        await RunAsync(OrganizationEvent("UPDATE", $"orgs/{siteId}/{siteId}") with { RealmId = "master" });
        await RunAsync(OrganizationEvent("UPDATE", $"orgs/{siteId}/{siteId}") with { Error = "unknown_error" });
        await RunAsync(OrganizationEvent("CREATE", $"orgs/{siteId}") with { ResourceType = "TEAM" });
        await RunAsync(OrganizationEvent("CREATE", "orgs"));
        await RunAsync(OrganizationEvent("CREATE", $"orgs/{siteId}/roles/billing/users/{ownerId}") with { ResourceType = AdminEventRoute.RoleMappingResource });

        // A Site the Server never created.
        await RunAsync(OrganizationEvent("CREATE", $"orgs/{Guid.CreateVersion7()}"));

        Assert.Equal(reads, identity.PhaseTwo.RosterReads);
        Assert.Equal(["site.created", "site.membership-granted"], await identity.AliasesAsync($"site/{siteId}"));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewUserId() => Guid.NewGuid().ToString();

    private static KeycloakAdminEvent MembershipEvent(string operation, string siteId, string userId) => new()
    {
        Id = Guid.NewGuid().ToString(),
        RealmId = Realm,
        ResourceType = AdminEventRoute.MembershipResource,
        OperationType = operation,
        ResourcePath = $"orgs/{siteId}/members/{userId}",
    };

    private static KeycloakAdminEvent OrganizationEvent(string operation, string path) => new()
    {
        Id = Guid.NewGuid().ToString(),
        RealmId = Realm,
        ResourceType = AdminEventRoute.OrganizationResource,
        OperationType = operation,
        ResourcePath = path,
    };

    private Task RunAsync(KeycloakAdminEvent adminEvent, int attempt = 1)
    {
        var activities = new IdentityReconciliationActivities(
            identity.Cluster.GrainFactory,
            Options.Create(new KeycloakEventOptions { RealmId = Realm }),
            NullLogger<IdentityReconciliationActivities>.Instance);
        var environment = new ActivityEnvironment { Info = ActivityEnvironment.DefaultInfo with { Attempt = attempt } };

        return environment.RunAsync(() => activities.ReconcileAsync(adminEvent));
    }

    private async Task<(string SiteId, string OwnerId)> CreateSiteAsync()
    {
        var ownerId = NewUserId();
        var created = await identity.User(ownerId).CreateSite("k1", "Home", Ct);
        Assert.Equal(SiteCreationOutcome.Created, created.Outcome);
        return (created.Site!.Id, ownerId);
    }
}
