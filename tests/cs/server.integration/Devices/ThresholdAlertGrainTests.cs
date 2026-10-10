using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Lots;
using Coldframe.Contracts.Notifications;
using Coldframe.Contracts.Sensors;
using Coldframe.Contracts.Sites;
using Coldframe.DeviceSimulator;
using Coldframe.Protocol.Device.V1;
using Coldframe.Server.Alerts;
using Coldframe.Server.IntegrationTests.Identity;
using Coldframe.Server.IntegrationTests.Journal;
using Coldframe.Server.Journal;
using Coldframe.Server.Lots;
using Coldframe.Server.Sensors;
using ProtocolKind = Coldframe.Protocol.Setup.V1.DeviceKind;

namespace Coldframe.Server.IntegrationTests.Devices;

/// <summary>
/// Threshold Alerts on a TestCluster with a fake clock (Story 6.1; AD-1, AD-5, AD-8, AD-19): the Device grain hands
/// every declared Sensor of an assigned Node its Reading after the frame is committed, the Sensor grain counts
/// streaks and journals an episode before it opens the Alert, the Alert grain reports to its Site grain, and the
/// lots projector derives <c>needsWater</c> from the Alert events. Frames come from the Device simulator only, 15
/// minutes of <c>measured_at</c> apart.
/// </summary>
[Collection(IngestSuites.Name)]
public sealed class ThresholdAlertGrainTests(IdentityCluster identity) : IClassFixture<IdentityCluster>
{
    private const string HubA = "92064422c012f481";

    // The soil probe of every test is calibrated dry 3000, wet 1000: 20 raw counts per percent.
    private const long DryRaw = 3000;

    private const long WetRaw = 1000;

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    // How long a Node waits before it sends an unacknowledged frame again; well inside the interval.
    private static readonly TimeSpan ResendDelay = TimeSpan.FromMinutes(1);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTimeOffset Now => identity.Time.GetUtcNow();

    [Fact]
    public async Task ThreeConsecutiveReadingsBelowTheLowOpenOneAlertOnceTheEpisodeIsJournaled()
    {
        var garden = await CalibratedGardenAsync();

        await ReadSoilAsync(garden, 20);
        await ReadSoilAsync(garden, 20);

        // None after two: the streak is journaled, no episode, no Alert.
        Assert.Equal(["sensor.streak-changed", "sensor.streak-changed"], await EvaluationAliasesAsync(garden.Soil));
        Assert.Empty(await identity.Site(garden.SiteId).OpenAlerts(Ct));

        await ReadSoilAsync(garden, 20);
        var openedAt = Now;

        // The third journals episode 1 and opens the Alert whose ID is the UUIDv5 of the Sensor and the episode.
        var alertId = AlertIds.Threshold(garden.Soil, 1);
        Assert.Equal(
            ["sensor.streak-changed", "sensor.streak-changed", "sensor.threshold-episode-opened", "sensor.alert-delivered"],
            await EvaluationAliasesAsync(garden.Soil));
        var episode = Assert.IsType<SensorThresholdEpisodeOpened>((await identity.Store.ReadStreamAsync($"sensor/{garden.Soil}", Ct))[^2].Data);
        Assert.Equal(
            new SensorThresholdEpisodeOpened(1, alertId, ThresholdSide.Low, garden.SiteId, garden.LotId, 20, 30, episode.Epoch, openedAt, openedAt),
            episode);

        var alert = await identity.Alert(alertId).Describe(Ct);
        Assert.Equal(
            new AlertSnapshot(
                alertId,
                AlertKind.Threshold,
                AlertLifecycle.Open,
                ThresholdSide.Low,
                garden.SiteId,
                garden.LotId,
                garden.Soil,
                garden.Node.DeviceId.ToString(),
                "soil_moisture",
                openedAt),
            alert);
        Assert.Equal(["alert.opened", "alert.site-notified"], await identity.AliasesAsync($"alert/{alertId}"));

        // The episode is in the journal before the Alert is.
        Assert.True(
            await PositionAsync($"sensor/{garden.Soil}", "sensor.threshold-episode-opened") < await PositionAsync($"alert/{alertId}", "alert.opened"),
            "The Sensor journals the episode before the Alert grain journals the Alert.");

        // The Site lists it, from its own stream.
        var listed = Assert.Single(await identity.Site(garden.SiteId).OpenAlerts(Ct));
        Assert.Equal(
            new SiteAlert(alertId, AlertKind.Threshold, ThresholdSide.Low, garden.LotId, garden.Soil, garden.Node.DeviceId.ToString(), "soil_moisture", openedAt),
            listed);
        Assert.Equal("site.alert-opened", (await identity.AliasesAsync($"site/{garden.SiteId}"))[^1]);

        // Steady Readings beyond the Threshold journal nothing more (DW-7).
        await ReadSoilAsync(garden, 20);
        await ReadSoilAsync(garden, 15);
        Assert.Equal(4, (await EvaluationAliasesAsync(garden.Soil)).Count);
    }

    [Fact]
    public async Task TwoOfThreeOpensNoAlertAndNoEpisode()
    {
        var garden = await CalibratedGardenAsync();

        foreach (var percent in new[] { 20, 20, 40, 20, 20 })
        {
            await ReadSoilAsync(garden, percent);
        }

        Assert.All(await EvaluationAliasesAsync(garden.Soil), alias => Assert.Equal("sensor.streak-changed", alias));
        var state = await SensorStateAsync(garden.Soil);
        Assert.Equal((0, new StreakState(null, ThresholdPosition.BelowLow, 2)), (state.Episode, state.Streak));
        Assert.Empty(await identity.Site(garden.SiteId).OpenAlerts(Ct));
        Assert.Empty(await identity.AliasesAsync($"alert/{AlertIds.Threshold(garden.Soil, 1)}"));
    }

