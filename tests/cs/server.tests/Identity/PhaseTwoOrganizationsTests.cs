using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Coldframe.Server.Identity;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Coldframe.Server.Tests.Identity;

/// <summary>
/// The real Phase Two client and service account against a stubbed HTTP handler: how Keycloak's answers
/// and failures map to results and <see cref="IdentityProviderUnavailableException"/>, the one retry after
/// 401, and the token cache on the injected clock.
/// </summary>
public sealed class PhaseTwoOrganizationsTests
{
    private const string TokenPath = "/realms/coldframe/protocol/openid-connect/token";
    private const int TokenLifetimeSeconds = 300;

    private static readonly DateTimeOffset Start = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    private static readonly PhaseTwoOrganization Organization = new(
        "0192f3a4-7c1e-7d2b-9a51-3f7e2c9b1d00",
        "0192f3a4-7c1e-7d2b-9a51-3f7e2c9b1d00",
        "Home",
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [IPhaseTwoOrganizations.IdempotencyKeyAttribute] = ["user-1:k1"],
        });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> Operations() => ["find", "get", "create", "role", "member", "grant", "roster"];

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task ServerErrorsMeanKeycloakIsUnavailable(string operation)
    {
        foreach (var status in new[] { HttpStatusCode.InternalServerError, HttpStatusCode.ServiceUnavailable })
        {
            var stub = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(status)));
            var (organizations, _) = Create(stub);

            await Assert.ThrowsAsync<IdentityProviderUnavailableException>(() => Call(organizations, operation));
        }
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task ATransportFailureMeansKeycloakIsUnavailable(string operation)
    {
        var stub = new StubHandler((_, _) => throw new HttpRequestException("Connection refused."));
        var (organizations, _) = Create(stub);

        await Assert.ThrowsAsync<IdentityProviderUnavailableException>(() => Call(organizations, operation));
    }

    [Fact]
    public async Task AHandlerTimeoutMeansKeycloakIsUnavailable()
    {
        var stub = new StubHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var (organizations, _) = Create(stub, timeout: TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<IdentityProviderUnavailableException>(() => organizations.GetAsync(Organization.Id, Ct));
    }

    [Fact]
    public async Task ATokenEndpointFailureMeansKeycloakIsUnavailable()
    {
        var stub = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var (organizations, _) = Create(stub, workingTokenEndpoint: false);

        await Assert.ThrowsAsync<IdentityProviderUnavailableException>(() => organizations.GetAsync(Organization.Id, Ct));
    }

    [Fact]
    public async Task CreateReportsCreatedOn201AndExistingOn409()
    {
        var created = Create(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)))).Organizations;
        var conflict = Create(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)))).Organizations;

        Assert.True(await created.CreateAsync(Organization, Ct));
        Assert.False(await conflict.CreateAsync(Organization, Ct));
    }

    [Fact]
    public async Task CreateSendsTheIdNameDisplayNameAndTag()
    {
        string? body = null;
        var stub = new StubHandler(async (request, cancellationToken) =>
        {
            body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.Created);
        });

        await Create(stub).Organizations.CreateAsync(Organization, Ct);

        Assert.Equal(
            $$$"""{"id":"{{{Organization.Id}}}","name":"{{{Organization.Name}}}","displayName":"Home","attributes":{"coldframe.idempotencyKey":["user-1:k1"]}}""",
            body);
    }

    [Fact]
    public async Task ASearchPhaseTwoCannotParseMatchesNothing()
    {
        var stub = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)));

        Assert.Empty(await Create(stub).Organizations.FindByAttributeAsync(IPhaseTwoOrganizations.IdempotencyKeyAttribute, "user-1:k\"1", Ct));
    }

    [Fact]
    public async Task A401RefreshesTheTokenOnceAndRetries()
    {
        var apiCalls = 0;
        var stub = new StubHandler((_, _) => Task.FromResult(
            ++apiCalls == 1
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : Json(HttpStatusCode.OK, "[]")));
        var (organizations, tokens) = Create(stub);

        Assert.Empty(await organizations.FindByAttributeAsync(IPhaseTwoOrganizations.IdempotencyKeyAttribute, "user-1:k1", Ct));

        Assert.Equal(2, apiCalls);
        Assert.Equal(2, tokens.Requests);
        Assert.Equal(["Bearer token-1", "Bearer token-2"], stub.Authorizations);
    }

    [Fact]
    public async Task A401AfterTheRefreshIsNotRetriedAgain()
    {
        var stub = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        var (organizations, tokens) = Create(stub);

        await Assert.ThrowsAsync<InvalidOperationException>(() => organizations.GetAsync(Organization.Id, Ct));

        Assert.Equal(2, stub.Authorizations.Count);
        Assert.Equal(2, tokens.Requests);
    }

    [Fact]
    public async Task TheTokenIsCachedUntilShortlyBeforeItExpires()
    {
        var time = new FakeTimeProvider(Start);
        var tokens = new TokenEndpoint();
        var account = new KeycloakServiceAccount(new StubFactory(tokens, api: null, Timeout.InfiniteTimeSpan), Options(), time);
        var renewAfter = TimeSpan.FromSeconds(TokenLifetimeSeconds) - KeycloakServiceAccount.RenewBeforeExpiry;

        Assert.Equal("token-1", await account.GetAccessTokenAsync(Ct));

        time.Advance(renewAfter - TimeSpan.FromSeconds(1));
        Assert.Equal("token-1", await account.GetAccessTokenAsync(Ct));
        Assert.Equal(1, tokens.Requests);

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("token-2", await account.GetAccessTokenAsync(Ct));
        Assert.Equal(2, tokens.Requests);
    }

    [Fact]
    public async Task TheRosterIsReadPageByPageWithTheHoldersOfEachRole()
    {
        var members = Enumerable.Range(0, PhaseTwoOrganizations.RosterPageSize + 3).Select(index => $"user-{index}").ToList();
        var requested = new List<string>();
        var stub = new StubHandler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
            requested.Add($"{path}{request.RequestUri.Query}");

            IEnumerable<string>? users = path switch
            {
                _ when path.EndsWith("/members", StringComparison.Ordinal) => members,
                _ when path.EndsWith("/roles/owner/users", StringComparison.Ordinal) => ["user-0"],
                _ when path.EndsWith("/roles/member/users", StringComparison.Ordinal) => members.Skip(1),
                _ when path.EndsWith("/roles/administrator/users", StringComparison.Ordinal) => [],
                _ => null,
            };

            if (users is null)
            {
                return Task.FromResult(Json(HttpStatusCode.OK, """{"id":"org","name":"org","displayName":"Garden"}"""));
            }

            var page = users.Skip(int.Parse(query["first"]!, CultureInfo.InvariantCulture)).Take(int.Parse(query["max"]!, CultureInfo.InvariantCulture));
            return Task.FromResult(Json(HttpStatusCode.OK, JsonSerializer.Serialize(page.Select(id => new { id, username = id }))));
        });

        var roster = await Create(stub).Organizations.GetRosterAsync(Organization.Id, Ct);

        Assert.NotNull(roster);
        Assert.Equal("Garden", roster.DisplayName);
        Assert.Equal(members.Count, roster.MemberIds.Count);
        Assert.True(roster.Holds("owner", "user-0"));
        Assert.False(roster.Holds("owner", "user-1"));
        Assert.Equal(members.Count - 1, roster.RoleHolders["member"].Count);
        Assert.Empty(roster.RoleHolders["administrator"]);
        Assert.Contains($"/realms/coldframe/orgs/{Organization.Id}/members?first={PhaseTwoOrganizations.RosterPageSize}&max={PhaseTwoOrganizations.RosterPageSize}", requested);
        Assert.All(requested, path => Assert.DoesNotContain("/admin/", path, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AMissingOrganizationHasNoRosterAndAMissingRoleHasNoHolders()
    {
        var gone = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        Assert.Null(await Create(gone).Organizations.GetRosterAsync(Organization.Id, Ct));

        var noRoles = new StubHandler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return Task.FromResult(
                path.Contains("/roles/", StringComparison.Ordinal) ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : path.EndsWith("/members", StringComparison.Ordinal) ? Json(HttpStatusCode.OK, """[{"id":"user-1"}]""")
                : Json(HttpStatusCode.OK, """{"id":"org","displayName":"Home"}"""));
        });

        var roster = await Create(noRoles).Organizations.GetRosterAsync(Organization.Id, Ct);

        Assert.NotNull(roster);
        Assert.Equal(["user-1"], roster.MemberIds);
        Assert.All(SiteGrain.OrganizationRoles.Values, role => Assert.Empty(roster.RoleHolders[role]));
    }

    [Fact]
    public async Task AnOrganizationDeletedWhileItsMembersAreReadHasNoRoster()
    {
        var stub = new StubHandler((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath.EndsWith("/members", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : Json(HttpStatusCode.OK, $$"""{"id":"{{Organization.Id}}","displayName":"Home"}""")));

        Assert.Null(await Create(stub).Organizations.GetRosterAsync(Organization.Id, Ct));
    }

    [Fact]
    public async Task AnEndpointThatIgnoresPagingEndsTheRead()
    {
        var full = JsonSerializer.Serialize(Enumerable.Range(0, PhaseTwoOrganizations.RosterPageSize).Select(index => new { id = $"user-{index}" }));
        var stub = new StubHandler((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath.EndsWith(Organization.Id, StringComparison.Ordinal)
                ? Json(HttpStatusCode.OK, """{"id":"org","displayName":"Home"}""")
                : Json(HttpStatusCode.OK, full)));

        var roster = await Create(stub).Organizations.GetRosterAsync(Organization.Id, Ct);

        Assert.Equal(PhaseTwoOrganizations.RosterPageSize, roster!.MemberIds.Count);
    }

    private static Task Call(PhaseTwoOrganizations organizations, string operation) => operation switch
    {
        "find" => organizations.FindByAttributeAsync(IPhaseTwoOrganizations.IdempotencyKeyAttribute, "user-1:k1", Ct),
        "get" => organizations.GetAsync(Organization.Id, Ct),
        "create" => organizations.CreateAsync(Organization, Ct),
        "role" => organizations.EnsureRoleAsync(Organization.Id, "owner", Ct),
        "member" => organizations.AddMemberAsync(Organization.Id, "user-1", Ct),
        "grant" => organizations.GrantRoleAsync(Organization.Id, "owner", "user-1", Ct),
        "roster" => organizations.GetRosterAsync(Organization.Id, Ct),
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    // By default a working token endpoint answers the token requests, so only the API calls meet the
    // stub; workingTokenEndpoint: false sends the token requests to the stub as well.
    private static (PhaseTwoOrganizations Organizations, TokenEndpoint Tokens) Create(
        StubHandler api,
        bool workingTokenEndpoint = true,
        TimeSpan? timeout = null)
    {
        var tokens = new TokenEndpoint();
        var factory = new StubFactory(workingTokenEndpoint ? tokens : null, api, timeout ?? TimeSpan.FromSeconds(10));
        var options = Options();
        var account = new KeycloakServiceAccount(factory, options, new FakeTimeProvider(Start));

        return (new PhaseTwoOrganizations(factory, account, options), tokens);
    }

    private static IOptions<KeycloakOptions> Options() => Microsoft.Extensions.Options.Options.Create(new KeycloakOptions
    {
        BaseUrl = new Uri("http://keycloak.test.invalid"),
        ClientSecret = "secret",
    });

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>
    /// A working token endpoint that issues token-1, token-2, … with a lifetime of 300 s.
    /// </summary>
    private sealed class TokenEndpoint
    {
        private int _requests;

        public int Requests => Volatile.Read(ref _requests);

        public HttpResponseMessage Issue() => Json(
            HttpStatusCode.OK,
            $$"""{"access_token":"token-{{Interlocked.Increment(ref _requests)}}","expires_in":{{TokenLifetimeSeconds}}}""");
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<string> Authorizations { get; } = [];

        public TokenEndpoint? Tokens { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Tokens is not null && request.RequestUri!.AbsolutePath == TokenPath)
            {
                return Task.FromResult(Tokens.Issue());
            }

            if (request.Headers.Authorization is { } authorization)
            {
                Authorizations.Add(authorization.ToString());
            }

            return respond(request, cancellationToken);
        }
    }

    private sealed class StubFactory(TokenEndpoint? tokens, StubHandler? api, TimeSpan timeout) : IHttpClientFactory
    {
        private readonly StubHandler _handler = Wire(tokens, api);

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false) { Timeout = timeout };

        private static StubHandler Wire(TokenEndpoint? tokens, StubHandler? api)
        {
            if (api is null)
            {
                return new StubHandler((_, _) => throw new InvalidOperationException("No API call expected.")) { Tokens = tokens };
            }

            api.Tokens = tokens;
            return api;
        }
    }
}
