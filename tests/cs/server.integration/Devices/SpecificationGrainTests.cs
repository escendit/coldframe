using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Sensors;
using Coldframe.Crypto;
using Coldframe.DeviceSimulator;
using Coldframe.Protocol.Device.V1;
using Coldframe.Server.Devices;
using Coldframe.Server.IntegrationTests.Identity;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using ProtocolKind = Coldframe.Protocol.Setup.V1.DeviceKind;

namespace Coldframe.Server.IntegrationTests.Devices;

/// <summary>
/// Sensor Specifications on a TestCluster with a fake clock (Story 4.6; AD-19): the Device grain asks for a
/// Node's Specification set when it does not know the frame's <c>spec_hash</c>, declares every Sensor of an
/// accepted set to its Sensor grain, and journals the hash and the Sensor list. Frames come from the Device
/// simulator only, which plays the Node: it sends the set once per request.
/// </summary>
[Collection(IngestSuites.Name)]
public sealed class SpecificationGrainTests(IdentityCluster identity) : IClassFixture<IdentityCluster>
{
    private const string HubA = "92064422c012f481";

    private const string Enrolled = "device.enrolled";
    private const string RelayChanged = "device.relay-changed";
    private const string SpecificationsDeclared = "device.specifications-declared";

    private static readonly string SiteId = Guid.CreateVersion7().ToString();

    private static readonly SensorSpecification Soil = new(CryptoSpec.SensorQuantitySoilMoisture, SensorUnit.RawCount, 0, 4095, true, 30, 80);
    private static readonly SensorSpecification Air = new(CryptoSpec.SensorQuantityAirTemperature, SensorUnit.MilliDegreeCelsius, -40_000, 85_000, false);
    private static readonly SensorSpecification Humidity = new(CryptoSpec.SensorQuantityRelativeHumidity, SensorUnit.MilliPercent, 0, 100_000, false);
    private static readonly SensorSpecification Gas = new(CryptoSpec.SensorQuantityGasResistance, SensorUnit.Ohm, 0, 100_000_000, false);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTimeOffset Now => identity.Time.GetUtcNow();

    [Fact]
    public async Task AnUnknownHashIsStoredAndAskedForAndNothingIsJournaledForSensors()
    {
        var node = await SeedNodeAsync();
        var frame = node.Wake(Now);
        Assert.Null(frame.Specifications);

        var first = await IngestAsync(node, node.SealFrame(frame));

        Assert.Equal(DeviceIngestStatus.Stored, first.Status);
        var downlink = node.OpenDownlink(SealedEnvelope.Parser.ParseFrom(first.Downlink));
        Assert.True(downlink.SpecificationsUnknown);
        Assert.Equal(SimulatedDevice.ReadingSeqs(frame), SimulatedDevice.AckedReadings(downlink));
        Assert.Equal(4L, await CountAsync("readings", node));

        // The Readings are stored under their derived Sensor IDs, and no Sensor exists for them yet.
        Assert.Equal(DefaultSensorIds(node), await StoredSensorIdsAsync(node));
        await AssertNoSensorAsync(DefaultSensorIds(node));
        Assert.Equal([Enrolled, RelayChanged], await identity.AliasesAsync(Stream(node)));

        // A resend is a duplicate as before, and is asked again.
        var resend = await IngestAsync(node, node.SealFrame(frame));
        Assert.Equal(DeviceIngestStatus.Duplicate, resend.Status);
        Assert.True(node.OpenDownlink(SealedEnvelope.Parser.ParseFrom(resend.Downlink)).SpecificationsUnknown);

        // A frame without a hash is never known.
        var bare = node.Wake(Now, attachSpecifications: false);
        bare.SpecHash = ByteString.Empty;
        Assert.True((await ReportAsync(node, bare)).SpecificationsUnknown);
        Assert.Equal([Enrolled, RelayChanged], await identity.AliasesAsync(Stream(node)));
    }