    [Fact]
    public async Task ThreeConsecutiveReadingsWithinCloseTheAlertAsRecovered()
    {
        var garden = await CalibratedGardenAsync();
        var alertId = await OpenLowAsync(garden);

        await ReadSoilAsync(garden, 40);
        await ReadSoilAsync(garden, 40);
        Assert.Equal(AlertLifecycle.Open, (await identity.Alert(alertId).Describe(Ct))!.Lifecycle);

        await ReadSoilAsync(garden, 40);
        var closedAt = Now;

        var alert = (await identity.Alert(alertId).Describe(Ct))!;
        Assert.Equal((AlertLifecycle.Closed, AlertCloseReason.Recovered, closedAt), (alert.Lifecycle, alert.Reason, alert.ClosedAt));
        Assert.Equal(
            ["alert.opened", "alert.site-notified", "alert.closed", "alert.site-notified"],
            await identity.AliasesAsync($"alert/{alertId}"));
        Assert.Empty(await identity.Site(garden.SiteId).OpenAlerts(Ct));
        Assert.Equal(
            new SiteAlertClosed(alertId, AlertCloseReason.Recovered, closedAt),
            (await identity.Store.ReadStreamAsync($"site/{garden.SiteId}", Ct))[^1].Data);

        var state = await SensorStateAsync(garden.Soil);
        Assert.Null(state.OpenAlert);
        Assert.Empty(state.PendingAlertDeliveries);
        Assert.Equal(1, state.Episode);
    }

    [Fact]
    public async Task AFlappingLotKeepsItsAlertOpen()
    {
        var garden = await CalibratedGardenAsync();
        var alertId = await OpenLowAsync(garden);

        foreach (var percent in new[] { 40, 40, 20, 40, 40 })
        {
            await ReadSoilAsync(garden, percent);
        }

        Assert.Equal(AlertLifecycle.Open, (await identity.Alert(alertId).Describe(Ct))!.Lifecycle);
        Assert.Equal(alertId, Assert.Single(await identity.Site(garden.SiteId).OpenAlerts(Ct)).AlertId);
        Assert.Equal(new StreakState(ThresholdSide.Low, ThresholdPosition.Within, 2), (await SensorStateAsync(garden.Soil)).Streak);
    }

    [Fact]
    public async Task ThreeReadingsAboveTheHighCloseTheLowAlertAndOpenTheNextEpisodeOnTheHighSide()
    {
        var garden = await CalibratedGardenAsync();
        Assert.Equal(
            SensorThresholdsOutcome.Changed,
            (await identity.Sensor(garden.Soil).SetThresholds(new SetSensorThresholds(High: new ThresholdSetting(ThresholdKind.Override, 60)), Ct)).Outcome);
        var low = await OpenLowAsync(garden);

        await ReadSoilAsync(garden, 70);
        await ReadSoilAsync(garden, 70);
        await ReadSoilAsync(garden, 70);

        var high = AlertIds.Threshold(garden.Soil, 2);
        var closed = (await identity.Alert(low).Describe(Ct))!;
        var opened = (await identity.Alert(high).Describe(Ct))!;
        Assert.Equal((AlertLifecycle.Closed, AlertCloseReason.Recovered), (closed.Lifecycle, closed.Reason));
        Assert.Equal((AlertLifecycle.Open, ThresholdSide.High), (opened.Lifecycle, opened.Side));

        // At most one open Threshold Alert per Sensor: the Site lists only the second.
        Assert.Equal(high, Assert.Single(await identity.Site(garden.SiteId).OpenAlerts(Ct)).AlertId);
        var state = await SensorStateAsync(garden.Soil);
        Assert.Equal((2, new SensorOpenAlert(high, 2, ThresholdSide.High)), (state.Episode, state.OpenAlert));
        Assert.Equal(
            ["sensor.threshold-episode-closed", "sensor.threshold-episode-opened", "sensor.alert-delivered", "sensor.alert-delivered"],
            (await EvaluationAliasesAsync(garden.Soil))[^4..]);
    }

    [Fact]
    public async Task AReadingThatIsNotNewerThanTheLastEvaluatedOneIsStoredAndNotEvaluated()
    {
        var garden = await CalibratedGardenAsync();
        await ReadSoilAsync(garden, 20);
        var second = await ReadSoilAsync(garden, 20);
        var before = await EvaluationAliasesAsync(garden.Soil);

        // A backlog Reading, taken an hour earlier, arrives late: stored, and the streak stays at two.
        var backlog = garden.Node.Wake(Now.AddHours(-1), readings: [Soil(20)]);
        Assert.Equal(DeviceIngestStatus.Stored, (await IngestAsync(garden.Node, backlog)).Status);
        Assert.Equal(1L, await ReadingsAsync(garden.Soil, backlog));

        // The Node sends the second frame again: a duplicate, evaluated again, and nothing changes.
        Assert.Equal(DeviceIngestStatus.Duplicate, (await IngestAsync(garden.Node, second)).Status);

        Assert.Equal(before, await EvaluationAliasesAsync(garden.Soil));
        Assert.Equal(new StreakState(null, ThresholdPosition.BelowLow, 2), (await SensorStateAsync(garden.Soil)).Streak);
        Assert.Empty(await identity.Site(garden.SiteId).OpenAlerts(Ct));

        // The next Reading in order is the third.
        await ReadSoilAsync(garden, 20);
        Assert.Single(await identity.Site(garden.SiteId).OpenAlerts(Ct));
    }

    [Fact]
    public async Task AFailedAlertCallAnswersRetryAndTheResentFrameCompletesWithoutADuplicate()
    {
        var garden = await CalibratedGardenAsync();
        await ReadSoilAsync(garden, 20);
        await ReadSoilAsync(garden, 20);
        identity.AlertFaults.FailNext(1);

        identity.Time.Advance(Interval);
        var third = garden.Node.Wake(Now, readings: [Soil(20)]);
        var failed = await IngestAsync(garden.Node, third);

        // The Reading is stored and the episode journaled, but the frame is not acknowledged.
        Assert.Equal(DeviceIngestStatus.Retry, failed.Status);
        Assert.Null(failed.Downlink);
        Assert.Equal(1L, await ReadingsAsync(garden.Soil, third));
        Assert.Equal(1, (await SensorStateAsync(garden.Soil)).Episode);

        // The Node resends: the frame is a duplicate, and its evaluation finishes the delivery.
        Assert.Equal(DeviceIngestStatus.Duplicate, (await IngestAsync(garden.Node, third)).Status);

        await AssertOneAlertAsync(garden);
    }

    [Fact]
    public async Task AFailedSiteCallAnswersRetryAndTheResentFrameCompletesWithoutADuplicate()
    {
        var garden = await CalibratedGardenAsync();
        await ReadSoilAsync(garden, 20);
        await ReadSoilAsync(garden, 20);
        identity.SiteFaults.FailNextAlertReports(1);

        identity.Time.Advance(Interval);
        var third = garden.Node.Wake(Now, readings: [Soil(20)]);

        // The Alert is open, but its Site does not know it yet: the frame is not acknowledged.
        Assert.Equal(DeviceIngestStatus.Retry, (await IngestAsync(garden.Node, third)).Status);
        Assert.Equal(AlertLifecycle.Open, (await identity.Alert(AlertIds.Threshold(garden.Soil, 1)).Describe(Ct))!.Lifecycle);

        Assert.Equal(DeviceIngestStatus.Duplicate, (await IngestAsync(garden.Node, third)).Status);

        await AssertOneAlertAsync(garden);
    }

