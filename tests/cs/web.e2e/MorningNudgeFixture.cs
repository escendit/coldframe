using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Coldframe.Web.E2ETests;

/// <summary>
/// A Keycloak User of the test, with the password the browser signs in with.
/// </summary>
public sealed record JourneyUser(string UserId, string Username, string Password);

/// <summary>
/// The whole stack of the journey, started once: its own AppHost instance with the test-only clock switch
/// (<c>Testing:ManualClock</c>), and the built web app (<c>node build</c>) on port 5173, the only redirect
/// the realm's <c>coldframe-web</c> client allows.
/// </summary>
public sealed class MorningNudgeFixture : IAsyncLifetime
{
    /// <summary>
    /// The web origin. The port is fixed by the realm file.
    /// </summary>
    public const string WebOrigin = "http://localhost:5173";

    /// <summary>
    /// Makes the browser resolve <c>localhost</c> to the IPv4 loopback, where the web app listens. Without it
    /// Chromium tries <c>::1</c> first, and whatever else listens on port 5173 there (a dev server of another
    /// project) would answer instead of the web app under test.
    /// </summary>
    public const string BrowserHostRule = "--host-resolver-rules=MAP localhost 127.0.0.1";

    private static readonly Uri WebLoopback = new("http://127.0.0.1:5173");

    private static readonly TimeSpan WebStartTimeout = TimeSpan.FromSeconds(60);

    private const string Realm = "coldframe";
    private const string ServerResource = "server";
    private const string KeycloakResource = "keycloak";
    private const string DatabaseResource = "coldframe";

    private static readonly TimeSpan ResourceTimeout = TimeSpan.FromMinutes(15);

    private const string BrowserRedirectUri = "http://localhost/coldframe-tests/callback";

    private readonly SemaphoreSlim _browserClientLock = new(1, 1);
    private string? _browserClientId;

    private readonly string _testClientId = $"coldframe-e2e-{Guid.NewGuid():N}";
    private readonly List<string> _webOutput = [];
    private DistributedApplication? _app;
    private NpgsqlDataSource? _database;
    private Process? _web;

    public DistributedApplication App => _app ?? throw new InvalidOperationException("The AppHost has not been started.");

    /// <summary>
    /// The AppHost's <c>coldframe</c> database, to read what the Server journaled.
    /// </summary>
    public NpgsqlDataSource Database => _database ?? throw new InvalidOperationException("The stack has not been started.");

    public async ValueTask InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(ResourceTimeout);

