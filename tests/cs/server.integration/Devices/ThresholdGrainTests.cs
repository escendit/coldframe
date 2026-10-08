using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Sensors;
using Coldframe.Crypto;
using Coldframe.DeviceSimulator;
using Coldframe.Protocol.Device.V1;
using Coldframe.Server.IntegrationTests.Identity;
using ProtocolKind = Coldframe.Protocol.Setup.V1.DeviceKind;

namespace Coldframe.Server.IntegrationTests.Devices;

/// <summary>
/// Thresholds on a TestCluster with a fake clock (Story 5.3; AD-19): the Sensor grain is the only validator and
/// writer, a change journals <c>sensor.thresholds-changed</c> and nothing else does, an override survives a
/// Specification redeclaration, and the proposed low is read, never journaled.
/// </summary>
[Collection(IngestSuites.Name)]
public sealed class ThresholdGrainTests(IdentityCluster identity) : IClassFixture<IdentityCluster>
{
    private static readonly SensorSpecification Soil = new(CryptoSpec.SensorQuantitySoilMoisture, SensorUnit.RawCount, 0, 4095, true, 30, 80);

    private static readonly SensorSpecification Air = new(CryptoSpec.SensorQuantityAirTemperature, SensorUnit.MilliDegreeCelsius, -40_000, 85_000, false);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ThresholdSetting Override(long value) => new(ThresholdKind.Override, value);

    [Fact]
    public async Task SettingTheLowJournalsThresholdsChangedAndTheReadShowsBothSides()
    {
        var soil = await DeclaredAsync(Soil);

        var result = await identity.Sensor(soil).SetThresholds(new SetSensorThresholds(Low: Override(25)), Ct);

        Assert.Equal(SensorThresholdsOutcome.Changed, result.Outcome);
        Assert.Equal((new ThresholdSetting(ThresholdKind.Override, 25), new ThresholdSetting(ThresholdKind.Default, 80)), (result.Thresholds!.Low, result.Thresholds.High));
        Assert.Equal(["sensor.declared", "sensor.thresholds-changed"], await identity.AliasesAsync($"sensor/{soil}"));
        var events = await identity.Store.ReadStreamAsync($"sensor/{soil}", Ct);
        Assert.Equal(new SensorThresholdsChanged(Override(25), ThresholdSetting.Default, identity.Time.GetUtcNow()), events[1].Data);
        Assert.Equal(result.Thresholds, await identity.Sensor(soil).GetThresholds(Ct));
    }

    [Fact]
    public async Task TheSameRequestAgainJournalsNothing()
    {
        var soil = await DeclaredAsync(Soil);
        await identity.Sensor(soil).SetThresholds(new SetSensorThresholds(Override(25), Override(70)), Ct);

        var again = await identity.Sensor(soil).SetThresholds(new SetSensorThresholds(Override(25), Override(70)), Ct);

        Assert.Equal(SensorThresholdsOutcome.Unchanged, again.Outcome);
        Assert.Equal(["sensor.declared", "sensor.thresholds-changed"], await identity.AliasesAsync($"sensor/{soil}"));
    }

    [Fact]
    public async Task AHighIsAddedClearedAndTheLowGoesBackToTheDefaultEachTimeAChange()
    {
        var soil = await DeclaredAsync(Soil with { DefaultHigh = null });

        Assert.Equal(SensorThresholdsOutcome.Changed, (await identity.Sensor(soil).SetThresholds(new SetSensorThresholds(High: Override(70)), Ct)).Outcome);
        Assert.Equal(SensorThresholdsOutcome.Changed, (await identity.Sensor(soil).SetThresholds(new SetSensorThresholds(High: new ThresholdSetting(ThresholdKind.Cleared)), Ct)).Outcome);
        await identity.Sensor(soil).SetThresholds(new SetSensorThresholds(Low: Override(25)), Ct);
        var back = await identity.Sensor(soil).SetThresholds(new SetSensorThresholds(Low: ThresholdSetting.Default), Ct);

        Assert.Equal((new ThresholdSetting(ThresholdKind.Default, 30), new ThresholdSetting(ThresholdKind.Cleared)), (back.Thresholds!.Low, back.Thresholds.High));
        Assert.Equal(
            ["sensor.declared", "sensor.thresholds-changed", "sensor.thresholds-changed", "sensor.thresholds-changed", "sensor.thresholds-changed"],
            await identity.AliasesAsync($"sensor/{soil}"));
    }

