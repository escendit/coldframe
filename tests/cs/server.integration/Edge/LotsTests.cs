using System.Net;
using System.Net.Http.Json;
using Coldframe.Contracts.Lots;
using Coldframe.Contracts.Sites;

namespace Coldframe.Server.IntegrationTests.Edge;

/// <summary>
/// The Lot endpoints on the AppHost (Story 1.9; FR-6, AD-2, AD-14, AD-18, AD-20): create idempotently,
/// list in the Server's order, rename, remove with a tombstone, and refuse to remove a claimed Lot.
/// </summary>
/// <remarks>
/// The Site is seeded as <c>site.*</c> events: Lots never touch Keycloak. Claims arrive with Node
/// assignment in Epic 4, so a claimed Lot is a fixture stream with <c>lot.claimed</c>.
/// </remarks>
public sealed class LotsTests(EdgeApiFixture edge) : IClassFixture<EdgeApiFixture>
{
    [Fact]
    public async Task CreatedLotsAreNoNodeHaveALotStreamAndAreListedInCreationOrder()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, admin, _) = await SeedSiteAsync(cancellationToken);
        using var server = edge.CreateServerClient(admin.AccessToken);

        var tomatoes = await CreateAsync(server, siteId, "Tomatoes", cancellationToken);
        var beans = await CreateAsync(server, siteId, "  Beans  ", cancellationToken);

        Assert.Equal(("Tomatoes", "noNode", 7), (tomatoes.Name, tomatoes.Status, Guid.Parse(tomatoes.Id).Version));
        Assert.Equal(("Beans", "noNode"), (beans.Name, beans.Status));
        Assert.Equal(["lot.created"], await edge.AliasesAsync($"lot/{tomatoes.Id}", cancellationToken));
        Assert.Equal(["lot.created"], await edge.AliasesAsync($"lot/{beans.Id}", cancellationToken));

        Assert.Equal([tomatoes, beans], await ListAsync(server, siteId, cancellationToken));
    }

    [Fact]
    public async Task TheListFollowsTheStatusOrderBeforeCreationOrder()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, _, member) = await SeedSiteAsync(cancellationToken);

        var free = await edge.SeedLotAsync(siteId, "Free", cancellationToken);
        var claimed = await edge.SeedLotAsync(siteId, "Claimed", cancellationToken, new LotClaimed("7C19"));

        using var server = edge.CreateServerClient(member.AccessToken);

        // unknown sorts before noNode (AD-14), although the claimed Lot is newer.
        Assert.Equal(
            [new LotBody(claimed, "Claimed", "unknown", null), new LotBody(free, "Free", "noNode", null)],
            await ListAsync(server, siteId, cancellationToken));
    }

    [Fact]
    public async Task ARetryWithTheSameKeyReturnsTheFirstLotAndAnotherNameIsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, admin, _) = await SeedSiteAsync(cancellationToken);
        using var server = edge.CreateServerClient(admin.AccessToken);
        var key = Guid.NewGuid().ToString();

        using var first = await PostLotAsync(server, siteId, key, new { name = "Tomatoes" }, cancellationToken);
        using var retry = await PostLotAsync(server, siteId, key, new { name = "Tomatoes" }, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal(first.Headers.Location, retry.Headers.Location);
        var lot = await ReadLotAsync(first, cancellationToken);
        Assert.Equal(lot, await ReadLotAsync(retry, cancellationToken));

        using var reused = await PostLotAsync(server, siteId, key, new { name = "Beans" }, cancellationToken);
        await EdgeApiTests.AssertProblemAsync(reused, HttpStatusCode.UnprocessableEntity, "urn:coldframe:problem:idempotency-key-reused", cancellationToken);

        Assert.Equal([lot], await ListAsync(server, siteId, cancellationToken));
    }

    [Fact]
    public async Task InvalidNamesAndAMissingKeyAreRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, admin, _) = await SeedSiteAsync(cancellationToken);
        using var server = edge.CreateServerClient(admin.AccessToken);

        foreach (var body in new object[] { new { name = "   " }, new { name = new string('n', 101) }, new { other = "Beans" } })
        {
            using var created = await PostLotAsync(server, siteId, Guid.NewGuid().ToString(), body, cancellationToken);
            await EdgeApiTests.AssertProblemAsync(created, HttpStatusCode.BadRequest, "urn:coldframe:problem:validation", cancellationToken);
        }

        using var missingKey = await PostLotAsync(server, siteId, null, new { name = "Beans" }, cancellationToken);
        await EdgeApiTests.AssertProblemAsync(missingKey, HttpStatusCode.BadRequest, "urn:coldframe:problem:idempotency-key-missing", cancellationToken);

        var lotId = await edge.SeedLotAsync(siteId, "Tomatoes", cancellationToken);
        using var renamed = await PatchNameAsync(server, $"/sites/{siteId}/lots/{lotId}", " ", cancellationToken);
        await EdgeApiTests.AssertProblemAsync(renamed, HttpStatusCode.BadRequest, "urn:coldframe:problem:validation", cancellationToken);

        Assert.Equal([new LotBody(lotId, "Tomatoes", "noNode", null)], await ListAsync(server, siteId, cancellationToken));
    }

    [Fact]
    public async Task ARenameIsListedAtOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, admin, _) = await SeedSiteAsync(cancellationToken);
        using var server = edge.CreateServerClient(admin.AccessToken);
        var lot = await CreateAsync(server, siteId, "Tomatoes", cancellationToken);

        using var renamed = await PatchNameAsync(server, $"/sites/{siteId}/lots/{lot.Id}", "Tomatoes east", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(lot with { Name = "Tomatoes east" }, await ReadLotAsync(renamed, cancellationToken));
        Assert.Equal([lot with { Name = "Tomatoes east" }], await ListAsync(server, siteId, cancellationToken));
        Assert.Equal(["lot.created", "lot.renamed"], await edge.AliasesAsync($"lot/{lot.Id}", cancellationToken));

        // The same name again changes nothing.
        using var again = await PatchNameAsync(server, $"/sites/{siteId}/lots/{lot.Id}", "Tomatoes east", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(["lot.created", "lot.renamed"], await edge.AliasesAsync($"lot/{lot.Id}", cancellationToken));
    }

    [Fact]
    public async Task ARemovedLotLeavesTheListButStaysResolvable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, admin, _) = await SeedSiteAsync(cancellationToken);
        using var server = edge.CreateServerClient(admin.AccessToken);
        var tomatoes = await CreateAsync(server, siteId, "Tomatoes", cancellationToken);
        var beans = await CreateAsync(server, siteId, "Beans", cancellationToken);

        using var removed = await server.DeleteAsync(LotUri(siteId, tomatoes.Id), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(["lot.created", "lot.removed"], await edge.AliasesAsync($"lot/{tomatoes.Id}", cancellationToken));
        Assert.Equal([beans], await ListAsync(server, siteId, cancellationToken));

        using var read = await server.GetAsync(LotUri(siteId, tomatoes.Id), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(tomatoes with { Removed = true }, await ReadLotAsync(read, cancellationToken));

        // A second removal answers the same and journals nothing; a removed Lot cannot be renamed.
        using var again = await server.DeleteAsync(LotUri(siteId, tomatoes.Id), cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Equal(["lot.created", "lot.removed"], await edge.AliasesAsync($"lot/{tomatoes.Id}", cancellationToken));

        using var renamed = await PatchNameAsync(server, $"/sites/{siteId}/lots/{tomatoes.Id}", "Back", cancellationToken);
        await EdgeApiTests.AssertProblemAsync(renamed, HttpStatusCode.NotFound, "urn:coldframe:problem:lot-not-found", cancellationToken);
    }

    [Fact]
    public async Task AClaimedLotRefusesRemovalAndNothingChanges()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, admin, _) = await SeedSiteAsync(cancellationToken);
        var lotId = await edge.SeedLotAsync(siteId, "Tomatoes", cancellationToken, new LotClaimed("7C19"));
        using var server = edge.CreateServerClient(admin.AccessToken);

        using var removed = await server.DeleteAsync(LotUri(siteId, lotId), cancellationToken);

        await EdgeApiTests.AssertProblemAsync(removed, HttpStatusCode.Conflict, "urn:coldframe:problem:lot-claimed", cancellationToken);
        Assert.Equal(["lot.created", "lot.claimed"], await edge.AliasesAsync($"lot/{lotId}", cancellationToken));
        Assert.Equal([new LotBody(lotId, "Tomatoes", "unknown", null)], await ListAsync(server, siteId, cancellationToken));
    }

    [Fact]
    public async Task OnlyTheClaimingNodesReleaseFreesTheLot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, _, member) = await SeedSiteAsync(cancellationToken);
        var lotId = await edge.SeedLotAsync(siteId, "Tomatoes", cancellationToken, new LotClaimed("A"), new LotReleased("B"));
        using var server = edge.CreateServerClient(member.AccessToken);

        // A release by another Node leaves the claim in place.
        Assert.Equal([new LotBody(lotId, "Tomatoes", "unknown", null)], await ListAsync(server, siteId, cancellationToken));

        var last = await edge.AppendAsync($"lot/{lotId}", 3, [new LotReleased("A")], cancellationToken);
        await edge.WaitForProjectionCheckpointAsync("lots", last);

        Assert.Equal([new LotBody(lotId, "Tomatoes", "noNode", null)], await ListAsync(server, siteId, cancellationToken));
    }

    [Fact]
    public async Task AnotherSitesLotOrAnUnknownIdIsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, admin, _) = await SeedSiteAsync(cancellationToken);
        var (otherSiteId, _, _) = await SeedSiteAsync(cancellationToken);
        var foreign = await edge.SeedLotAsync(otherSiteId, "Theirs", cancellationToken);
        using var server = edge.CreateServerClient(admin.AccessToken);

        foreach (var lotId in new[] { foreign, Guid.CreateVersion7().ToString(), "not-a-lot" })
        {
            using var read = await server.GetAsync(LotUri(siteId, lotId), cancellationToken);
            await EdgeApiTests.AssertProblemAsync(read, HttpStatusCode.NotFound, "urn:coldframe:problem:lot-not-found", cancellationToken);

            using var renamed = await PatchNameAsync(server, $"/sites/{siteId}/lots/{lotId}", "Mine", cancellationToken);
            await EdgeApiTests.AssertProblemAsync(renamed, HttpStatusCode.NotFound, "urn:coldframe:problem:lot-not-found", cancellationToken);

            using var removed = await server.DeleteAsync(LotUri(siteId, lotId), cancellationToken);
            await EdgeApiTests.AssertProblemAsync(removed, HttpStatusCode.NotFound, "urn:coldframe:problem:lot-not-found", cancellationToken);
        }

        Assert.Equal(["lot.created"], await edge.AliasesAsync($"lot/{foreign}", cancellationToken));
    }

    internal static async Task<HttpResponseMessage> PostLotAsync(
        HttpClient server,
        string siteId,
        string? key,
        object body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/sites/{siteId}/lots", UriKind.Relative))
        {
            Content = JsonContent.Create(body),
        };

        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        return await server.SendAsync(request, cancellationToken);
    }

    internal static Task<HttpResponseMessage> PatchNameAsync(HttpClient server, string path, string name, CancellationToken cancellationToken) =>
        server.PatchAsJsonAsync(new Uri(path, UriKind.Relative), new { name }, cancellationToken);

    internal static async Task<LotBody> ReadLotAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var body = await EdgeApiFixture.ReadJsonAsync(response, cancellationToken);
        return LotBody.Of(body.RootElement);
    }

    private static Uri LotUri(string siteId, string lotId) => new($"/sites/{siteId}/lots/{lotId}", UriKind.Relative);

    private static async Task<List<LotBody>> ListAsync(HttpClient server, string siteId, CancellationToken cancellationToken)
    {
        using var response = await server.GetAsync(new Uri($"/sites/{siteId}/lots", UriKind.Relative), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await EdgeApiFixture.ReadJsonAsync(response, cancellationToken);
        return [.. body.RootElement.GetProperty("lots").EnumerateArray().Select(LotBody.Of)];
    }

    private static async Task<LotBody> CreateAsync(HttpClient server, string siteId, string name, CancellationToken cancellationToken)
    {
        using var response = await PostLotAsync(server, siteId, Guid.NewGuid().ToString(), new { name }, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var lot = await ReadLotAsync(response, cancellationToken);
        Assert.Equal(new Uri($"/sites/{siteId}/lots/{lot.Id}", UriKind.Relative), response.Headers.Location);
        return lot;
    }

    // A Site with an Administrator and a Member, seeded as events.
    private async Task<(string SiteId, TestUser Administrator, TestUser Member)> SeedSiteAsync(CancellationToken cancellationToken)
    {
        var owner = await edge.CreateUserAsync("lots-owner", cancellationToken);
        var administrator = await edge.CreateUserAsync("lots-admin", cancellationToken);
        var member = await edge.CreateUserAsync("lots-member", cancellationToken);
        var siteId = Guid.CreateVersion7().ToString();

        var last = await edge.AppendAsync(
            $"site/{siteId}",
            [
                new SiteCreated("Lots", owner.UserId),
                new MembershipGranted(owner.UserId, SiteRole.Owner),
                new MembershipGranted(administrator.UserId, SiteRole.Administrator),
                new MembershipGranted(member.UserId, SiteRole.Member),
            ],
            cancellationToken);
        await edge.WaitForIdentityCheckpointAsync(last);

        return (siteId, administrator, member);
    }
}

/// <summary>
/// What the Lot endpoints of Story 1.9 answer about a Lot; <see cref="Removed"/> is null when the field is
/// absent. The status fields of Story 4.7 are checked as they are read and compared in <c>LotStatusTests</c>.
/// </summary>
internal sealed record LotBody(string Id, string Name, string Status, bool? Removed)
{
    public static LotBody Of(System.Text.Json.JsonElement lot)
    {
        var names = lot.EnumerateObject().Select(property => property.Name).ToList();
        string[] known = ["id", "name", "status", "statusSince", "lastReadingAt", "unknownCause", "pausedBy", "pausedUntil", "removed"];
        Assert.Subset(new HashSet<string>(known, StringComparer.Ordinal), names.ToHashSet(StringComparer.Ordinal));

        // Every Lot says since when it has its status, in UTC (Story 4.7); LotStatusTests checks the values.
        Assert.True(DateTimeOffset.TryParse(lot.GetProperty("statusSince").GetString(), System.Globalization.CultureInfo.InvariantCulture, out _));
        Assert.Equal(lot.GetProperty("status").GetString() == "unknown", lot.TryGetProperty("unknownCause", out _));

        return new LotBody(
            lot.GetProperty("id").GetString()!,
            lot.GetProperty("name").GetString()!,
            lot.GetProperty("status").GetString()!,
            lot.TryGetProperty("removed", out var removed) ? removed.GetBoolean() : null);
    }
}
