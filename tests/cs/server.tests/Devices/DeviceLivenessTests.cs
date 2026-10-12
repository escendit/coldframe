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

    [Theory]
    [InlineData("device/92064422c012f481", "92064422c012f481")]
    [InlineData("site/0192f3a4-7c1e-7d2b-9a51-3f7e2c9b1d00", null)]
    [InlineData("lot/0192f3a4-8a00-7c3d-8e4f-5a6b7c8d9e02", null)]
    public void OnlyDeviceStreamsAreProjected(string streamId, string? deviceId)
    {
        Assert.Equal(deviceId, DevicesProjector.DeviceIdOf(streamId));
    }

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