    [Fact]
    public async Task ADeclarationDeclaresEverySensorThenJournalsTheHashAndTheSensorList()
    {
        var node = await SeedNodeAsync();
        Assert.True((await ReportAsync(node, node.Wake(Now))).SpecificationsUnknown);
        var declaredAt = Now;
        var frame = node.Wake(declaredAt);
        Assert.Equal(SimulatedDevice.DefaultSpecifications(), frame.Specifications);

        var downlink = await ReportAsync(node, frame);

        Assert.False(downlink.SpecificationsUnknown);
        Assert.Equal(SimulatedDevice.ReadingSeqs(frame), SimulatedDevice.AckedReadings(downlink));
        Assert.Equal(8L, await CountAsync("readings", node));

        var ids = DefaultSensorIds(node);
        SensorSpecification[] specifications = [Soil, Air, Humidity, Gas];
        foreach (var (id, slot) in ids.Select((id, slot) => (id, slot)))
        {
            var only = Assert.Single(await identity.Store.ReadStreamAsync($"sensor/{id}", Ct));
            Assert.Equal(new SensorDeclared(node.DeviceId.ToString(), slot, specifications[slot], declaredAt), only.Data);
        }

        Assert.Equal([Enrolled, RelayChanged, SpecificationsDeclared], await identity.AliasesAsync(Stream(node)));
        var declared = await LastDeclarationAsync(node);
        Assert.Equal(node.SpecHash.ToByteArray(), declared.SpecHash);
        Assert.Equal(declaredAt, declared.DeclaredAt);
        Assert.Equal(
            [
                new DeclaredSensor(0, CryptoSpec.SensorQuantitySoilMoisture, ids[0]),
                new DeclaredSensor(1, CryptoSpec.SensorQuantityAirTemperature, ids[1]),
                new DeclaredSensor(2, CryptoSpec.SensorQuantityRelativeHumidity, ids[2]),
                new DeclaredSensor(3, CryptoSpec.SensorQuantityGasResistance, ids[3]),
            ],
            declared.Sensors);

        // Soil moisture is calibrated, in raw counts, with its default Thresholds of 30 and 80 percent.
        var soil = await identity.Sensor(ids[0]).Describe(Ct);
        Assert.Equal(
            new SensorSnapshot(
                ids[0].ToString(),
                node.DeviceId.ToString(),
                0,
                Soil,
                new ThresholdSetting(ThresholdKind.Default, 30),
                new ThresholdSetting(ThresholdKind.Default, 80)),
            soil);
        Assert.True(soil!.Specification.Calibration);
        Assert.Equal(SensorUnit.RawCount, soil.Specification.Unit);

        // The others are only watched: their unit and range, and both sides Default without a value.
        foreach (var slot in new[] { 1, 2, 3 })
        {
            Assert.Equal(
                new SensorSnapshot(ids[slot].ToString(), node.DeviceId.ToString(), slot, specifications[slot], ThresholdSetting.Default, ThresholdSetting.Default),
                await identity.Sensor(ids[slot]).Describe(Ct));
            Assert.False(specifications[slot].Calibration);
        }
    }

    [Fact]
    public async Task TheSameHashAgainJournalsNothingWithOrWithoutTheSet()
    {
        var node = await DeclaredNodeAsync();
        var device = await identity.AliasesAsync(Stream(node));

        var without = node.Wake(Now);
        Assert.Null(without.Specifications);
        Assert.False((await ReportAsync(node, without)).SpecificationsUnknown);

        var with = node.Wake(Now, attachSpecifications: true);
        Assert.NotNull(with.Specifications);
        Assert.False((await ReportAsync(node, with)).SpecificationsUnknown);

        // Known is known: even a set that breaks the contract is not looked at under the known hash.
        var broken = node.Wake(Now, attachSpecifications: true);
        broken.Specifications.Specifications.Clear();
        Assert.False((await ReportAsync(node, broken)).SpecificationsUnknown);

        Assert.Equal(device, await identity.AliasesAsync(Stream(node)));
        await AssertOnlyDeclaredAsync(DefaultSensorIds(node));
        Assert.Empty(InvalidSetWarnings(node));
    }

