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
    private const string WebClientSecretParameter = "coldframe-web-client-secret";
    private const string Realm = "coldframe";
    private const string WebClientId = "coldframe-web";
    private const string WebSignInCallback = "http://localhost:5173/.oidc/signin/callback";
    private const string WebSignOutCallback = "http://localhost:5173/.oidc/signout/callback";
    private const string MobileClientId = "coldframe-mobile";
    private const string MobileSignInCallback = "com.escendit.coldframe:/signin/callback";
    private const string MobileSignOutCallback = "com.escendit.coldframe:/signout/callback";
    private const string ServerClientId = "coldframe-server";
    private const string ServerClientSecretParameter = "coldframe-server-client-secret";
    private const string AudienceMapper = "oidc-audience-mapper";
    private const string CreateAdminUserAttribute = "_providerConfig.orgs.config.createAdminUser";

    private static readonly string[] EventListeners = ["jboss-logging", "temporal"];
    private static readonly string[] ServerServiceAccountRoles = ["manage-organizations", "view-organizations"];
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

    [Fact]
    public async Task ColdframeRealmPublishesADiscoveryDocumentWithPkce()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(AppHostFixture.ResourceTimeout);

        await fixture.WaitForHealthyAsync(KeycloakResource, timeout.Token);

        using var client = fixture.App.CreateHttpClient(KeycloakResource, "http");
        using var discovery = await GetJsonAsync(
            client,
            $"/realms/{Realm}/.well-known/openid-configuration",
            timeout.Token);
        var document = discovery.RootElement;

        var issuer = document.GetProperty("issuer").GetString();
        Assert.NotNull(issuer);
        Assert.EndsWith($"/realms/{Realm}", issuer, StringComparison.Ordinal);

        var methods = document.GetProperty("code_challenge_methods_supported")
            .EnumerateArray()
            .Select(method => method.GetString());
        Assert.Contains("S256", methods);

        foreach (var endpoint in (string[])["authorization_endpoint", "token_endpoint", "end_session_endpoint"])
        {
            Assert.False(
                string.IsNullOrEmpty(document.GetProperty(endpoint).GetString()),
                $"The discovery document has no '{endpoint}'.");
        }
    }

    [Fact]
    public async Task ColdframeRealmHasTheConfidentialWebClient()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(AppHostFixture.ResourceTimeout);

        await fixture.WaitForHealthyAsync(KeycloakResource, timeout.Token);

        using var keycloak = await CreateAdminClientAsync(timeout.Token);

        using (var realm = await GetJsonAsync(keycloak, $"/admin/realms/{Realm}", timeout.Token))
        {
            Assert.True(realm.RootElement.GetProperty("enabled").GetBoolean(), "The realm is disabled.");
            Assert.True(
                realm.RootElement.GetProperty("registrationAllowed").GetBoolean(),
                "The realm does not allow registration.");
            Assert.True(
                realm.RootElement.GetProperty("organizationsEnabled").GetBoolean(),
                "The realm does not have Organizations enabled.");
        }

        string id;

        using (var clients = await GetJsonAsync(
            keycloak,
            $"/admin/realms/{Realm}/clients?clientId={WebClientId}",
            timeout.Token))
        {
            var client = Assert.Single(clients.RootElement.EnumerateArray().ToArray());

            Assert.Equal(WebClientId, client.GetProperty("clientId").GetString());
            Assert.True(client.GetProperty("enabled").GetBoolean(), "The client is disabled.");
            Assert.False(client.GetProperty("publicClient").GetBoolean(), "The client is public.");
            Assert.False(client.GetProperty("bearerOnly").GetBoolean(), "The client is bearer-only.");
            Assert.True(client.GetProperty("standardFlowEnabled").GetBoolean(), "The standard flow is disabled.");
            Assert.False(client.GetProperty("implicitFlowEnabled").GetBoolean(), "The implicit flow is enabled.");
            Assert.False(
                client.GetProperty("directAccessGrantsEnabled").GetBoolean(),
                "Direct access grants are enabled.");
            Assert.False(
                client.GetProperty("serviceAccountsEnabled").GetBoolean(),
                "Service accounts are enabled.");
            Assert.Equal("client-secret", client.GetProperty("clientAuthenticatorType").GetString());

            var redirectUris = client.GetProperty("redirectUris")
                .EnumerateArray()
                .Select(uri => uri.GetString());
            Assert.Equal([WebSignInCallback], redirectUris);

            var attributes = client.GetProperty("attributes");
            Assert.Equal("S256", attributes.GetProperty("pkce.code.challenge.method").GetString());
            Assert.Equal(WebSignOutCallback, attributes.GetProperty("post.logout.redirect.uris").GetString());

            id = client.GetProperty("id").GetString()
                ?? throw new InvalidOperationException("The client has no id.");
        }

        // The realm file holds a placeholder; Keycloak substitutes the generated parameter on import.
        using var secret = await GetJsonAsync(
            keycloak,
            $"/admin/realms/{Realm}/clients/{id}/client-secret",
            timeout.Token);
        Assert.Equal(
            await GetParameterValueAsync(WebClientSecretParameter, timeout.Token),
            secret.RootElement.GetProperty("value").GetString());
    }

    [Fact]
    public async Task ColdframeRealmHasThePublicMobileClient()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(AppHostFixture.ResourceTimeout);

        await fixture.WaitForHealthyAsync(KeycloakResource, timeout.Token);

        using var keycloak = await CreateAdminClientAsync(timeout.Token);
        using var clients = await GetJsonAsync(
            keycloak,
            $"/admin/realms/{Realm}/clients?clientId={MobileClientId}",
            timeout.Token);
        var client = Assert.Single(clients.RootElement.EnumerateArray().ToArray());

        Assert.Equal(MobileClientId, client.GetProperty("clientId").GetString());
        Assert.True(client.GetProperty("enabled").GetBoolean(), "The client is disabled.");
        Assert.True(client.GetProperty("publicClient").GetBoolean(), "The client is confidential.");
        Assert.False(client.GetProperty("bearerOnly").GetBoolean(), "The client is bearer-only.");
        Assert.True(client.GetProperty("standardFlowEnabled").GetBoolean(), "The standard flow is disabled.");
        Assert.False(client.GetProperty("implicitFlowEnabled").GetBoolean(), "The implicit flow is enabled.");
        Assert.False(
            client.GetProperty("directAccessGrantsEnabled").GetBoolean(),
            "Direct access grants are enabled.");
        Assert.False(
            client.GetProperty("serviceAccountsEnabled").GetBoolean(),
            "Service accounts are enabled.");

        var redirectUris = client.GetProperty("redirectUris")
            .EnumerateArray()
            .Select(uri => uri.GetString());
        Assert.Equal([MobileSignInCallback], redirectUris);

        var attributes = client.GetProperty("attributes");
        Assert.Equal("S256", attributes.GetProperty("pkce.code.challenge.method").GetString());
        Assert.Equal(MobileSignOutCallback, attributes.GetProperty("post.logout.redirect.uris").GetString());
    }

    [Fact]
    public async Task ColdframeRealmHasTheServerServiceAccountWithOrganizationRolesOnly()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(AppHostFixture.ResourceTimeout);

        await fixture.WaitForHealthyAsync(KeycloakResource, timeout.Token);

        using var keycloak = await CreateAdminClientAsync(timeout.Token);

        string id;
        using (var clients = await GetJsonAsync(keycloak, $"/admin/realms/{Realm}/clients?clientId={ServerClientId}", timeout.Token))
        {
            var client = Assert.Single(clients.RootElement.EnumerateArray().ToArray());

            Assert.True(client.GetProperty("enabled").GetBoolean(), "The client is disabled.");
            Assert.False(client.GetProperty("publicClient").GetBoolean(), "The client is public.");
            Assert.True(client.GetProperty("serviceAccountsEnabled").GetBoolean(), "The service account is disabled.");
            Assert.False(client.GetProperty("standardFlowEnabled").GetBoolean(), "The standard flow is enabled.");
            Assert.False(client.GetProperty("implicitFlowEnabled").GetBoolean(), "The implicit flow is enabled.");
            Assert.False(client.GetProperty("directAccessGrantsEnabled").GetBoolean(), "Direct access grants are enabled.");
            Assert.Equal("client-secret", client.GetProperty("clientAuthenticatorType").GetString());

            id = client.GetProperty("id").GetString()
                ?? throw new InvalidOperationException("The client has no id.");
        }

        using (var secret = await GetJsonAsync(keycloak, $"/admin/realms/{Realm}/clients/{id}/client-secret", timeout.Token))
        {
            Assert.Equal(
                await GetParameterValueAsync(ServerClientSecretParameter, timeout.Token),
                secret.RootElement.GetProperty("value").GetString());
        }

        string userId;
        using (var serviceAccount = await GetJsonAsync(keycloak, $"/admin/realms/{Realm}/clients/{id}/service-account-user", timeout.Token))
        {
            userId = serviceAccount.RootElement.GetProperty("id").GetString()
                ?? throw new InvalidOperationException("The service account has no id.");
        }

        string realmManagement;
        using (var clients = await GetJsonAsync(keycloak, $"/admin/realms/{Realm}/clients?clientId=realm-management", timeout.Token))
        {
            realmManagement = Assert.Single(clients.RootElement.EnumerateArray().ToArray()).GetProperty("id").GetString()
                ?? throw new InvalidOperationException("realm-management has no id.");
        }

        using var roles = await GetJsonAsync(
            keycloak,
            $"/admin/realms/{Realm}/users/{userId}/role-mappings/clients/{realmManagement}",
            timeout.Token);

        // Exactly the two Organization roles: no Keycloak administration beyond Organizations.
        Assert.Equal(
            ServerServiceAccountRoles,
            roles.RootElement.EnumerateArray().Select(role => role.GetProperty("name").GetString()).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(WebClientId)]
    [InlineData(MobileClientId)]
    public async Task ClientTokensCarryTheServerAudience(string clientId)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(AppHostFixture.ResourceTimeout);

        await fixture.WaitForHealthyAsync(KeycloakResource, timeout.Token);

        using var keycloak = await CreateAdminClientAsync(timeout.Token);
        using var clients = await GetJsonAsync(keycloak, $"/admin/realms/{Realm}/clients?clientId={clientId}", timeout.Token);
        var client = Assert.Single(clients.RootElement.EnumerateArray().ToArray());

        Assert.True(client.TryGetProperty("protocolMappers", out var mappers), $"{clientId} has no protocol mappers.");
        Assert.Contains(
            mappers.EnumerateArray(),
            mapper => mapper.GetProperty("protocolMapper").GetString() == AudienceMapper
                && mapper.GetProperty("config").GetProperty("included.client.audience").GetString() == ServerClientId
                && mapper.GetProperty("config").GetProperty("access.token.claim").GetString() == "true");
    }

    [Fact]
    public async Task ColdframeRealmCreatesNoPlaceholderOrganizationAdmin()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(AppHostFixture.ResourceTimeout);

        await fixture.WaitForHealthyAsync(KeycloakResource, timeout.Token);

        using var keycloak = await CreateAdminClientAsync(timeout.Token);
        using var realm = await GetJsonAsync(keycloak, $"/admin/realms/{Realm}", timeout.Token);

        Assert.Equal(
            "false",
            realm.RootElement.GetProperty("attributes").GetProperty(CreateAdminUserAttribute).GetString());
    }

    private static async Task<JsonDocument> GetJsonAsync(
        HttpClient client,
        string path,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
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
