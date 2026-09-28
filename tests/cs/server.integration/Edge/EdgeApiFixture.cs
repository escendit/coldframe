using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Coldframe.Server.Journal;
using Npgsql;

namespace Coldframe.Server.IntegrationTests.Edge;

/// <summary>
/// A signed-in test User: the User ID (the <c>sub</c>) and an access token for the Server (or, when created
/// without the Server audience, an access token of the realm that lacks it).
/// </summary>
public sealed record TestUser(string UserId, string Username, string AccessToken);

/// <summary>
/// Talks to the AppHost's Server and Keycloak for the Edge API suites. It creates, through the admin API,
/// two test clients with direct access grants, one with the <c>coldframe-server</c> audience mapper and one
/// without, and test Users that sign in through them.
/// </summary>
public sealed class EdgeApiFixture(AppHostFixture fixture) : IAsyncLifetime
{
    public const string Realm = "coldframe";
    public const string ServerAudience = "coldframe-server";

    private const string ServerResource = "server";
    private const string KeycloakResource = "keycloak";
    private const string DatabaseResource = "coldframe";
    private const string AdminUserParameter = "keycloak-admin-username";
    private const string AdminPasswordParameter = "keycloak-admin-password";
    private const string ServerClientSecretParameter = "coldframe-server-client-secret";

    private string? _testClientId;
    private string? _noAudienceClientId;
    private NpgsqlDataSource? _dataSource;

    public AppHostFixture AppHost => fixture;

    /// <summary>
    /// The AppHost's <c>coldframe</c> database, the one the Server journals to.
    /// </summary>
    public NpgsqlDataSource Database => _dataSource ?? throw new InvalidOperationException("The fixture has not been initialized.");

    public async ValueTask InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(AppHostFixture.ResourceTimeout);

        await fixture.WaitForHealthyAsync(KeycloakResource, timeout.Token);
        await fixture.WaitForHealthyAsync(ServerResource, timeout.Token);

        _dataSource = NpgsqlDataSource.Create(
            await fixture.App.GetConnectionStringAsync(DatabaseResource, timeout.Token)
            ?? throw new InvalidOperationException($"The AppHost gives '{DatabaseResource}' no connection string."));

        _testClientId = $"coldframe-tests-{Guid.NewGuid():N}";
        _noAudienceClientId = $"coldframe-tests-no-audience-{Guid.NewGuid():N}";

