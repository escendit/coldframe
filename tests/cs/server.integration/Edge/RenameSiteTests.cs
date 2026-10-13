using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Coldframe.Server.Identity;

namespace Coldframe.Server.IntegrationTests.Edge;

/// <summary>
/// <c>PATCH /sites/{siteId}</c> on the AppHost with a real Site and Phase Two (Story 1.9; AD-3): the
/// Organization's <c>displayName</c> changes first, then the journal and the identity projection.
/// </summary>
public sealed class RenameSiteTests(EdgeApiFixture edge) : IClassFixture<EdgeApiFixture>
{
    [Fact]
    public async Task TheOwnerRenamesTheSiteInKeycloakAndEverywhereElse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var owner = await edge.CreateUserAsync("renamer", cancellationToken);
        using var server = edge.CreateServerClient(owner.AccessToken);
        var key = Guid.NewGuid().ToString();
        var siteId = await CreateSiteAsync(server, key, "Home", cancellationToken);

        using var renamed = await LotsTests.PatchNameAsync(server, $"/sites/{siteId}", "  Home garden ", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        using (var body = await EdgeApiFixture.ReadJsonAsync(renamed, cancellationToken))
        {
            Assert.Equal(siteId, body.RootElement.GetProperty("id").GetString());
            Assert.Equal("Home garden", body.RootElement.GetProperty("name").GetString());
            Assert.Equal("Owner", body.RootElement.GetProperty("role").GetString());

            // The answer is read from the identity projection, which says the Site is not paused (Story 8.2).
            Assert.False(body.RootElement.GetProperty("paused").GetBoolean());
            Assert.False(body.RootElement.TryGetProperty("pausedUntil", out _));
        }

        Assert.Equal(["site.created", "site.membership-granted", "site.renamed"], await edge.AliasesAsync($"site/{siteId}", cancellationToken));

        // Read-your-writes: the list shows the new name at once.
        using (var list = await server.GetAsync(new Uri("/sites", UriKind.Relative), cancellationToken))
        using (var sites = await EdgeApiFixture.ReadJsonAsync(list, cancellationToken))
        {
            var site = Assert.Single(sites.RootElement.GetProperty("sites").EnumerateArray());
            Assert.Equal("Home garden", site.GetProperty("name").GetString());
        }

        // Keycloak holds the name and keeps the creation tag.
        using var organizations = await edge.CreateOrganizationsClientAsync(cancellationToken);
        using var organization = await organizations.GetAsync(new Uri($"/realms/{EdgeApiFixture.Realm}/orgs/{siteId}", UriKind.Relative), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, organization.StatusCode);
        using var org = await EdgeApiFixture.ReadJsonAsync(organization, cancellationToken);
        Assert.Equal("Home garden", org.RootElement.GetProperty("displayName").GetString());
        Assert.Equal(siteId, org.RootElement.GetProperty("name").GetString());
        Assert.Contains(
            $"{owner.UserId}:{key}",
            org.RootElement.GetProperty("attributes").GetProperty(IPhaseTwoOrganizations.IdempotencyKeyAttribute).EnumerateArray().Select(value => value.GetString()));
    }

    // Story 8.2: the answer to a rename is a Site read like any other, so it carries the Site's Pause.
    [Fact]
    public async Task TheAnswerToRenamingAPausedSiteSaysPausedAndUntilWhen()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var owner = await edge.CreateUserAsync("renamer-paused", cancellationToken);
        using var server = edge.CreateServerClient(owner.AccessToken);
        var siteId = await CreateSiteAsync(server, Guid.NewGuid().ToString(), "Home", cancellationToken);

        // The Owner told the Server no time zone: the end is midnight UTC of the date.
        var until = DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime).AddDays(140);
        using var paused = await server.PutAsJsonAsync(
            new Uri($"/sites/{siteId}/pause", UriKind.Relative),
            new { until = until.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, paused.StatusCode);

        using var renamed = await LotsTests.PatchNameAsync(server, $"/sites/{siteId}", "Home garden", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        using var body = await EdgeApiFixture.ReadJsonAsync(renamed, cancellationToken);
        Assert.Equal(
            ["id", "name", "paused", "pausedUntil", "role"],
            body.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal("Home garden", body.RootElement.GetProperty("name").GetString());
        Assert.True(body.RootElement.GetProperty("paused").GetBoolean());
        Assert.Equal(
            until.ToDateTime(TimeOnly.MinValue).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
            body.RootElement.GetProperty("pausedUntil").GetString());

        // The same name again changes nothing and still says so.
        using var same = await LotsTests.PatchNameAsync(server, $"/sites/{siteId}", "Home garden", cancellationToken);
        using var sameBody = await EdgeApiFixture.ReadJsonAsync(same, cancellationToken);
        Assert.True(sameBody.RootElement.GetProperty("paused").GetBoolean());
    }

    [Fact]
    public async Task TheCurrentNameChangesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var owner = await edge.CreateUserAsync("renamer-same", cancellationToken);
        using var server = edge.CreateServerClient(owner.AccessToken);
        var siteId = await CreateSiteAsync(server, Guid.NewGuid().ToString(), "Home", cancellationToken);

        using var renamed = await LotsTests.PatchNameAsync(server, $"/sites/{siteId}", "Home", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(["site.created", "site.membership-granted"], await edge.AliasesAsync($"site/{siteId}", cancellationToken));
    }

    [Fact]
    public async Task AnInvalidNameIsRefusedAndNothingChanges()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var owner = await edge.CreateUserAsync("renamer-invalid", cancellationToken);
        using var server = edge.CreateServerClient(owner.AccessToken);
        var siteId = await CreateSiteAsync(server, Guid.NewGuid().ToString(), "Home", cancellationToken);

        foreach (var name in new[] { "   ", new string('n', 101) })
        {
            using var renamed = await LotsTests.PatchNameAsync(server, $"/sites/{siteId}", name, cancellationToken);
            await EdgeApiTests.AssertProblemAsync(renamed, HttpStatusCode.BadRequest, "urn:coldframe:problem:validation", cancellationToken);
        }

        using var noName = await server.PatchAsync(
            new Uri($"/sites/{siteId}", UriKind.Relative),
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            cancellationToken);
        await EdgeApiTests.AssertProblemAsync(noName, HttpStatusCode.BadRequest, "urn:coldframe:problem:validation", cancellationToken);

        Assert.Equal(["site.created", "site.membership-granted"], await edge.AliasesAsync($"site/{siteId}", cancellationToken));
    }

    private static async Task<string> CreateSiteAsync(HttpClient server, string key, string name, CancellationToken cancellationToken)
    {
        using var created = await EdgeApiTests.PostSiteAsync(server, key, new { name }, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var body = await EdgeApiFixture.ReadJsonAsync(created, cancellationToken);
        return body.RootElement.GetProperty("id").GetString()!;
    }
}
