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

    [Fact]
    public void AClosedAlertWithoutASideCarriesItsCloseAndTheNextCursor()
    {
        var opened = new DateTimeOffset(2026, 10, 9, 5, 45, 0, TimeSpan.Zero);
        var view = new AlertView(Guid.NewGuid(), "silent", null, "soil_moisture", "lot-1", "Tomatoes", "3f2a9c0d1e4b5a67", opened, opened.AddMinutes(55), "recovered");

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
