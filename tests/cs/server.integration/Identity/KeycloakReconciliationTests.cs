using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Aspire.Hosting.Testing;
using Coldframe.Server.Identity.Reconciliation;
using Coldframe.Server.IntegrationTests.Edge;
using Coldframe.Server.IntegrationTests.Journal;
using Temporalio.Client;

namespace Coldframe.Server.IntegrationTests.Identity;

/// <summary>
/// The Keycloak event pipeline end to end on the AppHost: Keycloak is changed through the Phase Two API
/// as an administrator would, <c>keycloak-temporal-extensions</c> starts <c>IdentityAdminEvent</c> in
/// Temporal, and the Server's worker reconciles the Site (AD-3, AD-5, AD-20).
/// </summary>
public sealed class KeycloakReconciliationTests(EdgeApiFixture edge) : IClassFixture<EdgeApiFixture>
{
    private const string TemporalResource = "temporal";
    private const string TemporalNamespace = "coldframe";
    private const string AdminEventWorkflow = "IdentityAdminEvent";
    private const string AdminTaskQueue = "keycloak-admin-queue";
    private const string UserEventWorkflow = "IdentityUserEvent";
    private const string UserTaskQueue = "keycloak-user-queue";
    private const string Orgs = $"/realms/{EdgeApiFixture.Realm}/orgs";

    // Reconciliation waits on Keycloak, Temporal and the worker's retries, which start at 1 s.
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task AMemberAddedInKeycloakGetsTheirRoleAndLosesItWhenRemoved()
    {
        var (owner, siteId) = await CreateSiteAsync("owner");
        var member = await edge.CreateUserAsync("member", Ct);
        using var organizations = await edge.CreateOrganizationsClientAsync(Ct);

        await SendAsync(organizations, HttpMethod.Put, $"{Orgs}/{siteId}/members/{member.UserId}");
        await SendAsync(organizations, HttpMethod.Put, $"{Orgs}/{siteId}/roles/administrator/users/{member.UserId}");

        await WaitForAsync(member, siteId, HttpStatusCode.OK, "Administrator");

        await SendAsync(organizations, HttpMethod.Delete, $"{Orgs}/{siteId}/members/{member.UserId}");

        await WaitForAsync(member, siteId, HttpStatusCode.Forbidden);
        await WaitForAsync(owner, siteId, HttpStatusCode.OK, "Owner");
        Assert.Contains("site.membership-revoked", await edge.AliasesAsync($"site/{siteId}", Ct));
    }

    [Fact]
    public async Task ADuplicateEventAndAnEchoOfTheSitesOwnWriteLeaveTheStreamUnchanged()
    {
        var (owner, siteId) = await CreateSiteAsync("owner");
        var member = await edge.CreateUserAsync("member", Ct);
        using var organizations = await edge.CreateOrganizationsClientAsync(Ct);

        await SendAsync(organizations, HttpMethod.Put, $"{Orgs}/{siteId}/members/{member.UserId}");
        await SendAsync(organizations, HttpMethod.Put, $"{Orgs}/{siteId}/roles/member/users/{member.UserId}");
        await WaitForAsync(member, siteId, HttpStatusCode.OK, "Member");

        var aliases = await edge.AliasesAsync($"site/{siteId}", Ct);
        var client = await ConnectTemporalAsync();

        // A copy of the membership event, delivered again.
        await RunAdminEventAsync(client, new KeycloakAdminEvent
        {
            Id = Guid.NewGuid().ToString(),
            RealmId = EdgeApiFixture.Realm,
            ResourceType = AdminEventRoute.MembershipResource,
            OperationType = "CREATE",
            ResourcePath = $"orgs/{siteId}/members/{member.UserId}",
        });

        // The event Keycloak raised for the Site grain's own owner grant.
        await RunAdminEventAsync(client, new KeycloakAdminEvent
        {
            Id = Guid.NewGuid().ToString(),
            RealmId = EdgeApiFixture.Realm,
            ResourceType = AdminEventRoute.RoleMappingResource,
            OperationType = "CREATE",
            ResourcePath = $"orgs/{siteId}/roles/owner/users/{owner.UserId}",
        });

        Assert.Equal(aliases, await edge.AliasesAsync($"site/{siteId}", Ct));
    }

    [Fact]
    public async Task RevokingOwnerFromTheOnlyOwnerInKeycloakIsRefusedAndTheOwnerKeepsOwner()
    {
        var (owner, siteId) = await CreateSiteAsync("owner");
        using var organizations = await edge.CreateOrganizationsClientAsync(Ct);

        await SendAsync(organizations, HttpMethod.Delete, $"{Orgs}/{siteId}/roles/owner/users/{owner.UserId}");

        await JournalWait.UntilAsync(
            async () => (await edge.AliasesAsync($"site/{siteId}", Ct)).Contains("site.ownerless-edit-refused"),
            $"Site {siteId} journals site.ownerless-edit-refused",
            Settle);

        await WaitForAsync(owner, siteId, HttpStatusCode.OK, "Owner");
        Assert.Single(await edge.AliasesAsync($"site/{siteId}", Ct), alias => alias == "site.ownerless-edit-refused");

        // Nothing was written back to Keycloak.
        using var owners = await organizations.GetAsync(new Uri($"{Orgs}/{siteId}/roles/owner/users", UriKind.Relative), Ct);
        owners.EnsureSuccessStatusCode();
        using var body = await EdgeApiFixture.ReadJsonAsync(owners, Ct);
        Assert.DoesNotContain(body.RootElement.EnumerateArray(), user => user.GetProperty("id").GetString() == owner.UserId);
    }