    [Fact]
    public async Task AChangedSpecificationChangesThatSensorOnlyAndTheDeviceJournalsTheNewHash()
    {
        var node = await DeclaredNodeAsync();
        var ids = DefaultSensorIds(node);
        var oldHash = node.SpecHash;

        // New firmware: slot 1 keeps its quantity, with another range and a default high Threshold.
        var changed = SimulatedDevice.DefaultSpecifications();
        changed.Specifications[1].RangeMax = 60_000;
        changed.Specifications[1].DefaultHigh = 45_000;
        node.Specifications = changed;
        Assert.NotEqual(oldHash, node.SpecHash);

        Assert.True((await ReportAsync(node, node.Wake(Now))).SpecificationsUnknown);
        Assert.Equal([Enrolled, RelayChanged, SpecificationsDeclared], await identity.AliasesAsync(Stream(node)));
        identity.Time.Advance(TimeSpan.FromMinutes(15));
        var changedAt = Now;
        Assert.False((await ReportAsync(node, node.Wake(changedAt))).SpecificationsUnknown);

        var air = Air with { RangeMax = 60_000, DefaultHigh = 45_000 };
        var events = await identity.Store.ReadStreamAsync($"sensor/{ids[1]}", Ct);
        Assert.Equal(["sensor.declared", "sensor.specification-changed"], await identity.AliasesAsync($"sensor/{ids[1]}"));
        Assert.Equal(new SensorSpecificationChanged(air, changedAt), events[1].Data);
        await AssertOnlyDeclaredAsync([ids[0], ids[2], ids[3]]);

        // The side follows the new default.
        var snapshot = await identity.Sensor(ids[1]).Describe(Ct);
        Assert.Equal((air, ThresholdSetting.Default, new ThresholdSetting(ThresholdKind.Default, 45_000)), (snapshot!.Specification, snapshot.Low, snapshot.High));

        Assert.Equal([Enrolled, RelayChanged, SpecificationsDeclared, SpecificationsDeclared], await identity.AliasesAsync(Stream(node)));
        var declared = await LastDeclarationAsync(node);
        Assert.Equal(node.SpecHash.ToByteArray(), declared.SpecHash);
        Assert.Equal(ids, declared.Sensors.Select(sensor => sensor.SensorId));

        // The old hash is no longer the known one: the Node that went back to it is asked again.
        node.Specifications = SimulatedDevice.DefaultSpecifications();
        Assert.True((await ReportAsync(node, node.Wake(Now, attachSpecifications: false))).SpecificationsUnknown);
    }

