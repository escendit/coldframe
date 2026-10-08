using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Lots;
using Coldframe.Contracts.Sensors;
using Coldframe.DeviceSimulator;
using Coldframe.Protocol.Device.V1;
using Coldframe.Server.Devices;
using Coldframe.Server.IntegrationTests.Identity;
using Coldframe.Server.IntegrationTests.Journal;
using Coldframe.Server.Journal;
using Coldframe.Server.Lots;
using Coldframe.Server.Sensors;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.Timers;
using ProtocolKind = Coldframe.Protocol.Setup.V1.DeviceKind;

namespace Coldframe.Server.IntegrationTests.Devices;

/// <summary>
/// Calibration on a TestCluster with a fake clock (Story 5.1; AD-9): the Sensor grain is the only validator and
/// writer, it sets the Calibration in force on the Device grain before it confirms and delivers it again from
/// persisted state when that fails, later Readings carry the Calibration ID, and the Lot leaves needs calibration.
/// Frames come from the Device simulator only.
/// </summary>
[Collection(IngestSuites.Name)]
public sealed class CalibrationGrainTests(IdentityCluster identity) : IClassFixture<IdentityCluster>
{
    private const string HubA = "92064422c012f481";

    // The two wakes that declare the Sensors store a soil Reading each, before any test Reading.
    private const int SetupReadings = 2;

    private static readonly string SiteId = Guid.CreateVersion7().ToString();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTimeOffset Now => identity.Time.GetUtcNow();

    [Fact]
    public async Task BothPointsJournalACalibrationAndSetItInForceOnTheDeviceBeforeTheCallConfirms()
    {
        var (node, soil) = await DeclaredNodeAsync();
        var dry = await StoreSoilAsync(node, 3000);
        var wet = await StoreSoilAsync(node, 1200);

        var result = await identity.Sensor(soil).Calibrate(new CalibrateSensor(new CalibrationPointRef(dry), new CalibrationPointRef(wet)), Ct);

        Assert.Equal(SensorCalibrationOutcome.Calibrated, result.Outcome);
        var calibration = Assert.IsType<SensorCalibration>(result.Calibration);
        Assert.NotEqual(Guid.Empty, calibration.Id);
        Assert.Equal((1, 3000L, 1200L), (calibration.Revision, calibration.DryRaw, calibration.WetRaw));
        Assert.Equal(Now, calibration.CalibratedAt);

        // The Sensor journaled the Calibration with both raw values, and then that the Device acknowledged it.
        var events = await identity.Store.ReadStreamAsync($"sensor/{soil}", Ct);
        Assert.Equal(["sensor.declared", "sensor.calibrated", "sensor.calibration-delivered"], await identity.AliasesAsync($"sensor/{soil}"));
        Assert.Equal(new SensorCalibrated(calibration.Id, 3000, 1200, Now), events[1].Data);
        Assert.Equal(new SensorCalibrationDelivered(calibration.Id, Now), events[2].Data);

        // The Device grain holds it, as a cache: it journaled only the Sensor and the Calibration ID.
        Assert.Equal(new DeviceCalibrationSet(soil, calibration.Id, 1, Now), (await identity.Store.ReadStreamAsync($"device/{node.DeviceId}", Ct))[^1].Data);
        Assert.Equal(calibration, (await identity.Sensor(soil).Describe(Ct))!.Calibration);
    }

    [Fact]
    public async Task TheRecentReadingsAreTheSensorsOwnNewestFirstAndBoundedBySince()
    {
        var (node, soil) = await DeclaredNodeAsync();
        var first = await StoreSoilAsync(node, 3000);
        var second = await StoreSoilAsync(node, 1200);
        var secondAt = Now;
        var third = await StoreSoilAsync(node, 2000);
        var readings = identity.SiloServices.GetRequiredService<SensorReadings>();

        var all = await readings.RecentAsync(soil, Now.AddHours(-1), 50, Ct);

        // The two setup Readings of the declaring wakes come first, then ours, newest first; the Sensor's own only.
        Assert.Equal([third, second, first], all.Take(3).Select(reading => reading.ReadingSeq));
        Assert.Equal([2000L, 1200L, 3000L], all.Take(3).Select(reading => reading.RawValue));

        // Bounded by time and by count.
        Assert.Equal([third], (await readings.RecentAsync(soil, secondAt.AddMilliseconds(1), 50, Ct)).Select(reading => reading.ReadingSeq));
        Assert.Equal([third, second], (await readings.RecentAsync(soil, Now.AddHours(-1), 2, Ct)).Select(reading => reading.ReadingSeq));
    }

