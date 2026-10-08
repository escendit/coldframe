using Coldframe.Contracts.Sensors;
using Coldframe.Server.Sensors;

namespace Coldframe.Server.Tests.Sensors;

/// <summary>
/// The Threshold rules of the Sensor grain (Story 5.3, AD-19): well-formed sides, a low below the high, a low
/// whenever there is a high, whole percent 0 to 100 for a calibrating Sensor, the proposed low, and a change
/// that changes nothing.
/// </summary>
public sealed class ThresholdRulesTests
{
    private static readonly SensorSpecification Soil = new("soil_moisture", SensorUnit.RawCount, 0, 4095, true, 30, 80);

    private static readonly SensorSpecification SoilNoDefault = Soil with { DefaultLow = null, DefaultHigh = null };

    private static readonly SensorSpecification Air = new("air_temperature", SensorUnit.MilliDegreeCelsius, -40_000, 85_000, false);

    private static ThresholdSetting Override(long value) => new(ThresholdKind.Override, value);

    private static ThresholdSetting Cleared => new(ThresholdKind.Cleared);

    private static ThresholdChange Change(SensorSpecification specification, ThresholdSetting? low, ThresholdSetting? high, ThresholdSetting? currentLow = null, ThresholdSetting? currentHigh = null) =>
        ThresholdRules.Evaluate(specification, currentLow ?? ThresholdSetting.Default, currentHigh ?? ThresholdSetting.Default, low, high);

    [Fact]
    public void ALowOverrideWithTheHighUnchangedIsAChange()
    {
        var change = Change(Soil, Override(25), null);

        Assert.Equal(SensorThresholdsOutcome.Changed, change.Outcome);
        Assert.Equal((Override(25), ThresholdSetting.Default), (change.Low, change.High));
    }

    [Fact]
    public void ARequestThatLeavesBothSidesAsTheyAreIsUnchanged()
    {
        Assert.Equal(SensorThresholdsOutcome.Unchanged, Change(Soil, null, null).Outcome);
        Assert.Equal(SensorThresholdsOutcome.Unchanged, Change(Soil, ThresholdSetting.Default, ThresholdSetting.Default).Outcome);
        Assert.Equal(SensorThresholdsOutcome.Unchanged, Change(Soil, Override(25), null, Override(25)).Outcome);
        Assert.Equal(SensorThresholdsOutcome.Unchanged, Change(Soil, null, Cleared, currentHigh: Cleared).Outcome);
    }

    [Fact]
    public void AHighCanBeAddedAndLaterClearedAndAClearedHighNeverNeedsALowBelowIt()
    {
        var added = Change(Soil, null, Override(70));
        Assert.Equal(SensorThresholdsOutcome.Changed, added.Outcome);
        Assert.Equal(Override(70), added.High);

        var cleared = Change(Soil, null, Cleared, currentHigh: Override(70));
        Assert.Equal((SensorThresholdsOutcome.Changed, Cleared), (cleared.Outcome, cleared.High));
    }

    [Fact]
    public void ASideGoesBackToTheDefault()
    {
        var change = Change(Soil, ThresholdSetting.Default, null, Override(25));

        Assert.Equal((SensorThresholdsOutcome.Changed, ThresholdSetting.Default), (change.Outcome, change.Low));
    }

    [Theory]
    [InlineData(70, 60)]
    [InlineData(60, 60)]
    public void ALowNotBelowTheHighIsRefused(long low, long high)
    {
        Assert.Equal(SensorThresholdsOutcome.LowNotBelowHigh, Change(Soil, Override(low), Override(high)).Outcome);
    }

    [Fact]
    public void ALowAboveTheHighThatIsKeptIsRefusedToo()
    {
        Assert.Equal(SensorThresholdsOutcome.LowNotBelowHigh, Change(Soil, Override(90), null).Outcome);
    }

    [Fact]
    public void AHighWithoutALowIsRefused()
    {
        Assert.Equal(SensorThresholdsOutcome.LowRequired, Change(Soil, Cleared, Override(70)).Outcome);
        Assert.Equal(SensorThresholdsOutcome.LowRequired, Change(SoilNoDefault, null, Override(70)).Outcome);
        Assert.Equal(SensorThresholdsOutcome.LowRequired, Change(Soil, Cleared, null).Outcome);
    }

    [Fact]
    public void ClearingBothSidesMakesTheSensorWatchedOnly()
    {
        var change = Change(Soil, Cleared, Cleared);

        Assert.Equal((SensorThresholdsOutcome.Changed, Cleared, Cleared), (change.Outcome, change.Low, change.High));
    }

    [Fact]
    public void AnEmptyHighIsAllowedAndNeverAlerts()
    {
        Assert.Equal(SensorThresholdsOutcome.Changed, Change(SoilNoDefault, Override(25), null).Outcome);
    }

    [Theory]
    [InlineData(ThresholdKind.Override, null)]
    [InlineData(ThresholdKind.Default, 30L)]
    [InlineData(ThresholdKind.Cleared, 30L)]
    [InlineData((ThresholdKind)7, null)]
    public void AMalformedSideIsRefused(ThresholdKind kind, long? value)
    {
        Assert.Equal(SensorThresholdsOutcome.MalformedSide, Change(Soil, new ThresholdSetting(kind, value), null).Outcome);
        Assert.Equal(SensorThresholdsOutcome.MalformedSide, Change(Soil, null, new ThresholdSetting(kind, value)).Outcome);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void ACalibratingSensorTakesWholePercentFrom0To100(long value)
    {
        Assert.Equal(SensorThresholdsOutcome.OutOfRange, Change(Soil, Override(value), null).Outcome);
    }

    [Fact]
    public void ACalibratingSensorAcceptsTheEndsOfTheScaleWhateverItsRawRange()
    {
        var change = Change(SoilNoDefault, Override(0), Override(100));

        Assert.Equal((SensorThresholdsOutcome.Changed, Override(0), Override(100)), (change.Outcome, change.Low, change.High));
    }

    [Theory]
    [InlineData(-40_001)]
    [InlineData(85_001)]
    public void AnotherSensorTakesValuesWithinItsSpecificationRange(long value)
    {
        Assert.Equal(SensorThresholdsOutcome.OutOfRange, Change(Air, Override(value), null).Outcome);
    }

    [Fact]
    public void AnotherSensorAcceptsAValueWithinItsRangeInTheSpecificationUnit()
    {
        var change = Change(Air, Override(5_000), Override(30_000));

        Assert.Equal((SensorThresholdsOutcome.Changed, Override(5_000), Override(30_000)), (change.Outcome, change.Low, change.High));
    }

    [Fact]
    public void TheProposedLowIsTwentyPercentOfTheRangeAboveItsMinimumWhenThereIsNoDefaultLow()
    {
        Assert.Equal(-15_000L, ThresholdRules.ProposedLow(Air));
        Assert.Equal(20L, ThresholdRules.ProposedLow(SoilNoDefault));
        Assert.Equal(8L, ThresholdRules.ProposedLow(new SensorSpecification("x", SensorUnit.RawCount, 3, 28, false)));
    }

    [Fact]
    public void ThereIsNoProposedLowWhenTheSpecificationHasADefaultLow()
    {
        Assert.Null(ThresholdRules.ProposedLow(Soil));
        Assert.Null(ThresholdRules.ProposedLow(Air with { DefaultLow = 0 }));
    }
}
