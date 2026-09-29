using System.Net;
using System.Text.Json;
using Coldframe.Contracts.Sites;

namespace Coldframe.Server.IntegrationTests.Edge;

/// <summary>
/// <c>GET /sites</c> on the AppHost: the caller's Active Sites with the caller's Role, from the identity
/// projection, in creation order (Story 1.8, "List my Sites").
/// </summary>
/// <remarks>
/// The Sites are seeded as <c>site.*</c> events on fresh streams of the Server's journal, one stream after
/// the other, so their projected <c>created_at</c> follows the seeding order.
/// </remarks>
public sealed class ListSitesTests(EdgeApiFixture edge) : IClassFixture<EdgeApiFixture>
{
    [Fact]
    public async Task ListsTheCallersActiveSitesWithTheirRoleInCreationOrder()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var caller = await edge.CreateUserAsync("lister", cancellationToken);
        var other = await edge.CreateUserAsync("lister-other", cancellationToken);

        var siteA = Guid.CreateVersion7().ToString();
        var siteB = Guid.CreateVersion7().ToString();
        var siteC = Guid.CreateVersion7().ToString();
        var siteD = Guid.CreateVersion7().ToString();

        // B is created by someone else, who then grants the caller Member.
        await edge.AppendAsync(
            $"site/{siteA}",
            [new SiteCreated("Allotment A", caller.UserId), new MembershipGranted(caller.UserId, SiteRole.Owner)],
            cancellationToken);
        await edge.AppendAsync(
            $"site/{siteB}",
            [
                new SiteCreated("Allotment B", other.UserId),
                new MembershipGranted(other.UserId, SiteRole.Owner),
                new MembershipGranted(caller.UserId, SiteRole.Member),
            ],
            cancellationToken);

        // C: the other User's Site, without the caller. D: the caller's, but Deleted.
        await edge.AppendAsync(
            $"site/{siteC}",
            [new SiteCreated("Allotment C", other.UserId), new MembershipGranted(other.UserId, SiteRole.Owner)],
            cancellationToken);
        var last = await edge.AppendAsync(
            $"site/{siteD}",
            [new SiteCreated("Allotment D", caller.UserId), new MembershipGranted(caller.UserId, SiteRole.Owner), new SiteDeleted()],
            cancellationToken);

        await edge.WaitForIdentityCheckpointAsync(last);

        using var server = edge.CreateServerClient(caller.AccessToken);
        using var response = await server.GetAsync(new Uri("/sites", UriKind.Relative), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            [(siteA, "Allotment A", "Owner"), (siteB, "Allotment B", "Member")],
            await ReadSitesAsync(response, cancellationToken));

        // The other User sees B and C as Owner, and never the caller's A.
        using var otherServer = edge.CreateServerClient(other.AccessToken);
        using var otherResponse = await otherServer.GetAsync(new Uri("/sites", UriKind.Relative), cancellationToken);

        Assert.Equal(
            [(siteB, "Allotment B", "Owner"), (siteC, "Allotment C", "Owner")],
            await ReadSitesAsync(otherResponse, cancellationToken));
    }

    [Fact]
    public async Task ACallerWithoutAMembershipGetsAnEmptyList()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var caller = await edge.CreateUserAsync("no-membership", cancellationToken);
        using var server = edge.CreateServerClient(caller.AccessToken);

        using var response = await server.GetAsync(new Uri("/sites", UriKind.Relative), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await EdgeApiFixture.ReadJsonAsync(response, cancellationToken);
        Assert.Equal(JsonValueKind.Array, body.RootElement.GetProperty("sites").ValueKind);
        Assert.Empty(body.RootElement.GetProperty("sites").EnumerateArray());
    }

    [Fact]
    public async Task ASiteCreatedThroughTheApiIsListedAtOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var caller = await edge.CreateUserAsync("list-created", cancellationToken);
        using var server = edge.CreateServerClient(caller.AccessToken);

        using var created = await EdgeApiTests.PostSiteAsync(server, Guid.NewGuid().ToString(), new { name = "Home" }, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdBody = await EdgeApiFixture.ReadJsonAsync(created, cancellationToken);
        var id = createdBody.RootElement.GetProperty("id").GetString()!;

        using var response = await server.GetAsync(new Uri("/sites", UriKind.Relative), cancellationToken);

        Assert.Equal([(id, "Home", "Owner")], await ReadSitesAsync(response, cancellationToken));
    }

    [Fact]
    public async Task WithoutATokenTheListIsUnauthorized()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var server = edge.CreateServerClient();

        using var response = await server.GetAsync(new Uri("/sites", UriKind.Relative), cancellationToken);

        await EdgeApiTests.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "urn:coldframe:problem:unauthorized", cancellationToken);
    }

    private static async Task<List<(string Id, string Name, string Role)>> ReadSitesAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var body = await EdgeApiFixture.ReadJsonAsync(response, cancellationToken);

        return body.RootElement.GetProperty("sites").EnumerateArray()
            .Select(site => (site.GetProperty("id").GetString()!, site.GetProperty("name").GetString()!, site.GetProperty("role").GetString()!))
            .ToList();
    }
}
