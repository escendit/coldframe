using System.Net;
using System.Net.Http.Json;
using Coldframe.Contracts.Sites;
using Coldframe.DeviceSimulator;
using Coldframe.Server.IntegrationTests.Edge;
using ProtocolKind = Coldframe.Protocol.Setup.V1.DeviceKind;

namespace Coldframe.Server.IntegrationTests.Devices;

/// <summary>
/// <c>POST /sites/{siteId}/devices/{deviceId}/move</c> and <c>/unassign</c> on the AppHost (Story 4.9): an
/// Administrator moves and unassigns a Node, a Member gets 403 and changes nothing, an occupied Lot is 409
/// <c>lot-claimed</c> and an unknown Node 404. The authorization matrix covers every Role and Site.
/// </summary>
public sealed class NodeMoveEndpointTests(EdgeApiFixture edge) : IClassFixture<EdgeApiFixture>
{
    [Fact]
    public async Task AnAdministratorMovesANodeAndUnassignsItAndTheListFollows()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var site = await SeedSiteAsync(cancellationToken);
        var tomatoes = await edge.SeedLotAsync(site.Id, "Tomatoes", cancellationToken);
        var peppers = await edge.SeedLotAsync(site.Id, "Peppers", cancellationToken);
        var node = await EnrolNodeAsync(site, tomatoes, cancellationToken);
        using var server = edge.CreateServerClient(site.Administrator.AccessToken);

        using var moved = await server.PostAsJsonAsync(MoveUri(site.Id, node), new { lotId = peppers }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Equal(peppers, await LotOfAsync(moved, cancellationToken));
        Assert.Equal(["device.enrolled", "device.assigned", "device.moved"], (await edge.AliasesAsync($"device/{node}", cancellationToken)).Take(3));

        using var unassigned = await server.PostAsync(UnassignUri(site.Id, node), content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, unassigned.StatusCode);
        Assert.Null(await LotOfAsync(unassigned, cancellationToken));

        using var again = await server.PostAsync(UnassignUri(site.Id, node), content: null, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }

    [Fact]
    public async Task AnOccupiedLotIs409LotClaimedAndAnUnknownNodeOrLotIs404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var site = await SeedSiteAsync(cancellationToken);
        var tomatoes = await edge.SeedLotAsync(site.Id, "Tomatoes", cancellationToken);
        var occupied = await edge.SeedLotAsync(site.Id, "Peppers", cancellationToken, new Coldframe.Contracts.Lots.LotClaimed("7c40000000000001"));
        var node = await EnrolNodeAsync(site, tomatoes, cancellationToken);
        using var server = edge.CreateServerClient(site.Administrator.AccessToken);

        using var claimed = await server.PostAsJsonAsync(MoveUri(site.Id, node), new { lotId = occupied }, cancellationToken);
        await EdgeApiTests.AssertProblemAsync(claimed, HttpStatusCode.Conflict, "urn:coldframe:problem:lot-claimed", cancellationToken);

        using var noLot = await server.PostAsJsonAsync(MoveUri(site.Id, node), new { lotId = Guid.CreateVersion7() }, cancellationToken);
        await EdgeApiTests.AssertProblemAsync(noLot, HttpStatusCode.NotFound, "urn:coldframe:problem:lot-not-found", cancellationToken);

        using var noNode = await server.PostAsJsonAsync(MoveUri(site.Id, "7c40ffffffffffff"), new { lotId = tomatoes }, cancellationToken);
        await EdgeApiTests.AssertProblemAsync(noNode, HttpStatusCode.NotFound, "urn:coldframe:problem:device-not-found", cancellationToken);

        using var bad = await server.PostAsJsonAsync(MoveUri(site.Id, node), new { lotId = "nope" }, cancellationToken);
        await EdgeApiTests.AssertProblemAsync(bad, HttpStatusCode.BadRequest, "urn:coldframe:problem:validation", cancellationToken);

        Assert.Equal(["device.enrolled", "device.assigned"], await edge.AliasesAsync($"device/{node}", cancellationToken));
    }

    [Fact]
    public async Task AMemberGets403AndTheNodeKeepsItsLot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var site = await SeedSiteAsync(cancellationToken);
        var tomatoes = await edge.SeedLotAsync(site.Id, "Tomatoes", cancellationToken);
        var peppers = await edge.SeedLotAsync(site.Id, "Peppers", cancellationToken);
        var node = await EnrolNodeAsync(site, tomatoes, cancellationToken);
        using var server = edge.CreateServerClient(site.Member.AccessToken);

        using var move = await server.PostAsJsonAsync(MoveUri(site.Id, node), new { lotId = peppers }, cancellationToken);
        await EdgeApiTests.AssertProblemAsync(move, HttpStatusCode.Forbidden, "urn:coldframe:problem:forbidden", cancellationToken);

        using var unassign = await server.PostAsync(UnassignUri(site.Id, node), content: null, cancellationToken);
        await EdgeApiTests.AssertProblemAsync(unassign, HttpStatusCode.Forbidden, "urn:coldframe:problem:forbidden", cancellationToken);

        Assert.Equal(["device.enrolled", "device.assigned"], await edge.AliasesAsync($"device/{node}", cancellationToken));
        Assert.Equal(["lot.created"], await edge.AliasesAsync($"lot/{peppers}", cancellationToken));
    }

    private static Uri MoveUri(string siteId, string deviceId) => new($"/sites/{siteId}/devices/{deviceId}/move", UriKind.Relative);

    private static Uri UnassignUri(string siteId, string deviceId) => new($"/sites/{siteId}/devices/{deviceId}/unassign", UriKind.Relative);

    private static async Task<string?> LotOfAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var body = await EdgeApiFixture.ReadJsonAsync(response, cancellationToken);
        return body.RootElement.TryGetProperty("lotId", out var lot) ? lot.GetString() : null;
    }

    private async Task<string> EnrolNodeAsync(SeededSite site, string lotId, CancellationToken cancellationToken)
    {
        var node = SimulatedDevice.Create(ProtocolKind.Node);
        var sealedEnrolment = node.SealEnrolment(await edge.GetEnrolmentPublicKeyAsync(cancellationToken), site.Id, lotId);
        using var server = edge.CreateServerClient(site.Administrator.AccessToken);
        using var enrolled = await EnrolmentTests.PostAsync(
            server,
            site.Id,
            Guid.NewGuid().ToString(),
            new EnrolBody(sealedEnrolment.DeviceId, sealedEnrolment.Kind, sealedEnrolment.Enc, sealedEnrolment.Ciphertext, sealedEnrolment.LotId),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, enrolled.StatusCode);

        return node.DeviceId.ToString();
    }

    private async Task<SeededSite> SeedSiteAsync(CancellationToken cancellationToken)
    {
        var owner = await edge.CreateUserAsync("move-owner", cancellationToken);
        var administrator = await edge.CreateUserAsync("move-admin", cancellationToken);
        var member = await edge.CreateUserAsync("move-member", cancellationToken);
        var siteId = Guid.CreateVersion7().ToString();

        var last = await edge.AppendAsync(
            $"site/{siteId}",
            [
                new SiteCreated("Move", owner.UserId),
                new MembershipGranted(owner.UserId, SiteRole.Owner),
                new MembershipGranted(administrator.UserId, SiteRole.Administrator),
                new MembershipGranted(member.UserId, SiteRole.Member),
            ],
            cancellationToken);
        await edge.WaitForIdentityCheckpointAsync(last);

        return new SeededSite(siteId, administrator, member);
    }

    private sealed record SeededSite(string Id, TestUser Administrator, TestUser Member);
}