        using var admin = await CreateAdminClientAsync(timeout.Token);
        await CreateTestClientAsync(admin, _testClientId, withServerAudience: true, timeout.Token);
        await CreateTestClientAsync(admin, _noAudienceClientId, withServerAudience: false, timeout.Token);
    }

    public async ValueTask DisposeAsync()
    {
        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync();
        }
    }

    /// <summary>
    /// An HTTP client for the Server, optionally with a bearer token.
    /// </summary>
    public HttpClient CreateServerClient(string? accessToken = null)
    {
        var client = fixture.App.CreateHttpClient(ServerResource, "http");

        if (accessToken is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return client;
    }

    /// <summary>
    /// Creates a Keycloak User in the realm and signs it in through the test client.
    /// </summary>
    /// <param name="label">A readable prefix of the username.</param>
    /// <param name="cancellationToken">Cancels the calls.</param>
    /// <param name="withServerAudience">
    /// <see langword="false"/> signs in through the test client without the audience mapper, so the token
    /// is valid for the realm but lacks the <c>coldframe-server</c> audience.
    /// </param>
    public async Task<TestUser> CreateUserAsync(string label, CancellationToken cancellationToken, bool withServerAudience = true)
    {
        var username = $"{label}-{Guid.NewGuid():N}";
        var password = Guid.NewGuid().ToString("N");

        using var admin = await CreateAdminClientAsync(cancellationToken);
        using var created = await admin.PostAsJsonAsync(
            new Uri($"/admin/realms/{Realm}/users", UriKind.Relative),
            new
            {
                username,
                email = $"{username}@example.org",
                firstName = label,
                lastName = "Test",
                enabled = true,
                emailVerified = true,
                credentials = new[] { new { type = "password", value = password, temporary = false } },
            },
            cancellationToken);
        created.EnsureSuccessStatusCode();

        var userId = created.Headers.Location?.Segments[^1]
            ?? throw new InvalidOperationException("Keycloak returned no location for the new User.");

        using var keycloak = fixture.App.CreateHttpClient(KeycloakResource, "http");
        var token = await RequestTokenAsync(
            keycloak,
            Realm,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "password",
                ["client_id"] = (withServerAudience ? _testClientId : _noAudienceClientId)!,
                ["username"] = username,
                ["password"] = password,
                ["scope"] = "openid",
            },
            cancellationToken);

        return new TestUser(userId, username, token);
    }

    /// <summary>
    /// An access token of the master realm's administrator: a valid token, but from the wrong issuer.
    /// </summary>
    public async Task<string> RequestAdminTokenAsync(CancellationToken cancellationToken)
    {
        using var keycloak = fixture.App.CreateHttpClient(KeycloakResource, "http");

        return await RequestTokenAsync(
            keycloak,
            "master",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "password",
                ["client_id"] = "admin-cli",
                ["username"] = await GetParameterValueAsync(AdminUserParameter, cancellationToken),
                ["password"] = await GetParameterValueAsync(AdminPasswordParameter, cancellationToken),
            },
            cancellationToken);
    }

    /// <summary>
    /// A client for the Phase Two Organizations API (<c>/realms/coldframe/orgs</c>), signed in as the
    /// Server's own service account, to check what the Server wrote.
    /// </summary>
    public async Task<HttpClient> CreateOrganizationsClientAsync(CancellationToken cancellationToken)
    {
        var client = fixture.App.CreateHttpClient(KeycloakResource, "http");

        try
        {
            var token = await RequestTokenAsync(
                client,
                Realm,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = ServerAudience,
                    ["client_secret"] = await GetParameterValueAsync(ServerClientSecretParameter, cancellationToken),
                },
                cancellationToken);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Appends events to a fresh stream of the Server's journal, exactly as a grain would.
    /// </summary>
    /// <returns>The global position of the last appended event.</returns>
    public async Task<long> AppendAsync(string streamId, IReadOnlyList<object> events, CancellationToken cancellationToken)
    {
        var store = new JournalStore(
            Database,
            new JournalSerializer(new EventTypeRegistry(new JournalOptions().EventAssemblies)),
            TimeProvider.System,
            new OutboxWakeup());

        Assert.True(await store.AppendAsync(streamId, 0, events, cancellationToken), $"The stream {streamId} exists already.");

        var appended = await store.ReadStreamAsync(streamId, cancellationToken);
        return appended[^1].Position;
    }

    /// <summary>
    /// Waits until the Server's identity projector has applied the journal up to <paramref name="position"/>.
    /// </summary>
    public Task WaitForIdentityCheckpointAsync(long position) =>
        Journal.JournalWait.UntilAsync(
            async () =>
            {
                await using var command = Database.CreateCommand(
                    "SELECT COALESCE((SELECT position FROM projection_checkpoints WHERE projector = 'identity'), 0)");
                return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))! >= position;
            },
            $"the identity checkpoint reaches position {position}");

    /// <summary>
    /// The aliases of a stream's events in the Server's journal, in version order.
    /// </summary>
    public async Task<List<string>> AliasesAsync(string streamId, CancellationToken cancellationToken)
    {
        var aliases = new List<string>();
        await using var command = Database.CreateCommand(
            "SELECT type_alias FROM journal_events WHERE stream_id = @stream_id ORDER BY version");
        command.Parameters.AddWithValue("stream_id", streamId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            aliases.Add(reader.GetString(0));
        }

        return aliases;
    }

    public static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
    }

    private static async Task CreateTestClientAsync(
        HttpClient admin,
        string clientId,
        bool withServerAudience,
        CancellationToken cancellationToken)
    {
        object[] mappers = withServerAudience
            ?
            [
                new
                {
                    name = "coldframe-server audience",
                    protocol = "openid-connect",
                    protocolMapper = "oidc-audience-mapper",
                    config = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["included.client.audience"] = ServerAudience,
                        ["access.token.claim"] = "true",
                        ["id.token.claim"] = "false",
                    },
                },
            ]
            : [];

        using var created = await admin.PostAsJsonAsync(
            new Uri($"/admin/realms/{Realm}/clients", UriKind.Relative),
            new
            {
                clientId,
                enabled = true,
                publicClient = true,
                standardFlowEnabled = false,
                directAccessGrantsEnabled = true,
                protocolMappers = mappers,
            },
            cancellationToken);
        created.EnsureSuccessStatusCode();
    }

    private static async Task<string> RequestTokenAsync(
        HttpClient keycloak,
        string realm,
        Dictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await keycloak.PostAsync(
            new Uri($"/realms/{realm}/protocol/openid-connect/token", UriKind.Relative),
            content,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            Assert.Fail($"Keycloak refused the token request for '{form["client_id"]}' in '{realm}': {(int)response.StatusCode} {error}");
        }

        using var token = await ReadJsonAsync(response, cancellationToken);
        return token.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Keycloak returned no access token.");
    }

    private async Task<HttpClient> CreateAdminClientAsync(CancellationToken cancellationToken)
    {
        var client = fixture.App.CreateHttpClient(KeycloakResource, "http");

        try
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                await RequestAdminTokenAsync(cancellationToken));

            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private async Task<string> GetParameterValueAsync(string name, CancellationToken cancellationToken)
    {
        var parameter = Assert.IsType<ParameterResource>(fixture.GetResource(name));

        return await parameter.GetValueAsync(cancellationToken)
            ?? throw new InvalidOperationException($"The parameter '{name}' has no value.");
    }
}
