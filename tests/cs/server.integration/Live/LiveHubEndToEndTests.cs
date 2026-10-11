using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Lots;
using Coldframe.Contracts.Sites;
using Coldframe.DeviceSimulator;
using Coldframe.Server.IntegrationTests.Devices;
using Coldframe.Server.IntegrationTests.Edge;
using Microsoft.AspNetCore.SignalR.Client;
using ProtocolKind = Coldframe.Protocol.Setup.V1.DeviceKind;

namespace Coldframe.Server.IntegrationTests.Live;

/// <summary>
/// The SignalR hub on the AppHost's Server (Story 6.6), with Keycloak's own tokens: a hint follows every
/// projector commit and every ingest, reaches the members of that Site only, and follows a Membership that is
/// granted or revoked while the connection stays open. The hub refuses a caller without a token, and the
/// AppHost, which is not given the clock switch, has no <c>/testing</c> endpoint.
/// </summary>
[Collection(IngestSuites.Name)]
public sealed class LiveHubEndToEndTests(EdgeApiFixture edge) : IClassFixture<EdgeApiFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ADirectRequestToTheHubWithoutATokenIs401ProblemDetails()
    {
        using var server = edge.CreateServerClient();

        using var negotiate = await server.PostAsync(new Uri("/live/negotiate?negotiateVersion=1", UriKind.Relative), content: null, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, negotiate.StatusCode);
        using var problem = await EdgeApiFixture.ReadJsonAsync(negotiate, Ct);
        Assert.Equal("urn:coldframe:problem:unauthorized", problem.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task WithoutTheClockSwitchTheAppHostsServerHasNoTestingEndpoint()
    {
        var user = await edge.CreateUserAsync("live-clock", Ct);
        using var server = edge.CreateServerClient(user.AccessToken);

        using var read = await server.GetAsync(new Uri("/testing/clock", UriKind.Relative), Ct);
        using var set = await server.PostAsJsonAsync(new Uri("/testing/clock", UriKind.Relative), new { now = "2099-01-01T00:00:00Z" }, Ct);
        using var wake = await server.PostAsync(new Uri($"/testing/users/{user.UserId}/wake", UriKind.Relative), content: null, Ct);

        Assert.Equal(
            (HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound),
            (read.StatusCode, set.StatusCode, wake.StatusCode));
    }

    [Fact]
    public async Task HintsFollowEveryProjectorCommitForTheSitesMembersOnlyAndFollowAMembershipWithoutAReconnect()
    {
        var owner = await edge.CreateUserAsync("live-owner", Ct);
        var member = await edge.CreateUserAsync("live-member", Ct);
        var outsider = await edge.CreateUserAsync("live-outsider", Ct);
        await using var ownerTab = await LiveProbe.ConnectAsync(edge, owner, Ct);
        await using var memberTab = await LiveProbe.ConnectAsync(edge, member, Ct);
        await using var outsiderTab = await LiveProbe.ConnectAsync(edge, outsider, Ct);
        var siteId = Guid.CreateVersion7().ToString();
        var node = Convert.ToHexStringLower(Guid.NewGuid().ToByteArray().AsSpan(0, 8));

        // Identity: the Owner is told about the Site the moment the Membership is committed.
        var granted = await edge.AppendAsync(
            $"site/{siteId}",
            [new SiteCreated("Live", owner.UserId), new MembershipGranted(owner.UserId, SiteRole.Owner)],
            Ct);
        var sites = await ownerTab.WaitForAsync("sites", siteId, Ct);
        Assert.Equal(granted, sites.Version);
        AssertOnlyTheThreeFields(sites);
        Assert.True(await CheckpointAsync("identity") >= sites.Version, "The hint arrived before its commit.");

        // Lots: the Owner, not yet the future Member.
        var lotId = Guid.CreateVersion7().ToString();
        var created = await edge.AppendAsync($"lot/{lotId}", [new LotCreated(siteId, "Tomatoes")], Ct);
        Assert.Equal(created, (await ownerTab.WaitForAsync("lots", lotId, Ct)).Version);

        // The Membership is granted while the Member's tab is open: told once, then a member like any other.
        var joined = await edge.AppendAsync($"site/{siteId}", 2, [new MembershipGranted(member.UserId, SiteRole.Member)], Ct);
        Assert.Equal(joined, (await memberTab.WaitForAsync("sites", siteId, Ct)).Version);

        var wrapped = new WrappedDeviceKey("0000000000000000", new byte[12], new byte[48]);
        var enrolled = await edge.AppendAsync($"device/{node}", [new DeviceEnrolled(siteId, DeviceKind.Node, wrapped, DateTimeOffset.UtcNow)], Ct);
        var alertId = Guid.NewGuid();
        var opened = await edge.AppendAsync(
            $"alert/{alertId:D}",
            [new AlertOpened(AlertKind.Threshold, ThresholdSide.Low, siteId, lotId, Guid.NewGuid(), node, "soil_moisture", 1, DateTimeOffset.UtcNow)],
            Ct);

        foreach (var tab in new[] { ownerTab, memberTab })
        {
            Assert.Equal(enrolled, (await tab.WaitForAsync("devices", node, Ct)).Version);
            Assert.Equal(opened, (await tab.WaitForAsync("alerts", alertId.ToString("D"), Ct)).Version);
        }

        // The Membership is revoked: told once more, and nothing of the Site afterwards.
        var revoked = await edge.AppendAsync($"site/{siteId}", 3, [new MembershipRevoked(member.UserId)], Ct);
        Assert.Equal(revoked, (await memberTab.WaitForAsync("sites", siteId, Ct, atLeast: revoked)).Version);
        var renamed = await edge.AppendAsync($"lot/{lotId}", 1, [new LotRenamed("Beans")], Ct);
        Assert.Equal(renamed, (await ownerTab.WaitForAsync("lots", lotId, Ct, atLeast: renamed)).Version);

        // A hint of their own last, so that everything before it has had its time to arrive.
        foreach (var (user, tab) in new[] { (member, memberTab), (outsider, outsiderTab) })
        {
            var own = Guid.CreateVersion7().ToString();
            await edge.AppendAsync($"site/{own}", [new SiteCreated("Own", user.UserId), new MembershipGranted(user.UserId, SiteRole.Owner)], Ct);
            await tab.WaitForAsync("sites", own, Ct);
        }

        Assert.DoesNotContain(memberTab.Hints, hint => hint.Id == lotId);
        Assert.DoesNotContain(memberTab.Hints, hint => hint.Version == renamed);
        Assert.Single(outsiderTab.Hints);
        Assert.All(ownerTab.Hints.Concat(memberTab.Hints).Concat(outsiderTab.Hints), AssertOnlyTheThreeFields);
        Assert.Equal(
            (HubConnectionState.Connected, HubConnectionState.Connected),
            (memberTab.State, outsiderTab.State));
        Assert.Empty(ownerTab.Notifications);
    }

    [Fact]
    public async Task AnIngestHintsTheLotsReadingsAfterItsCommitAndADuplicateFrameHintsNothing()
    {
        var administrator = await edge.CreateUserAsync("live-ingest", Ct);
        using var server = edge.CreateServerClient(administrator.AccessToken);
        using var siteResponse = await PostSiteAsync(server, "Live ingest");
        var siteId = (await EdgeApiFixture.ReadJsonAsync(siteResponse, Ct)).RootElement.GetProperty("id").GetString()!;
        var lotId = await edge.SeedLotAsync(siteId, "Tomatoes", Ct);
        var hub = await EnrolAsync(server, siteId, ProtocolKind.Hub, lotId: null);
        var node = await EnrolAsync(server, siteId, ProtocolKind.Node, lotId);
        await using var tab = await LiveProbe.ConnectAsync(edge, administrator, Ct);

        var measuredAt = DateTimeOffset.FromUnixTimeMilliseconds(TimeProvider.System.GetUtcNow().ToUnixTimeMilliseconds());
        var frame = node.Wake(measuredAt);
        Assert.Equal(["stored"], await IngestAsync(hub, node.SealFrame(frame)));

        var hint = await tab.WaitForAsync("readings", lotId, Ct);
        Assert.Equal(measuredAt.ToUnixTimeMilliseconds(), hint.Version);
        AssertOnlyTheThreeFields(hint);
        Assert.Equal(
            4L,
            await ScalarAsync("SELECT COUNT(*) FROM readings WHERE device_id = @id", node.DeviceId.ToString()));

        // The same frame again stores nothing new; a later one hints again, with a later version.
        Assert.Equal(["duplicate"], await IngestAsync(hub, node.SealFrame(frame)));
        var later = measuredAt.AddSeconds(1);
        Assert.Equal(["stored"], await IngestAsync(hub, node.SealFrame(node.Wake(later))));
        await tab.WaitForAsync("readings", lotId, Ct, atLeast: later.ToUnixTimeMilliseconds());

        Assert.Equal(
            [measuredAt.ToUnixTimeMilliseconds(), later.ToUnixTimeMilliseconds()],
            tab.Hints.Where(received => received.Resource == "readings").Select(received => received.Version));
    }

    private static void AssertOnlyTheThreeFields(ReceivedHint hint)
    {
        using var json = JsonDocument.Parse(hint.Json);
        Assert.Equal(["resource", "id", "version"], json.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.True(hint.Version > 0);
    }

    private static async Task<HttpResponseMessage> PostSiteAsync(HttpClient server, string name)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/sites", UriKind.Relative)) { Content = JsonContent.Create(new { name }) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var response = await server.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return response;
    }

    private async Task<SimulatedDevice> EnrolAsync(HttpClient server, string siteId, ProtocolKind kind, string? lotId)
    {
        var device = SimulatedDevice.Create(kind);
        var sealedEnrolment = device.SealEnrolment(await edge.GetEnrolmentPublicKeyAsync(Ct), siteId);
        using var enrolled = await EnrolmentTests.PostAsync(
            server,
            siteId,
            Guid.NewGuid().ToString(),
            new EnrolBody(sealedEnrolment.DeviceId, sealedEnrolment.Kind, sealedEnrolment.Enc, sealedEnrolment.Ciphertext, lotId),
            Ct);
        Assert.Equal(HttpStatusCode.Created, enrolled.StatusCode);

        return device;
    }

    private async Task<IEnumerable<string>> IngestAsync(SimulatedDevice hub, Coldframe.Protocol.Device.V1.SealedEnvelope frame)
    {
        using var server = edge.CreateServerClient();
        using var request = hub.IngestRequest(TimeProvider.System.GetUtcNow(), SimulatedDevice.IngestBody(frame));
        using var response = await server.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return SimulatedDevice.ReadIngestResponse(await response.Content.ReadAsByteArrayAsync(Ct)).Select(result => result.Status);
    }

    private async Task<long> CheckpointAsync(string projector) =>
        await ScalarAsync("SELECT position FROM projection_checkpoints WHERE projector = @id", projector);

    private async Task<long> ScalarAsync(string sql, string id)
    {
        await using var command = edge.Database.CreateCommand(sql);
        command.Parameters.AddWithValue("id", id);

        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }
}
