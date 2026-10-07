using Coldframe.Server.Lots;
using Coldframe.Server.Sensors;

namespace Coldframe.Server.Tests.Lots;

/// <summary>
/// Soil moisture is a percentage only for a Reading stored with a Calibration (Story 5.1): derived from that
/// Calibration's points and rounded to the nearest 5 percent. Every other value converts as before.
/// </summary>
public sealed class CalibratedConversionTests
{
    private static readonly CalibrationPoints Points = new(3000, 1200);

    [Theory]
    [InlineData(3000, 0)]
    [InlineData(2100, 50)]
    [InlineData(1200, 100)]
    [InlineData(4095, 0)]
    [InlineData(10, 100)]
    public void ACalibratedSoilReadingIsAPercentageRoundedToFive(long raw, double expected)
    {
        var converted = SensorConversion.Convert("soil_moisture", raw, Points);

        Assert.Equal((expected, "%"), converted!.Value);
    }

    [Fact]
    public void AnUncalibratedSoilReadingStaysTheRawCount() =>
        Assert.Equal((2100d, "raw"), SensorConversion.Convert("soil_moisture", 2100, calibration: null)!.Value);

    [Fact]
    public void AReadingOfAnotherQuantityIgnoresACalibration() =>
        Assert.Equal((21.5, "°C"), SensorConversion.Convert("air_temperature", 21_500, Points)!.Value);

    [Fact]
    public void AReadingStoredWithACalibrationReadsWhatItsOwnPointsSayWhateverTheSensorHasNow()
    {
        // History keeps its Calibration: the same raw value reads differently under another one.
        var before = SensorConversion.Convert("soil_moisture", 2100, new CalibrationPoints(3000, 1200));
        var after = SensorConversion.Convert("soil_moisture", 2100, new CalibrationPoints(3000, 600));

        Assert.Equal((50d, "%"), before!.Value);
        Assert.Equal((40d, "%"), after!.Value);
    }
}