    [Fact]
    public async Task AnUndeliveredOpenIsDeliveredWithNobodyCallingAndAfterASiloRestart()
    {
        var garden = await CalibratedGardenAsync();
        await ReadSoilAsync(garden, 20);
        await ReadSoilAsync(garden, 20);
        identity.AlertFaults.FailNext(int.MaxValue);

        identity.Time.Advance(Interval);
        Assert.Equal(DeviceIngestStatus.Retry, (await IngestAsync(garden.Node, garden.Node.Wake(Now, readings: [Soil(20)]))).Status);
        var pending = Assert.Single((await SensorStateAsync(garden.Soil)).PendingAlertDeliveries);
        Assert.Equal((AlertIds.Threshold(garden.Soil, 1), AlertLifecycle.Open), (pending.AlertId, pending.Change));

        // Every activation and everything held in memory is gone; the new silo has no fault. The first call
        // activates the Sensor grain, which delivers what its stream says is still undelivered.
        await RestartAsync();
        await identity.Sensor(garden.Soil).Describe(Ct);

        // The Site lists the Alert before the Sensor journals that it was delivered: wait for the second.
        await JournalWait.UntilAsync(
            async () => (await identity.Site(garden.SiteId).OpenAlerts(Ct)).Count == 1
                && (await SensorStateAsync(garden.Soil)).PendingAlertDeliveries.Count == 0,
            "the Sensor delivered its open Alert after the restart");
        await AssertOneAlertAsync(garden);
    }

    [Fact]
    public async Task AnUncalibratedSensorASensorWithoutALowAnUndeclaredSlotAndAnUnassignedNodeAreStoredAndNotEvaluated()
    {
        // 1. Uncalibrated: assigned and declared, three raw Readings that would read far below the low.
        var uncalibrated = await GardenAsync();
        for (var i = 0; i < 3; i++)
        {
            await ReadAsync(uncalibrated, new SimulatedReading(0, Quantity.SoilMoisture, 2900));
        }

        Assert.Equal(["sensor.declared"], await identity.AliasesAsync($"sensor/{uncalibrated.Soil}"));

        // 2. No effective low: the air-temperature Sensor is only watched, and a soil Sensor whose low was cleared.
        var watched = await CalibratedGardenAsync();
        var cleared = new ThresholdSetting(ThresholdKind.Cleared);
        Assert.Equal(SensorThresholdsOutcome.Changed, (await identity.Sensor(watched.Soil).SetThresholds(new SetSensorThresholds(cleared, cleared), Ct)).Outcome);
        for (var i = 0; i < 3; i++)
        {
            await ReadAsync(watched, Soil(5), new SimulatedReading(1, Quantity.AirTemperature, -30_000));
        }

        Assert.Empty(await EvaluationAliasesAsync(watched.Soil));
        Assert.Equal(["sensor.declared"], await identity.AliasesAsync($"sensor/{watched.Node.SensorId(1, Quantity.AirTemperature)}"));

        // 3. A slot the Node never declared (AD-19): stored under its derived Sensor ID, no Sensor stream.
        var undeclared = watched.Node.SensorId(7, Quantity.SoilMoisture);
        for (var i = 0; i < 3; i++)
        {
            await ReadAsync(watched, new SimulatedReading(7, Quantity.SoilMoisture, 2900));
        }

        Assert.Equal(3L, await identity.Database.ScalarAsync<long>("SELECT COUNT(*) FROM readings WHERE sensor_id = @id", ("id", undeclared)));
        Assert.Empty(await identity.AliasesAsync($"sensor/{undeclared}"));

        // 4. An unassigned Node (AD-8): calibrated and dry, but no Lot claims it.
        var unassigned = await CalibratedGardenAsync();
        Assert.Equal(DeviceAssignmentOutcome.Unassigned, (await identity.Device(unassigned.Node.DeviceId.ToString()).Unassign(unassigned.SiteId, Ct)).Outcome);
        for (var i = 0; i < 3; i++)
        {
            await ReadSoilAsync(unassigned, 10);
        }

        Assert.Empty(await EvaluationAliasesAsync(unassigned.Soil));

        foreach (var garden in new[] { uncalibrated, watched, unassigned })
        {
            Assert.Empty(await identity.Site(garden.SiteId).OpenAlerts(Ct));
        }
    }

    [Fact]
    public async Task ChangedThresholdsAndANewEpochResetTheStreak()
    {
        var garden = await CalibratedGardenAsync();

        // Thresholds changed after two: three more are needed.
        await ReadSoilAsync(garden, 20);
        await ReadSoilAsync(garden, 20);
        await identity.Sensor(garden.Soil).SetThresholds(new SetSensorThresholds(Low: new ThresholdSetting(ThresholdKind.Override, 35)), Ct);
        Assert.Equal(0, (await SensorStateAsync(garden.Soil)).Streak.Count);

        await ReadSoilAsync(garden, 20);
        await ReadSoilAsync(garden, 20);
        Assert.Empty(await identity.Site(garden.SiteId).OpenAlerts(Ct));

        // The Node moves to another Lot after two: a new epoch, and again three more are needed.
        var peppers = await SeedLotAsync(garden.SiteId, "Peppers");
        Assert.Equal(DeviceAssignmentOutcome.Moved, (await identity.Device(garden.Node.DeviceId.ToString()).Move(garden.SiteId, peppers, Ct)).Outcome);

        await ReadSoilAsync(garden, 20);
        await ReadSoilAsync(garden, 20);
        Assert.Empty(await identity.Site(garden.SiteId).OpenAlerts(Ct));
        Assert.Equal(new StreakState(null, ThresholdPosition.BelowLow, 2), (await SensorStateAsync(garden.Soil)).Streak);

        await ReadSoilAsync(garden, 20);

        // The Alert names the Lot the Node is on now.
        Assert.Equal(peppers, Assert.Single(await identity.Site(garden.SiteId).OpenAlerts(Ct)).LotId);
    }