    [Fact]
    public async Task AnOverrideAndAClearedSideSurviveARedeclaration()
    {
        // Thresholds have no API yet (Story 5.3): the override is seeded as the event that will set it.
        var node = await SeedNodeAsync();
        var soilId = node.SensorId(0, Quantity.SoilMoisture);
        var airId = node.SensorId(1, Quantity.AirTemperature);
        Assert.True(await identity.Store.AppendAsync(
            $"sensor/{soilId}",
            0,
            [
                new SensorDeclared(node.DeviceId.ToString(), 0, Soil, Now),
                new SensorThresholdsChanged(new ThresholdSetting(ThresholdKind.Override, 40), ThresholdSetting.Default, Now),
            ],
            Ct));
        Assert.True(await identity.Store.AppendAsync(
            $"sensor/{airId}",
            0,
            [
                new SensorDeclared(node.DeviceId.ToString(), 1, Air, Now),
                new SensorThresholdsChanged(ThresholdSetting.Default, new ThresholdSetting(ThresholdKind.Cleared), Now),
            ],
            Ct));

        var before = await identity.Sensor(soilId).Describe(Ct);
        Assert.Equal((new ThresholdSetting(ThresholdKind.Override, 40), new ThresholdSetting(ThresholdKind.Default, 80)), (before!.Low, before.High));

        // Redeclared with soil defaults of 25 and 75, and a default high for the air temperature.
        var set = SimulatedDevice.DefaultSpecifications();
        set.Specifications[0].DefaultLow = 25;
        set.Specifications[0].DefaultHigh = 75;
        set.Specifications[1].DefaultLow = 2_000;
        set.Specifications[1].DefaultHigh = 35_000;
        node.Specifications = set;
        Assert.True((await ReportAsync(node, node.Wake(Now))).SpecificationsUnknown);
        Assert.False((await ReportAsync(node, node.Wake(Now))).SpecificationsUnknown);

        string[] redeclared = ["sensor.declared", "sensor.thresholds-changed", "sensor.specification-changed"];
        Assert.Equal(redeclared, await identity.AliasesAsync($"sensor/{soilId}"));
        Assert.Equal(redeclared, await identity.AliasesAsync($"sensor/{airId}"));

        // Low stays the override of 40; high stays Default and now reads 75.
        var soil = await identity.Sensor(soilId).Describe(Ct);
        Assert.Equal(Soil with { DefaultLow = 25, DefaultHigh = 75 }, soil!.Specification);
        Assert.Equal((new ThresholdSetting(ThresholdKind.Override, 40), new ThresholdSetting(ThresholdKind.Default, 75)), (soil.Low, soil.High));

        // High stays Cleared with no value, whatever the new default; low follows it.
        var air = await identity.Sensor(airId).Describe(Ct);
        Assert.Equal(35_000, air!.Specification.DefaultHigh);
        Assert.Equal((new ThresholdSetting(ThresholdKind.Default, 2_000), new ThresholdSetting(ThresholdKind.Cleared)), (air.Low, air.High));

        // The same holds after the grains were lost.
        await identity.RestartSiloAsync();
        Assert.Equal(soil, await identity.Sensor(soilId).Describe(Ct));
        Assert.Equal(air, await identity.Sensor(airId).Describe(Ct));
    }

    [Fact]
    public async Task ANewQuantityAtASlotIsANewSensorAndTheOldOneIsUntouched()
    {
        var node = await DeclaredNodeAsync();
        var old = DefaultSensorIds(node);

        // New hardware: slot 1 now measures humidity, and the Node has two Sensors.
        node.Specifications = new SpecificationSet
        {
            Specifications =
            {
                SimulatedDevice.DefaultSpecifications().Specifications[0],
                SimulatedDevice.DefaultSpecifications().Specifications[2],
            },
        };
        var humidityId = node.SensorId(1, Quantity.RelativeHumidity);
        Assert.DoesNotContain(humidityId, old);

        Assert.True((await ReportAsync(node, node.Wake(Now))).SpecificationsUnknown);
        Assert.False((await ReportAsync(node, node.Wake(Now))).SpecificationsUnknown);

        var declared = Assert.Single(await identity.Store.ReadStreamAsync($"sensor/{humidityId}", Ct));
        Assert.Equal(new SensorDeclared(node.DeviceId.ToString(), 1, Humidity, Now), declared.Data);
        await AssertOnlyDeclaredAsync(old);

        // The Device's Sensor list holds only the new set.
        Assert.Equal(
            [
                new DeclaredSensor(0, CryptoSpec.SensorQuantitySoilMoisture, old[0]),
                new DeclaredSensor(1, CryptoSpec.SensorQuantityRelativeHumidity, humidityId),
            ],
            (await LastDeclarationAsync(node)).Sensors);
    }

