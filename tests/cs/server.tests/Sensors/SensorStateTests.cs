using Coldframe.Contracts.Sensors;
using Coldframe.Server.Sensors;

namespace Coldframe.Server.Tests.Sensors;

/// <summary>
/// The Sensor's state (AD-19): a declaration replaces the Specification only, and each Threshold side is
/// <c>Default | Override(value) | Cleared</c>.
/// </summary>
public sealed class SensorStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private static readonly SensorSpecification Soil = new("soil_moisture", SensorUnit.RawCount, 0, 4095, true, 30, 80);

    [Fact]
    public void AnUndeclaredSensorHasNoSpecificationAndNoThresholds()
    {
        var state = new SensorState();

        Assert.False(state.Declared);
        Assert.Null(state.Specification);
        Assert.Equal((ThresholdSetting.Default, ThresholdSetting.Default), (state.Low, state.High));
        Assert.Equal(((long?)null, (long?)null), (state.EffectiveLow, state.EffectiveHigh));
    }

    [Fact]
    public void ADeclaredSensorFollowsTheDefaultsOfItsSpecification()
    {
        var state = new SensorState();
        state.Apply(new SensorDeclared("5a4b3c2d1e0f7c20", 2, Soil, Now));

        Assert.True(state.Declared);
        Assert.Equal(("5a4b3c2d1e0f7c20", 2, Soil), (state.DeviceId, state.Slot, state.Specification));
        Assert.Equal((ThresholdKind.Default, ThresholdKind.Default), (state.Low.Kind, state.High.Kind));
        Assert.Equal((30L, 80L), (state.EffectiveLow, state.EffectiveHigh));
    }

    [Fact]
    public void ADefaultSideFollowsAChangedSpecificationAlsoWhenTheDefaultGoes()
    {
        var state = new SensorState();
        state.Apply(new SensorDeclared("5a4b3c2d1e0f7c20", 0, Soil, Now));

        state.Apply(new SensorSpecificationChanged(Soil with { DefaultLow = 25, DefaultHigh = 75 }, Now));
        Assert.Equal((25L, 75L), (state.EffectiveLow, state.EffectiveHigh));

        state.Apply(new SensorSpecificationChanged(Soil with { DefaultLow = null, DefaultHigh = null }, Now));
        Assert.Equal((ThresholdKind.Default, ThresholdKind.Default), (state.Low.Kind, state.High.Kind));
        Assert.Equal(((long?)null, (long?)null), (state.EffectiveLow, state.EffectiveHigh));
    }

    [Fact]
    public void AnOverrideKeepsItsValueAndAClearedSideStaysClearedWhenTheSpecificationChanges()
    {
        var state = new SensorState();
        state.Apply(new SensorDeclared("5a4b3c2d1e0f7c20", 0, Soil, Now));
        state.Apply(new SensorThresholdsChanged(new ThresholdSetting(ThresholdKind.Override, 40), new ThresholdSetting(ThresholdKind.Cleared), Now));

        Assert.Equal((40L, (long?)null), (state.EffectiveLow, state.EffectiveHigh));

        var changed = Soil with { RangeMax = 8191, DefaultLow = 25, DefaultHigh = 75 };
        state.Apply(new SensorSpecificationChanged(changed, Now));

        Assert.Equal(changed, state.Specification);
        Assert.Equal(new ThresholdSetting(ThresholdKind.Override, 40), state.Low);
        Assert.Equal(new ThresholdSetting(ThresholdKind.Cleared), state.High);
        Assert.Equal((40L, (long?)null), (state.EffectiveLow, state.EffectiveHigh));
    }

    [Fact]
    public void ASideSetBackToDefaultReadsTheSpecificationAgainAndOnlyAnOverrideKeepsAValue()
    {
        var state = new SensorState();
        state.Apply(new SensorDeclared("5a4b3c2d1e0f7c20", 0, Soil, Now));
        state.Apply(new SensorThresholdsChanged(new ThresholdSetting(ThresholdKind.Override, 40), new ThresholdSetting(ThresholdKind.Override, 90), Now));

        // A value on a side that is no override is not kept.
        state.Apply(new SensorThresholdsChanged(new ThresholdSetting(ThresholdKind.Default, 55), new ThresholdSetting(ThresholdKind.Cleared, 66), Now));

        Assert.Equal((ThresholdSetting.Default, new ThresholdSetting(ThresholdKind.Cleared)), (state.Low, state.High));
        Assert.Equal((30L, (long?)null), (state.EffectiveLow, state.EffectiveHigh));
    }
}
