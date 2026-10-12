using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Sites;
using Coldframe.DeviceSimulator;
using Coldframe.Server.IntegrationTests.Edge;
using ProtocolKind = Coldframe.Protocol.Setup.V1.DeviceKind;

namespace Coldframe.Server.IntegrationTests.Devices;

/// <summary>
/// <c>PUT /sites/{siteId}/devices/{deviceId}/silence-window</c> on the AppHost (Story 7.2): every endpoint row of
/// the story's matrix. An Administrator or Owner sets a Device's own Silence Window and returns it to the default;
/// a value out of range, a Member and an unknown Device change nothing, and the Device's journal shows it. The
/// authorization matrix covers every Role and Site.
/// </summary>
public sealed class SilenceWindowEndpointTests(EdgeApiFixture edge) : IClassFixture<EdgeApiFixture>
{
    private const string WindowSet = "device.silence-window-set";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAdministratorSetsANodesWindowAndTheSameValueAgainJournalsNothing()
    {
        var site = await SeedSiteAsync();
        var node = await EnrolAsync(site, ProtocolKind.Node);
        using var server = edge.CreateServerClient(site.Administrator.AccessToken);

        using var set = await server.PutAsJsonAsync(WindowUri(site.Id, node), new { windowSeconds = 28800 }, Ct);

        Assert.Equal((28800, true), await WindowAsync(set));
        var journaled = Assert.IsType<DeviceSilenceWindowSet>(Assert.Single(await edge.ReadStreamAsync($"device/{node}", Ct), row => row.Data is DeviceSilenceWindowSet).Data);
        Assert.Equal(28800, journaled.WindowSeconds);

        using var again = await server.PutAsJsonAsync(WindowUri(site.Id, node), new { windowSeconds = 28800 }, Ct);

        Assert.Equal((28800, true), await WindowAsync(again));
        Assert.Single(await edge.AliasesAsync($"device/{node}", Ct), alias => alias == WindowSet);

        // The Devices list shows the window in force: 8 h, the Node's own.
        Assert.Equal((28800, true), await ListedWindowAsync(site, node, expected: 28800));
    }

    [Fact]
    public async Task AnOwnerReturnsADeviceToTheDefaultOfItsKind()
    {
        var site = await SeedSiteAsync();
        var node = await EnrolAsync(site, ProtocolKind.Node);
        var hub = await EnrolAsync(site, ProtocolKind.Hub);
        using var server = edge.CreateServerClient(site.Owner.AccessToken);
        using var set = await server.PutAsJsonAsync(WindowUri(site.Id, node), new { windowSeconds = 3600 }, Ct);
        Assert.Equal((3600, true), await WindowAsync(set));

        // No windowSeconds: the default, 6 h for a Node and 5 min for a Hub.
        using var nodeDefault = await server.PutAsJsonAsync(WindowUri(site.Id, node), new { }, Ct);
        using var hubDefault = await server.PutAsJsonAsync(WindowUri(site.Id, hub), new { }, Ct);

        Assert.Equal((21600, false), await WindowAsync(nodeDefault));
        Assert.Equal((300, false), await WindowAsync(hubDefault));

        // The Node journaled the set and the clear; the Hub was on its default already and journaled nothing.
        Assert.Equal(2, (await edge.AliasesAsync($"device/{node}", Ct)).Count(alias => alias == WindowSet));
        Assert.DoesNotContain(WindowSet, await edge.AliasesAsync($"device/{hub}", Ct));
        Assert.Equal((21600, false), await ListedWindowAsync(site, node, expected: 21600));
        Assert.Equal((300, false), await ListedWindowAsync(site, hub, expected: 300));
    }