    [Fact]
    public async Task ReadingsOfAnUndeclaredSlotAreStoredAndTheLaterDeclarationUsesTheirSensorIds()
    {
        // Before any declaration.
        var node = await SeedNodeAsync();
        var early = node.Wake(Now);
        Assert.Equal(SimulatedDevice.ReadingSeqs(early), SimulatedDevice.AckedReadings(await ReportAsync(node, early)));
        var stored = await StoredSensorIdsAsync(node);
        Assert.Equal(DefaultSensorIds(node), stored);
        await AssertNoSensorAsync(stored);

        // The declaration arrives later: each Sensor is declared under the ID its stored rows carry.
        Assert.False((await ReportAsync(node, node.Wake(Now))).SpecificationsUnknown);
        await AssertOnlyDeclaredAsync(stored);
        Assert.Equal(stored, (await LastDeclarationAsync(node)).Sensors.Select(sensor => sensor.SensorId));

        // A slot beyond the set, and a declared slot with another quantity than the declared one.
        var beyond = node.SensorId(7, Quantity.SoilMoisture);
        var otherQuantity = node.SensorId(0, Quantity.AirTemperature);
        var stray = node.Wake(Now, readings: [new SimulatedReading(7, Quantity.SoilMoisture, 1_700), new SimulatedReading(0, Quantity.AirTemperature, 19_250)]);
        var result = await IngestAsync(node, node.SealFrame(stray));

        Assert.Equal(DeviceIngestStatus.Stored, result.Status);
        var downlink = node.OpenDownlink(SealedEnvelope.Parser.ParseFrom(result.Downlink));
        Assert.Equal(SimulatedDevice.ReadingSeqs(stray), SimulatedDevice.AckedReadings(downlink));
        Assert.False(downlink.SpecificationsUnknown);
        Assert.Equal(10L, await CountAsync("readings", node));
        Assert.Equal(1L, await identity.Database.ScalarAsync<long>("SELECT COUNT(*) FROM readings WHERE sensor_id = @id", ("id", beyond)));
        Assert.Equal(1L, await identity.Database.ScalarAsync<long>("SELECT COUNT(*) FROM readings WHERE sensor_id = @id", ("id", otherQuantity)));
        await AssertNoSensorAsync([beyond, otherQuantity]);
        Assert.Equal([Enrolled, RelayChanged, SpecificationsDeclared], await identity.AliasesAsync(Stream(node)));
    }

    [Fact]
    public async Task TheNodeSendsTheSetOncePerRequestAndALostFrameIsAskedForAgain()
    {
        var node = await SeedNodeAsync();
        Assert.False(node.SpecificationsRequested);
        Assert.True((await ReportAsync(node, node.Wake(Now))).SpecificationsUnknown);
        Assert.True(node.SpecificationsRequested);

        // The next wake attaches the set, the one after does not. The frame with the set is lost.
        var lost = node.Wake(Now);
        Assert.NotNull(lost.Specifications);
        Assert.False(node.SpecificationsRequested);
        var after = node.Wake(Now);
        Assert.Null(after.Specifications);
        Assert.Equal(node.SpecHash, after.SpecHash);

        // The Server still does not know the hash, so the next downlink asks again.
        Assert.True((await ReportAsync(node, after)).SpecificationsUnknown);
        await AssertNoSensorAsync(DefaultSensorIds(node));
        Assert.Equal([Enrolled, RelayChanged], await identity.AliasesAsync(Stream(node)));

        var again = node.Wake(Now);
        Assert.NotNull(again.Specifications);
        Assert.False((await ReportAsync(node, again)).SpecificationsUnknown);
        Assert.False(node.SpecificationsRequested);
        Assert.Null(node.Wake(Now).Specifications);
        await AssertOnlyDeclaredAsync(DefaultSensorIds(node));
    }