        // The switch is given to the AppHost, which hands it on to the Server; without it there is no clock to move.
        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Coldframe_AppHost>(["--Testing:ManualClock=true"], timeout.Token);
        builder.Services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());

        _app = await builder.BuildAsync(timeout.Token);
        await _app.StartAsync(timeout.Token);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync(KeycloakResource, WaitBehavior.StopOnResourceUnavailable, timeout.Token);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync(ServerResource, WaitBehavior.StopOnResourceUnavailable, timeout.Token);

        _database = NpgsqlDataSource.Create(
            await _app.GetConnectionStringAsync(DatabaseResource, timeout.Token)
            ?? throw new InvalidOperationException($"The AppHost gives '{DatabaseResource}' no connection string."));

        using (var admin = await CreateAdminClientAsync(timeout.Token))
        {
            await CreateTestClientAsync(admin, timeout.Token);
        }

        await StartWebAppAsync(timeout.Token);
    }

    public async ValueTask DisposeAsync()
    {
        _browserClientLock.Dispose();

        if (_web is not null)
        {
            if (!_web.HasExited)
            {
                _web.Kill(entireProcessTree: true);
                await _web.WaitForExitAsync();
            }

            _web.Dispose();
        }

        if (_database is not null)
        {
            await _database.DisposeAsync();
        }

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    /// <summary>
    /// An HTTP client for the Server with a fresh access token of <paramref name="user"/> (tokens are short-lived
    /// and the journey is not).
    /// </summary>
    public async Task<HttpClient> CreateServerClientAsync(JourneyUser user, CancellationToken cancellationToken)
    {
        var client = App.CreateHttpClient(ServerResource, "http");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await RequestUserTokenAsync(user, cancellationToken));

        return client;
    }

    /// <summary>
    /// An HTTP client for the Server without a token, for the Device endpoints.
    /// </summary>
    public HttpClient CreateDeviceClient() => App.CreateHttpClient(ServerResource, "http");

    /// <summary>
    /// Creates a Keycloak User with a password, as someone who registered.
    /// </summary>
    public async Task<JourneyUser> CreateUserAsync(string label, CancellationToken cancellationToken)
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

        return new JourneyUser(userId, username, password);
    }

    /// <summary>
    /// Signs <paramref name="invitee"/> in through Keycloak's browser flow and accepts their pending Phase Two
    /// invitation to <paramref name="organizationId"/> on the invitation page that the required action shows
    /// (Story 9.1; as <c>EdgeApiFixture.AcceptInvitationAsync</c> of the Server's integration tests). Keycloak then
    /// emits the <c>CUSTOM_REQUIRED_ACTION</c> user event that brings the Membership to the Server. Cookies are kept
    /// by hand: Keycloak marks them Secure, which a cookie container does not send over the stack's plain HTTP.
    /// </summary>
    public async Task AcceptInvitationAsync(JourneyUser invitee, string organizationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invitee);

        var clientId = await EnsureBrowserClientAsync(cancellationToken);
        var cookies = new Dictionary<string, string>(StringComparer.Ordinal);

        using var addressed = App.CreateHttpClient(KeycloakResource, "http");
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        using var keycloak = new HttpClient(handler) { BaseAddress = addressed.BaseAddress };

        var authorize = new Uri(
            $"/realms/{Realm}/protocol/openid-connect/auth?client_id={clientId}&response_type=code&scope=openid"
            + $"&redirect_uri={Uri.EscapeDataString(BrowserRedirectUri)}",
            UriKind.Relative);

        using var loginPage = await BrowseAsync(keycloak, cookies, HttpMethod.Get, authorize, null, cancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.OK, loginPage.StatusCode);

        var page = await BrowseAsync(
            keycloak,
            cookies,
            HttpMethod.Post,
            FormAction(await loginPage.Content.ReadAsStringAsync(cancellationToken)),
            [new("username", invitee.Username), new("password", invitee.Password)],
            cancellationToken);

        // The required action redirects to its own page first.
        while (page.StatusCode == System.Net.HttpStatusCode.Found
            && page.Headers.Location is { } next
            && !next.AbsoluteUri.StartsWith(BrowserRedirectUri, StringComparison.Ordinal))
        {
            page.Dispose();
            page = await BrowseAsync(keycloak, cookies, HttpMethod.Get, next, null, cancellationToken);
        }

        using (page)
        {
            var html = await page.Content.ReadAsStringAsync(cancellationToken);
            Assert.True(
                html.Contains($"value=\"{organizationId}\"", StringComparison.Ordinal),
                $"Keycloak showed no invitation to {organizationId} ({(int)page.StatusCode}).");

            using var accepted = await BrowseAsync(
                keycloak,
                cookies,
                HttpMethod.Post,
                FormAction(html),
                [new("orgs", organizationId), new("accept", "Accept")],
                cancellationToken);

            Assert.Equal(System.Net.HttpStatusCode.Found, accepted.StatusCode);
            Assert.StartsWith(BrowserRedirectUri, accepted.Headers.Location?.AbsoluteUri ?? string.Empty, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// What the web app wrote to its output so far, for a failing test.
    /// </summary>
    public string WebOutput()
    {
        lock (_webOutput)
        {
            return string.Join(Environment.NewLine, _webOutput);
        }
    }

    private async Task<string> RequestUserTokenAsync(JourneyUser user, CancellationToken cancellationToken)
    {
        using var keycloak = App.CreateHttpClient(KeycloakResource, "http");

        return await RequestTokenAsync(
            keycloak,
            Realm,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "password",
                ["client_id"] = _testClientId,
                ["username"] = user.Username,
                ["password"] = user.Password,
                ["scope"] = "openid",
            },
            cancellationToken);
    }

    // The built web app, as a deployment runs it, against this AppHost's Server and Keycloak.
    private async Task StartWebAppAsync(CancellationToken cancellationToken)
    {
        var webFolder = Path.Combine(RepositoryRoot(), "apps", "ts", "web");

        Assert.True(
            File.Exists(Path.Combine(webFolder, "build", "index.js")),
            "The web app is not built. Run `pnpm --filter @coldframe/web build` first.");

        var server = App.GetEndpoint(ServerResource, "http");
        var keycloak = App.GetEndpoint(KeycloakResource, "http");
        var start = new ProcessStartInfo("node")
        {
            WorkingDirectory = webFolder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("build");
        start.Environment["HOST"] = WebLoopback.Host;
        start.Environment["PORT"] = WebLoopback.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment["ORIGIN"] = WebOrigin;
        start.Environment["COLDFRAME_SERVER_URL"] = server.GetLeftPart(UriPartial.Authority);
        start.Environment["KEYCLOAK_ISSUER"] = $"{keycloak.GetLeftPart(UriPartial.Authority)}/realms/{Realm}";
        start.Environment["KEYCLOAK_CLIENT_ID"] = "coldframe-web";
        start.Environment["KEYCLOAK_CLIENT_SECRET"] = await GetParameterValueAsync("coldframe-web-client-secret", cancellationToken);
        start.Environment["KEYCLOAK_ALLOW_INSECURE_HTTP"] = "true";

        _web = Process.Start(start) ?? throw new InvalidOperationException("The web app did not start.");
        _web.OutputDataReceived += (_, line) => Keep(line.Data);
        _web.ErrorDataReceived += (_, line) => Keep(line.Data);
        _web.BeginOutputReadLine();
        _web.BeginErrorReadLine();

        using var client = new HttpClient { BaseAddress = WebLoopback };
        var deadline = TimeProvider.System.GetUtcNow() + WebStartTimeout;

        while (true)
        {
            Assert.False(_web.HasExited, $"The web app exited (is port 5173 on 127.0.0.1 in use?):{Environment.NewLine}{WebOutput()}");
            Assert.True(
                TimeProvider.System.GetUtcNow() < deadline,
                $"The web app did not answer on {WebLoopback} within {WebStartTimeout.TotalSeconds} s:{Environment.NewLine}{WebOutput()}");

            try
            {
                // A static file: every page answers a first visit with a redirect.
                using var response = await client.GetAsync(new Uri("/_app/version.json", UriKind.Relative), cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }
    }

    private void Keep(string? line)
    {
        if (line is not null)
        {
            lock (_webOutput)
            {
                _webOutput.Add(line);
            }
        }
    }

    private static string RepositoryRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "Coldframe.slnx")))
            {
                return folder.FullName;
            }
        }

        throw new InvalidOperationException($"Coldframe.slnx was not found above {AppContext.BaseDirectory}.");
    }

    // A public client with the direct grant and the Server's audience, for the test's own REST calls: the
    // realm's coldframe-web client allows the browser flow only.
    private async Task CreateTestClientAsync(HttpClient admin, CancellationToken cancellationToken)
    {
        using var created = await admin.PostAsJsonAsync(
            new Uri($"/admin/realms/{Realm}/clients", UriKind.Relative),
            new
            {
                clientId = _testClientId,
                enabled = true,
                publicClient = true,
                standardFlowEnabled = false,
                directAccessGrantsEnabled = true,
                protocolMappers = new object[]
                {
                    new
                    {
                        name = "coldframe-server audience",
                        protocol = "openid-connect",
                        protocolMapper = "oidc-audience-mapper",
                        config = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["included.client.audience"] = "coldframe-server",
                            ["access.token.claim"] = "true",
                            ["id.token.claim"] = "false",
                        },
                    },
                },
            },
            cancellationToken);
        created.EnsureSuccessStatusCode();
    }

    // A public client with the browser flow, for AcceptInvitationAsync; created once per stack.
    private async Task<string> EnsureBrowserClientAsync(CancellationToken cancellationToken)
    {
        await _browserClientLock.WaitAsync(cancellationToken);

        try
        {
            if (_browserClientId is { } existing)
            {
                return existing;
            }

            var clientId = $"coldframe-e2e-browser-{Guid.NewGuid():N}";
            using var admin = await CreateAdminClientAsync(cancellationToken);
            using var created = await admin.PostAsJsonAsync(
                new Uri($"/admin/realms/{Realm}/clients", UriKind.Relative),
                new
                {
                    clientId,
                    enabled = true,
                    publicClient = true,
                    standardFlowEnabled = true,
                    directAccessGrantsEnabled = false,
                    redirectUris = new[] { BrowserRedirectUri },
                },
                cancellationToken);
            created.EnsureSuccessStatusCode();

            return _browserClientId = clientId;
        }
        finally
        {
            _browserClientLock.Release();
        }
    }

    private static async Task<HttpResponseMessage> BrowseAsync(
        HttpClient keycloak,
        Dictionary<string, string> cookies,
        HttpMethod method,
        Uri uri,
        IEnumerable<KeyValuePair<string, string>>? form,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, uri);

        if (form is not null)
        {
            request.Content = new FormUrlEncodedContent(form);
        }

        if (cookies.Count > 0)
        {
            request.Headers.Add("Cookie", string.Join("; ", cookies.Select(cookie => $"{cookie.Key}={cookie.Value}")));
        }

        var response = await keycloak.SendAsync(request, cancellationToken);

        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var setCookie in setCookies)
            {
                var pair = setCookie.Split(';', 2)[0].Split('=', 2);
                if (pair.Length == 2)
                {
                    cookies[pair[0].Trim()] = pair[1];
                }
            }
        }

        return response;
    }

    // The action of the page's first form, HTML-decoded.
    private static Uri FormAction(string html)
    {
        var match = System.Text.RegularExpressions.Regex.Match(html, "<form[^>]*action=\"([^\"]+)\"", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
        Assert.True(match.Success, "The Keycloak page has no form.");
        return new Uri(System.Net.WebUtility.HtmlDecode(match.Groups[1].Value), UriKind.Absolute);
    }

    private async Task<HttpClient> CreateAdminClientAsync(CancellationToken cancellationToken)
    {
        var client = App.CreateHttpClient(KeycloakResource, "http");

        try
        {
            var token = await RequestTokenAsync(
                client,
                "master",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["grant_type"] = "password",
                    ["client_id"] = "admin-cli",
                    ["username"] = await GetParameterValueAsync("keycloak-admin-username", cancellationToken),
                    ["password"] = await GetParameterValueAsync("keycloak-admin-password", cancellationToken),
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

    private static async Task<string> RequestTokenAsync(HttpClient keycloak, string realm, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await keycloak.PostAsync(
            new Uri($"/realms/{realm}/protocol/openid-connect/token", UriKind.Relative),
            content,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            Assert.Fail($"Keycloak refused the token request for '{form["client_id"]}' in '{realm}': {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(cancellationToken)}");
        }

        using var token = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        return token.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Keycloak returned no access token.");
    }

    private async Task<string> GetParameterValueAsync(string name, CancellationToken cancellationToken)
    {
        var model = App.Services.GetRequiredService<DistributedApplicationModel>();
        var parameter = model.Resources.OfType<ParameterResource>().Single(resource => string.Equals(resource.Name, name, StringComparison.Ordinal));

        return await parameter.GetValueAsync(cancellationToken)
            ?? throw new InvalidOperationException($"The parameter '{name}' has no value.");
    }
}
