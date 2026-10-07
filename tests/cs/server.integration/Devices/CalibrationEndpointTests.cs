using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Lots;
using Coldframe.Contracts.Sensors;
using Coldframe.Contracts.Sites;
using Coldframe.Server.IntegrationTests.Edge;

namespace Coldframe.Server.IntegrationTests.Devices;

/// <summary>
/// <c>POST /sites/{siteId}/sensors/{sensorId}/calibration</c> on the AppHost (Story 5.1): an Administrator saves
/// the points of a Calibration, a Member gets 403 and changes nothing, an indistinct pair or an unknown Reading
/// is a 400 Problem Details, and a Sensor that is not a Node's of the Site is a 404. The authorization matrix
/// covers every Role and Site.
/// </summary>
[Collection(IngestSuites.Name)]
public sealed class CalibrationEndpointTests(EdgeApiFixture edge) : IClassFixture<EdgeApiFixture>
{
    private static readonly SensorSpecification Soil = new("soil_moisture", SensorUnit.RawCount, 0, 4095, Calibration: true, DefaultLow: 30);

    private static readonly SensorSpecification Air = new("air_temperature", SensorUnit.MilliDegreeCelsius, -40_000, 85_000, Calibration: false);

    [Fact]
    public async Task AnAdministratorCalibratesTheSensorAndTheLotLeavesNeedsCalibrationWithItsPercentageOnTheNextReading()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var site = await SeedSiteAsync(cancellationToken);
        var node = await SeedNodeAsync(site.Id, cancellationToken);
        var lotId = await edge.SeedLotAsync(site.Id, "Tomatoes", cancellationToken, new LotClaimed(node));
        var soil = await DeclareAsync(node, site.Id, cancellationToken, Soil);
        await StoreReadingAsync(node, soil[0], 1, 3000, null, cancellationToken);
        await StoreReadingAsync(node, soil[0], 2, 1200, null, cancellationToken);
        Assert.Equal("needsCalibration", await LotStatusAsync(site.Member, site.Id, lotId, cancellationToken));
        using var server = edge.CreateServerClient(site.Administrator.AccessToken);

        using var response = await server.PostAsJsonAsync(Uri(site.Id, soil[0]), new { dry = new { readingSeq = 1 }, wet = new { readingSeq = 2 } }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await EdgeApiFixture.ReadJsonAsync(response, cancellationToken);
        var calibrationId = body.RootElement.GetProperty("calibrationId").GetGuid();
        Assert.True(body.RootElement.GetProperty("calibrated").GetBoolean());
        Assert.Equal(3000, body.RootElement.GetProperty("dry").GetProperty("rawValue").GetInt64());
        Assert.Equal(1200, body.RootElement.GetProperty("wet").GetProperty("rawValue").GetInt64());
        Assert.False(body.RootElement.TryGetProperty("pendingDry", out _));

        // The Sensor journaled it and the Device grain holds it, before the answer.
        Assert.Equal(["sensor.declared", "sensor.calibrated", "sensor.calibration-delivered"], await edge.AliasesAsync($"sensor/{soil[0]}", cancellationToken));
        Assert.Equal("device.calibration-set", (await edge.AliasesAsync($"device/{node}", cancellationToken))[^1]);

        // The Lot left needs calibration when the Calibration was saved, and shows no percentage yet.
        Assert.Equal("ok", await LotStatusAsync(site.Member, site.Id, lotId, cancellationToken));
        var before = await SoilReadingAsync(site.Member, site.Id, lotId, cancellationToken);
        Assert.Equal(("raw", 1200d), (before.GetProperty("unit").GetString(), before.GetProperty("value").GetDouble()));

        // The next Reading is stored with the Calibration's ID: 2100 of 3000 (dry) to 1200 (wet) is 50 percent.
        await StoreReadingAsync(node, soil[0], 3, 2100, calibrationId, cancellationToken);
        var after = await SoilReadingAsync(site.Member, site.Id, lotId, cancellationToken);
        Assert.Equal(("%", 50d), (after.GetProperty("unit").GetString(), after.GetProperty("value").GetDouble()));
    }

