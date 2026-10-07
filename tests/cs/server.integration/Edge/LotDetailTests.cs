using System.Globalization;
using System.Net;
using System.Text.Json;
using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Lots;
using Coldframe.Contracts.Sites;
using Coldframe.Server.IntegrationTests.Devices;

namespace Coldframe.Server.IntegrationTests.Edge;

/// <summary>
/// <c>GET /sites/{siteId}/lots/{lotId}</c> with its Node and Sensors, and <c>GET /sites/{siteId}/lots/{lotId}/history</c>
/// on the AppHost (Story 4.8). Readings and device reports are seeded exactly as the Device grain stores them.
/// </summary>
[Collection(IngestSuites.Name)]
public sealed class LotDetailTests(EdgeApiFixture edge) : IClassFixture<EdgeApiFixture>
{
    [Fact]
    public async Task TheLotCarriesTheNewestReadingPerSensorInServerUnitsAndTheNodesReport()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, member) = await SeedSiteAsync(cancellationToken);
        var (lotId, node, claimedAt) = await SeedLotWithNodeAsync(siteId, cancellationToken);

        // A Reading from before the claim, then two wakes with all four quantities; the newest of each counts.
        await StoreReadingAsync(node, 0, "soil_moisture", 111, claimedAt.AddHours(-2), cancellationToken);
        await StoreWakeAsync(node, claimedAt.AddMinutes(1), 640, 20_000, 55_000, 150_000, cancellationToken);
        await StoreWakeAsync(node, claimedAt.AddMinutes(16), 612, 14_400, 78_250, 142_000, cancellationToken);
        await StoreReportAsync(node, claimedAt.AddMinutes(1), 70, "charging", cancellationToken);
        await StoreReportAsync(node, claimedAt.AddMinutes(16), 62, "not_charging", cancellationToken);

        using var lot = await GetLotAsync(member, siteId, lotId, cancellationToken);
        var root = lot.RootElement;

        Assert.True(root.TryGetProperty("node", out var nodeBlock));
        Assert.Equal(node, nodeBlock.GetProperty("deviceId").GetString());
        Assert.Equal(62, nodeBlock.GetProperty("batteryPercent").GetInt32());
        Assert.Equal("notCharging", nodeBlock.GetProperty("charging").GetString());
        Assert.Equal(Format(claimedAt.AddMinutes(16)), nodeBlock.GetProperty("lastSeenAt").GetString());

        var sensors = root.GetProperty("sensors").EnumerateArray().ToList();
        var at = Format(claimedAt.AddMinutes(16));

        Assert.Equal(4, sensors.Count);
        AssertSensor(sensors[0], "soil_moisture", 612, "raw", at);
        AssertSensor(sensors[1], "air_temperature", 14.4, "°C", at);
        AssertSensor(sensors[2], "relative_humidity", 78.25, "%", at);
        AssertSensor(sensors[3], "gas_resistance", 142, "kΩ", at);
    }

    [Fact]
    public async Task EachSensorCarriesItsSensorIdAndIsCalibratableOnlyWhenADeclaredSpecificationSaysSo()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, member) = await SeedSiteAsync(cancellationToken);
        var (lotId, node, claimedAt) = await SeedLotWithNodeAsync(siteId, cancellationToken);
        await StoreWakeAsync(node, claimedAt.AddMinutes(1), 640, 20_000, 55_000, 150_000, cancellationToken);

        using var lot = await GetLotAsync(member, siteId, lotId, cancellationToken);

        // The seeded Sensors were declared by no Node, so none has a Specification that calls for Calibration.
        foreach (var sensor in lot.RootElement.GetProperty("sensors").EnumerateArray())
        {
            Assert.True(Guid.TryParse(sensor.GetProperty("sensorId").GetString(), out _));
            Assert.False(sensor.GetProperty("calibratable").GetBoolean());
        }
    }

    [Fact]
    public async Task TheListNeverCarriesTheNodeOrSensors()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, member) = await SeedSiteAsync(cancellationToken);
        var (_, node, claimedAt) = await SeedLotWithNodeAsync(siteId, cancellationToken);
        await StoreWakeAsync(node, claimedAt.AddMinutes(1), 600, 1, 1, 1, cancellationToken);

        using var server = edge.CreateServerClient(member.AccessToken);
        using var response = await server.GetAsync(new Uri($"/sites/{siteId}/lots", UriKind.Relative), cancellationToken);
        using var body = await EdgeApiFixture.ReadJsonAsync(response, cancellationToken);

        var lot = Assert.Single(body.RootElement.GetProperty("lots").EnumerateArray());
        Assert.False(lot.TryGetProperty("node", out _));
        Assert.False(lot.TryGetProperty("sensors", out _));
    }

    [Fact]
    public async Task ALotWithoutANodeHasNeitherNodeNorSensors()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, member) = await SeedSiteAsync(cancellationToken);
        var lotId = await edge.SeedLotAsync(siteId, "Empty", cancellationToken);

        using var lot = await GetLotAsync(member, siteId, lotId, cancellationToken);

        Assert.False(lot.RootElement.TryGetProperty("node", out _));
        Assert.False(lot.RootElement.TryGetProperty("sensors", out _));
    }

    [Fact]
    public async Task ANodeWithoutReadingsOrReportsHasEmptySensorsAndOnlyItsId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, member) = await SeedSiteAsync(cancellationToken);
        var (lotId, node, _) = await SeedLotWithNodeAsync(siteId, cancellationToken);

        using var lot = await GetLotAsync(member, siteId, lotId, cancellationToken);

        var nodeBlock = lot.RootElement.GetProperty("node");
        Assert.Equal([node], nodeBlock.EnumerateObject().Select(property => property.Value.GetString()));
        Assert.Equal(0, lot.RootElement.GetProperty("sensors").GetArrayLength());
    }

    [Fact]
    public async Task AReportWithoutABatteryReadingOrChargerStateOmitsBoth()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, member) = await SeedSiteAsync(cancellationToken);
        var (lotId, node, claimedAt) = await SeedLotWithNodeAsync(siteId, cancellationToken);
        await StoreReportAsync(node, claimedAt.AddMinutes(1), null, "unknown", cancellationToken);

        using var lot = await GetLotAsync(member, siteId, lotId, cancellationToken);

        var nodeBlock = lot.RootElement.GetProperty("node");
        Assert.Equal(["deviceId", "lastSeenAt"], nodeBlock.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(Format(claimedAt.AddMinutes(1)), nodeBlock.GetProperty("lastSeenAt").GetString());
    }

    [Fact]
    public async Task TheHistoryDefaultsToTheLastThirtyUtcDaysAscendingWithMissingDaysAbsent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, member) = await SeedSiteAsync(cancellationToken);
        var (lotId, node, _) = await SeedLotWithNodeAsync(siteId, cancellationToken);
        var today = DateTimeOffset.UtcNow.Date;

        // Claimed long ago for the test: move the claim back so older Readings count.
        await BackdateClaimAsync(lotId, DateTimeOffset.UtcNow.AddDays(-60), cancellationToken);

        foreach (var offset in new[] { 40, 35, 29, 10, 10, 3, 0 })
        {
            await StoreReadingAsync(node, 0, "soil_moisture", 1000 + (offset * 10), new DateTimeOffset(today.AddDays(-offset), TimeSpan.Zero), cancellationToken);
        }

        await StoreReadingAsync(node, 0, "soil_moisture", 900, new DateTimeOffset(today.AddDays(-10), TimeSpan.Zero).AddHours(3), cancellationToken);

        var history = await GetHistoryAsync(member, siteId, lotId, "quantity=soil_moisture", cancellationToken);

        Assert.Equal("soil_moisture", history.GetProperty("quantity").GetString());
        Assert.Equal("raw", history.GetProperty("unit").GetString());
        Assert.False(history.TryGetProperty("nextCursor", out _));

        var days = history.GetProperty("days").EnumerateArray().ToList();
        Assert.Equal(
            new[] { today.AddDays(-29), today.AddDays(-10), today.AddDays(-3), today }.Select(Day),
            days.Select(day => day.GetProperty("day").GetString()));

        var tenDaysAgo = days[1];
        Assert.Equal(900, tenDaysAgo.GetProperty("low").GetDouble());
        Assert.Equal(1100, tenDaysAgo.GetProperty("high").GetDouble());
        Assert.Equal(3, tenDaysAgo.GetProperty("readingCount").GetInt32());
    }

    [Fact]
    public async Task TheHistoryConvertsUnitsAndTakesFromAndTo()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, member) = await SeedSiteAsync(cancellationToken);
        var (lotId, node, _) = await SeedLotWithNodeAsync(siteId, cancellationToken);
        await BackdateClaimAsync(lotId, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), cancellationToken);

        await StoreReadingAsync(node, 1, "air_temperature", 12_500, new DateTimeOffset(2026, 3, 1, 6, 0, 0, TimeSpan.Zero), cancellationToken);
        await StoreReadingAsync(node, 1, "air_temperature", 18_250, new DateTimeOffset(2026, 3, 1, 14, 0, 0, TimeSpan.Zero), cancellationToken);
        await StoreReadingAsync(node, 1, "air_temperature", 9_000, new DateTimeOffset(2026, 3, 2, 14, 0, 0, TimeSpan.Zero), cancellationToken);
        await StoreReadingAsync(node, 1, "air_temperature", 7_000, new DateTimeOffset(2026, 3, 5, 14, 0, 0, TimeSpan.Zero), cancellationToken);

        var history = await GetHistoryAsync(
            member,
            siteId,
            lotId,
            "quantity=air_temperature&from=2026-03-01T10:00:00Z&to=2026-03-03T00:00:00Z",
            cancellationToken);

        Assert.Equal("°C", history.GetProperty("unit").GetString());

        // The window is whole UTC days: from 2026-03-01 includes that day's 06:00 Reading.
        var days = history.GetProperty("days").EnumerateArray().ToList();
        Assert.Equal(["2026-03-01", "2026-03-02"], days.Select(day => day.GetProperty("day").GetString()));
        Assert.Equal(12.5, days[0].GetProperty("low").GetDouble());
        Assert.Equal(18.25, days[0].GetProperty("high").GetDouble());
        Assert.Equal(2, days[0].GetProperty("readingCount").GetInt32());
        Assert.Equal(9, days[1].GetProperty("low").GetDouble());
    }

    [Fact]
    public async Task ADateOnlyToIncludesThatWholeUtcDay()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, member) = await SeedSiteAsync(cancellationToken);
        var (lotId, node, _) = await SeedLotWithNodeAsync(siteId, cancellationToken);
        await BackdateClaimAsync(lotId, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), cancellationToken);

        await StoreReadingAsync(node, 0, "soil_moisture", 300, new DateTimeOffset(2026, 3, 2, 18, 0, 0, TimeSpan.Zero), cancellationToken);
        await StoreReadingAsync(node, 0, "soil_moisture", 400, new DateTimeOffset(2026, 3, 3, 0, 0, 1, TimeSpan.Zero), cancellationToken);

        var history = await GetHistoryAsync(member, siteId, lotId, "quantity=soil_moisture&from=2026-03-01&to=2026-03-02", cancellationToken);

        var day = Assert.Single(history.GetProperty("days").EnumerateArray());
        Assert.Equal("2026-03-02", day.GetProperty("day").GetString());
    }

    [Fact]
    public async Task PagesJoinedByTheCursorReproduceTheFullListAndTheLastPageHasNone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, member) = await SeedSiteAsync(cancellationToken);
        var (lotId, node, _) = await SeedLotWithNodeAsync(siteId, cancellationToken);
        await BackdateClaimAsync(lotId, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), cancellationToken);

        for (var day = 1; day <= 25; day++)
        {
            await StoreReadingAsync(node, 0, "soil_moisture", 100 + day, new DateTimeOffset(2026, 2, day, 12, 0, 0, TimeSpan.Zero), cancellationToken);
        }

        const string window = "quantity=soil_moisture&from=2026-02-01T00:00:00Z&to=2026-02-28T00:00:00Z";
        var full = (await GetHistoryAsync(member, siteId, lotId, window, cancellationToken)).GetProperty("days").EnumerateArray().Select(day => day.GetProperty("day").GetString()).ToList();
        Assert.Equal(25, full.Count);

        var joined = new List<string?>();
        var pages = 0;
        string? cursor = null;

        do
        {
            var query = window + "&limit=10" + (cursor is null ? string.Empty : "&cursor=" + Uri.EscapeDataString(cursor));
            var page = await GetHistoryAsync(member, siteId, lotId, query, cancellationToken);
            joined.AddRange(page.GetProperty("days").EnumerateArray().Select(day => day.GetProperty("day").GetString()));
            cursor = page.TryGetProperty("nextCursor", out var next) ? next.GetString() : null;
            pages++;
        }
        while (cursor is not null);

        Assert.Equal(3, pages);
        Assert.Equal(full, joined);
    }

    [Fact]
    public async Task HistoryFollowsTheNodeAfterAReassignment()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, member) = await SeedSiteAsync(cancellationToken);
        var (oldLot, node, _) = await SeedLotWithNodeAsync(siteId, cancellationToken);
        var recent = DateTimeOffset.UtcNow.AddMinutes(-30);
        await StoreReadingAsync(node, 0, "soil_moisture", 500, recent, cancellationToken);

        // The Node moves: the new Lot claims it, then the old Lot lets it go.
        var newLot = await edge.SeedLotAsync(siteId, "New", cancellationToken, new LotClaimed(node));
        var last = await edge.AppendAsync($"lot/{oldLot}", (await edge.ReadStreamAsync($"lot/{oldLot}", cancellationToken)).Count, [new LotReleased(node)], cancellationToken);
        await edge.WaitForProjectionCheckpointAsync("lots", last);

        // A Reading after the move; the one before it belongs to the Node's past.
        await StoreReadingAsync(node, 0, "soil_moisture", 700, DateTimeOffset.UtcNow, cancellationToken);

        var oldHistory = await GetHistoryAsync(member, siteId, oldLot, "quantity=soil_moisture", cancellationToken);
        var newHistory = await GetHistoryAsync(member, siteId, newLot, "quantity=soil_moisture", cancellationToken);

        Assert.Equal(0, oldHistory.GetProperty("days").GetArrayLength());
        var day = Assert.Single(newHistory.GetProperty("days").EnumerateArray());
        Assert.Equal(700, day.GetProperty("low").GetDouble());
        Assert.Equal(1, day.GetProperty("readingCount").GetInt32());
    }

    [Theory]
    [InlineData("")]
    [InlineData("quantity=temperature")]
    [InlineData("quantity=soil_moisture&from=yesterday")]
    [InlineData("quantity=soil_moisture&to=2026-13-40")]
    [InlineData("quantity=soil_moisture&from=2026-03-02T00:00:00Z&to=2026-03-01T00:00:00Z")]
    [InlineData("quantity=soil_moisture&cursor=not-a-cursor")]
    [InlineData("quantity=soil_moisture&limit=0")]
    [InlineData("quantity=soil_moisture&limit=367")]
    [InlineData("quantity=soil_moisture&limit=ten")]
    [InlineData("quantity=soil_moisture&quantity=air_temperature")]
    public async Task ABadHistoryQueryIsAValidationProblem(string query)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, member) = await SeedSiteAsync(cancellationToken);
        var (lotId, _, _) = await SeedLotWithNodeAsync(siteId, cancellationToken);

        using var server = edge.CreateServerClient(member.AccessToken);
        using var response = await server.GetAsync(new Uri($"/sites/{siteId}/lots/{lotId}/history?{query}", UriKind.Relative), cancellationToken);

        await EdgeApiTests.AssertProblemAsync(response, HttpStatusCode.BadRequest, "urn:coldframe:problem:validation", cancellationToken);
    }

    [Fact]
    public async Task AnUnknownLotIsNotFoundAndAStrangerIsForbidden()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, member) = await SeedSiteAsync(cancellationToken);
        var stranger = await edge.CreateUserAsync("history-stranger", cancellationToken);

        using var asMember = edge.CreateServerClient(member.AccessToken);

        foreach (var unknown in new[] { Guid.CreateVersion7().ToString(), "not-a-lot" })
        {
            using var missing = await asMember.GetAsync(new Uri($"/sites/{siteId}/lots/{unknown}/history?quantity=soil_moisture", UriKind.Relative), cancellationToken);
            await EdgeApiTests.AssertProblemAsync(missing, HttpStatusCode.NotFound, "urn:coldframe:problem:lot-not-found", cancellationToken);
        }

        var lotId = await edge.SeedLotAsync(siteId, "Visible", cancellationToken);
        using var asStranger = edge.CreateServerClient(stranger.AccessToken);
        using var forbidden = await asStranger.GetAsync(new Uri($"/sites/{siteId}/lots/{lotId}/history?quantity=soil_moisture", UriKind.Relative), cancellationToken);
        await EdgeApiTests.AssertProblemAsync(forbidden, HttpStatusCode.Forbidden, "urn:coldframe:problem:forbidden", cancellationToken);
    }

    private static void AssertSensor(JsonElement sensor, string quantity, double value, string unit, string measuredAt)
    {
        Assert.Equal(quantity, sensor.GetProperty("quantity").GetString());
        Assert.Equal(value, sensor.GetProperty("value").GetDouble());
        Assert.Equal(unit, sensor.GetProperty("unit").GetString());
        Assert.Equal(measuredAt, sensor.GetProperty("measuredAt").GetString());
    }

    private static string Format(DateTimeOffset time) =>
        time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static string Day(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string NewDeviceId() => Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8));

    private async Task<JsonDocument> GetLotAsync(TestUser user, string siteId, string lotId, CancellationToken cancellationToken)
    {
        using var server = edge.CreateServerClient(user.AccessToken);
        using var response = await server.GetAsync(new Uri($"/sites/{siteId}/lots/{lotId}", UriKind.Relative), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await EdgeApiFixture.ReadJsonAsync(response, cancellationToken);
    }

    private async Task<JsonElement> GetHistoryAsync(TestUser user, string siteId, string lotId, string query, CancellationToken cancellationToken)
    {
        using var server = edge.CreateServerClient(user.AccessToken);
        using var response = await server.GetAsync(new Uri($"/sites/{siteId}/lots/{lotId}/history?{query}", UriKind.Relative), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await EdgeApiFixture.ReadJsonAsync(response, cancellationToken);
        return body.RootElement.Clone();
    }

    // A Lot holding a freshly enrolled Node; the claim time is the journal time of the claim.
    private async Task<(string LotId, string Node, DateTimeOffset ClaimedAt)> SeedLotWithNodeAsync(string siteId, CancellationToken cancellationToken)
    {
        var node = NewDeviceId();
        var wrapped = new WrappedDeviceKey("0000000000000000", new byte[12], new byte[48]);
        await edge.AppendAsync($"device/{node}", [new DeviceEnrolled(siteId, DeviceKind.Node, wrapped, DateTimeOffset.UtcNow)], cancellationToken);
        var lotId = await edge.SeedLotAsync(siteId, "Tomatoes", cancellationToken, new LotClaimed(node));
        var claimedAt = (await edge.ReadStreamAsync($"lot/{lotId}", cancellationToken))[1].RecordedAt;

        return (lotId, node, claimedAt);
    }

    private async Task BackdateClaimAsync(string lotId, DateTimeOffset claimedAt, CancellationToken cancellationToken)
    {
        await using var command = edge.Database.CreateCommand("UPDATE lots SET claimed_at = @at WHERE lot_id = @lot_id");
        command.Parameters.AddWithValue("at", claimedAt.ToUniversalTime());
        command.Parameters.AddWithValue("lot_id", lotId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task StoreWakeAsync(string node, DateTimeOffset at, long soil, long temperature, long humidity, long gas, CancellationToken cancellationToken)
    {
        await StoreReadingAsync(node, 0, "soil_moisture", soil, at, cancellationToken);
        await StoreReadingAsync(node, 1, "air_temperature", temperature, at, cancellationToken);
        await StoreReadingAsync(node, 2, "relative_humidity", humidity, at, cancellationToken);
        await StoreReadingAsync(node, 3, "gas_resistance", gas, at, cancellationToken);
    }

    private async Task StoreReadingAsync(string node, int slot, string quantity, long value, DateTimeOffset measuredAt, CancellationToken cancellationToken)
    {
        await using var command = edge.Database.CreateCommand(
            """
            INSERT INTO readings (device_id, sensor_id, reading_seq, measured_at, slot, quantity, raw_value, time_unsynced, received_at)
            VALUES (@device_id, @sensor_id, @seq, @measured_at, @slot, @quantity, @value, false, @measured_at)
            """);
        command.Parameters.AddWithValue("device_id", node);
        command.Parameters.AddWithValue("sensor_id", Guid.NewGuid());
        command.Parameters.AddWithValue("seq", (decimal)Random.Shared.NextInt64(1, long.MaxValue));
        command.Parameters.AddWithValue("measured_at", measuredAt.ToUniversalTime());
        command.Parameters.AddWithValue("slot", slot);
        command.Parameters.AddWithValue("quantity", quantity);
        command.Parameters.AddWithValue("value", value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task StoreReportAsync(string node, DateTimeOffset measuredAt, short? battery, string charging, CancellationToken cancellationToken)
    {
        await using var command = edge.Database.CreateCommand(
            """
            INSERT INTO device_reports (device_id, reading_seq, measured_at, battery_percent, charging, time_unsynced, received_at)
            VALUES (@device_id, @seq, @measured_at, @battery, @charging, false, @measured_at)
            """);
        command.Parameters.AddWithValue("device_id", node);
        command.Parameters.AddWithValue("seq", (decimal)Random.Shared.NextInt64(1, long.MaxValue));
        command.Parameters.AddWithValue("measured_at", measuredAt.ToUniversalTime());
        command.Parameters.Add(new Npgsql.NpgsqlParameter("battery", NpgsqlTypes.NpgsqlDbType.Smallint) { Value = (object?)battery ?? DBNull.Value });
        command.Parameters.AddWithValue("charging", charging);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // A Site with a Member, seeded as events.
    private async Task<(string SiteId, TestUser Member)> SeedSiteAsync(CancellationToken cancellationToken)
    {
        var owner = await edge.CreateUserAsync("detail-owner", cancellationToken);
        var member = await edge.CreateUserAsync("detail-member", cancellationToken);
        var siteId = Guid.CreateVersion7().ToString();

        var last = await edge.AppendAsync(
            $"site/{siteId}",
            [
                new SiteCreated("Detail", owner.UserId),
                new MembershipGranted(owner.UserId, SiteRole.Owner),
                new MembershipGranted(member.UserId, SiteRole.Member),
            ],
            cancellationToken);
        await edge.WaitForIdentityCheckpointAsync(last);

        return (siteId, member);
    }
}
