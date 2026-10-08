using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Lots;
using Coldframe.Contracts.Sensors;
using Coldframe.Contracts.Sites;
using Coldframe.Server.IntegrationTests.Devices;

namespace Coldframe.Server.IntegrationTests.Edge;

/// <summary>
/// What the apps need to draw a Threshold (Story 5.4): the Lot's <c>moisturePercent</c> and
/// <c>lowThresholdPercent</c> in the list and in the detail, and a soil-moisture History in percent once its
/// Readings were stored with a Calibration (UX-DR5, UX-DR32, UX-DR33).
/// </summary>
[Collection(IngestSuites.Name)]
public sealed class LotThresholdBandTests(EdgeApiFixture edge) : IClassFixture<EdgeApiFixture>
{
    private static readonly SensorSpecification Soil = new("soil_moisture", SensorUnit.RawCount, 0, 4095, Calibration: true, DefaultLow: 30);

    [Fact]
    public async Task ACalibratedLotCarriesItsPercentageAndItsLowThresholdInTheListAndTheDetail()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var site = await SeedSiteAsync(cancellationToken);
        var (lotId, node, soil) = await SeedCalibratedLotAsync(site, cancellationToken);
        var calibrationId = await CalibrateAsync(site, soil, cancellationToken);
        await StoreReadingAsync(node, soil, 3, 2100, DateTimeOffset.UtcNow.AddMinutes(-5), calibrationId, cancellationToken);

        var detail = await GetLotAsync(site, lotId, cancellationToken);
        var listed = await GetListedLotAsync(site, lotId, cancellationToken);

        foreach (var lot in new[] { detail, listed })
        {
            Assert.Equal(50d, lot.GetProperty("moisturePercent").GetDouble());
            Assert.Equal(30d, lot.GetProperty("lowThresholdPercent").GetDouble());
        }

        using var admin = edge.CreateServerClient(site.Administrator.AccessToken);
        using var put = await admin.PutAsJsonAsync(new Uri($"/sites/{site.Id}/sensors/{soil}/thresholds", UriKind.Relative), new { low = new { kind = "override", value = 25 } }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        Assert.Equal(25d, (await GetLotAsync(site, lotId, cancellationToken)).GetProperty("lowThresholdPercent").GetDouble());
    }

    [Fact]
    public async Task ALotWhoseNewestReadingHasNoCalibrationCarriesNeitherField()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var site = await SeedSiteAsync(cancellationToken);
        var (lotId, node, soil) = await SeedCalibratedLotAsync(site, cancellationToken);
        await CalibrateAsync(site, soil, cancellationToken);

        // Stored before the Calibration: raw, so its percentage appears with the next Reading, never before.
        await StoreReadingAsync(node, soil, 3, 2100, DateTimeOffset.UtcNow.AddMinutes(-5), null, cancellationToken);