    [Fact]
    public async Task ARefusedRequestJournalsNothing()
    {
        var soil = await DeclaredAsync(Soil);
        var air = await DeclaredAsync(Air);
        var cleared = new ThresholdSetting(ThresholdKind.Cleared);

        Assert.Equal(SensorThresholdsOutcome.LowNotBelowHigh, (await identity.Sensor(soil).SetThresholds(new SetSensorThresholds(Override(70), Override(60)), Ct)).Outcome);
        Assert.Equal(SensorThresholdsOutcome.LowNotBelowHigh, (await identity.Sensor(soil).SetThresholds(new SetSensorThresholds(Override(60), Override(60)), Ct)).Outcome);
        Assert.Equal(SensorThresholdsOutcome.LowRequired, (await identity.Sensor(soil).SetThresholds(new SetSensorThresholds(Low: cleared), Ct)).Outcome);
        Assert.Equal(SensorThresholdsOutcome.LowRequired, (await identity.Sensor(air).SetThresholds(new SetSensorThresholds(High: Override(30_000)), Ct)).Outcome);
        Assert.Equal(SensorThresholdsOutcome.MalformedSide, (await identity.Sensor(soil).SetThresholds(new SetSensorThresholds(Low: new ThresholdSetting(ThresholdKind.Override)), Ct)).Outcome);
        Assert.Equal(SensorThresholdsOutcome.MalformedSide, (await identity.Sensor(soil).SetThresholds(new SetSensorThresholds(Low: new ThresholdSetting(ThresholdKind.Default, 30)), Ct)).Outcome);
        Assert.Equal(SensorThresholdsOutcome.OutOfRange, (await identity.Sensor(soil).SetThresholds(new SetSensorThresholds(Low: Override(101)), Ct)).Outcome);
        Assert.Equal(SensorThresholdsOutcome.OutOfRange, (await identity.Sensor(air).SetThresholds(new SetSensorThresholds(Low: Override(90_000)), Ct)).Outcome);

        Assert.Equal(["sensor.declared"], await identity.AliasesAsync($"sensor/{soil}"));
        Assert.Equal(["sensor.declared"], await identity.AliasesAsync($"sensor/{air}"));
        Assert.Equal(SensorThresholdsOutcome.NotDeclared, (await identity.Sensor(Guid.NewGuid()).SetThresholds(new SetSensorThresholds(Low: Override(25)), Ct)).Outcome);
    }

    [Fact]
    public async Task AWatchedSensorWithoutADefaultProposesTwentyPercentOfItsRangeAndNoHighAndJournalsNothing()
    {
        var air = await DeclaredAsync(Air);

        var thresholds = await identity.Sensor(air).GetThresholds(Ct);

        Assert.Equal(-15_000L, thresholds!.ProposedLow);
        Assert.Equal((new ThresholdSetting(ThresholdKind.Default), new ThresholdSetting(ThresholdKind.Default)), (thresholds.Low, thresholds.High));
        Assert.Equal(["sensor.declared"], await identity.AliasesAsync($"sensor/{air}"));
        Assert.Null((await identity.Sensor(await DeclaredAsync(Soil)).GetThresholds(Ct))!.ProposedLow);
        Assert.Null(await identity.Sensor(Guid.NewGuid()).GetThresholds(Ct));
    }

    [Fact]
    public async Task AnOverrideAndAClearedSideSurviveASpecificationRedeclaration()
    {
        var node = SimulatedDevice.Create(ProtocolKind.Node);
        var id = node.SensorId(0, Quantity.SoilMoisture);
        await identity.Sensor(id).Declare(new DeclareSensor(node.DeviceId.ToString(), 0, Soil), Ct);
        await identity.Sensor(id).SetThresholds(new SetSensorThresholds(Override(25), new ThresholdSetting(ThresholdKind.Cleared)), Ct);

        var changed = await identity.Sensor(id).Declare(new DeclareSensor(node.DeviceId.ToString(), 0, Soil with { DefaultLow = 40, DefaultHigh = 90, RangeMax = 8191 }), Ct);

        Assert.Equal(SensorDeclarationOutcome.SpecificationChanged, changed.Outcome);
        var thresholds = await identity.Sensor(id).GetThresholds(Ct);
        Assert.Equal((Override(25), new ThresholdSetting(ThresholdKind.Cleared)), (thresholds!.Low, thresholds.High));

        // And after the grain restarts and replays its journal.
        await identity.RestartSiloAsync();
        Assert.Equal(thresholds, await identity.Sensor(id).GetThresholds(Ct));
    }

    [Fact]
    public async Task ASensorWhoseSpecificationCallsForCalibrationTakesWholePercent0To100WhateverItsRawRange()
    {
        var soil = await DeclaredAsync(Soil with { RangeMax = 10 });

        Assert.Equal(SensorThresholdsOutcome.Changed, (await identity.Sensor(soil).SetThresholds(new SetSensorThresholds(Override(0), Override(100)), Ct)).Outcome);
        Assert.Equal(SensorThresholdsOutcome.OutOfRange, (await identity.Sensor(soil).SetThresholds(new SetSensorThresholds(High: Override(101)), Ct)).Outcome);
        Assert.Equal(SensorThresholdsOutcome.OutOfRange, (await identity.Sensor(soil).SetThresholds(new SetSensorThresholds(Low: Override(-1)), Ct)).Outcome);
    }

    private async Task<Guid> DeclaredAsync(SensorSpecification specification)
    {
        var node = SimulatedDevice.Create(ProtocolKind.Node);
        var id = node.SensorId(0, specification.Quantity == CryptoSpec.SensorQuantitySoilMoisture ? Quantity.SoilMoisture : Quantity.AirTemperature);
        await identity.Sensor(id).Declare(new DeclareSensor(node.DeviceId.ToString(), 0, specification), Ct);
        return id;
    }
}