    [Fact]
    public async Task AnInvalidSetIsIgnoredWithOneWarningAndTheReadingsAreStored()
    {
        (string Name, Action<NodeFrame> Break)[] faults =
        [
            ("an empty hash", frame => frame.SpecHash = ByteString.Empty),
            ("a hash over 32 bytes", frame => frame.SpecHash = ByteString.CopyFrom(new byte[33])),
            ("no Specification", frame => frame.Specifications.Specifications.Clear()),
            ("33 Specifications", frame => frame.Specifications.Specifications.Add(Enumerable.Repeat(frame.Specifications.Specifications[1], 29))),
            ("an unknown quantity", frame => frame.Specifications.Specifications[3].Quantity = (Quantity)99),
            ("an unset quantity", frame => frame.Specifications.Specifications[3].Quantity = Quantity.Unspecified),
            ("an unknown unit", frame => frame.Specifications.Specifications[3].Unit = (Unit)99),
            ("an unset unit", frame => frame.Specifications.Specifications[3].Unit = Unit.Unspecified),
            ("an empty range", frame => frame.Specifications.Specifications[2].RangeMin = frame.Specifications.Specifications[2].RangeMax),
            ("a default low at the default high", frame => frame.Specifications.Specifications[0].DefaultLow = 80),
            ("a calibrating default above 100 percent", frame => frame.Specifications.Specifications[0].DefaultHigh = 101),
            ("a calibrating default below 0 percent", frame => frame.Specifications.Specifications[0].DefaultLow = -1),
            ("a default outside the range", frame => frame.Specifications.Specifications[1].DefaultHigh = 85_001),
        ];

        foreach (var (name, fault) in faults)
        {
            var node = await SeedNodeAsync();
            var frame = node.Wake(Now, attachSpecifications: true);
            fault(frame);

            var result = await IngestAsync(node, node.SealFrame(frame));

            Assert.True(result.Status == DeviceIngestStatus.Stored, $"A set with {name} was answered with {result.Status}.");
            var downlink = node.OpenDownlink(SealedEnvelope.Parser.ParseFrom(result.Downlink));
            Assert.True(downlink.SpecificationsUnknown, $"A set with {name} was not asked for again.");
            Assert.Equal(SimulatedDevice.ReadingSeqs(frame), SimulatedDevice.AckedReadings(downlink));
            Assert.Equal(4L, await CountAsync("readings", node));
            await AssertNoSensorAsync(DefaultSensorIds(node));
            Assert.Equal([Enrolled, RelayChanged], await identity.AliasesAsync(Stream(node)));

            // One warning, naming only the Device.
            var warning = Assert.Single(InvalidSetWarnings(node));
            Assert.Equal(LogLevel.Warning, warning.Level);
            Assert.Equal(
                $"Device {node.DeviceId} sent a Specification set that breaks the contract; it is ignored and asked for again.",
                warning.Message);
        }

        // The fault is fixed: the same Node declares with its next frame.
        var repaired = await SeedNodeAsync();
        var bad = repaired.Wake(Now, attachSpecifications: true);
        bad.Specifications.Specifications[0].DefaultLow = 90;
        Assert.True((await ReportAsync(repaired, bad)).SpecificationsUnknown);
        Assert.False((await ReportAsync(repaired, repaired.Wake(Now))).SpecificationsUnknown);
        await AssertOnlyDeclaredAsync(DefaultSensorIds(repaired));
        Assert.Single(InvalidSetWarnings(repaired));
    }

    [Fact]
    public async Task ADeclarationThatFailsIsRetryAndLeavesTheHashUnknownUntilItSucceeds()
    {
        var node = await SeedNodeAsync();
        var ids = DefaultSensorIds(node);
        Guid[] healthy = [ids[0], ids[1], ids[3]];
        Assert.True((await ReportAsync(node, node.Wake(Now))).SpecificationsUnknown);

        // The humidity Sensor cannot be declared: every Declare call on its grain throws.
        identity.SensorFaults.FailDeclarations(ids[2]);

        var frame = node.Wake(Now);
        Assert.NotNull(frame.Specifications);
        var failed = await IngestAsync(node, node.SealFrame(frame));

        // No acknowledgement and no accepted set, but the frame's Readings are committed.
        Assert.Equal(DeviceIngestStatus.Retry, failed.Status);
        Assert.Null(failed.Downlink);
        Assert.Equal([Enrolled, RelayChanged], await identity.AliasesAsync(Stream(node)));
        Assert.Equal(8L, await CountAsync("readings", node));

        // The Sensor grains come first: the ones that could be declared were, before the Device journaled anything.
        await AssertOnlyDeclaredAsync(healthy);
        await AssertNoSensorAsync([ids[2]]);

        // The hash stays unknown: the next frame is asked again.
        Assert.True((await ReportAsync(node, node.Wake(Now, attachSpecifications: false))).SpecificationsUnknown);
        Assert.Equal([Enrolled, RelayChanged], await identity.AliasesAsync(Stream(node)));

        // The fault is removed: the set the Node sends next is accepted, and nothing is declared twice.
        identity.SensorFaults.Restore(ids[2]);
        var again = node.Wake(Now);
        Assert.NotNull(again.Specifications);
        Assert.False((await ReportAsync(node, again)).SpecificationsUnknown);

        Assert.Equal([Enrolled, RelayChanged, SpecificationsDeclared], await identity.AliasesAsync(Stream(node)));
        Assert.Equal(ids, (await LastDeclarationAsync(node)).Sensors.Select(sensor => sensor.SensorId));
        await AssertOnlyDeclaredAsync(ids);
    }