        foreach (var lot in new[] { await GetLotAsync(site, lotId, cancellationToken), await GetListedLotAsync(site, lotId, cancellationToken) })
        {
            Assert.False(lot.TryGetProperty("moisturePercent", out _));
            Assert.False(lot.TryGetProperty("lowThresholdPercent", out _));
        }
    }

    [Fact]
    public async Task ACalibratedSensorWithNoLowThresholdSendsThePercentageOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var site = await SeedSiteAsync(cancellationToken);
        var (lotId, node, soil) = await SeedCalibratedLotAsync(site, cancellationToken);
        var calibrationId = await CalibrateAsync(site, soil, cancellationToken);
        await StoreReadingAsync(node, soil, 3, 2100, DateTimeOffset.UtcNow.AddMinutes(-5), calibrationId, cancellationToken);
        using var admin = edge.CreateServerClient(site.Administrator.AccessToken);
        using var put = await admin.PutAsJsonAsync(new Uri($"/sites/{site.Id}/sensors/{soil}/thresholds", UriKind.Relative), new { low = new { kind = "cleared" } }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var lot = await GetLotAsync(site, lotId, cancellationToken);

        Assert.Equal(50d, lot.GetProperty("moisturePercent").GetDouble());
        Assert.False(lot.TryGetProperty("lowThresholdPercent", out _));
    }

    [Fact]
    public async Task TheSoilMoistureHistoryOfACalibratedSensorIsInPercentPerDay()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var site = await SeedSiteAsync(cancellationToken);
        var (lotId, node, soil) = await SeedCalibratedLotAsync(site, cancellationToken);
        var calibrationId = await CalibrateAsync(site, soil, cancellationToken);
        var day = DateTimeOffset.UtcNow.Date.AddDays(-3);

        // Dry 3000 reads 0 percent, wet 1200 reads 100: 2100 is 50, 1650 is 75, 2700 is 15 (rounded to 5).
        await StoreReadingAsync(node, soil, 10, 2100, day.AddHours(6), calibrationId, cancellationToken);
        await StoreReadingAsync(node, soil, 11, 1650, day.AddHours(12), calibrationId, cancellationToken);
        await StoreReadingAsync(node, soil, 12, 2700, day.AddDays(1).AddHours(6), calibrationId, cancellationToken);

        var history = await GetHistoryAsync(site, lotId, "quantity=soil_moisture", cancellationToken);

        Assert.Equal("%", history.GetProperty("unit").GetString());
        var days = history.GetProperty("days").EnumerateArray().ToList();
        Assert.Equal(2, days.Count);
        Assert.Equal((50d, 75d, 2), (days[0].GetProperty("low").GetDouble(), days[0].GetProperty("high").GetDouble(), days[0].GetProperty("readingCount").GetInt32()));
        Assert.Equal((15d, 15d, 1), (days[1].GetProperty("low").GetDouble(), days[1].GetProperty("high").GetDouble(), days[1].GetProperty("readingCount").GetInt32()));
    }

    [Fact]
    public async Task AHistoryOfReadingsStoredBeforeTheCalibrationStaysRaw()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var site = await SeedSiteAsync(cancellationToken);
        var (lotId, node, soil) = await SeedCalibratedLotAsync(site, cancellationToken);
        await CalibrateAsync(site, soil, cancellationToken);
        await StoreReadingAsync(node, soil, 10, 2100, DateTimeOffset.UtcNow.Date.AddDays(-2).AddHours(6), null, cancellationToken);

        var history = await GetHistoryAsync(site, lotId, "quantity=soil_moisture", cancellationToken);

        Assert.Equal("raw", history.GetProperty("unit").GetString());
        Assert.Equal(2100d, history.GetProperty("days")[0].GetProperty("low").GetDouble());
    }

    [Fact]
    public async Task AMixedHistoryKeepsOnlyTheDaysWithCalibratedReadingsAndCountsOnlyThose()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var site = await SeedSiteAsync(cancellationToken);
        var (lotId, node, soil) = await SeedCalibratedLotAsync(site, cancellationToken);
        var calibrationId = await CalibrateAsync(site, soil, cancellationToken);
        var day = DateTimeOffset.UtcNow.Date.AddDays(-4);

        await StoreReadingAsync(node, soil, 10, 2100, day.AddHours(6), null, cancellationToken);
        await StoreReadingAsync(node, soil, 11, 2100, day.AddDays(1).AddHours(6), null, cancellationToken);
        await StoreReadingAsync(node, soil, 12, 1650, day.AddDays(1).AddHours(7), calibrationId, cancellationToken);

        var history = await GetHistoryAsync(site, lotId, "quantity=soil_moisture", cancellationToken);

        Assert.Equal("%", history.GetProperty("unit").GetString());
        var only = Assert.Single(history.GetProperty("days").EnumerateArray());
        Assert.Equal(day.AddDays(1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), only.GetProperty("day").GetString());
        Assert.Equal((75d, 75d, 1), (only.GetProperty("low").GetDouble(), only.GetProperty("high").GetDouble(), only.GetProperty("readingCount").GetInt32()));
    }

    [Fact]
    public async Task EveryPageOfAPagedHistoryHasTheUnitOfTheWholeWindow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var site = await SeedSiteAsync(cancellationToken);
        var (lotId, node, soil) = await SeedCalibratedLotAsync(site, cancellationToken);
        var calibrationId = await CalibrateAsync(site, soil, cancellationToken);
        var day = DateTimeOffset.UtcNow.Date.AddDays(-4);

        // The first day of the window is raw only; a later one is calibrated. Pages of one day each.
        await StoreReadingAsync(node, soil, 10, 2100, day.AddHours(6), null, cancellationToken);
        await StoreReadingAsync(node, soil, 11, 1650, day.AddDays(1).AddHours(6), calibrationId, cancellationToken);

        var first = await GetHistoryAsync(site, lotId, "quantity=soil_moisture&limit=1", cancellationToken);
        var cursor = first.GetProperty("nextCursor").GetString();
        var second = await GetHistoryAsync(site, lotId, $"quantity=soil_moisture&limit=1&cursor={Uri.EscapeDataString(cursor!)}", cancellationToken);

        Assert.Equal("%", first.GetProperty("unit").GetString());
        Assert.Equal("%", second.GetProperty("unit").GetString());
        Assert.Equal(75d, Assert.Single(second.GetProperty("days").EnumerateArray()).GetProperty("low").GetDouble());
    }

    private static string NewDeviceId() => Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8));

    private async Task<JsonElement> GetLotAsync(SeededSite site, string lotId, CancellationToken cancellationToken)
    {
        using var server = edge.CreateServerClient(site.Member.AccessToken);
        using var response = await server.GetAsync(new Uri($"/sites/{site.Id}/lots/{lotId}", UriKind.Relative), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await EdgeApiFixture.ReadJsonAsync(response, cancellationToken);

        return body.RootElement.Clone();
    }

    private async Task<JsonElement> GetListedLotAsync(SeededSite site, string lotId, CancellationToken cancellationToken)
    {
        using var server = edge.CreateServerClient(site.Member.AccessToken);
        using var response = await server.GetAsync(new Uri($"/sites/{site.Id}/lots", UriKind.Relative), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await EdgeApiFixture.ReadJsonAsync(response, cancellationToken);

        return body.RootElement.GetProperty("lots").EnumerateArray().Single(lot => lot.GetProperty("id").GetString() == lotId).Clone();
    }

    private async Task<JsonElement> GetHistoryAsync(SeededSite site, string lotId, string query, CancellationToken cancellationToken)
    {
        using var server = edge.CreateServerClient(site.Member.AccessToken);
        using var response = await server.GetAsync(new Uri($"/sites/{site.Id}/lots/{lotId}/history?{query}", UriKind.Relative), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await EdgeApiFixture.ReadJsonAsync(response, cancellationToken);

        return body.RootElement.Clone();
    }

    // A Lot holding a Node with a declared soil Sensor and two raw Readings (3000 dry, 1200 wet) to calibrate from.
    private async Task<(string LotId, string Node, Guid Soil)> SeedCalibratedLotAsync(SeededSite site, CancellationToken cancellationToken)
    {
        var node = NewDeviceId();
        var wrapped = new WrappedDeviceKey("0000000000000000", new byte[12], new byte[48]);
        await edge.AppendAsync($"device/{node}", [new DeviceEnrolled(site.Id, DeviceKind.Node, wrapped, TimeProvider.System.GetUtcNow())], cancellationToken);
        var lotId = await edge.SeedLotAsync(site.Id, "Tomatoes", cancellationToken, new LotClaimed(node));

        var at = TimeProvider.System.GetUtcNow();
        var soil = Guid.NewGuid();
        await edge.AppendAsync($"sensor/{soil}", [new SensorDeclared(node, 0, Soil, at)], cancellationToken);
        var version = (await edge.ReadStreamAsync($"device/{node}", cancellationToken)).Count;
        var last = await edge.AppendAsync($"device/{node}", version, [new DeviceSpecificationsDeclared([1, 2, 3], [new DeclaredSensor(0, "soil_moisture", soil)], at)], cancellationToken);
        await edge.WaitForProjectionCheckpointAsync("lots", last);

        // The claim is backdated so Readings of the last days count.
        await using (var command = edge.Database.CreateCommand("UPDATE lots SET claimed_at = @at WHERE lot_id = @lot_id"))
        {
            command.Parameters.AddWithValue("at", DateTimeOffset.UtcNow.AddDays(-60));
            command.Parameters.AddWithValue("lot_id", lotId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await StoreReadingAsync(node, soil, 1, 3000, DateTimeOffset.UtcNow.AddDays(-45), null, cancellationToken);
        await StoreReadingAsync(node, soil, 2, 1200, DateTimeOffset.UtcNow.AddDays(-45).AddMinutes(1), null, cancellationToken);

        return (lotId, node, soil);
    }

    private async Task<Guid> CalibrateAsync(SeededSite site, Guid soil, CancellationToken cancellationToken)
    {
        using var admin = edge.CreateServerClient(site.Administrator.AccessToken);
        using var response = await admin.PostAsJsonAsync(
            new Uri($"/sites/{site.Id}/sensors/{soil}/calibration", UriKind.Relative),
            new { dry = new { readingSeq = 1 }, wet = new { readingSeq = 2 } },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await EdgeApiFixture.ReadJsonAsync(response, cancellationToken);

        return body.RootElement.GetProperty("calibrationId").GetGuid();
    }

    private async Task StoreReadingAsync(string node, Guid sensorId, int seq, long raw, DateTimeOffset measuredAt, Guid? calibrationId, CancellationToken cancellationToken)
    {
        await using var command = edge.Database.CreateCommand(
            """
            INSERT INTO readings (device_id, sensor_id, reading_seq, measured_at, slot, quantity, raw_value, calibration_id, time_unsynced, received_at)
            VALUES (@device_id, @sensor_id, @seq, @measured_at, 0, 'soil_moisture', @raw, @calibration_id, false, @measured_at)
            """);
        command.Parameters.AddWithValue("device_id", node);
        command.Parameters.AddWithValue("sensor_id", sensorId);
        command.Parameters.AddWithValue("seq", (decimal)seq);
        command.Parameters.AddWithValue("measured_at", measuredAt.ToUniversalTime());
        command.Parameters.AddWithValue("raw", raw);
        command.Parameters.AddWithValue("calibration_id", NpgsqlTypes.NpgsqlDbType.Uuid, (object?)calibrationId ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SeededSite> SeedSiteAsync(CancellationToken cancellationToken)
    {
        var owner = await edge.CreateUserAsync("band-owner", cancellationToken);
        var administrator = await edge.CreateUserAsync("band-admin", cancellationToken);
        var member = await edge.CreateUserAsync("band-member", cancellationToken);
        var siteId = Guid.CreateVersion7().ToString();

        var last = await edge.AppendAsync(
            $"site/{siteId}",
            [
                new SiteCreated("Band", owner.UserId),
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