    [Fact]
    public async Task ANewEpochAfterOneReadingNeedsThreeMoreAndTheThirdOpensTheAlert()
    {
        var garden = await CalibratedGardenAsync();
        await ReadSoilAsync(garden, 20);

        var peppers = await SeedLotAsync(garden.SiteId, "Peppers");
        Assert.Equal(DeviceAssignmentOutcome.Moved, (await identity.Device(garden.Node.DeviceId.ToString()).Move(garden.SiteId, peppers, Ct)).Outcome);

        // The streak is at one again under the new epoch, and it counts on from there.
        await ReadSoilAsync(garden, 20);
        await ReadSoilAsync(garden, 20);
        Assert.Empty(await identity.Site(garden.SiteId).OpenAlerts(Ct));
        Assert.Equal(new StreakState(null, ThresholdPosition.BelowLow, 2), (await SensorStateAsync(garden.Soil)).Streak);

        await ReadSoilAsync(garden, 20);
        Assert.Equal(AlertIds.Threshold(garden.Soil, 1), Assert.Single(await identity.Site(garden.SiteId).OpenAlerts(Ct)).AlertId);
    }

    [Fact]
    public async Task APausedNodesReadingsAreNotEvaluated()
    {
        var garden = await CalibratedGardenAsync();
        var stream = $"device/{garden.Node.DeviceId}";
        var version = (await identity.Store.ReadStreamAsync(stream, Ct)).Count;
        Assert.True(await identity.Store.AppendAsync(stream, version, [new DevicePaused(DevicePauseSource.Device, null, Now)], Ct));

        // The Device grain reads its Pause from its stream when the new silo activates it.
        await RestartAsync();

        await ReadSoilAsync(garden, 10);
        await ReadSoilAsync(garden, 10);
        await ReadSoilAsync(garden, 10);

        Assert.Empty(await EvaluationAliasesAsync(garden.Soil));
        Assert.Empty(await identity.Site(garden.SiteId).OpenAlerts(Ct));
    }

    [Fact]
    public async Task AReadingIsComparedAsThePercentageRoundedToFiveThatTheLotTileShows()
    {
        var garden = await CalibratedGardenAsync();

        // 28 percent reads "~30 %", and the low is 30: not below.
        await ReadSoilAsync(garden, 28);
        await ReadSoilAsync(garden, 28);
        await ReadSoilAsync(garden, 28);

        Assert.Empty(await EvaluationAliasesAsync(garden.Soil));
        Assert.Empty(await identity.Site(garden.SiteId).OpenAlerts(Ct));
    }

    [Fact]
    public async Task AnUnsyncedDryReadingSentThreeTimesCountsOnceAndOpensNoAlert()
    {
        var garden = await CalibratedGardenAsync();

        // A Node without synced time: the Server gives the Reading the receive time, which every resend moves later.
        var (dry, measuredAt) = WakeUnsynced(garden, 20);
        Assert.Equal(DeviceIngestStatus.Stored, (await IngestAsync(garden.Node, dry)).Status);
        await ResendAsync(garden, dry);
        await ResendAsync(garden, dry);

        // Each resend was evaluated with the time stored at first delivery, which is not newer: a streak of one.
        Assert.Equal(measuredAt, await KeyMeasuredAtAsync(garden, dry));
        var streak = Assert.IsType<SensorStreakChanged>(Assert.Single(await EvaluationEventsAsync(garden.Soil)));
        Assert.Equal((ThresholdPosition.BelowLow, 1, measuredAt), (streak.Position, streak.Count, streak.MeasuredAt));
        Assert.Empty(await identity.Site(garden.SiteId).OpenAlerts(Ct));
        Assert.Empty(await identity.AliasesAsync($"alert/{AlertIds.Threshold(garden.Soil, 1)}"));

        // Two more Readings of their own are still needed.
        await ReadSoilAsync(garden, 20);
        Assert.Empty(await identity.Site(garden.SiteId).OpenAlerts(Ct));
        await ReadSoilAsync(garden, 20);
        Assert.Single(await identity.Site(garden.SiteId).OpenAlerts(Ct));
    }

    [Fact]
    public async Task AnUnsyncedReadingInRangeSentThreeTimesClosesNoAlert()
    {
        var garden = await CalibratedGardenAsync();
        var alertId = await OpenLowAsync(garden);

        var (recovered, _) = WakeUnsynced(garden, 40);
        Assert.Equal(DeviceIngestStatus.Stored, (await IngestAsync(garden.Node, recovered)).Status);
        await ResendAsync(garden, recovered);
        await ResendAsync(garden, recovered);

        Assert.Equal(AlertLifecycle.Open, (await identity.Alert(alertId).Describe(Ct))!.Lifecycle);
        Assert.Equal(new StreakState(ThresholdSide.Low, ThresholdPosition.Within, 1), (await SensorStateAsync(garden.Soil)).Streak);
        Assert.Equal(alertId, Assert.Single(await identity.Site(garden.SiteId).OpenAlerts(Ct)).AlertId);
    }

    [Fact]
    public async Task AnUnsyncedReadingWhoseFirstEvaluationFailedIsEvaluatedOnceWithTheStoredTime()
    {
        var garden = await CalibratedGardenAsync();
        identity.SensorFaults.FailNextEvaluations(1);

        // The frame is committed, its evaluation throws: stored, not acknowledged, nothing evaluated.
        var (dry, measuredAt) = WakeUnsynced(garden, 20);
        var failed = await IngestAsync(garden.Node, dry);
        Assert.Equal(DeviceIngestStatus.Retry, failed.Status);
        Assert.Null(failed.Downlink);
        Assert.Equal(1L, await ReadingsAsync(garden.Soil, dry));
        Assert.Empty(await EvaluationAliasesAsync(garden.Soil));

        // The resend is a duplicate that carries a later time; the Reading is evaluated with the stored one.
        await ResendAsync(garden, dry);
        var streak = Assert.IsType<SensorStreakChanged>(Assert.Single(await EvaluationEventsAsync(garden.Soil)));
        Assert.Equal((ThresholdPosition.BelowLow, 1, measuredAt), (streak.Position, streak.Count, streak.MeasuredAt));

        // Exactly once: another resend changes nothing.
        await ResendAsync(garden, dry);
        Assert.Single(await EvaluationEventsAsync(garden.Soil));
        Assert.Empty(await identity.Site(garden.SiteId).OpenAlerts(Ct));
    }