    [Fact]
    public async Task ADryPointAloneIsKeptAndTheWetOneCompletesItInEitherOrder()
    {
        var (node, soil) = await DeclaredNodeAsync();
        var dry = await StoreSoilAsync(node, 3000);
        var wet = await StoreSoilAsync(node, 1200);

        var first = await identity.Sensor(soil).Calibrate(new CalibrateSensor(Dry: new CalibrationPointRef(dry)), Ct);

        Assert.Equal(SensorCalibrationOutcome.PointRecorded, first.Outcome);
        Assert.Null(first.Calibration);
        Assert.Equal((3000L, (long?)null), (first.PendingDryRaw, first.PendingWetRaw));
        Assert.Equal(["sensor.declared", "sensor.calibration-point-recorded"], await identity.AliasesAsync($"sensor/{soil}"));

        // Still uncalibrated: the Device holds nothing, and a Reading is stored without a Calibration.
        Assert.Null((await identity.Sensor(soil).Describe(Ct))!.Calibration);
        await StoreSoilAsync(node, 2000);
        Assert.Null(await CalibrationOfAsync(soil, "reading_seq DESC"));

        // The kept point survives the grain: the silo restarts, the wet point completes the pair.
        await identity.RestartSiloAsync();
        Assert.Equal(3000L, (await identity.Sensor(soil).Describe(Ct))!.PendingDryRaw);

        var second = await identity.Sensor(soil).Calibrate(new CalibrateSensor(Wet: new CalibrationPointRef(wet)), Ct);

        Assert.Equal(SensorCalibrationOutcome.Calibrated, second.Outcome);
        Assert.Equal((3000L, 1200L), (second.Calibration!.DryRaw, second.Calibration.WetRaw));
        Assert.Equal((null, null), (second.PendingDryRaw, second.PendingWetRaw));
    }

    [Fact]
    public async Task AWetPointFirstWorksToo()
    {
        var (node, soil) = await DeclaredNodeAsync();
        var wet = await StoreSoilAsync(node, 1100);
        var dry = await StoreSoilAsync(node, 3100);

        Assert.Equal(
            SensorCalibrationOutcome.PointRecorded,
            (await identity.Sensor(soil).Calibrate(new CalibrateSensor(Wet: new CalibrationPointRef(wet)), Ct)).Outcome);
        var done = await identity.Sensor(soil).Calibrate(new CalibrateSensor(Dry: new CalibrationPointRef(dry)), Ct);

        Assert.Equal(SensorCalibrationOutcome.Calibrated, done.Outcome);
        Assert.Equal((3100L, 1100L), (done.Calibration!.DryRaw, done.Calibration.WetRaw));
    }

    [Fact]
    public async Task ADryLowWetHighProbeIsCalibratedToo()
    {
        var (node, soil) = await DeclaredNodeAsync();
        var dry = await StoreSoilAsync(node, 200);
        var wet = await StoreSoilAsync(node, 2000);

        var result = await identity.Sensor(soil).Calibrate(new CalibrateSensor(new CalibrationPointRef(dry), new CalibrationPointRef(wet)), Ct);

        Assert.Equal(SensorCalibrationOutcome.Calibrated, result.Outcome);
        Assert.Equal((200L, 2000L), (result.Calibration!.DryRaw, result.Calibration.WetRaw));
    }