    [Fact]
    public async Task AnOrganizationDeletedInKeycloakDeletesTheSite()
    {
        var (owner, siteId) = await CreateSiteAsync("owner");
        using var organizations = await edge.CreateOrganizationsClientAsync(Ct);

        await SendAsync(organizations, HttpMethod.Delete, $"{Orgs}/{siteId}");

        await WaitForAsync(owner, siteId, HttpStatusCode.NotFound);
        Assert.Equal("site.deleted", (await edge.AliasesAsync($"site/{siteId}", Ct))[^1]);
    }

    [Fact]
    public async Task ARenameInKeycloakRenamesTheSite()
    {
        var (owner, siteId) = await CreateSiteAsync("owner");
        using var organizations = await edge.CreateOrganizationsClientAsync(Ct);

        // Phase Two replaces the Organization with the representation it is given.
        var organization = await organizations.GetFromJsonAsync<JsonObject>(new Uri($"{Orgs}/{siteId}", UriKind.Relative), Ct);
        Assert.NotNull(organization);
        organization["displayName"] = "Allotment";
        using (var updated = await organizations.PutAsJsonAsync(new Uri($"{Orgs}/{siteId}", UriKind.Relative), organization, Ct))
        {
            Assert.True(updated.IsSuccessStatusCode, $"PUT {Orgs}/{siteId} answered {(int)updated.StatusCode}.");
        }

        using var server = edge.CreateServerClient(owner.AccessToken);
        await JournalWait.UntilAsync(
            async () =>
            {
                using var response = await server.GetAsync(new Uri($"/sites/{siteId}", UriKind.Relative), Ct);
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    return false;
                }

                using var body = await EdgeApiFixture.ReadJsonAsync(response, Ct);
                return body.RootElement.GetProperty("name").GetString() == "Allotment";
            },
            $"GET /sites/{siteId} answers the name Allotment",
            Settle);
    }

    [Fact]
    public async Task AUserEventWorkflowCompletes()
    {
        var client = await ConnectTemporalAsync();
        var userEvent = new KeycloakUserEvent
        {
            Id = Guid.NewGuid().ToString(),
            Time = 1_790_000_000_000,
            Type = "LOGIN",
            RealmId = EdgeApiFixture.Realm,
            ClientId = "coldframe-web",
            UserId = Guid.NewGuid().ToString(),
            Details = new Dictionary<string, string?>(StringComparer.Ordinal) { ["auth_method"] = "openid-connect" },
        };

        var handle = await client.StartWorkflowAsync(
            UserEventWorkflow,
            [userEvent],
            new WorkflowOptions(userEvent.Id, UserTaskQueue) { ExecutionTimeout = Settle });

        await handle.GetResultAsync().WaitAsync(Settle, Ct);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task SendAsync(HttpClient client, HttpMethod method, string path)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        using var response = await client.SendAsync(request, Ct);

        Assert.True(response.IsSuccessStatusCode, $"{method} {path} answered {(int)response.StatusCode}.");
    }

    private static async Task RunAdminEventAsync(TemporalClient client, KeycloakAdminEvent adminEvent)
    {
        var handle = await client.StartWorkflowAsync(
            AdminEventWorkflow,
            [adminEvent],
            new WorkflowOptions(adminEvent.Id!, AdminTaskQueue) { ExecutionTimeout = Settle });

        await handle.GetResultAsync();
    }

    private async Task<TemporalClient> ConnectTemporalAsync()
    {
        var endpoint = edge.AppHost.App.GetEndpoint(TemporalResource, "grpc");

        return await TemporalClient.ConnectAsync(new TemporalClientConnectOptions($"{endpoint.Host}:{endpoint.Port}")
        {
            Namespace = TemporalNamespace,
        });
    }

    private async Task<(TestUser Owner, string SiteId)> CreateSiteAsync(string label)
    {
        var owner = await edge.CreateUserAsync(label, Ct);
        using var server = edge.CreateServerClient(owner.AccessToken);
        using var created = await EdgeApiTests.PostSiteAsync(server, Guid.NewGuid().ToString(), new { name = "Home" }, Ct);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var body = await EdgeApiFixture.ReadJsonAsync(created, Ct);

        return (owner, body.RootElement.GetProperty("id").GetString()!);
    }

    // Waits until the user's GET /sites/{id} answers the status and, for 200, the Role.
    private async Task WaitForAsync(TestUser user, string siteId, HttpStatusCode status, string? role = null)
    {
        using var server = edge.CreateServerClient(user.AccessToken);

        await JournalWait.UntilAsync(
            async () =>
            {
                using var response = await server.GetAsync(new Uri($"/sites/{siteId}", UriKind.Relative), Ct);
                var actualRole = string.Empty;

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    using var body = await EdgeApiFixture.ReadJsonAsync(response, Ct);
                    actualRole = body.RootElement.GetProperty("role").GetString();
                }

                return response.StatusCode == status && (role is null || actualRole == role);
            },
            $"GET /sites/{siteId} answers {(int)status} {role} for {user.Username}",
            Settle);
    }
}
