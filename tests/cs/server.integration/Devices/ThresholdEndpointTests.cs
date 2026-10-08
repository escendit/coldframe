using System.Net;
using System.Net.Http.Json;
using System.Text;
using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Sensors;
using Coldframe.Contracts.Sites;
using Coldframe.Server.IntegrationTests.Edge;

namespace Coldframe.Server.IntegrationTests.Devices;

/// <summary>
/// <c>GET</c> and <c>PUT /sites/{siteId}/sensors/{sensorId}/thresholds</c> on the AppHost (Story 5.3): an
/// Administrator sets them in display units, a Member reads and gets 403 on a change, a refusal is a 400 Problem
/// Details that journals nothing, and a Sensor of another Site is a 404. The authorization matrix covers every
/// Role and Site.
/// </summary>
[Collection(IngestSuites.Name)]
public sealed class ThresholdEndpointTests(EdgeApiFixture edge) : IClassFixture<EdgeApiFixture>
{
    private static readonly SensorSpecification Soil = new("soil_moisture", SensorUnit.RawCount, 0, 4095, Calibration: true, DefaultLow: 30);

    private static readonly SensorSpecification Air = new("air_temperature", SensorUnit.MilliDegreeCelsius, -40_000, 85_000, Calibration: false);

    private static readonly SensorSpecification Humidity = new("relative_humidity", SensorUnit.MilliPercent, 0, 100_000, Calibration: false);

    private static readonly SensorSpecification Gas = new("gas_resistance", SensorUnit.Ohm, 0, 100_000_000, Calibration: false);

    private const string Validation = "urn:coldframe:problem:validation";

    [Fact]
    public async Task AnAdministratorSetsTheLowAndTheAnswerAndTheReadShowBothSides()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var site = await SeedSiteAsync(cancellationToken);
        var soil = (await DeclareAsync(site.Id, cancellationToken, Soil))[0];
        using var admin = edge.CreateServerClient(site.Administrator.AccessToken);

        using var response = await admin.PutAsJsonAsync(Uri(site.Id, soil), new { low = new { kind = "override", value = 25 } }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await EdgeApiFixture.ReadJsonAsync(response, cancellationToken);
        Assert.Equal("%", body.RootElement.GetProperty("unit").GetString());
        Assert.Equal(("override", 25m), (body.RootElement.GetProperty("low").GetProperty("kind").GetString(), body.RootElement.GetProperty("low").GetProperty("value").GetDecimal()));
        Assert.Equal("default", body.RootElement.GetProperty("high").GetProperty("kind").GetString());
        Assert.False(body.RootElement.TryGetProperty("proposedLow", out _));
        Assert.Equal(["sensor.declared", "sensor.thresholds-changed"], await edge.AliasesAsync($"sensor/{soil}", cancellationToken));

        using var read = await admin.GetAsync(Uri(site.Id, soil), cancellationToken);
        using var readBody = await EdgeApiFixture.ReadJsonAsync(read, cancellationToken);
        Assert.Equal(25m, readBody.RootElement.GetProperty("low").GetProperty("value").GetDecimal());
    }

