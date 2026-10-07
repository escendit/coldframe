using Coldframe.Server.Devices;
using Coldframe.Server.Edge;
using Coldframe.Server.Lots;

namespace Coldframe.Server.Tests.Lots;

/// <summary>
/// The pure parts of Lot detail (Story 4.8): unit conversion, the opaque history cursor and the mapping of a
/// Node in the Devices list.
/// </summary>
public sealed class LotDetailUnitTests
{
    [Theory]
    [InlineData("soil_moisture", 612, 612.0, "raw")]
    [InlineData("air_temperature", 14_400, 14.4, "°C")]
    [InlineData("air_temperature", -2_500, -2.5, "°C")]
    [InlineData("relative_humidity", 78_250, 78.25, "%")]
    [InlineData("gas_resistance", 142_000, 142.0, "kΩ")]
    public void ValuesAreConvertedOnTheServer(string quantity, long raw, double value, string unit)
    {
        var converted = SensorConversion.Convert(quantity, raw);

        Assert.Equal((value, unit), converted);
    }

    [Fact]
    public void SoilMoistureIsNeverAPercentage()
    {
        Assert.Equal("raw", SensorConversion.Convert("soil_moisture", 4095)!.Value.Unit);
    }

    [Fact]
    public void AnUnknownQuantityHasNoConversion()
    {
        Assert.Null(SensorConversion.Convert("co2", 400));
    }

    [Fact]
    public void ACursorRoundTripsAndIsOpaque()
    {
        var cursor = LotDetailReadModel.EncodeCursor(new DateOnly(2026, 2, 10));

        Assert.Equal(new DateOnly(2026, 2, 10), LotDetailReadModel.DecodeCursor(cursor));
        Assert.DoesNotContain("2026", cursor, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("!!!")]
    [InlineData("MjAyNi0wMi0xMA")]
    public void AMalformedCursorDecodesToNothing(string cursor)
    {
        Assert.Null(LotDetailReadModel.DecodeCursor(cursor));
    }

    [Theory]
    [InlineData("charging", "charging")]
    [InlineData("not_charging", "notCharging")]
    [InlineData("unknown", null)]
    [InlineData(null, null)]
    public void AChargerTokenMapsToTheContractsChargeState(string? stored, string? expected)
    {
        Assert.Equal(expected, EdgeApi.ToChargeState(stored));
    }

    [Fact]
    public void ANodeCarriesItsLotNameBatteryChargingAndReportTime()
    {
        var now = new DateTimeOffset(2026, 10, 6, 7, 4, 0, TimeSpan.Zero);
        var reported = now.AddMinutes(-10);

        var item = EdgeApi.ToDeviceListItem(
            new DeviceView("92064422c012f481", "node", "lot-1", null, "Tomatoes", 62, "charging", reported),
            now);

        Assert.Equal("Tomatoes", item.LotName);
        Assert.Equal(62, item.BatteryPercent);
        Assert.Equal("charging", item.Charging);
        Assert.Equal("2026-10-06T06:54:00.000Z", item.LastSeenAt);
    }

    [Fact]
    public void AHubKeepsItsHeartbeatTimeAndCarriesNoNodeFields()
    {
        var now = new DateTimeOffset(2026, 10, 6, 7, 4, 0, TimeSpan.Zero);

        var item = EdgeApi.ToDeviceListItem(new DeviceView("92064422c012f481", "hub", null, now.AddSeconds(-30)), now);

        Assert.Equal("2026-10-06T07:03:30.000Z", item.LastSeenAt);
        Assert.Null(item.LotName);
        Assert.Null(item.BatteryPercent);
        Assert.Null(item.Charging);
    }
}
