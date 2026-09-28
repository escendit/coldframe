using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coldframe.Server.Identity;

namespace Coldframe.Server.IntegrationTests.Edge;

/// <summary>
/// Create Site and read it back over HTTP on the AppHost: real Keycloak tokens, real Phase Two, and the
/// Server's journal and identity projection (FR-6, AD-3, AD-4).
/// </summary>
public sealed class EdgeApiTests(EdgeApiFixture edge) : IClassFixture<EdgeApiFixture>
{
    private const string ProblemJson = "application/problem+json";

    [Fact]
    public async Task CreateSiteAnswers201CreatesTheTaggedOrganizationAndIsReadableAtOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = await edge.CreateUserAsync("owner", cancellationToken);
        var key = Guid.NewGuid().ToString();
        using var server = edge.CreateServerClient(user.AccessToken);

        using var created = await PostSiteAsync(server, key, new { name = "Home" }, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await ReadSiteAsync(created, cancellationToken);
        Assert.Equal("Home", body.Name);
        Assert.Equal("Owner", body.Role);
        Assert.Equal(new Uri($"/sites/{body.Id}", UriKind.Relative), created.Headers.Location);

        // The Organization in Phase Two: tagged with the request, the caller a member with role owner,
        // and no placeholder org-admin User.
        using var organizations = await edge.CreateOrganizationsClientAsync(cancellationToken);

        using (var organization = await GetJsonAsync(organizations, $"/realms/{EdgeApiFixture.Realm}/orgs/{body.Id}", cancellationToken))
        {
            var root = organization.RootElement;
            Assert.Equal(body.Id, root.GetProperty("id").GetString());
            Assert.Equal(body.Id, root.GetProperty("name").GetString());
            Assert.Equal("Home", root.GetProperty("displayName").GetString());
            Assert.Equal(
                [$"{user.UserId}:{key}"],
                root.GetProperty("attributes").GetProperty(IPhaseTwoOrganizations.IdempotencyKeyAttribute)
                    .EnumerateArray().Select(value => value.GetString()));
        }

        using (var members = await GetJsonAsync(organizations, $"/realms/{EdgeApiFixture.Realm}/orgs/{body.Id}/members", cancellationToken))
        {
            var member = Assert.Single(members.RootElement.EnumerateArray().ToArray());
            Assert.Equal(user.UserId, member.GetProperty("id").GetString());
            Assert.DoesNotContain(
                members.RootElement.EnumerateArray(),
                candidate => candidate.GetProperty("username").GetString()!.StartsWith("org-admin-", StringComparison.Ordinal));
        }

        using (var owners = await GetJsonAsync(organizations, $"/realms/{EdgeApiFixture.Realm}/orgs/{body.Id}/roles/owner/users", cancellationToken))
        {
            Assert.Contains(owners.RootElement.EnumerateArray(), owner => owner.GetProperty("id").GetString() == user.UserId);
        }

        Assert.Equal(["site.created", "site.membership-granted"], await edge.AliasesAsync($"site/{body.Id}", cancellationToken));

        // Read-your-writes: the very next request sees the Site and the Role.
        using var read = await server.GetAsync(new Uri($"/sites/{body.Id}", UriKind.Relative), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(body, await ReadSiteAsync(read, cancellationToken));
    }

    [Fact]
    public async Task ARetryWithTheSameKeyReturnsTheOriginalResultAndOneOrganization()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = await edge.CreateUserAsync("retry", cancellationToken);
        var key = Guid.NewGuid().ToString();
        using var server = edge.CreateServerClient(user.AccessToken);

        using var first = await PostSiteAsync(server, key, new { name = "Home" }, cancellationToken);
        using var second = await PostSiteAsync(server, key, new { name = "Home" }, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(cancellationToken), await second.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(first.Headers.Location, second.Headers.Location);

        using var organizations = await edge.CreateOrganizationsClientAsync(cancellationToken);
        var query = Uri.EscapeDataString($"{IPhaseTwoOrganizations.IdempotencyKeyAttribute}:\"{user.UserId}:{key}\"");
        using var tagged = await GetJsonAsync(organizations, $"/realms/{EdgeApiFixture.Realm}/orgs?q={query}", cancellationToken);

        Assert.Single(tagged.RootElement.EnumerateArray().ToArray());

        // The same key with another name is refused and creates nothing.
        using var reused = await PostSiteAsync(server, key, new { name = "Other" }, cancellationToken);
        await AssertProblemAsync(reused, HttpStatusCode.UnprocessableEntity, "urn:coldframe:problem:idempotency-key-reused", cancellationToken);
    }

    [Theory]
    [InlineData("0192f3a4-7c1e-7d2b-9a51-3f7e2c9b1d00")]
    [InlineData("not-a-site-id")]
    public async Task AnUnknownSiteIsNotFound(string siteId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = await edge.CreateUserAsync("stranger", cancellationToken);
        using var server = edge.CreateServerClient(user.AccessToken);

        using var response = await server.GetAsync(new Uri($"/sites/{siteId}", UriKind.Relative), cancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "urn:coldframe:problem:site-not-found", cancellationToken);
    }

    [Fact]
    public async Task WithoutATokenEveryEndpointIsUnauthorized()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var server = edge.CreateServerClient();

        using var create = await PostSiteAsync(server, Guid.NewGuid().ToString(), new { name = "Home" }, cancellationToken);
        await AssertProblemAsync(create, HttpStatusCode.Unauthorized, "urn:coldframe:problem:unauthorized", cancellationToken);

        using var read = await server.GetAsync(new Uri($"/sites/{Guid.NewGuid()}", UriKind.Relative), cancellationToken);
        await AssertProblemAsync(read, HttpStatusCode.Unauthorized, "urn:coldframe:problem:unauthorized", cancellationToken);
    }