    [Fact]
    public async Task LaterReadingsCarryTheCalibrationIdAndAreAPercentageRoundedToFiveWhileStoredOnesKeepTheirs()
    {
        var (node, soil) = await DeclaredNodeAsync();
        await StoreSoilAsync(node, 2500);
        var dry = await StoreSoilAsync(node, 3000);
        var wet = await StoreSoilAsync(node, 1200);

        var first = (await identity.Sensor(soil).Calibrate(new CalibrateSensor(new CalibrationPointRef(dry), new CalibrationPointRef(wet)), Ct)).Calibration!;

        // The Readings stored before the Calibration have none; the next one is stamped with its ID.
        await StoreSoilAsync(node, 2100);
        Assert.Equal(new Guid?[] { null, null, null, first.Id }, (await CalibrationIdsAsync(soil)).Skip(SetupReadings));

        // Recalibration with new points: only later Readings use the new Calibration.
        var newDry = await StoreSoilAsync(node, 3500);
        var newWet = await StoreSoilAsync(node, 1100);
        var second = (await identity.Sensor(soil).Calibrate(new CalibrateSensor(new CalibrationPointRef(newDry), new CalibrationPointRef(newWet)), Ct)).Calibration!;
        await StoreSoilAsync(node, 2100);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, second.Revision);
        Assert.Equal(
            new Guid?[] { null, null, null, first.Id, first.Id, first.Id, second.Id },
            (await CalibrationIdsAsync(soil)).Skip(SetupReadings));
        Assert.Equal(new DeviceCalibrationSet(soil, second.Id, 2, second.CalibratedAt), (await identity.Store.ReadStreamAsync($"device/{node.DeviceId}", Ct))[^1].Data);
        Assert.Equal(
            ["sensor.declared", "sensor.calibrated", "sensor.calibration-delivered", "sensor.calibrated", "sensor.calibration-delivered"],
            await identity.AliasesAsync($"sensor/{soil}"));

