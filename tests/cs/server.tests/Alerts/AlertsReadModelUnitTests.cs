using System.Text.Json;
using System.Text.Json.Serialization;
using Coldframe.Contracts.Alerts;
using Coldframe.Server.Alerts;
using Coldframe.Server.Edge;

namespace Coldframe.Server.Tests.Alerts;

/// <summary>
/// The pure parts of the Alerts list (Story 6.2): the opaque, versioned cursor, the hand-mapped names of the
/// contract's enums and the property names of the response.
/// </summary>
public sealed class AlertsReadModelUnitTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACursorRoundTripsAndIsOpaque(bool closed)
    {
        // PostgreSQL keeps microseconds, and so does the cursor.
        var at = new DateTimeOffset(2026, 10, 9, 5, 45, 12, TimeSpan.Zero).AddTicks(1_234_560);
        var alertId = Guid.Parse("0b7f2a8e-5d0c-5e1b-9d53-3d1b5f6f2a10");

        var cursor = AlertsReadModel.EncodeCursor(new AlertCursor(closed, at, alertId));

        Assert.Equal(new AlertCursor(closed, at, alertId), AlertsReadModel.DecodeCursor(cursor));
        Assert.DoesNotContain("0b7f2a8e", cursor, StringComparison.Ordinal);
        Assert.DoesNotContain("2026", cursor, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("!!!")]
    [InlineData("MjAyNi0wMi0xMA")]

    // "d1:2026-02-10", a history cursor: another version is not an Alerts cursor.
    [InlineData("ZDE6MjAyNi0wMi0xMA")]

    // "a1:x:1:0b7f2a8e5d0c5e1b9d533d1b5f6f2a10", an unknown section.
    [InlineData("YTE6eDoxOjBiN2YyYThlNWQwYzVlMWI5ZDUzM2QxYjVmNmYyYTEw")]

    // "a1:o:soon:0b7f2a8e5d0c5e1b9d533d1b5f6f2a10", no time.
    [InlineData("YTE6bzpzb29uOjBiN2YyYThlNWQwYzVlMWI5ZDUzM2QxYjVmNmYyYTEw")]

    // "a1:o:1:nobody", no Alert ID.
    [InlineData("YTE6bzoxOm5vYm9keQ")]

    // "a1:o:9223372036854775807:0b7f2a8e5d0c5e1b9d533d1b5f6f2a10", a time no clock reaches.
    [InlineData("YTE6bzo5MjIzMzcyMDM2ODU0Nzc1ODA3OjBiN2YyYThlNWQwYzVlMWI5ZDUzM2QxYjVmNmYyYTEw")]
    public void AMalformedCursorDecodesToNothing(string cursor)
    {
        Assert.Null(AlertsReadModel.DecodeCursor(cursor));
    }

    [Fact]
    public void EveryEnumValueHasALowercaseContractName()
    {
        Assert.Equal("threshold", AlertNames.Kind(AlertKind.Threshold));
        Assert.Equal("silent", AlertNames.Kind(AlertKind.Silent));
        Assert.Equal("battery", AlertNames.Kind(AlertKind.Battery));
        Assert.Equal("uncalibrated", AlertNames.Kind(AlertKind.Uncalibrated));
        Assert.Equal("low", AlertNames.Side(ThresholdSide.Low));
        Assert.Equal("high", AlertNames.Side(ThresholdSide.High));
        Assert.Null(AlertNames.Side(null));
        Assert.Equal(
            ["recovered", "paused", "unassigned", "calibrated", "removed"],
            Enum.GetValues<AlertCloseReason>().Select(AlertNames.Reason));

        // A value added to a contract enum must be named by hand before the projector meets it.
        Assert.All(Enum.GetValues<AlertKind>(), kind => Assert.Matches("^[a-z]+$", AlertNames.Kind(kind)));
        Assert.All(Enum.GetValues<ThresholdSide>(), side => Assert.Matches("^[a-z]+$", AlertNames.Side(side)!));
    }

    [Fact]
    public void AnOpenAlertHasTheContractsPropertyNamesAndOmitsWhatItLacks()
    {
        var opened = new DateTimeOffset(2026, 10, 9, 5, 45, 0, TimeSpan.Zero);
        var view = new AlertView(Guid.Parse("0b7f2a8e-5d0c-5e1b-9d53-3d1b5f6f2a10"), "threshold", "low", "soil_moisture", "lot-1", "Tomatoes", "3f2a9c0d1e4b5a67", opened, null, null);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new AlertListResponse([EdgeApi.ToAlert(view)], 1), Web));

        Assert.Equal(["alerts", "openCount"], Names(json.RootElement));
        var alert = json.RootElement.GetProperty("alerts")[0];
        Assert.Equal(["deviceId", "id", "kind", "lotId", "lotName", "openedAt", "quantity", "side"], Names(alert));
        Assert.Equal("0b7f2a8e-5d0c-5e1b-9d53-3d1b5f6f2a10", alert.GetProperty("id").GetString());
        Assert.Equal("2026-10-09T05:45:00.000Z", alert.GetProperty("openedAt").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("openCount").GetInt32());
    }

    // Story 7.3: a battery Alert says the level it opened with; any other Health Alert has no such member.
    [Fact]
    public void AnOpenBatteryAlertCarriesTheLevelItOpenedWith()
    {
        var opened = new DateTimeOffset(2026, 10, 13, 9, 30, 0, TimeSpan.Zero);
        var view = new HealthAlertView(Guid.Parse("ae0cf71d-0fe1-50b9-b548-f4b9e57572b7"), "battery", "9c0d1e2f3a4b5c68", "node", "lot-5", "Lettuce", null, opened, null, null, 14);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new AlertListResponse([], 1, HealthAlerts: [EdgeApi.ToHealthAlert(view)]), Web));

        var alert = json.RootElement.GetProperty("healthAlerts")[0];
        Assert.Equal(["batteryPercent", "deviceId", "deviceKind", "id", "kind", "lotId", "lotName", "openedAt"], Names(alert));
        Assert.Equal(("battery", 14, "Lettuce"), (alert.GetProperty("kind").GetString(), alert.GetProperty("batteryPercent").GetInt32(), alert.GetProperty("lotName").GetString()));

        // A level the contract cannot describe is left out rather than sent.
        Assert.Null(EdgeApi.ToHealthAlert(view with { BatteryPercent = 101 }).BatteryPercent);
        Assert.Null(EdgeApi.ToHealthAlert(view with { BatteryPercent = null }).BatteryPercent);
    }

    // Story 7.2: Health Alerts are listed beside the Threshold Alerts, and only without the cursor.
    [Fact]
    public void AnOpenHealthAlertOfANodeOnALotCarriesItsDeviceKindItsLotAndWhenItWasLastHeard()
    {
        var opened = new DateTimeOffset(2026, 10, 6, 7, 5, 0, TimeSpan.Zero);
        var view = new HealthAlertView(Guid.Parse("7c1d2e3f-4a5b-5c6d-8e7f-9a0b1c2d3e4f"), "silent", "9c0d1e4b5a673f2a", "node", "lot-4", "Beans", opened.AddHours(-6), opened, null, null);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new AlertListResponse([], 1, HealthAlerts: [EdgeApi.ToHealthAlert(view)]), Web));

        Assert.Equal(["alerts", "healthAlerts", "openCount"], Names(json.RootElement));
        var alert = json.RootElement.GetProperty("healthAlerts")[0];
        Assert.Equal(["deviceId", "deviceKind", "id", "kind", "lotId", "lotName", "openedAt", "silentSince"], Names(alert));
        Assert.Equal(("silent", "node", "Beans"), (alert.GetProperty("kind").GetString(), alert.GetProperty("deviceKind").GetString(), alert.GetProperty("lotName").GetString()));
        Assert.Equal("2026-10-06T01:05:00.000Z", alert.GetProperty("silentSince").GetString());
        Assert.Equal("2026-10-06T07:05:00.000Z", alert.GetProperty("openedAt").GetString());
    }

    [Fact]
    public void AClosedHealthAlertOfAHubHasNoLotAndOneOpenedBeforeTheStoryNoSilentSince()
    {
        var opened = new DateTimeOffset(2026, 10, 6, 7, 0, 0, TimeSpan.Zero);
        var view = new HealthAlertView(Guid.NewGuid(), "silent", "3f2a9c0d1e4b5a67", "hub", null, null, null, opened, opened.AddMinutes(20), "recovered");

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new AlertListResponse([], 0, HealthAlerts: [EdgeApi.ToHealthAlert(view)]), Web));

        Assert.Equal(
            ["closedAt", "deviceId", "deviceKind", "id", "kind", "openedAt", "reason"],
            Names(json.RootElement.GetProperty("healthAlerts")[0]));
    }

    [Fact]
    public void AHealthAlertWhoseLotNameCannotBeReadNamesNoLotAtAll()
    {
        var opened = new DateTimeOffset(2026, 10, 6, 7, 0, 0, TimeSpan.Zero);
        var view = new HealthAlertView(Guid.NewGuid(), "silent", "9c0d1e4b5a673f2a", "node", "lot-4", null, null, opened, null, null);

        var alert = EdgeApi.ToHealthAlert(view);

        Assert.Equal(((string?)null, (string?)null), (alert.LotId, alert.LotName));
    }

    [Fact]
    public void APageReadWithACursorCarriesNoHealthAlerts()
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new AlertListResponse([], 3, "next"), Web));

        Assert.Equal(["alerts", "nextCursor", "openCount"], Names(json.RootElement));
    }

    [Fact]
    public void AClosedAlertCarriesItsCloseAndTheNextCursor()
    {
        var opened = new DateTimeOffset(2026, 10, 9, 5, 45, 0, TimeSpan.Zero);
        var view = new AlertView(Guid.NewGuid(), "threshold", null, "soil_moisture", "lot-1", "Tomatoes", "3f2a9c0d1e4b5a67", opened, opened.AddMinutes(55), "recovered");

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new AlertListResponse([EdgeApi.ToAlert(view)], 0, "next"), Web));

        Assert.Equal(["alerts", "nextCursor", "openCount"], Names(json.RootElement));
        var alert = json.RootElement.GetProperty("alerts")[0];
        Assert.Equal(["closedAt", "deviceId", "id", "kind", "lotId", "lotName", "openedAt", "quantity", "reason"], Names(alert));
        Assert.Equal("2026-10-09T06:40:00.000Z", alert.GetProperty("closedAt").GetString());
        Assert.Equal("recovered", alert.GetProperty("reason").GetString());
    }

    private static List<string> Names(JsonElement element) =>
        [.. element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)];
}