    [Fact]
    public async Task AReadingWhoseKeyWasStoredWithoutATimeIsEvaluatedWithTheTimeOfTheFrameThatCarriesIt()
    {
        var garden = await CalibratedGardenAsync();
        identity.SensorFaults.FailNextEvaluations(1);
        var (dry, _) = WakeUnsynced(garden, 20);
        Assert.Equal(DeviceIngestStatus.Retry, (await IngestAsync(garden.Node, dry)).Status);

        // As a key stored before reading_keys kept the time: nothing to evaluate the resend with but its own.
        await identity.Database.ExecuteAsync(
            "UPDATE reading_keys SET measured_at = NULL WHERE device_id = @device_id",
            ("device_id", garden.Node.DeviceId.ToString()));

        identity.Time.Advance(ResendDelay);
        var resentAt = Now;
        Assert.Equal(DeviceIngestStatus.Duplicate, (await IngestAsync(garden.Node, dry)).Status);

        var streak = Assert.IsType<SensorStreakChanged>(Assert.Single(await EvaluationEventsAsync(garden.Soil)));
        Assert.Equal(resentAt, streak.MeasuredAt);
        Assert.Equal(1L, await identity.Database.ScalarAsync<long>(
            "SELECT count(*) FROM reading_keys WHERE device_id = @device_id AND sensor_id = @sensor_id AND reading_seq = @seq AND measured_at IS NULL",
            ("device_id", garden.Node.DeviceId.ToString()),
            ("sensor_id", garden.Soil),
            ("seq", (decimal)SeqOf(dry))));
    }

    [Fact]
    public async Task AFirstDeliveryIsEvaluatedWithTheTimeItsKeyHoldsAtTheDatabasesPrecision()
    {
        var garden = await CalibratedGardenAsync();

        // Half a microsecond more than timestamptz keeps: the time the key holds is not the one the frame came with.
        var half = TimeSpan.FromTicks(5);
        identity.Time.Advance(half);
        try
        {
            var (dry, receivedAt) = WakeUnsynced(garden, 20);
            Assert.Equal(DeviceIngestStatus.Stored, (await IngestAsync(garden.Node, dry)).Status);

            // The first delivery is evaluated with the stored time too, so a resend, which gets the same one, is not newer.
            var stored = await KeyMeasuredAtAsync(garden, dry);
            Assert.NotEqual(receivedAt, stored);
            var streak = Assert.IsType<SensorStreakChanged>(Assert.Single(await EvaluationEventsAsync(garden.Soil)));
            Assert.Equal(stored, streak.MeasuredAt);

            await ResendAsync(garden, dry);
            Assert.Single(await EvaluationEventsAsync(garden.Soil));
        }
        finally
        {
            // Back to a whole millisecond, which a synced frame's time is, for the suites that share the clock.
            identity.Time.Advance(TimeSpan.FromMilliseconds(1) - half);
        }
    }

    [Fact]
    public async Task ASiteThatIsNotActiveKeepsNoAlerts()
    {
        var siteId = Guid.CreateVersion7().ToString();
        var alert = new SiteAlert(Guid.NewGuid(), AlertKind.Threshold, ThresholdSide.Low, Guid.CreateVersion7().ToString(), Guid.NewGuid(), "5a4b3c2d1e0f7c20", "soil_moisture", Now);

        // Acknowledged, so that no Alert grain keeps reporting to a Site that is not there; it has nobody to tell.
        Assert.Empty((await identity.Site(siteId).AlertOpened(alert, Ct)).Members);
        Assert.Empty((await identity.Site(siteId).AlertClosed(alert.AlertId, AlertCloseReason.Recovered, Now, Ct)).Members);

        Assert.Empty(await identity.AliasesAsync($"site/{siteId}"));
        Assert.Empty(await identity.Site(siteId).OpenAlerts(Ct));
    }

    [Fact]
    public async Task AnOpenAlertStaysOpenWhenTheThresholdsChange()
    {
        var garden = await CalibratedGardenAsync();
        var alertId = await OpenLowAsync(garden);
        await ReadSoilAsync(garden, 40);
        await ReadSoilAsync(garden, 40);

        await identity.Sensor(garden.Soil).SetThresholds(new SetSensorThresholds(Low: new ThresholdSetting(ThresholdKind.Override, 25)), Ct);
        await ReadSoilAsync(garden, 40);

        // The closing streak started again: the third Reading within since the Alert opened does not close it.
        Assert.Equal(AlertLifecycle.Open, (await identity.Alert(alertId).Describe(Ct))!.Lifecycle);

        await ReadSoilAsync(garden, 40);
        await ReadSoilAsync(garden, 40);
        Assert.Equal(AlertLifecycle.Closed, (await identity.Alert(alertId).Describe(Ct))!.Lifecycle);
    }

    [Fact]
    public async Task AnOpenLowAlertOnTheSoilSensorMakesTheLotNeedWaterAndItIsOkAgainOnceClosed()
    {
        var garden = await CalibratedGardenAsync();
        var other = await SeedLotAsync(garden.SiteId, "Aubergines");
        await CatchUpLotsAsync();
        Assert.Equal("ok", (await identity.Lots.FindLotAsync(garden.SiteId, garden.LotId, Ct))!.Status);

        await OpenLowAsync(garden);
        var openedAt = Now;
        await CatchUpLotsAsync();

        var lot = (await identity.Lots.FindLotAsync(garden.SiteId, garden.LotId, Ct))!;
        Assert.Equal(("needsWater", openedAt), (lot.Status, lot.StatusSince));

        // First in the list, before the Lot that was created later and has no Node.
        Assert.Equal([garden.LotId, other], (await identity.Lots.ListLotsAsync(garden.SiteId, Ct)).Select(listed => listed.LotId));

        await ReadSoilAsync(garden, 40);
        await ReadSoilAsync(garden, 40);
        await ReadSoilAsync(garden, 40);
        var closedAt = Now;
        await CatchUpLotsAsync();

        lot = (await identity.Lots.FindLotAsync(garden.SiteId, garden.LotId, Ct))!;
        Assert.Equal(("ok", closedAt), (lot.Status, lot.StatusSince));
    }

