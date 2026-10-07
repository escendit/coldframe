using Coldframe.Contracts.Sensors;
using Coldframe.Server.Sensors;

namespace Coldframe.Server.Tests.Sensors;

/// <summary>
/// The Calibration's arithmetic (Story 5.1): two-point linear in either orientation of the raw values, clamped
/// to 0 to 100 and rounded to the nearest 5 percent; and the rule that tells distinct points from indistinct ones.
/// </summary>
public sealed class CalibrationMathTests
{
    [Theory]
    [InlineData(3000, 3000, 1200, 0)]
    [InlineData(1200, 3000, 1200, 100)]
    [InlineData(2100, 3000, 1200, 50)]
    [InlineData(2500, 3000, 1200, 30)]
    [InlineData(1600, 3000, 1200, 80)]
    public void ADryHighWetLowProbeReadsLinearlyBetweenItsPoints(long raw, long dry, long wet, int expected) =>
        Assert.Equal(expected, CalibrationMath.Percent(raw, dry, wet));

    [Theory]
    [InlineData(200, 200, 2000, 0)]
    [InlineData(2000, 200, 2000, 100)]
    [InlineData(1100, 200, 2000, 50)]
    [InlineData(560, 200, 2000, 20)]
    public void ADryLowWetHighProbeReadsLinearlyToo(long raw, long dry, long wet, int expected) =>
        Assert.Equal(expected, CalibrationMath.Percent(raw, dry, wet));

    [Theory]
    [InlineData(4095, 3000, 1200)]
    [InlineData(3001, 3000, 1200)]
    [InlineData(0, 200, 2000)]
    public void AReadingBeyondTheDryPointReadsZero(long raw, long dry, long wet) =>
        Assert.Equal(0, CalibrationMath.Percent(raw, dry, wet));

    [Theory]
    [InlineData(0, 3000, 1200)]
    [InlineData(1199, 3000, 1200)]
    [InlineData(4095, 200, 2000)]
    public void AReadingBeyondTheWetPointReadsOneHundred(long raw, long dry, long wet) =>
        Assert.Equal(100, CalibrationMath.Percent(raw, dry, wet));

    [Theory]
    [InlineData(0, 0, 1000, 0)]
    [InlineData(24, 0, 1000, 0)]
    [InlineData(25, 0, 1000, 5)]
    [InlineData(74, 0, 1000, 5)]
    [InlineData(75, 0, 1000, 10)]
    [InlineData(976, 0, 1000, 100)]
    [InlineData(974, 0, 1000, 95)]
    public void AValueIsRoundedToTheNearestFivePercentAndAHalfRoundsUp(long raw, long dry, long wet, int expected)
    {
        // Raw 25 of 1000 is 2.5 %: the nearest 5 is 5 (half rounds up); raw 24 is 2.4 %: 0.
        Assert.Equal(expected, CalibrationMath.Percent(raw, dry, wet));
    }

    [Fact]
    public void EveryPercentIsAMultipleOfFiveBetweenZeroAndOneHundred()
    {
        for (var raw = -50L; raw <= 1100; raw += 7)
        {
            var percent = CalibrationMath.Percent(raw, 100, 900);
            Assert.InRange(percent, 0, 100);
            Assert.Equal(0, percent % 5);
        }
    }

    [Theory]
    [InlineData(1200, 1200, false)]
    [InlineData(1200, 1201, false)]
    [InlineData(1200, 1215, false)]
    [InlineData(1200, 1216, true)]
    [InlineData(1216, 1200, true)]
    [InlineData(1215, 1200, false)]
    [InlineData(3000, 1200, true)]
    [InlineData(-5, 11, true)]
    public void PointsAreDistinctFromTheMinimumSpanOn(long dry, long wet, bool distinct)
    {
        Assert.Equal(16, SensorCalibrationLimits.MinimumSpan);
        Assert.Equal(distinct, CalibrationMath.AreDistinct(dry, wet));
    }

    [Fact]
    public void ThePercentOfIndistinctPointsIsNeverAskedFor()
    {
        // Equal points would divide by zero: the rule refuses them before any Reading is derived from them.
        Assert.Throws<ArgumentException>(() => CalibrationMath.Percent(10, 5, 5));
    }
}