    [Fact]
    public async Task OnePointKeepsTheSensorUncalibratedAndTheOtherCompletesIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var site = await SeedSiteAsync(cancellationToken);
        var node = await SeedNodeAsync(site.Id, cancellationToken);
        var lotId = await edge.SeedLotAsync(site.Id, "Peppers", cancellationToken, new LotClaimed(node));
        var soil = await DeclareAsync(node, site.Id, cancellationToken, Soil);
        await StoreReadingAsync(node, soil[0], 1, 3000, null, cancellationToken);
        await StoreReadingAsync(node, soil[0], 2, 1200, null, cancellationToken);
        using var server = edge.CreateServerClient(site.Administrator.AccessToken);

        using var dry = await server.PostAsJsonAsync(Uri(site.Id, soil[0]), new { dry = new { readingSeq = 1 } }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, dry.StatusCode);
        using var kept = await EdgeApiFixture.ReadJsonAsync(dry, cancellationToken);
        Assert.False(kept.RootElement.GetProperty("calibrated").GetBoolean());
        Assert.False(kept.RootElement.TryGetProperty("calibrationId", out _));
        Assert.Equal(3000, kept.RootElement.GetProperty("pendingDry").GetProperty("rawValue").GetInt64());
        Assert.Equal("needsCalibration", await LotStatusAsync(site.Member, site.Id, lotId, cancellationToken));