    [Fact]
    public async Task AHubTakesAWindowOfItsOwnAtBothEndsOfTheRange()
    {
        var site = await SeedSiteAsync();
        var hub = await EnrolAsync(site, ProtocolKind.Hub);
        using var server = edge.CreateServerClient(site.Administrator.AccessToken);

        using var shortest = await server.PutAsJsonAsync(WindowUri(site.Id, hub), new { windowSeconds = 60 }, Ct);
        using var longest = await server.PutAsJsonAsync(WindowUri(site.Id, hub), new { windowSeconds = 2592000 }, Ct);

        Assert.Equal((60, true), await WindowAsync(shortest));
        Assert.Equal((2592000, true), await WindowAsync(longest));
    }

    [Theory]
    [InlineData("""{"windowSeconds": 59}""")]
    [InlineData("""{"windowSeconds": 2592001}""")]
    [InlineData("""{"windowSeconds": 90.5}""")]
    [InlineData("""{"windowSeconds": "28800"}""")]
    [InlineData("""{"windowSeconds": -3600}""")]
    [InlineData("""[28800]""")]
    [InlineData("not json")]
    [InlineData("")]
    public async Task AValueOutOfRangeOrOfAnotherShapeIs400ValidationAndJournalsNothing(string body)
    {
        var site = await SeedSiteAsync();
        var node = await EnrolAsync(site, ProtocolKind.Node);
        var before = await edge.AliasesAsync($"device/{node}", Ct);
        using var server = edge.CreateServerClient(site.Administrator.AccessToken);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await server.PutAsync(WindowUri(site.Id, node), content, Ct);

        await EdgeApiTests.AssertProblemAsync(response, HttpStatusCode.BadRequest, "urn:coldframe:problem:validation", Ct);
        Assert.Equal(before, await edge.AliasesAsync($"device/{node}", Ct));
    }

    [Fact]
    public async Task AMemberGets403AndNothingIsJournaled()
    {
        var site = await SeedSiteAsync();
        var node = await EnrolAsync(site, ProtocolKind.Node);
        var before = await edge.AliasesAsync($"device/{node}", Ct);
        using var server = edge.CreateServerClient(site.Member.AccessToken);

        using var set = await server.PutAsJsonAsync(WindowUri(site.Id, node), new { windowSeconds = 28800 }, Ct);
        using var clear = await server.PutAsJsonAsync(WindowUri(site.Id, node), new { }, Ct);

        await EdgeApiTests.AssertProblemAsync(set, HttpStatusCode.Forbidden, "urn:coldframe:problem:forbidden", Ct);
        await EdgeApiTests.AssertProblemAsync(clear, HttpStatusCode.Forbidden, "urn:coldframe:problem:forbidden", Ct);
        Assert.Equal(before, await edge.AliasesAsync($"device/{node}", Ct));
    }