    [Fact]
    public async Task ATokenFromAnotherIssuerIsUnauthorized()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var server = edge.CreateServerClient(await edge.RequestAdminTokenAsync(cancellationToken));

        using var response = await server.GetAsync(new Uri($"/sites/{Guid.NewGuid()}", UriKind.Relative), cancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "urn:coldframe:problem:unauthorized", cancellationToken);
    }

    [Fact]
    public async Task ARealmTokenWithoutTheServerAudienceIsUnauthorized()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = await edge.CreateUserAsync("no-audience", cancellationToken, withServerAudience: false);
        Assert.DoesNotContain(EdgeApiFixture.ServerAudience, AudiencesOf(user.AccessToken));

        using var server = edge.CreateServerClient(user.AccessToken);

        using var create = await PostSiteAsync(server, Guid.NewGuid().ToString(), new { name = "Home" }, cancellationToken);
        await AssertProblemAsync(create, HttpStatusCode.Unauthorized, "urn:coldframe:problem:unauthorized", cancellationToken);

        using var read = await server.GetAsync(new Uri($"/sites/{Guid.NewGuid()}", UriKind.Relative), cancellationToken);
        await AssertProblemAsync(read, HttpStatusCode.Unauthorized, "urn:coldframe:problem:unauthorized", cancellationToken);
    }

    [Fact]
    public async Task TheServersOwnServiceAccountIsNotACaller()
    {
        // Its token is for Keycloak's admin API (audience realm-management), not for the Server. Should a
        // mapper ever add the Server audience, this fails before the service account could act as a User.
        var cancellationToken = TestContext.Current.CancellationToken;
        using var organizations = await edge.CreateOrganizationsClientAsync(cancellationToken);
        var token = organizations.DefaultRequestHeaders.Authorization!.Parameter!;
        Assert.DoesNotContain(EdgeApiFixture.ServerAudience, AudiencesOf(token));

        using var server = edge.CreateServerClient(token);
        using var read = await server.GetAsync(new Uri($"/sites/{Guid.NewGuid()}", UriKind.Relative), cancellationToken);

        await AssertProblemAsync(read, HttpStatusCode.Unauthorized, "urn:coldframe:problem:unauthorized", cancellationToken);
    }

    [Fact]
    public async Task TheHealthEndpointStaysAnonymous()
    {
        using var server = edge.CreateServerClient();

        using var response = await server.GetAsync(new Uri("/.well-known/healthz", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ARequestWithoutAKeyOrWithABadNameIsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = await edge.CreateUserAsync("validation", cancellationToken);
        using var server = edge.CreateServerClient(user.AccessToken);

        using (var noKey = await PostSiteAsync(server, null, new { name = "Home" }, cancellationToken))
        {
            await AssertProblemAsync(noKey, HttpStatusCode.BadRequest, "urn:coldframe:problem:idempotency-key-missing", cancellationToken);
        }

        using (var longKey = await PostSiteAsync(server, new string('k', 201), new { name = "Home" }, cancellationToken))
        {
            await AssertProblemAsync(longKey, HttpStatusCode.BadRequest, "urn:coldframe:problem:validation", cancellationToken);
        }

        foreach (var body in new object[] { new { name = "   " }, new { name = new string('n', 101) }, new { other = "Home" } })
        {
            using var badName = await PostSiteAsync(server, Guid.NewGuid().ToString(), body, cancellationToken);
            await AssertProblemAsync(badName, HttpStatusCode.BadRequest, "urn:coldframe:problem:validation", cancellationToken);
        }
    }

    internal static async Task<HttpResponseMessage> PostSiteAsync(HttpClient server, string? key, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/sites", UriKind.Relative))
        {
            Content = JsonContent.Create(body),
        };

        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        return await server.SendAsync(request, cancellationToken);
    }

    internal static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string type,
        CancellationToken cancellationToken)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);

        using var problem = await EdgeApiFixture.ReadJsonAsync(response, cancellationToken);
        Assert.Equal(type, problem.RootElement.GetProperty("type").GetString());
        Assert.Equal((int)status, problem.RootElement.GetProperty("status").GetInt32());
    }

    private static async Task<SiteBody> ReadSiteAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var document = await EdgeApiFixture.ReadJsonAsync(response, cancellationToken);
        var root = document.RootElement;

        // camelCase, exactly these three fields.
        Assert.Equal(["id", "name", "role"], root.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));

        return new SiteBody(root.GetProperty("id").GetString()!, root.GetProperty("name").GetString()!, root.GetProperty("role").GetString()!);
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string path, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            Assert.Fail($"GET {path} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
        }

        return await EdgeApiFixture.ReadJsonAsync(response, cancellationToken);
    }

    /// <summary>
    /// The <c>aud</c> values of a JWT, read without validating it.
    /// </summary>
    internal static List<string> AudiencesOf(string accessToken)
    {
        var payload = accessToken.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
        using var claims = JsonDocument.Parse(Convert.FromBase64String(payload));

        if (!claims.RootElement.TryGetProperty("aud", out var audience))
        {
            return [];
        }

        return audience.ValueKind == JsonValueKind.Array
            ? [.. audience.EnumerateArray().Select(value => value.GetString()!)]
            : [audience.GetString()!];
    }

    private sealed record SiteBody(string Id, string Name, string Role);
}