    [Fact]
    public async Task AHighSideAlertAndAnAlertOfAnotherQuantityLeaveTheLotStatusAsItIs()
    {
        // Too wet: three Readings above the high.
        var wet = await CalibratedGardenAsync();
        await ReadSoilAsync(wet, 90);
        await ReadSoilAsync(wet, 90);
        await ReadSoilAsync(wet, 90);

        // Too cold: the air-temperature Sensor is compared in its Specification's unit, milli-degrees Celsius.
        var cold = await CalibratedGardenAsync();
        var air = cold.Node.SensorId(1, Quantity.AirTemperature);
        Assert.Equal(
            SensorThresholdsOutcome.Changed,
            (await identity.Sensor(air).SetThresholds(new SetSensorThresholds(Low: new ThresholdSetting(ThresholdKind.Override, 5_000)), Ct)).Outcome);
        for (var i = 0; i < 3; i++)
        {
            await ReadAsync(cold, Soil(50), new SimulatedReading(1, Quantity.AirTemperature, 4_999));
        }

        await CatchUpLotsAsync();

        var high = Assert.Single(await identity.Site(wet.SiteId).OpenAlerts(Ct));
        var low = Assert.Single(await identity.Site(cold.SiteId).OpenAlerts(Ct));
        Assert.Equal((ThresholdSide.High, "soil_moisture"), (high.Side, high.Quantity));
        Assert.Equal((ThresholdSide.Low, "air_temperature", air), (low.Side, low.Quantity, low.SensorId));
        Assert.Equal("ok", (await identity.Lots.FindLotAsync(wet.SiteId, wet.LotId, Ct))!.Status);
        Assert.Equal("ok", (await identity.Lots.FindLotAsync(cold.SiteId, cold.LotId, Ct))!.Status);
    }

    [Fact]
    public async Task TheStreakTheOpenAlertAndTheSiteSetSurviveASiloRestart()
    {
        var streak = await CalibratedGardenAsync();
        await ReadSoilAsync(streak, 20);
        await ReadSoilAsync(streak, 20);
        var open = await CalibratedGardenAsync();
        var alertId = await OpenLowAsync(open);
        await ReadSoilAsync(open, 40);
        await ReadSoilAsync(open, 40);

        await RestartAsync();

        // The next Reading continues the streak of two: it is the third.
        Assert.Empty(await identity.Site(streak.SiteId).OpenAlerts(Ct));
        await ReadSoilAsync(streak, 20);
        Assert.Equal(AlertIds.Threshold(streak.Soil, 1), Assert.Single(await identity.Site(streak.SiteId).OpenAlerts(Ct)).AlertId);

        // The Site still lists the open Alert, and the next Reading within is the third: it closes.
        Assert.Equal(alertId, Assert.Single(await identity.Site(open.SiteId).OpenAlerts(Ct)).AlertId);
        await ReadSoilAsync(open, 40);
        Assert.Equal(AlertLifecycle.Closed, (await identity.Alert(alertId).Describe(Ct))!.Lifecycle);
        Assert.Empty(await identity.Site(open.SiteId).OpenAlerts(Ct));
    }

    [Fact]
    public async Task OnlyTheOpeningSensorClosesAnAlert()
    {
        var garden = await CalibratedGardenAsync();
        var alertId = await OpenLowAsync(garden);

        // Another caller, here the test's client: refused, whatever reason it gives.
        foreach (var reason in Enum.GetValues<AlertCloseReason>())
        {
            var refused = await identity.Alert(alertId).Close(new CloseAlert(reason, Now), Ct);
            Assert.Equal(AlertOutcome.Refused, refused.Outcome);
        }

        // An Alert that was never opened has nobody who may close it.
        Assert.Equal(AlertOutcome.NotOpened, (await identity.Alert(Guid.NewGuid()).Close(new CloseAlert(AlertCloseReason.Recovered, Now), Ct)).Outcome);

        // An open that does not derive this Alert's ID, as another Sensor's would not, is refused too.
        var foreign = new OpenAlert(Guid.NewGuid(), 1, ThresholdSide.Low, garden.SiteId, garden.LotId, garden.Node.DeviceId.ToString(), "soil_moisture", Now);
        Assert.Equal(AlertOutcome.Refused, (await identity.Alert(alertId).Open(foreign, Ct)).Outcome);

        Assert.Equal(AlertLifecycle.Open, (await identity.Alert(alertId).Describe(Ct))!.Lifecycle);
        Assert.Equal(["alert.opened", "alert.site-notified"], await identity.AliasesAsync($"alert/{alertId}"));
        Assert.Equal(alertId, Assert.Single(await identity.Site(garden.SiteId).OpenAlerts(Ct)).AlertId);
    }

    [Fact]
    public async Task OpeningAnAlertAgainAndReportingItAgainJournalNothing()
    {
        var garden = await CalibratedGardenAsync();
        var alertId = await OpenLowAsync(garden);
        var listed = Assert.Single(await identity.Site(garden.SiteId).OpenAlerts(Ct));
        var site = await identity.AliasesAsync($"site/{garden.SiteId}");

        var again = await identity.Alert(alertId).Open(
            new OpenAlert(garden.Soil, 1, ThresholdSide.Low, garden.SiteId, garden.LotId, garden.Node.DeviceId.ToString(), "soil_moisture", Now.AddHours(1)),
            Ct);
        var reported = await identity.Site(garden.SiteId).AlertOpened(listed, Ct);

        // The Site names its members every time, so the Alert grain can finish telling them (Story 6.4).
        Assert.Equal([garden.OwnerId], reported.Members);
        Assert.Equal(new AlertResult(AlertOutcome.Open, Reported: true), again);
        Assert.Equal(["alert.opened", "alert.site-notified"], await identity.AliasesAsync($"alert/{alertId}"));
        Assert.Equal(site, await identity.AliasesAsync($"site/{garden.SiteId}"));

        // A close the Site never saw opened, and one reported twice, change nothing either.
        var closed = await identity.Site(garden.SiteId).AlertClosed(Guid.NewGuid(), AlertCloseReason.Recovered, Now, Ct);
        Assert.Equal([garden.OwnerId], closed.Members);
        Assert.Equal(site, await identity.AliasesAsync($"site/{garden.SiteId}"));
    }