    [Fact]
    public async Task TheSameRequestAgainIs200AndJournalsNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var site = await SeedSiteAsync(cancellationToken);
        var soil = (await DeclareAsync(site.Id, cancellationToken, Soil))[0];
        using var admin = edge.CreateServerClient(site.Administrator.AccessToken);
        var request = new { low = new { kind = "override", value = 25 }, high = new { kind = "override", value = 70 } };
        using var first = await admin.PutAsJsonAsync(Uri(site.Id, soil), request, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var again = await admin.PutAsJsonAsync(Uri(site.Id, soil), request, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(["sensor.declared", "sensor.thresholds-changed"], await edge.AliasesAsync($"sensor/{soil}", cancellationToken));
    }

    [Fact]
    public async Task ADisplayValueIsConvertedToTheSpecificationUnitAndBack()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var site = await SeedSiteAsync(cancellationToken);
        var sensors = await DeclareAsync(site.Id, cancellationToken, Air, Gas);
        using var admin = edge.CreateServerClient(site.Administrator.AccessToken);

        using var air = await admin.PutAsJsonAsync(Uri(site.Id, sensors[0]), new { low = new { kind = "override", value = 5.5 }, high = new { kind = "override", value = 30 } }, cancellationToken);
        using var gas = await admin.PutAsJsonAsync(Uri(site.Id, sensors[1]), new { low = new { kind = "override", value = 12.5 } }, cancellationToken);

        using var airBody = await EdgeApiFixture.ReadJsonAsync(air, cancellationToken);
        Assert.Equal(("°C", 5.5m, 30m), (airBody.RootElement.GetProperty("unit").GetString(), airBody.RootElement.GetProperty("low").GetProperty("value").GetDecimal(), airBody.RootElement.GetProperty("high").GetProperty("value").GetDecimal()));
        using var gasBody = await EdgeApiFixture.ReadJsonAsync(gas, cancellationToken);
        Assert.Equal(("kΩ", 12.5m), (gasBody.RootElement.GetProperty("unit").GetString(), gasBody.RootElement.GetProperty("low").GetProperty("value").GetDecimal()));

        // The grain stores the Specification unit: milli-°C and Ω.
        var events = await edge.ReadStreamAsync($"sensor/{sensors[0]}", cancellationToken);
        Assert.Equal(
            new SensorThresholdsChanged(new ThresholdSetting(ThresholdKind.Override, 5_500), new ThresholdSetting(ThresholdKind.Override, 30_000), ((SensorThresholdsChanged)events[1].Data).ChangedAt),
            events[1].Data);
        Assert.Equal(12_500L, ((SensorThresholdsChanged)(await edge.ReadStreamAsync($"sensor/{sensors[1]}", cancellationToken))[1].Data).Low.Value);
    }

    [Fact]
    public async Task AHumidityValueIsConvertedFromPercentToMilliPercentAndBack()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var site = await SeedSiteAsync(cancellationToken);
        var humidity = (await DeclareAsync(site.Id, cancellationToken, Humidity))[0];
        using var admin = edge.CreateServerClient(site.Administrator.AccessToken);

        using var response = await admin.PutAsJsonAsync(Uri(site.Id, humidity), new { low = new { kind = "override", value = 40.5 } }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await EdgeApiFixture.ReadJsonAsync(response, cancellationToken);
        Assert.Equal(("%", 40.5m), (body.RootElement.GetProperty("unit").GetString(), body.RootElement.GetProperty("low").GetProperty("value").GetDecimal()));
        Assert.Equal(40_500L, ((SensorThresholdsChanged)(await edge.ReadStreamAsync($"sensor/{humidity}", cancellationToken))[1].Data).Low.Value);
    }

    [Fact]
    public async Task AValueFinerThanTheStoredUnitIsA400AndNothingIsJournaled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var site = await SeedSiteAsync(cancellationToken);
        var air = (await DeclareAsync(site.Id, cancellationToken, Air))[0];
        using var admin = edge.CreateServerClient(site.Administrator.AccessToken);

        using var response = await admin.PutAsJsonAsync(Uri(site.Id, air), new { low = new { kind = "override", value = 5.0004 } }, cancellationToken);

