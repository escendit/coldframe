using System.Text.Json;
using Coldframe.Server.Devices;
using Coldframe.Server.Edge;
using Coldframe.Server.Tests.Edge;
using Microsoft.AspNetCore.Http;

namespace Coldframe.Server.Tests.Devices;

/// <summary>
/// The online rule of the Devices list (Story 3.7): a Device is online only while its last heartbeat is at
/// most <see cref="DeviceLiveness.HubOnlineWindow"/> old on the Server clock, and how a list item carries it.
/// </summary>
public sealed class DeviceLivenessTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 7, 4, 0, TimeSpan.Zero);

    [Fact]
    public void TheOnlineWindowIsTwoMissedHeartbeats()
    {
        Assert.Equal(TimeSpan.FromSeconds(120), DeviceLiveness.HubOnlineWindow);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(30, true)]
    [InlineData(119, true)]
    [InlineData(120, true)]
    [InlineData(121, false)]
    [InlineData(600, false)]
    public void ADeviceIsOnlineUpToAndIncludingTheWindow(int secondsAgo, bool online)
    {
        Assert.Equal(online, DeviceLiveness.IsOnline(Now.AddSeconds(-secondsAgo), Now));
    }

    [Fact]
    public void OneMillisecondPastTheWindowIsOffline()
    {
        Assert.False(DeviceLiveness.IsOnline(Now - DeviceLiveness.HubOnlineWindow - TimeSpan.FromMilliseconds(1), Now));
    }

    [Fact]
    public void ADeviceNeverSeenIsOffline()
    {
        Assert.False(DeviceLiveness.IsOnline(null, Now));
    }

    [Fact]
    public async Task AnOnlineHubIsListedWithItsLastSeenTimeInUtc()
    {
        var seen = new DateTimeOffset(2026, 10, 6, 9, 3, 30, 120, TimeSpan.FromHours(2));

        var item = EdgeApi.ToDeviceListItem(new DeviceView("92064422c012f481", "hub", null, seen), Now);
        using var body = await SerializeAsync(new DeviceListResponse([item]));

        var device = Assert.Single(body.RootElement.GetProperty("devices").EnumerateArray());
        Assert.Equal(
            ["id", "kind", "lastSeenAt", "online", "silenceWindowOverridden", "silenceWindowSeconds"],
            device.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal("92064422c012f481", device.GetProperty("id").GetString());
        Assert.Equal("hub", device.GetProperty("kind").GetString());
        Assert.Equal("2026-10-06T07:03:30.120Z", device.GetProperty("lastSeenAt").GetString());
        Assert.True(device.GetProperty("online").GetBoolean());
    }

    [Fact]
    public async Task ADeviceNeverSeenOmitsLastSeenAtAndIsOffline()
    {
        var item = EdgeApi.ToDeviceListItem(new DeviceView("92064422c012f481", "hub", null, null), Now);
        using var body = await SerializeAsync(new DeviceListResponse([item]));

        var device = Assert.Single(body.RootElement.GetProperty("devices").EnumerateArray());
        Assert.Equal(
            ["id", "kind", "online", "silenceWindowOverridden", "silenceWindowSeconds"],
            device.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.False(device.GetProperty("online").GetBoolean());
    }

    // Story 7.2: the Silence Window in force is the Device's own, else the default of its kind, and silentSince
    // is sent exactly while the Device is silent.
    [Theory]
    [InlineData("hub", null, 300, false)]
    [InlineData("node", null, 21600, false)]
    [InlineData("node", 28800L, 28800, true)]
    [InlineData("hub", 60L, 60, true)]
    public async Task ADeviceCarriesTheSilenceWindowInForceAndWhetherItIsItsOwn(string kind, long? overridden, long seconds, bool isOverridden)
    {
        var item = EdgeApi.ToDeviceListItem(new DeviceView("92064422c012f481", kind, null, null, SilenceWindowOverrideSeconds: overridden), Now);
        using var body = await SerializeAsync(new DeviceListResponse([item]));

        var device = Assert.Single(body.RootElement.GetProperty("devices").EnumerateArray());
        Assert.Equal(seconds, device.GetProperty("silenceWindowSeconds").GetInt64());
        Assert.Equal(isOverridden, device.GetProperty("silenceWindowOverridden").GetBoolean());
        Assert.False(device.TryGetProperty("silentSince", out _));
    }

    [Fact]
    public async Task ASilentDeviceCarriesWhenItWasLastHeardInUtc()
    {
        var lastHeard = new DateTimeOffset(2026, 10, 6, 3, 5, 0, TimeSpan.FromHours(2));

        var item = EdgeApi.ToDeviceListItem(new DeviceView("92064422c012f481", "node", null, null, SilentSince: lastHeard), Now);
        using var body = await SerializeAsync(new DeviceListResponse([item]));

        var device = Assert.Single(body.RootElement.GetProperty("devices").EnumerateArray());
        Assert.Equal("2026-10-06T01:05:00.000Z", device.GetProperty("silentSince").GetString());
    }

    [Fact]
    public async Task AnAssignedNodeCarriesItsLot()
    {
        const string lotId = "0192f3a4-8a00-7c3d-8e4f-5a6b7c8d9e02";

        var item = EdgeApi.ToDeviceListItem(new DeviceView("92064422c012f481", "node", lotId, null), Now);
        using var body = await SerializeAsync(new DeviceListResponse([item]));

        var device = Assert.Single(body.RootElement.GetProperty("devices").EnumerateArray());
        Assert.Equal("node", device.GetProperty("kind").GetString());
        Assert.Equal(lotId, device.GetProperty("lotId").GetString());
    }

    // Story 8.2 (DW-112): a paused Device names its Pause sources, device before site, and the later end; one
    // that is not paused carries neither member.
    [Fact]
    public async Task APausedDeviceCarriesItsSourcesAndTheLaterEndAndAnUnpausedOneNeither()
    {
        var deviceEnd = new DateTimeOffset(2027, 3, 1, 0, 0, 0, TimeSpan.FromHours(1));
        var siteEnd = new DateTimeOffset(2027, 4, 1, 0, 0, 0, TimeSpan.FromHours(2));

        using var body = await SerializeAsync(new DeviceListResponse(
        [
            EdgeApi.ToDeviceListItem(new DeviceView("92064422c012f481", "hub", null, null, DevicePaused: true), Now),
            EdgeApi.ToDeviceListItem(new DeviceView("92064422c012f482", "node", null, null, SitePaused: true, SitePausedUntil: siteEnd), Now),
            EdgeApi.ToDeviceListItem(
                new DeviceView("92064422c012f483", "node", null, null, DevicePaused: true, DevicePausedUntil: deviceEnd, SitePaused: true, SitePausedUntil: siteEnd),
                Now),
            EdgeApi.ToDeviceListItem(
                new DeviceView("92064422c012f484", "node", null, null, DevicePaused: true, DevicePausedUntil: deviceEnd, SitePaused: true),
                Now),
            EdgeApi.ToDeviceListItem(new DeviceView("92064422c012f485", "node", null, null), Now),
        ]));

        var devices = body.RootElement.GetProperty("devices").EnumerateArray().ToList();

        Assert.Equal(["device"], Sources(devices[0]));
        Assert.False(devices[0].TryGetProperty("pausedUntil", out _));
        Assert.Equal(["site"], Sources(devices[1]));
        Assert.Equal("2027-03-31T22:00:00.000Z", devices[1].GetProperty("pausedUntil").GetString());
        Assert.Equal(["device", "site"], Sources(devices[2]));
        Assert.Equal("2027-03-31T22:00:00.000Z", devices[2].GetProperty("pausedUntil").GetString());

        // One source without an end: the Pause has no end.
        Assert.Equal(["device", "site"], Sources(devices[3]));
        Assert.False(devices[3].TryGetProperty("pausedUntil", out _));
        Assert.False(devices[4].TryGetProperty("pausedBy", out _));
        Assert.False(devices[4].TryGetProperty("pausedUntil", out _));
    }

    // An end kept for a source that is not in force is never shown.
    [Fact]
    public async Task AnEndOfASourceThatIsNotInForceIsNotSent()
    {
        var item = EdgeApi.ToDeviceListItem(
            new DeviceView("92064422c012f481", "node", null, null, DevicePausedUntil: Now.AddDays(3), SitePausedUntil: Now.AddDays(4)),
            Now);
        using var body = await SerializeAsync(new DeviceListResponse([item]));

        var device = Assert.Single(body.RootElement.GetProperty("devices").EnumerateArray());
        Assert.False(device.TryGetProperty("pausedBy", out _));
        Assert.False(device.TryGetProperty("pausedUntil", out _));
    }

    [Theory]
    [InlineData("device/92064422c012f481", "92064422c012f481")]
    [InlineData("site/0192f3a4-7c1e-7d2b-9a51-3f7e2c9b1d00", null)]
    [InlineData("lot/0192f3a4-8a00-7c3d-8e4f-5a6b7c8d9e02", null)]
    public void OnlyDeviceStreamsAreProjected(string streamId, string? deviceId)
    {
        Assert.Equal(deviceId, DevicesProjector.DeviceIdOf(streamId));
    }

    private static List<string?> Sources(JsonElement device) =>
        [.. device.GetProperty("pausedBy").EnumerateArray().Select(source => source.GetString())];

    // Through the Server's own JSON options, exactly as the endpoint writes it.
    private static async Task<JsonDocument> SerializeAsync(DeviceListResponse response)
    {
        await using var app = EdgeTestHost.Build();
        using var stream = new MemoryStream();
        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Response.Body = stream;

        await TypedResults.Ok(response).ExecuteAsync(context);

        stream.Position = 0;
        return await JsonDocument.ParseAsync(stream, cancellationToken: TestContext.Current.CancellationToken);
    }
}
