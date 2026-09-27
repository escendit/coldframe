using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace Coldframe.Server.IntegrationTests;

public sealed class KeycloakTests(AppHostFixture fixture)
{
    private const string KeycloakResource = "keycloak";
    private const string TemporalResource = "temporal";
    private const string TemporalNamespace = "coldframe";
    private const string AdminEventWorkflow = "IdentityAdminEvent";
    private const string AdminUserParameter = "keycloak-admin-username";
    private const string AdminPasswordParameter = "keycloak-admin-password";

    private static readonly string[] EventListeners = ["jboss-logging", "temporal"];
    private static readonly TimeSpan WorkflowTimeout = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan WorkflowPollInterval = TimeSpan.FromMilliseconds(500);

    [Fact]
    public async Task ServerInfoReportsPhaseTwoWithTheTemporalEventListener()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(AppHostFixture.ResourceTimeout);

        await fixture.WaitForHealthyAsync(KeycloakResource, timeout.Token);

        using var client = await CreateAdminClientAsync(timeout.Token);
        using var response = await client.GetAsync(new Uri("/admin/serverinfo", UriKind.Relative), timeout.Token);
        response.EnsureSuccessStatusCode();

        await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var serverInfo = await JsonDocument.ParseAsync(body, cancellationToken: timeout.Token);

        var version = serverInfo.RootElement.GetProperty("systemInfo").GetProperty("version").GetString();
        Assert.Equal("26.6.7", version);

        var providers = serverInfo.RootElement.GetProperty("providers");

        // keycloak-orgs ships only in the Phase Two distribution.
        Assert.True(
            providers.TryGetProperty("organizationProvider", out _),
            "Keycloak does not list the Phase Two organization SPI.");

        var eventListeners = providers.GetProperty("eventsListener").GetProperty("providers");
        Assert.True(
            eventListeners.TryGetProperty("temporal", out _),
            "Keycloak does not list the 'temporal' event listener.");
    }

    [Fact]
    public async Task AdminEventStartsAWorkflowInTheTemporalNamespace()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(AppHostFixture.ResourceTimeout);

        await fixture.WaitForHealthyAsync(TemporalResource, timeout.Token);
        await fixture.WaitForHealthyAsync(KeycloakResource, timeout.Token);

        using var keycloak = await CreateAdminClientAsync(timeout.Token);

        using var configured = await keycloak.PutAsJsonAsync(
            new Uri("/admin/realms/master/events/config", UriKind.Relative),
            new { adminEventsEnabled = true, eventsListeners = EventListeners },
            timeout.Token);
        configured.EnsureSuccessStatusCode();

        using var created = await keycloak.PostAsJsonAsync(
            new Uri("/admin/realms/master/users", UriKind.Relative),
            new { username = $"listener-probe-{Guid.NewGuid():N}", enabled = false },
            timeout.Token);
        created.EnsureSuccessStatusCode();

        using var temporal = fixture.App.CreateHttpClient(TemporalResource, "ui");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        deadline.CancelAfter(WorkflowTimeout);
        using var poll = new PeriodicTimer(WorkflowPollInterval);

        try
        {
            do
            {
                if (await HasAdminEventWorkflowAsync(temporal, deadline.Token))
                {
                    return;
                }
            }
            while (await poll.WaitForNextTickAsync(deadline.Token));
        }
        catch (OperationCanceledException) when (!timeout.IsCancellationRequested)
        {
            Assert.Fail(
                $"No '{AdminEventWorkflow}' workflow appeared in the Temporal namespace " +
                $"'{TemporalNamespace}' within {WorkflowTimeout.TotalSeconds:0} seconds.");
        }
    }

    private static async Task<bool> HasAdminEventWorkflowAsync(HttpClient temporal, CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString($"WorkflowType = '{AdminEventWorkflow}'");
        var path = new Uri($"/api/v1/namespaces/{TemporalNamespace}/workflows?query={query}", UriKind.Relative);

        using var response = await temporal.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var workflows = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);

        return workflows.RootElement.TryGetProperty("executions", out var executions)
            && executions.GetArrayLength() > 0;
    }

    private async Task<HttpClient> CreateAdminClientAsync(CancellationToken cancellationToken)
    {
        var client = fixture.App.CreateHttpClient(KeycloakResource, "http");

        try
        {
            var accessToken = await RequestAdminTokenAsync(client, cancellationToken);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private async Task<string> RequestAdminTokenAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "password",
            ["client_id"] = "admin-cli",
            ["username"] = await GetParameterValueAsync(AdminUserParameter, cancellationToken),
            ["password"] = await GetParameterValueAsync(AdminPasswordParameter, cancellationToken),
        });

        using var response = await client.PostAsync(
            new Uri("/realms/master/protocol/openid-connect/token", UriKind.Relative),
            form,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var token = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);

        return token.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Keycloak returned no access token.");
    }

    private async Task<string> GetParameterValueAsync(string name, CancellationToken cancellationToken)
    {
        var parameter = Assert.IsType<ParameterResource>(fixture.GetResource(name));

        return await parameter.GetValueAsync(cancellationToken)
            ?? throw new InvalidOperationException($"The parameter '{name}' has no value.");
    }
}