        await EdgeApiTests.AssertProblemAsync(response, HttpStatusCode.BadRequest, Validation, cancellationToken);
        Assert.Equal(["sensor.declared"], await edge.AliasesAsync($"sensor/{air}", cancellationToken));
    }

    [Fact]
    public async Task AWatchedSensorWithoutADefaultReadsItsProposedLowAndNoProposedHigh()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var site = await SeedSiteAsync(cancellationToken);
        var air = (await DeclareAsync(site.Id, cancellationToken, Air))[0];
        using var member = edge.CreateServerClient(site.Member.AccessToken);

        using var response = await member.GetAsync(Uri(site.Id, air), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await EdgeApiFixture.ReadJsonAsync(response, cancellationToken);
        Assert.Equal(-15m, body.RootElement.GetProperty("proposedLow").GetDecimal());
        Assert.Equal("default", body.RootElement.GetProperty("low").GetProperty("kind").GetString());
        Assert.False(body.RootElement.GetProperty("low").TryGetProperty("value", out _));
        Assert.False(body.RootElement.TryGetProperty("proposedHigh", out _));
        Assert.Equal(["sensor.declared"], await edge.AliasesAsync($"sensor/{air}", cancellationToken));
    }

    [Fact]
    public async Task EveryRefusalIsA400ProblemDetailsAndNothingIsJournaled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var site = await SeedSiteAsync(cancellationToken);
        var sensors = await DeclareAsync(site.Id, cancellationToken, Soil, Air);
        using var admin = edge.CreateServerClient(site.Administrator.AccessToken);

        var bodies = new[]
        {
            // Low not below high, and a high without a low.
            """{"low":{"kind":"override","value":70},"high":{"kind":"override","value":60}}""",
            """{"low":{"kind":"override","value":60},"high":{"kind":"override","value":60}}""",
            """{"low":{"kind":"cleared"},"high":{"kind":"override","value":70}}""",
            // A malformed side.
            """{"low":{"kind":"override"}}""",
            """{"low":{"kind":"default","value":30}}""",
            """{"low":{"kind":"cleared","value":30}}""",
            """{"low":{"kind":"nope"}}""",
            """{"low":{}}""",
            // Out of 0-100 %, and not a whole percent.
            """{"low":{"kind":"override","value":101}}""",
            """{"low":{"kind":"override","value":-1}}""",
            """{"low":{"kind":"override","value":25.5}}""",
            // No side, no JSON object.
            "{}",
            "[]",
            """{"low":"x"}""",
        };
        foreach (var json in bodies)
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await admin.PutAsync(Uri(site.Id, sensors[0]), content, cancellationToken);
            await EdgeApiTests.AssertProblemAsync(response, HttpStatusCode.BadRequest, Validation, cancellationToken);
        }

        using var notJson = await admin.PutAsync(Uri(site.Id, sensors[0]), new StringContent("low", Encoding.UTF8, "text/plain"), cancellationToken);
        await EdgeApiTests.AssertProblemAsync(notJson, HttpStatusCode.BadRequest, Validation, cancellationToken);

        // Outside the Specification's range, in display units.
        using var hot = await admin.PutAsJsonAsync(Uri(site.Id, sensors[1]), new { low = new { kind = "override", value = 86 } }, cancellationToken);
        await EdgeApiTests.AssertProblemAsync(hot, HttpStatusCode.BadRequest, Validation, cancellationToken);
        using var huge = await admin.PutAsJsonAsync(Uri(site.Id, sensors[1]), new { low = new { kind = "override", value = 1e20 } }, cancellationToken);
        await EdgeApiTests.AssertProblemAsync(huge, HttpStatusCode.BadRequest, Validation, cancellationToken);

        Assert.Equal(["sensor.declared"], await edge.AliasesAsync($"sensor/{sensors[0]}", cancellationToken));
        Assert.Equal(["sensor.declared"], await edge.AliasesAsync($"sensor/{sensors[1]}", cancellationToken));
    }

    [Fact]
    public async Task AMemberReadsThresholdsButGets403OnAChangeAndNothingIsJournaled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var site = await SeedSiteAsync(cancellationToken);
        var soil = (await DeclareAsync(site.Id, cancellationToken, Soil))[0];
        using var member = edge.CreateServerClient(site.Member.AccessToken);

        using var read = await member.GetAsync(Uri(site.Id, soil), cancellationToken);
        using var write = await member.PutAsJsonAsync(Uri(site.Id, soil), new { low = new { kind = "override", value = 25 } }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        await EdgeApiTests.AssertProblemAsync(write, HttpStatusCode.Forbidden, "urn:coldframe:problem:forbidden", cancellationToken);
        Assert.Equal(["sensor.declared"], await edge.AliasesAsync($"sensor/{soil}", cancellationToken));
    }

    [Fact]
    public async Task AnUnknownSensorOrOneOfAnotherSiteIs404SensorNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var site = await SeedSiteAsync(cancellationToken);
        var other = await SeedSiteAsync(cancellationToken);
        var foreign = (await DeclareAsync(other.Id, cancellationToken, Soil))[0];
        using var admin = edge.CreateServerClient(site.Administrator.AccessToken);
        var body = new { low = new { kind = "override", value = 25 } };

        foreach (var sensor in new[] { Guid.NewGuid(), foreign })
        {
            using var read = await admin.GetAsync(Uri(site.Id, sensor), cancellationToken);
            await EdgeApiTests.AssertProblemAsync(read, HttpStatusCode.NotFound, "urn:coldframe:problem:sensor-not-found", cancellationToken);
            using var write = await admin.PutAsJsonAsync(Uri(site.Id, sensor), body, cancellationToken);
            await EdgeApiTests.AssertProblemAsync(write, HttpStatusCode.NotFound, "urn:coldframe:problem:sensor-not-found", cancellationToken);
        }

        using var notAnId = await admin.GetAsync(new Uri($"/sites/{site.Id}/sensors/nope/thresholds", UriKind.Relative), cancellationToken);
        await EdgeApiTests.AssertProblemAsync(notAnId, HttpStatusCode.NotFound, "urn:coldframe:problem:sensor-not-found", cancellationToken);
        Assert.Equal(["sensor.declared"], await edge.AliasesAsync($"sensor/{foreign}", cancellationToken));
    }

    private static Uri Uri(string siteId, Guid sensorId) => new($"/sites/{siteId}/sensors/{sensorId}/thresholds", UriKind.Relative);

    // An enrolled Node and its declared Sensors, as the grains journal them: every Sensor first, then the Device's set.
    private async Task<List<Guid>> DeclareAsync(string siteId, CancellationToken cancellationToken, params SensorSpecification[] specifications)
    {
        var deviceId = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8));
        var wrapped = new WrappedDeviceKey("0000000000000000", new byte[12], new byte[48]);
        await edge.AppendAsync($"device/{deviceId}", [new DeviceEnrolled(siteId, DeviceKind.Node, wrapped, TimeProvider.System.GetUtcNow())], cancellationToken);

        var at = TimeProvider.System.GetUtcNow();
        var sensors = new List<DeclaredSensor>();
        foreach (var (specification, slot) in specifications.Select((specification, slot) => (specification, slot)))
        {
            var sensorId = Guid.NewGuid();
            await edge.AppendAsync($"sensor/{sensorId}", [new SensorDeclared(deviceId, slot, specification, at)], cancellationToken);
            sensors.Add(new DeclaredSensor(slot, specification.Quantity, sensorId));
        }

        var version = (await edge.ReadStreamAsync($"device/{deviceId}", cancellationToken)).Count;
        await edge.AppendAsync($"device/{deviceId}", version, [new DeviceSpecificationsDeclared([1, 2, 3], sensors, at)], cancellationToken);

        return [.. sensors.Select(sensor => sensor.SensorId)];
    }

    private async Task<SeededSite> SeedSiteAsync(CancellationToken cancellationToken)
    {
        var owner = await edge.CreateUserAsync("thresholds-owner", cancellationToken);
        var administrator = await edge.CreateUserAsync("thresholds-admin", cancellationToken);
        var member = await edge.CreateUserAsync("thresholds-member", cancellationToken);
        var siteId = Guid.CreateVersion7().ToString();

        var last = await edge.AppendAsync(
            $"site/{siteId}",
            [
                new SiteCreated("Thresholds", owner.UserId),
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