    [Fact]
    public async Task APausedNodeDeclaresItsSensorsAndStoresNoReading()
    {
        var node = await SeedNodeAsync(new DevicePaused(DevicePauseSource.Device, null, Now));

        Assert.True((await ReportAsync(node, node.Wake(Now))).SpecificationsUnknown);
        var frame = node.Wake(Now);
        var downlink = await ReportAsync(node, frame);

        Assert.False(downlink.SpecificationsUnknown);
        Assert.Equal(SimulatedDevice.ReadingSeqs(frame), SimulatedDevice.AckedReadings(downlink));
        await AssertOnlyDeclaredAsync(DefaultSensorIds(node));
        Assert.Equal([Enrolled, "device.paused", RelayChanged, SpecificationsDeclared], await identity.AliasesAsync(Stream(node)));
        Assert.Equal(0L, await CountAsync("readings", node));
        Assert.Equal(0L, await CountAsync("device_reports", node));
    }

    [Fact]
    public async Task AfterARestartTheSameHashIsStillKnownAndNothingIsJournaled()
    {
        var node = await DeclaredNodeAsync();
        var device = await identity.AliasesAsync(Stream(node));
        Assert.Equal([Enrolled, RelayChanged, SpecificationsDeclared], device);

        await identity.RestartSiloAsync();

        Assert.False((await ReportAsync(node, node.Wake(Now))).SpecificationsUnknown);
        Assert.False((await ReportAsync(node, node.Wake(Now, attachSpecifications: true))).SpecificationsUnknown);
        Assert.Equal(device, await identity.AliasesAsync(Stream(node)));
        await AssertOnlyDeclaredAsync(DefaultSensorIds(node));
    }