        // The same raw value reads what the Calibration it was stored under says, rounded to the nearest 5.
        var detail = identity.SiloServices.GetRequiredService<LotDetailReadModel>();
        var newest = Assert.Single(await detail.LatestReadingsAsync(node.DeviceId.ToString(), Now.AddDays(-1), Ct), reading => reading.Slot == 0);
        Assert.Equal(2100, newest.RawValue);
        Assert.Equal(new CalibrationPoints(3500, 1100), newest.Calibration);
        Assert.Equal((60d, "%"), SensorConversion.Convert("soil_moisture", newest.RawValue, newest.Calibration)!.Value);
        Assert.Equal((50d, "%"), SensorConversion.Convert("soil_moisture", 2100, new CalibrationPoints(first.DryRaw, first.WetRaw))!.Value);
    }

    [Fact]
    public async Task IndistinctPointsAreRefusedAndNothingIsPersisted()
    {
        var (node, soil) = await DeclaredNodeAsync();
        var a = await StoreSoilAsync(node, 1500);
        var b = await StoreSoilAsync(node, 1500);
        var close = await StoreSoilAsync(node, 1515);
        var far = await StoreSoilAsync(node, 1516);

        foreach (var (dry, wet) in new[] { (a, b), (a, close), (close, a) })
        {
            var result = await identity.Sensor(soil).Calibrate(new CalibrateSensor(new CalibrationPointRef(dry), new CalibrationPointRef(wet)), Ct);

            Assert.Equal(SensorCalibrationOutcome.IndistinctPoints, result.Outcome);
        }

        // A kept point meets a new one that is too close: refused, and the kept point is still the only one.
        Assert.Equal(SensorCalibrationOutcome.PointRecorded, (await identity.Sensor(soil).Calibrate(new CalibrateSensor(Dry: new CalibrationPointRef(a)), Ct)).Outcome);
        Assert.Equal(SensorCalibrationOutcome.IndistinctPoints, (await identity.Sensor(soil).Calibrate(new CalibrateSensor(Wet: new CalibrationPointRef(b)), Ct)).Outcome);
        Assert.Equal(["sensor.declared", "sensor.calibration-point-recorded"], await identity.AliasesAsync($"sensor/{soil}"));
        Assert.Equal(1500L, (await identity.Sensor(soil).Describe(Ct))!.PendingDryRaw);

        // 16 counts apart is the smallest span that tells them apart.
        Assert.Equal(SensorCalibrationOutcome.Calibrated, (await identity.Sensor(soil).Calibrate(new CalibrateSensor(Wet: new CalibrationPointRef(far)), Ct)).Outcome);
    }

    [Fact]
    public async Task AnUnknownReadingASensorWithoutCalibrationAnUnknownSensorAndAnEmptyRequestAreRefused()
    {
        var (node, soil) = await DeclaredNodeAsync();
        var stored = await StoreSoilAsync(node, 3000);
        var airId = node.SensorId(1, Quantity.AirTemperature);
        var airSeq = await AirSeqAsync(airId);

        Assert.Equal(
            SensorCalibrationOutcome.UnknownReading,
            (await identity.Sensor(soil).Calibrate(new CalibrateSensor(Dry: new CalibrationPointRef(stored + 1000)), Ct)).Outcome);

        // The soil Sensor's Reading is no Reading of another Sensor, and a Reading is not found by its seq alone.
        Assert.Equal(
            SensorCalibrationOutcome.NotCalibratable,
            (await identity.Sensor(airId).Calibrate(new CalibrateSensor(Dry: new CalibrationPointRef(airSeq)), Ct)).Outcome);
        Assert.Equal(
            SensorCalibrationOutcome.NotCalibratable,
            (await identity.Sensor(Guid.NewGuid()).Calibrate(new CalibrateSensor(Dry: new CalibrationPointRef(stored)), Ct)).Outcome);
        Assert.Equal(SensorCalibrationOutcome.NoPoint, (await identity.Sensor(soil).Calibrate(new CalibrateSensor(), Ct)).Outcome);

        Assert.Equal(["sensor.declared"], await identity.AliasesAsync($"sensor/{soil}"));
        Assert.Equal(["sensor.declared"], await identity.AliasesAsync($"sensor/{airId}"));
    }

    [Fact]
    public async Task AFailingDeviceCallKeepsTheCalibrationAndItIsDeliveredAgainUntilTheDeviceHoldsIt()
    {
        var (node, soil) = await DeclaredNodeAsync();
        var deviceId = node.DeviceId.ToString();
        var dry = await StoreSoilAsync(node, 3000);
        var wet = await StoreSoilAsync(node, 1200);
        identity.DeviceFaults.FailCalibrations(deviceId);

        var result = await identity.Sensor(soil).Calibrate(new CalibrateSensor(new CalibrationPointRef(dry), new CalibrationPointRef(wet)), Ct);

        // Persisted, but not confirmed: the Device holds nothing, so a Reading is still stored without it.
        Assert.Equal(SensorCalibrationOutcome.NotDelivered, result.Outcome);
        Assert.NotNull(result.Calibration);
        Assert.Equal(["sensor.declared", "sensor.calibrated"], await identity.AliasesAsync($"sensor/{soil}"));
        await StoreSoilAsync(node, 2000);
        Assert.Null(await CalibrationOfAsync(soil, "reading_seq DESC"));

        // The Device recovers: the Sensor delivers the persisted Calibration, with nobody calling it.
        identity.DeviceFaults.Restore(deviceId);
        await JournalWait.UntilAsync(
            async () => (await identity.AliasesAsync($"sensor/{soil}")).Contains("sensor.calibration-delivered", StringComparer.Ordinal),
            "the Sensor delivered its Calibration again");

        Assert.Equal(new DeviceCalibrationSet(soil, result.Calibration.Id, 1, Now), (await identity.Store.ReadStreamAsync($"device/{node.DeviceId}", Ct))[^1].Data);
        await StoreSoilAsync(node, 2100);
        Assert.Equal(result.Calibration.Id, await CalibrationOfAsync(soil, "reading_seq DESC"));

        // The same request again is the Calibration in force: no new ID, nothing journaled.
        var again = await identity.Sensor(soil).Calibrate(new CalibrateSensor(new CalibrationPointRef(dry), new CalibrationPointRef(wet)), Ct);
        Assert.Equal(SensorCalibrationOutcome.Calibrated, again.Outcome);
        Assert.Equal(result.Calibration.Id, again.Calibration!.Id);
        Assert.Equal(["sensor.declared", "sensor.calibrated", "sensor.calibration-delivered"], await identity.AliasesAsync($"sensor/{soil}"));
    }

    [Fact]
    public async Task TheDeliveryReminderExistsWhileTheCalibrationIsUndeliveredAndIsGoneOnceTheDeviceAcknowledges()
    {
        var (node, soil) = await DeclaredNodeAsync();
        var dry = await StoreSoilAsync(node, 3000);
        var wet = await StoreSoilAsync(node, 1200);
        identity.DeviceFaults.FailCalibrations(node.DeviceId.ToString());

        var result = await identity.Sensor(soil).Calibrate(new CalibrateSensor(new CalibrationPointRef(dry), new CalibrationPointRef(wet)), Ct);

        Assert.Equal(SensorCalibrationOutcome.NotDelivered, result.Outcome);
        Assert.NotNull(await ReminderAsync(soil));

        identity.DeviceFaults.Restore(node.DeviceId.ToString());
        await JournalWait.UntilAsync(
            async () => (await identity.AliasesAsync($"sensor/{soil}")).Contains("sensor.calibration-delivered", StringComparer.Ordinal),
            "the Sensor delivered its Calibration");
        await JournalWait.UntilAsync(async () => await ReminderAsync(soil) is null, "the delivery reminder is gone");
    }

    [Fact]
    public async Task APersistedCalibrationIsDeliveredAfterASiloRestart()
    {
        var (node, soil) = await DeclaredNodeAsync();
        var dry = await StoreSoilAsync(node, 3000);
        var wet = await StoreSoilAsync(node, 1200);
        identity.DeviceFaults.FailCalibrations(node.DeviceId.ToString());

        var result = await identity.Sensor(soil).Calibrate(new CalibrateSensor(new CalibrationPointRef(dry), new CalibrationPointRef(wet)), Ct);
        Assert.Equal(SensorCalibrationOutcome.NotDelivered, result.Outcome);

        // Every activation and everything held in memory is gone; the new silo has no fault.
        await identity.RestartSiloAsync();

        // The first call activates the grain, which replays its stream and delivers what is still undelivered.
        var snapshot = await identity.Sensor(soil).Describe(Ct);
        Assert.Equal(result.Calibration, snapshot!.Calibration);
        await JournalWait.UntilAsync(
            async () => (await identity.AliasesAsync($"sensor/{soil}")).Contains("sensor.calibration-delivered", StringComparer.Ordinal),
            "the Sensor delivered its Calibration after the restart");

        Assert.Equal(new DeviceCalibrationSet(soil, result.Calibration!.Id, 1, Now), (await identity.Store.ReadStreamAsync($"device/{node.DeviceId}", Ct))[^1].Data);
    }

    [Fact]
    public async Task TheDeviceGrainSetsACalibrationOnceAndNeverGoesBackToAnOlderOne()
    {
        var (node, soil) = await DeclaredNodeAsync();
        var device = identity.Device(node.DeviceId.ToString());
        var one = Guid.CreateVersion7();
        var two = Guid.CreateVersion7();

        await device.SetCalibration(new SetCalibration(soil, one, 1), Ct);
        await device.SetCalibration(new SetCalibration(soil, one, 1), Ct);
        await device.SetCalibration(new SetCalibration(soil, two, 2), Ct);
        await device.SetCalibration(new SetCalibration(soil, one, 1), Ct);

        var aliases = await identity.AliasesAsync($"device/{node.DeviceId}");
        Assert.Equal(2, aliases.Count(alias => alias == "device.calibration-set"));
        await StoreSoilAsync(node, 2000);
        Assert.Equal(two, await CalibrationOfAsync(soil, "reading_seq DESC"));
    }

    [Fact]
    public async Task OnlyAnEnrolledNodeHoldsACalibration()
    {
        var hub = SimulatedDevice.Create(ProtocolKind.Hub);
        await identity.Store.AppendAsync(
            $"device/{hub.DeviceId}",
            0,
            [new DeviceEnrolled(SiteId, DeviceKind.Hub, identity.Vault.Wrap(hub.DeviceId, hub.Keys.DeviceKey), Now)],
            Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            identity.Device(hub.DeviceId.ToString()).SetCalibration(new SetCalibration(Guid.NewGuid(), Guid.NewGuid(), 1), Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            identity.Device("00000000000000aa").SetCalibration(new SetCalibration(Guid.NewGuid(), Guid.NewGuid(), 1), Ct));
    }

    [Fact]
    public async Task SavingTheCalibrationMovesTheLotOutOfNeedsCalibrationAndItsPercentageAppearsWithTheNextReading()
    {
        var (node, soil) = await DeclaredNodeAsync();
        var lotId = Guid.CreateVersion7().ToString();
        Assert.True(await identity.Store.AppendAsync($"lot/{lotId}", 0, [new LotCreated(SiteId, "Tomatoes"), new LotClaimed(node.DeviceId.ToString())], Ct));
        await identity.SiloServices.GetProjectionRunner<LotsProjector>().CatchUpAsync(Ct);

        Assert.Equal("needsCalibration", (await identity.Lots.FindLotAsync(SiteId, lotId, Ct))!.Status);

        var dry = await StoreSoilAsync(node, 3000);
        var wet = await StoreSoilAsync(node, 1200);
        await identity.Sensor(soil).Calibrate(new CalibrateSensor(new CalibrationPointRef(dry), new CalibrationPointRef(wet)), Ct);

        // At once, without a poll: the Sensor grain brought the projection up to date before it confirmed.
        var lot = (await identity.Lots.FindLotAsync(SiteId, lotId, Ct))!;
        Assert.Equal("ok", lot.Status);
        Assert.Equal(Now, lot.StatusSince);

        // No percentage before the next Reading is stored: the newest stored Reading is raw.
        var detail = identity.SiloServices.GetRequiredService<LotDetailReadModel>();
        var before = Assert.Single(await detail.LatestReadingsAsync(node.DeviceId.ToString(), Now.AddDays(-1), Ct), reading => reading.Slot == 0);
        Assert.Null(before.Calibration);
        Assert.Equal((1200d, "raw"), SensorConversion.Convert("soil_moisture", before.RawValue, before.Calibration)!.Value);

        await StoreSoilAsync(node, 2100);
        var after = Assert.Single(await detail.LatestReadingsAsync(node.DeviceId.ToString(), Now.AddDays(-1), Ct), reading => reading.Slot == 0);
        Assert.Equal((50d, "%"), SensorConversion.Convert("soil_moisture", after.RawValue, after.Calibration)!.Value);
    }

    // An enrolled Node that followed the downlinks, so its four Sensors are declared; the soil Sensor's ID.
    private async Task<(SimulatedDevice Node, Guid Soil)> DeclaredNodeAsync()
    {
        var node = SimulatedDevice.Create(ProtocolKind.Node);
        var enrolled = new DeviceEnrolled(SiteId, DeviceKind.Node, identity.Vault.Wrap(node.DeviceId, node.Keys.DeviceKey), Now);
        Assert.True(await identity.Store.AppendAsync($"device/{node.DeviceId}", 0, [enrolled], Ct));

        Assert.True((await ReportAsync(node, node.Wake(Now))).SpecificationsUnknown);
        Assert.False((await ReportAsync(node, node.Wake(Now))).SpecificationsUnknown);

        return (node, node.SensorId(0, Quantity.SoilMoisture));
    }

    // Stores one soil-moisture Reading (the others of the wake too) and returns its reading_seq.
    private async Task<ulong> StoreSoilAsync(SimulatedDevice node, long raw)
    {
        // Each Reading is a second later than the last, so "the newest" is never a tie.
        identity.Time.Advance(TimeSpan.FromSeconds(1));
        var frame = node.Wake(
            Now,
            readings:
            [
                new SimulatedReading(0, Quantity.SoilMoisture, raw),
                new SimulatedReading(1, Quantity.AirTemperature, 21_500),
            ]);
        await ReportAsync(node, frame);

        return frame.Readings.Single(reading => reading.Slot == 0).ReadingSeq;
    }

    private async Task<ulong> AirSeqAsync(Guid airId) =>
        (ulong)await identity.Database.ScalarAsync<decimal>(
            "SELECT MAX(reading_seq) FROM readings WHERE sensor_id = @id",
            ("id", airId));

    // The Calibration ID of the Sensor's stored Readings in the order they were stored.
    private async Task<List<Guid?>> CalibrationIdsAsync(Guid sensorId)
    {
        var ids = new List<Guid?>();
        await using var command = identity.Database.DataSource.CreateCommand(
            "SELECT calibration_id FROM readings WHERE sensor_id = @id ORDER BY reading_seq");
        command.Parameters.AddWithValue("id", sensorId);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            ids.Add(await reader.IsDBNullAsync(0, Ct) ? null : reader.GetGuid(0));
        }

        return ids;
    }

    private async Task<Guid?> CalibrationOfAsync(Guid sensorId, string order) =>
        await identity.Database.ScalarAsync<Guid?>($"SELECT calibration_id FROM readings WHERE sensor_id = @id ORDER BY {order} LIMIT 1", ("id", sensorId));

    // The grain's reminder, read from the silo's reminder service; the 1-minute period is never waited for.
    private async Task<IGrainReminder?> ReminderAsync(Guid sensorId) =>
        await identity.SiloServices.GetRequiredService<IReminderService>().GetReminder(
            GrainId.Create("sensor", sensorId.ToString("D")),
            "deliver-calibration");

    private async Task<Downlink> ReportAsync(SimulatedDevice node, NodeFrame frame)
    {
        var envelope = node.SealFrame(frame);
        var result = await identity.Device(node.DeviceId.ToString()).Ingest(
            new DeviceIngest(envelope.ProtocolVersion, envelope.Counter, envelope.Ciphertext.ToByteArray(), HubA),
            Ct);

        Assert.Equal(DeviceIngestStatus.Stored, result.Status);
        return node.OpenDownlink(SealedEnvelope.Parser.ParseFrom(result.Downlink));
    }
}