    [Fact]
    public async Task ASensorCrossingItsLowNotifiesTheSitesMemberOnceAndItsRecoveryNotifiesNobody()
    {
        var garden = await CalibratedGardenAsync();

        // The Owner's User grain holds the Site, as after Create Site or a reconciliation; noon is inside the
        // default Notification Window, in UTC while the User has no time zone.
        await identity.User(garden.OwnerId).SyncSiteMembership(garden.SiteId, SiteRole.Owner, Ct);
        var noon = new DateTimeOffset(Now.UtcDateTime.Date, TimeSpan.Zero).AddHours(36);
        identity.Time.Advance(noon - Now - (3 * Interval));

        var alertId = await OpenLowAsync(garden);
        var openedAt = Now;

        // Nothing calls the User grain: the Alert grain told it, and its own timer hands the Notifier what is due.
        await JournalWait.UntilAsync(
            () => Task.FromResult(identity.Notifications.For(garden.OwnerId).Count > 0),
            "the member was notified of the Alert");
        var sent = Assert.Single(identity.Notifications.For(garden.OwnerId));
        Assert.Equal(
            new Notification(garden.OwnerId, garden.SiteId, NotificationKind.Alert, openedAt, null, sent.Notification.Entries),
            sent.Notification with { Registrations = null, TimeZone = null, Window = null });
        Assert.Equal(
            new NotificationEntry(alertId, AlertKind.Threshold, ThresholdSide.Low, garden.LotId, garden.Soil, garden.Node.DeviceId.ToString(), "soil_moisture", openedAt),
            Assert.Single(sent.Notification.Entries));
        Assert.InRange(sent.SentAt - sent.Notification.DueAt, TimeSpan.Zero, TimeSpan.FromMinutes(1));
        Assert.Equal(
            ["alert.opened", "alert.site-notified"],
            await identity.AliasesAsync($"alert/{alertId}"));

        // Three Readings within: the Alert closes, the User grain drops it, and closing never notifies.
        await ReadSoilAsync(garden, 40);
        await ReadSoilAsync(garden, 40);
        await ReadSoilAsync(garden, 40);

        Assert.Equal(AlertLifecycle.Closed, (await identity.Alert(alertId).Describe(Ct))!.Lifecycle);
        Assert.Equal(
            ["user.site-membership-changed", "user.site-alerts-pulled", "user.alert-tracked", "user.notification-sent", "user.alert-dropped"],
            await identity.AliasesAsync($"user/{garden.OwnerId}"));
        await identity.WakeUserAsync(garden.OwnerId);
        Assert.Single(identity.Notifications.For(garden.OwnerId));
        Assert.False(await identity.HasWakeReminderAsync(garden.OwnerId));
    }

    [Fact]
    public async Task RebuildingTheLotsReadModelFromTheJournalGivesEveryLotTheSameStatus()
    {
        var dry = await CalibratedGardenAsync();
        await OpenLowAsync(dry);
        var recovered = await CalibratedGardenAsync();
        await OpenLowAsync(recovered);
        await ReadSoilAsync(recovered, 40);
        await ReadSoilAsync(recovered, 40);
        await ReadSoilAsync(recovered, 40);
        await CatchUpLotsAsync();

        var before = await LotStatusesAsync();
        Assert.Contains((dry.LotId, "needsWater"), before.Select(lot => (lot.LotId, lot.Status)));
        Assert.Contains((recovered.LotId, "ok"), before.Select(lot => (lot.LotId, lot.Status)));

        // The documented rebuild: the rows of the projector's tables and its checkpoint, then from position 0.
        await identity.Database.ExecuteAsync(
            """
            DELETE FROM projection_checkpoints WHERE projector = 'lots';
            DELETE FROM lots;
            DELETE FROM lot_status_devices;
            DELETE FROM lot_status_sensors;
            DELETE FROM lot_status_alerts;
            DELETE FROM calibrations;
            """);
        await CatchUpLotsAsync();

        Assert.Equal(before, await LotStatusesAsync());
    }

    private static SimulatedReading Soil(int percent) => new(0, Quantity.SoilMoisture, DryRaw - ((DryRaw - WetRaw) * percent / 100));

    // An Active Site, a Lot, and a Node enrolled on that Lot that declared its four Sensors.
    private async Task<Garden> GardenAsync()
    {
        var userId = Guid.NewGuid().ToString();
        var siteId = Guid.CreateVersion7().ToString();
        Assert.True(await identity.Store.AppendAsync(
            $"site/{siteId}",
            0,
            [new SiteCreated("Home", userId), new MembershipGranted(userId, SiteRole.Owner)],
            Ct));
        var lotId = await SeedLotAsync(siteId, "Tomatoes");

        var node = SimulatedDevice.Create(ProtocolKind.Node);
        var enrol = new EnrolDevice(siteId, DeviceKind.Node, identity.Vault.Wrap(node.DeviceId, node.Keys.DeviceKey), userId, Guid.NewGuid().ToString("N"), lotId);
        Assert.Equal(DeviceEnrolmentOutcome.Enrolled, (await identity.Device(node.DeviceId.ToString()).Enrol(enrol, Ct)).Outcome);

        var garden = new Garden(siteId, lotId, node, node.SensorId(0, Quantity.SoilMoisture), userId);
        Assert.True((await ReportAsync(garden, node.Wake(Now))).SpecificationsUnknown);
        Assert.False((await ReportAsync(garden, node.Wake(Now))).SpecificationsUnknown);

        return garden;
    }

    // The same, with the soil Sensor calibrated: its default Thresholds are 30 and 80 percent.
    private async Task<Garden> CalibratedGardenAsync()
    {
        var garden = await GardenAsync();
        var dry = await ReadAsync(garden, new SimulatedReading(0, Quantity.SoilMoisture, DryRaw));
        var wet = await ReadAsync(garden, new SimulatedReading(0, Quantity.SoilMoisture, WetRaw));

        var calibrated = await identity.Sensor(garden.Soil).Calibrate(
            new CalibrateSensor(new CalibrationPointRef(SeqOf(dry)), new CalibrationPointRef(SeqOf(wet))),
            Ct);
        Assert.Equal(SensorCalibrationOutcome.Calibrated, calibrated.Outcome);

        return garden;
    }

    // Three Readings at 20 percent: the low Alert of the Sensor's next episode is open.
    private async Task<Guid> OpenLowAsync(Garden garden)
    {
        var episode = (await SensorStateAsync(garden.Soil)).Episode + 1;
        await ReadSoilAsync(garden, 20);
        await ReadSoilAsync(garden, 20);
        await ReadSoilAsync(garden, 20);

        var alertId = AlertIds.Threshold(garden.Soil, episode);
        Assert.Equal(AlertLifecycle.Open, (await identity.Alert(alertId).Describe(Ct))?.Lifecycle);
        return alertId;
    }

    private Task<NodeFrame> ReadSoilAsync(Garden garden, int percent) => ReadAsync(garden, Soil(percent));

    // One wake, 15 minutes after the last one, stored and acknowledged.
    private async Task<NodeFrame> ReadAsync(Garden garden, params SimulatedReading[] readings)
    {
        identity.Time.Advance(Interval);
        var frame = garden.Node.Wake(Now, readings: readings);
        await ReportAsync(garden, frame);
        return frame;
    }

