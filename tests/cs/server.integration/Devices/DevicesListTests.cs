using System.Globalization;
using System.Net;
using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Sites;
using Coldframe.DeviceSimulator;
using Coldframe.Server.IntegrationTests.Edge;

namespace Coldframe.Server.IntegrationTests.Devices;

/// <summary>
/// <c>GET /sites/{siteId}/devices</c> on the AppHost (Story 3.7): one test per Server row of the story's
/// matrix. Rows come from the devices projection of the Device streams; <c>online</c> is the Server's, from
/// its clock and the last <c>device.seen</c>.
/// </summary>
/// <remarks>
/// The AppHost's Server runs on the real clock, so a heartbeat that stopped is a <c>device.seen</c> seeded
/// with an old <c>SeenAt</c> on a fresh <c>device/{id}</c> stream, exactly as the grain journals it. The live
/// path (enrol, heartbeat, list) goes through the Device simulator.
/// </remarks>
public sealed class DevicesListTests(EdgeApiFixture edge) : IClassFixture<EdgeApiFixture>
{
    private const string Projector = "devices";

    [Fact]
    public async Task AnEnrolledHeartbeatingHubIsOnlineWithItsLastSeenTime()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, administrator, member) = await SeedSiteAsync(cancellationToken);
        var hub = SimulatedDevice.Create();

        using (var asAdministrator = edge.CreateServerClient(administrator.AccessToken))
        {
            var sealedEnrolment = hub.SealEnrolment(await edge.GetEnrolmentPublicKeyAsync(cancellationToken), siteId);
            using var enrolled = await EnrolmentTests.PostAsync(
                asAdministrator,
                siteId,
                Guid.NewGuid().ToString(),
                new EnrolBody(sealedEnrolment.DeviceId, sealedEnrolment.Kind, sealedEnrolment.Enc, sealedEnrolment.Ciphertext),
                cancellationToken);
            Assert.Equal(HttpStatusCode.Created, enrolled.StatusCode);
        }

        using (var asDevice = edge.CreateServerClient())
        {
            using var request = hub.HeartbeatRequest(Now());
            using var beat = await asDevice.SendAsync(request, cancellationToken);
            Assert.Equal(HttpStatusCode.OK, beat.StatusCode);
        }

        var events = await edge.ReadStreamAsync($"device/{hub.DeviceId}", cancellationToken);
        var seen = Assert.IsType<DeviceSeen>(events[^1].Data);
        await edge.WaitForProjectionCheckpointAsync(Projector, events[^1].Position);

        var device = Assert.Single(await ListAsync(member, siteId, cancellationToken));

        Assert.Equal(hub.DeviceId.ToString(), device.Id);
        Assert.Equal("hub", device.Kind);
        Assert.Null(device.LotId);
        Assert.True(device.Online);
        Assert.Equal(Truncate(seen.SeenAt), device.LastSeenAt);
    }

    [Theory]
    [InlineData(30, true)]
    [InlineData(121, false)]
    [InlineData(600, false)]
    public async Task AHubIsOnlineOnlyWhileItsLastHeartbeatIsWithinTheWindow(int secondsAgo, bool online)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, _, member) = await SeedSiteAsync(cancellationToken);
        var seenAt = Now().AddSeconds(-secondsAgo);
        var deviceId = await SeedDeviceAsync(siteId, DeviceKind.Hub, cancellationToken, new DeviceSeen(seenAt, seenAt.ToUnixTimeMilliseconds(), null));

        var device = Assert.Single(await ListAsync(member, siteId, cancellationToken));

        // The last-seen time is the heartbeat's, unchanged, whether the Hub is online or not.
        Assert.Equal(new DeviceBody(deviceId, "hub", null, Truncate(seenAt), online), device);
    }

    [Fact]
    public async Task TheLastSeenTimeIsTheNewestHeartbeat()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, _, member) = await SeedSiteAsync(cancellationToken);
        var first = Now().AddMinutes(-20);
        var last = Now().AddMinutes(-10);
        var deviceId = await SeedDeviceAsync(
            siteId,
            DeviceKind.Hub,
            cancellationToken,
            new DeviceSeen(first, first.ToUnixTimeMilliseconds(), null),
            new DeviceSeen(last, last.ToUnixTimeMilliseconds(), 600_000));

        Assert.Equal(
            [new DeviceBody(deviceId, "hub", null, Truncate(last), false)],
            await ListAsync(member, siteId, cancellationToken));
    }

    [Fact]
    public async Task AnOlderHeartbeatAppliedAfterANewerOneDoesNotMoveTheLastSeenTimeBackwards()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, _, member) = await SeedSiteAsync(cancellationToken);
        var newer = Now().AddMinutes(-10);
        var older = Now().AddMinutes(-20);
        var deviceId = await SeedDeviceAsync(
            siteId,
            DeviceKind.Hub,
            cancellationToken,
            new DeviceSeen(newer, newer.ToUnixTimeMilliseconds(), 600_000),
            new DeviceSeen(older, older.ToUnixTimeMilliseconds(), null));

        Assert.Equal(
            [new DeviceBody(deviceId, "hub", null, Truncate(newer), false)],
            await ListAsync(member, siteId, cancellationToken));
    }

    [Fact]
    public async Task AnEnrolmentAppliedAgainKeepsTheLotAndTheLastSeenTime()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, _, member) = await SeedSiteAsync(cancellationToken);
        var lotId = Guid.CreateVersion7().ToString();
        var seen = Now().AddMinutes(-10);
        var wrapped = new WrappedDeviceKey("0000000000000000", new byte[12], new byte[48]);
        var deviceId = await SeedDeviceAsync(
            siteId,
            DeviceKind.Node,
            cancellationToken,
            new DeviceAssigned(siteId, lotId, Now()),
            new DeviceSeen(seen, seen.ToUnixTimeMilliseconds(), null),
            new DeviceEnrolled(siteId, DeviceKind.Node, wrapped, Now()));

        Assert.Equal(
            [new DeviceBody(deviceId, "node", lotId, Truncate(seen), false)],
            await ListAsync(member, siteId, cancellationToken));
    }

    [Fact]
    public async Task AHubThatNeverHeartbeatedHasNoLastSeenTimeAndIsOffline()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, _, member) = await SeedSiteAsync(cancellationToken);
        var deviceId = await SeedDeviceAsync(siteId, DeviceKind.Hub, cancellationToken);

        using var server = edge.CreateServerClient(member.AccessToken);
        using var response = await server.GetAsync(DevicesUri(siteId), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await EdgeApiFixture.ReadJsonAsync(response, cancellationToken);

        var device = Assert.Single(body.RootElement.GetProperty("devices").EnumerateArray());
        Assert.Equal(["id", "kind", "online"], device.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(deviceId, device.GetProperty("id").GetString());
        Assert.False(device.GetProperty("online").GetBoolean());
    }

    [Fact]
    public async Task ASiteWithoutEnrolmentsListsNoDevices()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, _, member) = await SeedSiteAsync(cancellationToken);

        // A roster entry without an enrolment is not a Device of the list: rows come from Device streams only.
        var last = await edge.AppendAsync(
            $"site/{siteId}",
            3,
            [new DeviceRegistered(NewDeviceId(), DeviceKind.Hub, $"{member.UserId}:{Guid.NewGuid()}", Now())],
            cancellationToken);
        await edge.WaitForProjectionCheckpointAsync(Projector, last);

        using var server = edge.CreateServerClient(member.AccessToken);
        using var response = await server.GetAsync(DevicesUri(siteId), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var body = await EdgeApiFixture.ReadJsonAsync(response, cancellationToken);

        Assert.Equal(["devices"], body.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(0, body.RootElement.GetProperty("devices").GetArrayLength());
    }

    [Fact]
    public async Task HubsAndNodesAreListedByDeviceIdAndANodeCarriesItsLot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, _, member) = await SeedSiteAsync(cancellationToken);
        var lotId = Guid.CreateVersion7().ToString();
        var hub = await SeedDeviceAsync(siteId, DeviceKind.Hub, cancellationToken);
        var node = await SeedDeviceAsync(siteId, DeviceKind.Node, cancellationToken, new DeviceAssigned(siteId, lotId, Now()));
        var unassigned = await SeedDeviceAsync(siteId, DeviceKind.Node, cancellationToken);

        var expected = new[]
        {
            new DeviceBody(hub, "hub", null, null, false),
            new DeviceBody(node, "node", lotId, null, false),
            new DeviceBody(unassigned, "node", null, null, false),
        };

        Assert.Equal(
            expected.OrderBy(device => device.Id, StringComparer.Ordinal),
            await ListAsync(member, siteId, cancellationToken));
    }

    [Fact]
    public async Task AnotherSitesDeviceIsAbsent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteA, _, memberA) = await SeedSiteAsync(cancellationToken);
        var (siteB, _, memberB) = await SeedSiteAsync(cancellationToken);
        var hubB = await SeedDeviceAsync(siteB, DeviceKind.Hub, cancellationToken);

        Assert.Empty(await ListAsync(memberA, siteA, cancellationToken));
        Assert.Equal([hubB], (await ListAsync(memberB, siteB, cancellationToken)).Select(device => device.Id));
    }

    [Fact]
    public async Task ACallerWithoutARoleIsForbiddenAndAnUnknownSiteIsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, _, _) = await SeedSiteAsync(cancellationToken);
        await SeedDeviceAsync(siteId, DeviceKind.Hub, cancellationToken);
        var stranger = await edge.CreateUserAsync("devices-stranger", cancellationToken);
        using var server = edge.CreateServerClient(stranger.AccessToken);

        using var forbidden = await server.GetAsync(DevicesUri(siteId), cancellationToken);
        await EdgeApiTests.AssertProblemAsync(forbidden, HttpStatusCode.Forbidden, "urn:coldframe:problem:forbidden", cancellationToken);

        foreach (var unknown in new[] { Guid.CreateVersion7().ToString(), "not-a-site-id" })
        {
            using var missing = await server.GetAsync(DevicesUri(unknown), cancellationToken);
            await EdgeApiTests.AssertProblemAsync(missing, HttpStatusCode.NotFound, "urn:coldframe:problem:site-not-found", cancellationToken);
        }
    }

    private static Uri DevicesUri(string siteId) => new($"/sites/{siteId}/devices", UriKind.Relative);

    private static DateTimeOffset Now() => TimeProvider.System.GetUtcNow();

    // The contract's lastSeenAt has milliseconds; PostgreSQL keeps microseconds.
    private static string Truncate(DateTimeOffset time) =>
        time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static string NewDeviceId() => Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8));

    private async Task<List<DeviceBody>> ListAsync(TestUser user, string siteId, CancellationToken cancellationToken)
    {
        using var server = edge.CreateServerClient(user.AccessToken);
        using var response = await server.GetAsync(DevicesUri(siteId), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await EdgeApiFixture.ReadJsonAsync(response, cancellationToken);

        return
        [
            .. body.RootElement.GetProperty("devices").EnumerateArray().Select(device => new DeviceBody(
                device.GetProperty("id").GetString()!,
                device.GetProperty("kind").GetString()!,
                device.TryGetProperty("lotId", out var lotId) ? lotId.GetString() : null,
                device.TryGetProperty("lastSeenAt", out var lastSeenAt) ? lastSeenAt.GetString() : null,
                device.GetProperty("online").GetBoolean())),
        ];
    }

    // An enrolled Device on a fresh device/{id} stream, with any further events, as the grain journals them.
    private async Task<string> SeedDeviceAsync(string siteId, DeviceKind kind, CancellationToken cancellationToken, params object[] more)
    {
        var deviceId = NewDeviceId();
        var wrapped = new WrappedDeviceKey("0000000000000000", new byte[12], new byte[48]);
        var last = await edge.AppendAsync(
            $"device/{deviceId}",
            [new DeviceEnrolled(siteId, kind, wrapped, Now().AddHours(-1)), .. more],
            cancellationToken);
        await edge.WaitForProjectionCheckpointAsync(Projector, last);
        return deviceId;
    }

    // A Site with an Administrator and a Member, seeded as events.
    private async Task<(string SiteId, TestUser Administrator, TestUser Member)> SeedSiteAsync(CancellationToken cancellationToken)
    {
        var administrator = await edge.CreateUserAsync("devices-admin", cancellationToken);
        var member = await edge.CreateUserAsync("devices-member", cancellationToken);
        var siteId = Guid.CreateVersion7().ToString();

        var last = await edge.AppendAsync(
            $"site/{siteId}",
            [
                new SiteCreated("Devices", administrator.UserId),
                new MembershipGranted(administrator.UserId, SiteRole.Owner),
                new MembershipGranted(member.UserId, SiteRole.Member),
            ],
            cancellationToken);
        await edge.WaitForIdentityCheckpointAsync(last);

        return (siteId, administrator, member);
    }
}

/// <summary>
/// A Device as the Devices list answers it; <see cref="LotId"/> and <see cref="LastSeenAt"/> are null when absent.
/// </summary>
internal sealed record DeviceBody(string Id, string Kind, string? LotId, string? LastSeenAt, bool Online);