        using var wet = await server.PostAsJsonAsync(Uri(site.Id, soil[0]), new { wet = new { readingSeq = 2 } }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, wet.StatusCode);
        using var done = await EdgeApiFixture.ReadJsonAsync(wet, cancellationToken);
        Assert.True(done.RootElement.GetProperty("calibrated").GetBoolean());
        Assert.Equal("ok", await LotStatusAsync(site.Member, site.Id, lotId, cancellationToken));
    }

    [Fact]
    public async Task IndistinctPointsAnUnknownReadingAndMalformedBodiesAre400ProblemDetailsAndNothingIsJournaled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var site = await SeedSiteAsync(cancellationToken);
        var node = await SeedNodeAsync(site.Id, cancellationToken);
        var sensors = await DeclareAsync(node, site.Id, cancellationToken, Soil, Air);
        await StoreReadingAsync(node, sensors[0], 1, 1500, null, cancellationToken);
        await StoreReadingAsync(node, sensors[0], 2, 1510, null, cancellationToken);
        await StoreReadingAsync(node, sensors[1], 3, 21_500, null, cancellationToken, slot: 1, quantity: "air_temperature");
        using var server = edge.CreateServerClient(site.Administrator.AccessToken);

        // Equal raw values, and a span below the minimum.
        using var equal = await server.PostAsJsonAsync(Uri(site.Id, sensors[0]), new { dry = new { readingSeq = 1 }, wet = new { readingSeq = 1 } }, cancellationToken);
        await EdgeApiTests.AssertProblemAsync(equal, HttpStatusCode.BadRequest, "urn:coldframe:problem:validation", cancellationToken);
        using var close = await server.PostAsJsonAsync(Uri(site.Id, sensors[0]), new { dry = new { readingSeq = 1 }, wet = new { readingSeq = 2 } }, cancellationToken);
        await EdgeApiTests.AssertProblemAsync(close, HttpStatusCode.BadRequest, "urn:coldframe:problem:validation", cancellationToken);

        // A Reading the Sensor has not stored, and one of another Sensor.
        using var unknown = await server.PostAsJsonAsync(Uri(site.Id, sensors[0]), new { dry = new { readingSeq = 99 } }, cancellationToken);
        await EdgeApiTests.AssertProblemAsync(unknown, HttpStatusCode.BadRequest, "urn:coldframe:problem:validation", cancellationToken);
        using var foreign = await server.PostAsJsonAsync(Uri(site.Id, sensors[0]), new { dry = new { readingSeq = 3 } }, cancellationToken);
        await EdgeApiTests.AssertProblemAsync(foreign, HttpStatusCode.BadRequest, "urn:coldframe:problem:validation", cancellationToken);

        // A Sensor whose Specification has no Calibration.
        using var air = await server.PostAsJsonAsync(Uri(site.Id, sensors[1]), new { dry = new { readingSeq = 3 } }, cancellationToken);
        await EdgeApiTests.AssertProblemAsync(air, HttpStatusCode.BadRequest, "urn:coldframe:problem:validation", cancellationToken);

        // No point, a point without a Reading, a Reading that is no number, and a body that is no JSON.
        foreach (var json in new[] { "{}", """{"dry":{}}""", """{"dry":{"readingSeq":"x"}}""", """{"wet":{"readingSeq":-1}}""", "[]" })
        {
            using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            using var response = await server.PostAsync(Uri(site.Id, sensors[0]), content, cancellationToken);
            await EdgeApiTests.AssertProblemAsync(response, HttpStatusCode.BadRequest, "urn:coldframe:problem:validation", cancellationToken);
        }

        using var notJson = await server.PostAsync(Uri(site.Id, sensors[0]), new StringContent("dry", System.Text.Encoding.UTF8, "text/plain"), cancellationToken);
        await EdgeApiTests.AssertProblemAsync(notJson, HttpStatusCode.BadRequest, "urn:coldframe:problem:validation", cancellationToken);

        Assert.Equal(["sensor.declared"], await edge.AliasesAsync($"sensor/{sensors[0]}", cancellationToken));
        Assert.Equal(["sensor.declared"], await edge.AliasesAsync($"sensor/{sensors[1]}", cancellationToken));
    }

    [Fact]
    public async Task AnUnknownSensorOrOneOfAnotherSiteIs404SensorNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var site = await SeedSiteAsync(cancellationToken);
        var other = await SeedSiteAsync(cancellationToken);
        var node = await SeedNodeAsync(other.Id, cancellationToken);
        var foreign = await DeclareAsync(node, other.Id, cancellationToken, Soil);
        await StoreReadingAsync(node, foreign[0], 1, 3000, null, cancellationToken);
        using var server = edge.CreateServerClient(site.Administrator.AccessToken);
        var body = new { dry = new { readingSeq = 1 } };

        using var unknown = await server.PostAsJsonAsync(Uri(site.Id, Guid.NewGuid()), body, cancellationToken);
        await EdgeApiTests.AssertProblemAsync(unknown, HttpStatusCode.NotFound, "urn:coldframe:problem:sensor-not-found", cancellationToken);

        using var notAnId = await server.PostAsJsonAsync(new Uri($"/sites/{site.Id}/sensors/nope/calibration", UriKind.Relative), body, cancellationToken);
        await EdgeApiTests.AssertProblemAsync(notAnId, HttpStatusCode.NotFound, "urn:coldframe:problem:sensor-not-found", cancellationToken);

        using var ofAnotherSite = await server.PostAsJsonAsync(Uri(site.Id, foreign[0]), body, cancellationToken);
        await EdgeApiTests.AssertProblemAsync(ofAnotherSite, HttpStatusCode.NotFound, "urn:coldframe:problem:sensor-not-found", cancellationToken);

        Assert.Equal(["sensor.declared"], await edge.AliasesAsync($"sensor/{foreign[0]}", cancellationToken));
    }

    [Fact]
    public async Task AMemberGets403AndNothingIsJournaled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var site = await SeedSiteAsync(cancellationToken);
        var node = await SeedNodeAsync(site.Id, cancellationToken);
        var soil = await DeclareAsync(node, site.Id, cancellationToken, Soil);
        await StoreReadingAsync(node, soil[0], 1, 3000, null, cancellationToken);
        using var server = edge.CreateServerClient(site.Member.AccessToken);

        using var response = await server.PostAsJsonAsync(Uri(site.Id, soil[0]), new { dry = new { readingSeq = 1 } }, cancellationToken);

        await EdgeApiTests.AssertProblemAsync(response, HttpStatusCode.Forbidden, "urn:coldframe:problem:forbidden", cancellationToken);
        Assert.Equal(["sensor.declared"], await edge.AliasesAsync($"sensor/{soil[0]}", cancellationToken));
    }

    private static Uri Uri(string siteId, Guid sensorId) => new($"/sites/{siteId}/sensors/{sensorId}/calibration", UriKind.Relative);

    private static string NewDeviceId() => Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8));

    // An enrolled Node, as its Device stream starts: the Edge API only asks the Device grain which Site it is on.
    private async Task<string> SeedNodeAsync(string siteId, CancellationToken cancellationToken)
    {
        var deviceId = NewDeviceId();
        var wrapped = new WrappedDeviceKey("0000000000000000", new byte[12], new byte[48]);
        await edge.AppendAsync($"device/{deviceId}", [new DeviceEnrolled(siteId, DeviceKind.Node, wrapped, TimeProvider.System.GetUtcNow())], cancellationToken);
        return deviceId;
    }

    // Declares the Sensors as the grains journal it: every Sensor first, then the Device's set.
    private async Task<List<Guid>> DeclareAsync(string deviceId, string siteId, CancellationToken cancellationToken, params SensorSpecification[] specifications)
    {
        var at = TimeProvider.System.GetUtcNow();
        var sensors = new List<DeclaredSensor>();

        foreach (var (specification, slot) in specifications.Select((specification, slot) => (specification, slot)))
        {
            var sensorId = Guid.NewGuid();
            await edge.AppendAsync($"sensor/{sensorId}", [new SensorDeclared(deviceId, slot, specification, at)], cancellationToken);
            sensors.Add(new DeclaredSensor(slot, specification.Quantity, sensorId));
        }

        var version = (await edge.ReadStreamAsync($"device/{deviceId}", cancellationToken)).Count;
        var last = await edge.AppendAsync($"device/{deviceId}", version, [new DeviceSpecificationsDeclared([1, 2, 3], sensors, at)], cancellationToken);
        await edge.WaitForProjectionCheckpointAsync("lots", last);

        return [.. sensors.Select(sensor => sensor.SensorId)];
    }

    // A stored Reading, as the Device grain writes it.
    private async Task StoreReadingAsync(
        string deviceId,
        Guid sensorId,
        int seq,
        long raw,
        Guid? calibrationId,
        CancellationToken cancellationToken,
        int slot = 0,
        string quantity = "soil_moisture")
    {
        await using var command = edge.Database.CreateCommand(
            """
            INSERT INTO readings (device_id, sensor_id, reading_seq, measured_at, slot, quantity, raw_value, calibration_id, time_unsynced, received_at)
            VALUES (@device_id, @sensor_id, @seq, @measured_at, @slot, @quantity, @raw, @calibration_id, false, @measured_at)
            """);
        command.Parameters.AddWithValue("device_id", deviceId);
        command.Parameters.AddWithValue("sensor_id", sensorId);
        command.Parameters.AddWithValue("seq", (decimal)seq);

        // Later than the Node's claim, one second apart per Reading.
        command.Parameters.AddWithValue("measured_at", TimeProvider.System.GetUtcNow().AddMinutes(1).AddSeconds(seq));
        command.Parameters.AddWithValue("slot", slot);
        command.Parameters.AddWithValue("quantity", quantity);
        command.Parameters.AddWithValue("raw", raw);
        command.Parameters.AddWithValue("calibration_id", NpgsqlTypes.NpgsqlDbType.Uuid, (object?)calibrationId ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<string> LotStatusAsync(TestUser user, string siteId, string lotId, CancellationToken cancellationToken)
    {
        using var server = edge.CreateServerClient(user.AccessToken);
        using var response = await server.GetAsync(new System.Uri($"/sites/{siteId}/lots/{lotId}", UriKind.Relative), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await EdgeApiFixture.ReadJsonAsync(response, cancellationToken);

        return body.RootElement.GetProperty("status").GetString()!;
    }

    private async Task<JsonElement> SoilReadingAsync(TestUser user, string siteId, string lotId, CancellationToken cancellationToken)
    {
        using var server = edge.CreateServerClient(user.AccessToken);
        using var response = await server.GetAsync(new System.Uri($"/sites/{siteId}/lots/{lotId}", UriKind.Relative), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await EdgeApiFixture.ReadJsonAsync(response, cancellationToken);

        return body.RootElement.GetProperty("sensors").EnumerateArray().Single(sensor => sensor.GetProperty("quantity").GetString() == "soil_moisture").Clone();
    }

    private async Task<SeededSite> SeedSiteAsync(CancellationToken cancellationToken)
    {
        var owner = await edge.CreateUserAsync("calibration-owner", cancellationToken);
        var administrator = await edge.CreateUserAsync("calibration-admin", cancellationToken);
        var member = await edge.CreateUserAsync("calibration-member", cancellationToken);
        var siteId = Guid.CreateVersion7().ToString();

        var last = await edge.AppendAsync(
            $"site/{siteId}",
            [
                new SiteCreated("Calibration", owner.UserId),
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