    // One wake of a Node without synced time, 15 minutes after the last one and not sent yet: the Reading is taken
    // as the frame is sealed, so the Server gives it the time it receives the frame, returned here for the first
    // delivery.
    private (NodeFrame Frame, DateTimeOffset MeasuredAt) WakeUnsynced(Garden garden, int percent)
    {
        identity.Time.Advance(Interval);
        return (garden.Node.WakeUnsynced(garden.Node.UptimeMs, readings: [Soil(percent)]), Now);
    }

    // The Node sends a frame again a minute later: every key is known, so it is a duplicate.
    private async Task ResendAsync(Garden garden, NodeFrame frame)
    {
        identity.Time.Advance(ResendDelay);
        Assert.Equal(DeviceIngestStatus.Duplicate, (await IngestAsync(garden.Node, frame)).Status);
    }

    // The time reading_keys keeps with the soil Reading of a frame.
    private async Task<DateTimeOffset> KeyMeasuredAtAsync(Garden garden, NodeFrame frame) =>
        new(
            await identity.Database.ScalarAsync<DateTime>(
                "SELECT measured_at FROM reading_keys WHERE device_id = @device_id AND sensor_id = @sensor_id AND reading_seq = @seq",
                ("device_id", garden.Node.DeviceId.ToString()),
                ("sensor_id", garden.Soil),
                ("seq", (decimal)SeqOf(frame))),
            TimeSpan.Zero);

    private async Task<Downlink> ReportAsync(Garden garden, NodeFrame frame)
    {
        var result = await IngestAsync(garden.Node, frame);

        Assert.Equal(DeviceIngestStatus.Stored, result.Status);
        return garden.Node.OpenDownlink(SealedEnvelope.Parser.ParseFrom(result.Downlink));
    }

    // Seals the frame with the Node's next counter, also when it is sent again.
    private Task<DeviceIngestResult> IngestAsync(SimulatedDevice node, NodeFrame frame)
    {
        var envelope = node.SealFrame(frame);
        return identity.Device(node.DeviceId.ToString()).Ingest(
            new DeviceIngest(envelope.ProtocolVersion, envelope.Counter, envelope.Ciphertext.ToByteArray(), HubA),
            Ct);
    }

    private static ulong SeqOf(NodeFrame frame) => frame.Readings.Single(reading => reading.Slot == 0).ReadingSeq;

    private async Task<long> ReadingsAsync(Guid sensorId, NodeFrame frame) =>
        await identity.Database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM readings WHERE sensor_id = @id AND reading_seq = @seq",
            ("id", sensorId),
            ("seq", (decimal)SeqOf(frame)));

    private async Task<string> SeedLotAsync(string siteId, string name)
    {
        var lotId = Guid.CreateVersion7().ToString();
        Assert.True(await identity.Store.AppendAsync($"lot/{lotId}", 0, [new LotCreated(siteId, name)], Ct));
        return lotId;
    }

    // The Sensor's evaluation and delivery events, in order; its declaration, Calibration and Thresholds left out.
    private async Task<List<string>> EvaluationAliasesAsync(Guid sensorId) =>
        [.. (await identity.AliasesAsync($"sensor/{sensorId}")).Where(alias =>
            alias is "sensor.streak-changed" or "sensor.threshold-episode-opened" or "sensor.threshold-episode-closed" or "sensor.alert-delivered")];

    // The same events with their payloads.
    private async Task<List<object>> EvaluationEventsAsync(Guid sensorId) =>
        [.. (await identity.Store.ReadStreamAsync($"sensor/{sensorId}", Ct))
            .Select(journaled => journaled.Data)
            .Where(data => data is SensorStreakChanged or SensorThresholdEpisodeOpened or SensorThresholdEpisodeClosed or SensorAlertDelivered)];

    // The Sensor grain's state, replayed from its journal stream.
    private async Task<SensorState> SensorStateAsync(Guid sensorId)
    {
        var state = new SensorState();

        foreach (var @event in await identity.Store.ReadStreamAsync($"sensor/{sensorId}", Ct))
        {
            ((dynamic)state).Apply((dynamic)@event.Data);
        }

        return state;
    }

    private async Task<long> PositionAsync(string streamId, string alias) =>
        await identity.Database.ScalarAsync<long>(
            "SELECT MIN(position) FROM journal_events WHERE stream_id = @stream_id AND type_alias = @alias",
            ("stream_id", streamId),
            ("alias", alias));

    // Exactly one episode, one Alert and one entry in the Site's set, however often the delivery was tried.
    private async Task AssertOneAlertAsync(Garden garden)
    {
        var alertId = AlertIds.Threshold(garden.Soil, 1);
        var state = await SensorStateAsync(garden.Soil);

        Assert.Equal(1, state.Episode);
        Assert.Empty(state.PendingAlertDeliveries);
        Assert.Single(await EvaluationAliasesAsync(garden.Soil), alias => alias == "sensor.threshold-episode-opened");
        Assert.Equal(["alert.opened", "alert.site-notified"], await identity.AliasesAsync($"alert/{alertId}"));
        Assert.Equal(alertId, Assert.Single(await identity.Site(garden.SiteId).OpenAlerts(Ct)).AlertId);
        Assert.Single(await identity.AliasesAsync($"site/{garden.SiteId}"), alias => alias == "site.alert-opened");
        Assert.Empty(await identity.AliasesAsync($"alert/{AlertIds.Threshold(garden.Soil, 2)}"));
    }

    private Task CatchUpLotsAsync() => identity.SiloServices.GetProjectionRunner<LotsProjector>().CatchUpAsync(Ct);

    private async Task<List<(string LotId, string Status, DateTimeOffset StatusSince)>> LotStatusesAsync()
    {
        var lots = new List<(string, string, DateTimeOffset)>();
        await using var command = identity.Database.DataSource.CreateCommand("SELECT lot_id, status, status_since FROM lots ORDER BY lot_id");
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            lots.Add((reader.GetString(0), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2)));
        }

        return lots;
    }

    // A silo restart sets the fake clock back to its start; Readings must stay newer than the ones evaluated.
    private async Task RestartAsync()
    {
        var now = Now;
        await identity.RestartSiloAsync();
        identity.Time.SetUtcNow(now);
    }

    private sealed record Garden(string SiteId, string LotId, SimulatedDevice Node, Guid Soil, string OwnerId);
}