    [Fact]
    public async Task ADeviceOfAnotherSiteAndAnUnenrolledOneAre404DeviceNotFoundAndAMalformedIdIs400()
    {
        var site = await SeedSiteAsync();
        var other = await SeedSiteAsync();
        var theirs = await EnrolAsync(other, ProtocolKind.Node);
        var before = await edge.AliasesAsync($"device/{theirs}", Ct);
        var unenrolled = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8));
        using var server = edge.CreateServerClient(site.Administrator.AccessToken);

        foreach (var deviceId in new[] { theirs, unenrolled })
        {
            using var response = await server.PutAsJsonAsync(WindowUri(site.Id, deviceId), new { windowSeconds = 28800 }, Ct);

            await EdgeApiTests.AssertProblemAsync(response, HttpStatusCode.NotFound, "urn:coldframe:problem:device-not-found", Ct);
        }

        foreach (var deviceId in new[] { "not-a-device-id", "3F2A9C0D1E4B5A67" })
        {
            using var response = await server.PutAsJsonAsync(WindowUri(site.Id, deviceId), new { windowSeconds = 28800 }, Ct);

            await EdgeApiTests.AssertProblemAsync(response, HttpStatusCode.BadRequest, "urn:coldframe:problem:validation", Ct);
        }

        // The other Site's Node was not touched, and the unenrolled Device got no stream.
        Assert.Equal(before, await edge.AliasesAsync($"device/{theirs}", Ct));
        Assert.Empty(await edge.AliasesAsync($"device/{unenrolled}", Ct));
    }

    private static Uri WindowUri(string siteId, string deviceId) => new($"/sites/{siteId}/devices/{deviceId}/silence-window", UriKind.Relative);

    private static async Task<(int WindowSeconds, bool Overridden)> WindowAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await EdgeApiFixture.ReadJsonAsync(response, Ct);
        Assert.Equal(["overridden", "windowSeconds"], body.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));

        return (body.RootElement.GetProperty("windowSeconds").GetInt32(), body.RootElement.GetProperty("overridden").GetBoolean());
    }

    // The Device's window as the Devices list shows it, read until the projection shows the expected one: the
    // projector is not caught up inside the grain call.
    private async Task<(int WindowSeconds, bool Overridden)> ListedWindowAsync(SeededSite site, string deviceId, int expected)
    {
        using var server = edge.CreateServerClient(site.Member.AccessToken);
        var deadline = TimeProvider.System.GetUtcNow().AddSeconds(30);

        while (true)
        {
            using var response = await server.GetAsync(new Uri($"/sites/{site.Id}/devices", UriKind.Relative), Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = await EdgeApiFixture.ReadJsonAsync(response, Ct);
            var device = body.RootElement.GetProperty("devices").EnumerateArray().FirstOrDefault(item => item.GetProperty("id").GetString() == deviceId);

            // No row yet: the enrolment itself is still on its way to the projection.
            if (device.ValueKind == JsonValueKind.Object)
            {
                var listed = (device.GetProperty("silenceWindowSeconds").GetInt32(), device.GetProperty("silenceWindowOverridden").GetBoolean());

                if (listed.Item1 == expected || TimeProvider.System.GetUtcNow() >= deadline)
                {
                    Assert.False(device.TryGetProperty("silentSince", out _));
                    return listed;
                }
            }

            Assert.True(TimeProvider.System.GetUtcNow() < deadline, $"Device {deviceId} did not show in the Devices list.");

            await Task.Delay(TimeSpan.FromMilliseconds(100), Ct);
        }
    }

    private async Task<string> EnrolAsync(SeededSite site, ProtocolKind kind)
    {
        var device = SimulatedDevice.Create(kind);
        var sealedEnrolment = device.SealEnrolment(await edge.GetEnrolmentPublicKeyAsync(Ct), site.Id);
        using var server = edge.CreateServerClient(site.Administrator.AccessToken);
        using var enrolled = await EnrolmentTests.PostAsync(
            server,
            site.Id,
            Guid.NewGuid().ToString(),
            new EnrolBody(sealedEnrolment.DeviceId, sealedEnrolment.Kind, sealedEnrolment.Enc, sealedEnrolment.Ciphertext, sealedEnrolment.LotId),
            Ct);
        Assert.Equal(HttpStatusCode.Created, enrolled.StatusCode);

        return device.DeviceId.ToString();
    }

    private async Task<SeededSite> SeedSiteAsync()
    {
        var owner = await edge.CreateUserAsync("window-owner", Ct);
        var administrator = await edge.CreateUserAsync("window-admin", Ct);
        var member = await edge.CreateUserAsync("window-member", Ct);
        var siteId = Guid.CreateVersion7().ToString();

        var last = await edge.AppendAsync(
            $"site/{siteId}",
            [
                new SiteCreated("Window", owner.UserId),
                new MembershipGranted(owner.UserId, SiteRole.Owner),
                new MembershipGranted(administrator.UserId, SiteRole.Administrator),
                new MembershipGranted(member.UserId, SiteRole.Member),
            ],
            Ct);
        await edge.WaitForIdentityCheckpointAsync(last);

        return new SeededSite(siteId, owner, administrator, member);
    }

    private sealed record SeededSite(string Id, TestUser Owner, TestUser Administrator, TestUser Member);
}