    [Fact]
    public async Task ADeclarationThatDoesNotDeriveTheSensorIdIsAnArgumentErrorAndJournalsNothing()
    {
        var node = SimulatedDevice.Create(ProtocolKind.Node);
        var other = SimulatedDevice.Create(ProtocolKind.Node);
        var deviceId = node.DeviceId.ToString();
        var soilId = node.SensorId(0, Quantity.SoilMoisture);

        DeclareSensor[] wrong =
        [
            new(deviceId, 1, Soil),
            new(deviceId, -1, Soil),
            new(deviceId, 0, Air),
            new(other.DeviceId.ToString(), 0, Soil),
            new("not-a-device", 0, Soil),
        ];
        foreach (var request in wrong)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => identity.Sensor(soilId).Declare(request, Ct));
        }

        var stranger = Guid.NewGuid();
        await Assert.ThrowsAsync<ArgumentException>(() => identity.Sensor(stranger).Declare(new DeclareSensor(deviceId, 0, Soil), Ct));

        await AssertNoSensorAsync([soilId, stranger]);
        Assert.Null(await identity.Sensor(soilId).Describe(Ct));

        // The request that derives the ID declares; the same one again journals nothing.
        var request0 = new DeclareSensor(deviceId, 0, Soil);
        Assert.Equal(SensorDeclarationOutcome.Declared, (await identity.Sensor(soilId).Declare(request0, Ct)).Outcome);
        Assert.Equal(SensorDeclarationOutcome.Unchanged, (await identity.Sensor(soilId).Declare(request0, Ct)).Outcome);
        Assert.Equal(
            SensorDeclarationOutcome.SpecificationChanged,
            (await identity.Sensor(soilId).Declare(request0 with { Specification = Soil with { RangeMax = 8191 } }, Ct)).Outcome);
        Assert.Equal(["sensor.declared", "sensor.specification-changed"], await identity.AliasesAsync($"sensor/{soilId}"));
    }

    private static string Stream(SimulatedDevice node) => $"device/{node.DeviceId}";

    private static List<Guid> DefaultSensorIds(SimulatedDevice node) =>
        [.. SimulatedDevice.DefaultReadings.Select(reading => node.SensorId(reading.Slot, reading.Quantity))];

    private List<CapturedLog> InvalidSetWarnings(SimulatedDevice node) =>
    [
        .. identity.Logs.Records.Where(record =>
            record.Category == typeof(DeviceGrain).FullName
            && record.EventId.Id == 2
            && record.Message.Contains(node.DeviceId.ToString(), StringComparison.Ordinal)),
    ];

    private async Task AssertNoSensorAsync(IEnumerable<Guid> sensorIds)
    {
        foreach (var id in sensorIds)
        {
            Assert.Empty(await identity.AliasesAsync($"sensor/{id}"));
        }
    }

    private async Task AssertOnlyDeclaredAsync(IEnumerable<Guid> sensorIds)
    {
        foreach (var id in sensorIds)
        {
            Assert.Equal(["sensor.declared"], await identity.AliasesAsync($"sensor/{id}"));
        }
    }

    private async Task<DeviceSpecificationsDeclared> LastDeclarationAsync(SimulatedDevice node) =>
        Assert.IsType<DeviceSpecificationsDeclared>((await identity.Store.ReadStreamAsync(Stream(node), Ct))[^1].Data);

    // The distinct Sensor IDs of the Node's stored Readings, in slot order.
    private async Task<List<Guid>> StoredSensorIdsAsync(SimulatedDevice node)
    {
        var ids = new List<Guid>();
        await using var command = identity.Database.DataSource.CreateCommand(
            "SELECT sensor_id FROM readings WHERE device_id = @id GROUP BY sensor_id, slot ORDER BY slot");
        command.Parameters.AddWithValue("id", node.DeviceId.ToString());
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }

    // A Node that followed the downlinks: its first frame was asked for the set, its second declared it.
    private async Task<SimulatedDevice> DeclaredNodeAsync()
    {
        var node = await SeedNodeAsync();
        Assert.True((await ReportAsync(node, node.Wake(Now))).SpecificationsUnknown);
        Assert.False((await ReportAsync(node, node.Wake(Now))).SpecificationsUnknown);
        return node;
    }

    // An enrolled Node, seeded as its journal events.
    private async Task<SimulatedDevice> SeedNodeAsync(params object[] more)
    {
        var node = SimulatedDevice.Create(ProtocolKind.Node);
        var enrolled = new DeviceEnrolled(SiteId, DeviceKind.Node, identity.Vault.Wrap(node.DeviceId, node.Keys.DeviceKey), Now);

        Assert.True(await identity.Store.AppendAsync(Stream(node), 0, [enrolled, .. more], Ct));
        return node;
    }

    // Ingests a frame that is stored and acknowledged, and lets the Node open the downlink.
    private async Task<Downlink> ReportAsync(SimulatedDevice node, NodeFrame frame)
    {
        var result = await IngestAsync(node, node.SealFrame(frame));

        Assert.Equal(DeviceIngestStatus.Stored, result.Status);
        return node.OpenDownlink(SealedEnvelope.Parser.ParseFrom(result.Downlink));
    }

    private Task<DeviceIngestResult> IngestAsync(SimulatedDevice node, SealedEnvelope envelope) =>
        identity.Device(node.DeviceId.ToString()).Ingest(
            new DeviceIngest(envelope.ProtocolVersion, envelope.Counter, envelope.Ciphertext.ToByteArray(), HubA),
            Ct);

    private async Task<long> CountAsync(string table, SimulatedDevice device) =>
        await identity.Database.ScalarAsync<long>($"SELECT COUNT(*) FROM {table} WHERE device_id = @id", ("id", device.DeviceId.ToString()));
}
