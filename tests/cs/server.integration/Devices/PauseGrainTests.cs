using System.Security.Cryptography;
using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Lots;
using Coldframe.Contracts.Sensors;
using Coldframe.Contracts.Sites;
using Coldframe.Crypto;
using Coldframe.DeviceSimulator;
using Coldframe.Protocol.Device.V1;
using Coldframe.Server.Alerts;
using Coldframe.Server.IntegrationTests.Identity;
using Coldframe.Server.Journal;
using Coldframe.Server.Lots;
using Coldframe.Server.Sensors;
using Microsoft.Extensions.DependencyInjection;
using ProtocolKind = Coldframe.Protocol.Setup.V1.DeviceKind;

namespace Coldframe.Server.IntegrationTests.Devices;

/// <summary>
/// Pause on the Device grain, on a TestCluster with a fake clock (Story 8.1; AD-6, AD-8): a Device is paused and
/// resumed by command, holds its own source and its Site's apart, closes its own Alerts as <c>paused</c> and tells
/// its Sensors at once, discards frames while paused, lifts its own end date by itself, also across a restart,
/// and is not reported silent the moment a Pause ends. The Site's side is <c>SitePauseGrainTests</c>. Frames and
/// heartbeats come from the Device simulator only; the Device's reminder is fired by hand.
/// </summary>
[Collection(IngestSuites.Name)]
public sealed class PauseGrainTests(IdentityCluster identity) : IClassFixture<IdentityCluster>
{
    // A relay Hub no Site enrolled: it suppresses nothing.
    private const string UnknownHub = "92064422c012f481";

    private const string Paused = "device.paused";

    private const string Resumed = "device.resumed";

    private const string SilenceStarted = "device.silence-started";

    private const string SilenceEnded = "device.silence-ended";

    private const string BatteryEnded = "device.battery-low-ended";

    private const string Delivered = "device.alert-delivered";

    private const long DryRaw = 3000;

    private const long WetRaw = 1000;

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    private static readonly TimeSpan NodeWindow = TimeSpan.FromHours(6);

    private static readonly TimeSpan HubWindow = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTimeOffset Now => identity.Time.GetUtcNow();

    [Fact]
    public async Task ADeviceIsPausedAndResumedByCommandAndTheSameCommandAgainJournalsNothing()
    {
        var site = await SiteAsync();
        var (node, lotId) = await NodeAsync(site, "Beans");
        var nodeId = node.DeviceId.ToString();
        var stream = $"device/{nodeId}";
        var device = identity.Device(nodeId);
        var soil = node.SensorId(0, Quantity.SoilMoisture);
        var epoch = (await identity.DeviceStateAsync(nodeId)).EvaluationEpoch;
        var journaled = await identity.AliasesAsync(stream);

        // Paused without an end: one event, the next epoch, the Lot reads paused and the Sensor knows at once.
        var paused = await device.SetPause(site.SiteId, null, Ct);

        Assert.Equal(DevicePauseOutcome.Changed, paused.Outcome);
        Assert.Equal([DevicePauseSource.Device], paused.PausedBy);
        Assert.Null(paused.PausedUntil);
        Assert.Equal([Paused], (await identity.AliasesAsync(stream)).Skip(journaled.Count));
        var state = await identity.DeviceStateAsync(nodeId);
        Assert.Equal((true, epoch + 1), (state.IsPaused, state.EvaluationEpoch));

        // Read-your-writes: the lots row says so when the command returns, with no catch-up and no poll here.
        Assert.Equal("paused", await LotRowStatusAsync(lotId));
        Assert.Equal(new SensorContext(site.SiteId, lotId, true, epoch + 1), (await SensorStateAsync(soil)).Context);

        // The same Pause again: the same answer, nothing journaled, the epoch where it was.
        var again = await device.SetPause(site.SiteId, null, Ct);

        Assert.Equal(DevicePauseOutcome.Unchanged, again.Outcome);
        Assert.Equal(paused.PausedBy, again.PausedBy);
        Assert.Equal(journaled.Count + 1, (await identity.AliasesAsync(stream)).Count);
        Assert.Equal(epoch + 1, (await identity.DeviceStateAsync(nodeId)).EvaluationEpoch);

        // Another end is a change, the same end is none, and no end is a change again.
        var until = Now.AddDays(10);
        Assert.Equal((DevicePauseOutcome.Changed, (DateTimeOffset?)until), Answer(await device.SetPause(site.SiteId, until, Ct)));
        Assert.Equal((DevicePauseOutcome.Unchanged, (DateTimeOffset?)until), Answer(await device.SetPause(site.SiteId, until, Ct)));
        Assert.Equal((DevicePauseOutcome.Changed, (DateTimeOffset?)null), Answer(await device.SetPause(site.SiteId, null, Ct)));
        Assert.Equal(3, (await identity.AliasesAsync(stream)).Count(alias => alias == Paused));

        // Resumed: the Silence Window counts from now, and the Lot has its live status back.
        identity.Time.Advance(TimeSpan.FromHours(9));
        var resumed = await device.LiftPause(site.SiteId, Ct);

        Assert.Equal(DevicePauseOutcome.Changed, resumed.Outcome);
        Assert.Empty(resumed.PausedBy);
        Assert.Null(resumed.PausedUntil);
        state = await identity.DeviceStateAsync(nodeId);
        Assert.Equal((false, (DateTimeOffset?)Now), (state.IsPaused, state.SilenceFrom));
        Assert.Equal("needsCalibration", await LotRowStatusAsync(lotId));
        Assert.False((await SensorStateAsync(soil)).Context!.Paused);

        // Resuming a Device that is not paused journals nothing.
        var before = await identity.AliasesAsync(stream);
        Assert.Equal(DevicePauseOutcome.Unchanged, (await device.LiftPause(site.SiteId, Ct)).Outcome);
        Assert.Equal(before, await identity.AliasesAsync(stream));
        Assert.Single(before, alias => alias == Resumed);
    }

    [Fact]
    public async Task ADevicePausedByItselfAndByItsSiteResumesOnlyWhenBothAreLifted()
    {
        var site = await SiteAsync();
        var (node, lotId) = await NodeAsync(site, "Beans");
        var nodeId = node.DeviceId.ToString();
        var stream = $"device/{nodeId}";
        var device = identity.Device(nodeId);
        var lastReport = Now;
        var march = Now.AddDays(120);

        Assert.Equal(DevicePauseOutcome.Changed, (await device.SetPause(site.SiteId, null, Ct)).Outcome);
        identity.Time.Advance(TimeSpan.FromHours(1));

        // The Site's Pause is told, not commanded: the Device makes its site source match, once.
        Assert.Equal(DeviceSitePauseOutcome.Applied, await device.SetSitePause(site.SiteId, new SitePause(true, march, 1), Ct));
        var journaled = await identity.AliasesAsync(stream);
        var epoch = (await identity.DeviceStateAsync(nodeId)).EvaluationEpoch;
        Assert.Equal(DeviceSitePauseOutcome.Unchanged, await device.SetSitePause(site.SiteId, new SitePause(true, march, 1), Ct));

        // An older revision arriving late, and a Site the Device is not on, change nothing.
        Assert.Equal(DeviceSitePauseOutcome.Stale, await device.SetSitePause(site.SiteId, SitePause.NotPaused, Ct));
        Assert.Equal(DeviceSitePauseOutcome.NotEnrolled, await device.SetSitePause(Guid.CreateVersion7().ToString(), new SitePause(false, null, 9), Ct));
        Assert.Equal(journaled, await identity.AliasesAsync(stream));
        var state = await identity.DeviceStateAsync(nodeId);
        Assert.Equal(epoch, state.EvaluationEpoch);
        Assert.Equal(
            new Dictionary<DevicePauseSource, DateTimeOffset?> { [DevicePauseSource.Device] = null, [DevicePauseSource.Site] = march },
            state.PausedBy);

        // The Device's own source lifted: still paused, by the Site, and the silence clock has not moved.
        identity.Time.Advance(TimeSpan.FromHours(1));
        var lifted = await device.LiftPause(site.SiteId, Ct);

        Assert.Equal((DevicePauseOutcome.Changed, (DateTimeOffset?)march), Answer(lifted));
        Assert.Equal([DevicePauseSource.Site], lifted.PausedBy);
        state = await identity.DeviceStateAsync(nodeId);
        Assert.Equal((true, (DateTimeOffset?)lastReport), (state.IsPaused, state.SilenceFrom));
        Assert.Equal("paused", await LotStatusAsync(lotId));

        // Paused by itself again, then the Site resumes: still paused, by the Device.
        Assert.Equal(DevicePauseOutcome.Changed, (await device.SetPause(site.SiteId, null, Ct)).Outcome);
        Assert.Equal(DeviceSitePauseOutcome.Applied, await device.SetSitePause(site.SiteId, new SitePause(false, null, 2), Ct));
        state = await identity.DeviceStateAsync(nodeId);
        Assert.Equal((true, (DateTimeOffset?)lastReport), (state.IsPaused, state.SilenceFrom));
        Assert.Equal([DevicePauseSource.Device], state.PausedBy.Keys);
        Assert.Equal("paused", await LotStatusAsync(lotId));

        // Only the second lift resumes it.
        identity.Time.Advance(TimeSpan.FromHours(1));
        Assert.Empty((await device.LiftPause(site.SiteId, Ct)).PausedBy);
        state = await identity.DeviceStateAsync(nodeId);
        Assert.Equal((false, (DateTimeOffset?)Now), (state.IsPaused, state.SilenceFrom));
        Assert.Equal("needsCalibration", await LotStatusAsync(lotId));
    }

    [Fact]
    public async Task APausedNodeClosesItsSilentAndBatteryAlertsAsPausedAndOpensNoneHoweverLongItStaysQuiet()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, "Beans");
        var nodeId = node.DeviceId.ToString();
        var stream = $"device/{nodeId}";

        // Three low reports, then silence: both of the Node's own Alerts are open, beside its Sensor's.
        for (var report = 0; report < 3; report++)
        {
            identity.Time.Advance(Interval);
            await ReportAsync(node, node.Wake(Now, 14, ChargeStatus.NotCharging));
        }

        identity.Time.Advance(NodeWindow);
        await identity.WakeDeviceAsync(nodeId);
        var battery = AlertIds.Battery(nodeId, 1);
        var silent = AlertIds.Silent(nodeId, 1);
        var uncalibrated = AlertIds.Uncalibrated(node.SensorId(0, Quantity.SoilMoisture), 1);
        Assert.Equal(
            new[] { battery, silent, uncalibrated }.Order(),
            (await identity.Site(site.SiteId).OpenAlerts(Ct)).Select(alert => alert.AlertId).Order());
        var journaled = await identity.AliasesAsync(stream);

        identity.Time.Advance(TimeSpan.FromMinutes(7));
        var pausedAt = Now;
        Assert.Equal(DevicePauseOutcome.Changed, (await identity.Device(nodeId).SetPause(site.SiteId, null, Ct)).Outcome);

        // One write closes the Device's own episodes, and the Alert grain holds both closes before the call returns.
        Assert.Equal(
            [Paused, SilenceEnded, BatteryEnded, Delivered, Delivered],
            (await identity.AliasesAsync(stream)).Skip(journaled.Count));

        foreach (var alertId in new[] { battery, silent, uncalibrated })
        {
            var closed = (await identity.Alert(alertId).Describe(Ct))!;
            Assert.Equal((AlertLifecycle.Closed, AlertCloseReason.Paused, pausedAt), (closed.Lifecycle, closed.Reason, closed.ClosedAt));
        }

        Assert.Empty(await identity.Site(site.SiteId).OpenAlerts(Ct));
        var state = await identity.DeviceStateAsync(nodeId);
        Assert.Null(state.OpenSilentAlert);
        Assert.Null(state.OpenBatteryAlert);
        Assert.Empty(state.PendingAlertDeliveries);

        // A Pause is no recovery.
        Assert.Null(state.LastRecoveredAt);

        // The read model GET /sites/{siteId}/alerts serves shows each of them closed with reason paused.
        await identity.SiloServices.GetProjectionRunner<AlertsProjector>().CatchUpAsync(Ct);
        var alerts = identity.SiloServices.GetRequiredService<AlertsReadModel>();
        var listed = await alerts.ListHealthAlertsAsync(site.SiteId, Now - AlertsReadModel.ClosedRetention, Ct);
        Assert.Equal(["battery", "silent", "uncalibrated"], listed.Select(alert => alert.Kind).Order(StringComparer.Ordinal));
        Assert.All(listed, alert => Assert.Equal(((DateTimeOffset?)pausedAt, "paused"), (alert.ClosedAt, alert.Reason)));
        Assert.Equal(0, await alerts.CountOpenAsync(site.SiteId, Ct));

        // A winter of silence and of wakes: nothing opens.
        journaled = await identity.AliasesAsync(stream);

        for (var month = 0; month < 4; month++)
        {
            identity.Time.Advance(TimeSpan.FromDays(30));
            await identity.WakeDeviceAsync(nodeId);
        }

        Assert.Equal(journaled, await identity.AliasesAsync(stream));
        Assert.Empty(await identity.Site(site.SiteId).OpenAlerts(Ct));
    }

    [Fact]
    public async Task APauseClearsTheBatteryStreaksSoThatTheCountStartsOverAfterTheResume()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, "Beans");
        var nodeId = node.DeviceId.ToString();

        // Two low reports: one more would open the Alert.
        for (var report = 0; report < 2; report++)
        {
            identity.Time.Advance(Interval);
            await ReportAsync(node, node.Wake(Now, 14, ChargeStatus.NotCharging));
        }

        Assert.Equal(2, (await identity.DeviceStateAsync(nodeId)).BatteryLowStreak);

        Assert.Equal(DevicePauseOutcome.Changed, (await identity.Device(nodeId).SetPause(site.SiteId, null, Ct)).Outcome);
        Assert.Equal(0, (await identity.DeviceStateAsync(nodeId)).BatteryLowStreak);
        Assert.Equal(DevicePauseOutcome.Changed, (await identity.Device(nodeId).LiftPause(site.SiteId, Ct)).Outcome);

        // The third low report overall is the first after the Pause: no Alert.
        identity.Time.Advance(Interval);
        await ReportAsync(node, node.Wake(Now, 14, ChargeStatus.NotCharging));

        var state = await identity.DeviceStateAsync(nodeId);
        Assert.Equal((1, 0, (DeviceBatteryAlert?)null), (state.BatteryLowStreak, state.BatteryEpisode, state.OpenBatteryAlert));
    }

    [Fact]
    public async Task APausedNodeClosesItsSensorsThresholdAlertAsPausedAtOnce()
    {
        var site = await SiteAsync();
        var (node, lotId) = await NodeAsync(site, "Tomatoes");
        var nodeId = node.DeviceId.ToString();
        var soil = node.SensorId(0, Quantity.SoilMoisture);

        // Calibrated, then three Readings at 20 %: below the default low of 30 %.
        var dry = await ReadAsync(node, new SimulatedReading(0, Quantity.SoilMoisture, DryRaw));
        var wet = await ReadAsync(node, new SimulatedReading(0, Quantity.SoilMoisture, WetRaw));
        var calibrated = await identity.Sensor(soil).Calibrate(new CalibrateSensor(new CalibrationPointRef(SeqOf(dry)), new CalibrationPointRef(SeqOf(wet))), Ct);
        Assert.Equal(SensorCalibrationOutcome.Calibrated, calibrated.Outcome);

        for (var reading = 0; reading < 3; reading++)
        {
            await ReadAsync(node, Soil(20));
        }

        var threshold = AlertIds.Threshold(soil, 1);
        Assert.Equal(threshold, Assert.Single(await identity.Site(site.SiteId).OpenAlerts(Ct)).AlertId);
        Assert.Equal("needsWater", await LotStatusAsync(lotId));

        identity.Time.Advance(TimeSpan.FromMinutes(3));
        Assert.Equal(DevicePauseOutcome.Changed, (await identity.Device(nodeId).SetPause(site.SiteId, null, Ct)).Outcome);

        // No wake and no frame in between: the Sensor was told by the command itself.
        var closed = (await identity.Alert(threshold).Describe(Ct))!;
        Assert.Equal((AlertLifecycle.Closed, AlertCloseReason.Paused, Now), (closed.Lifecycle, closed.Reason, closed.ClosedAt));
        Assert.Empty(await identity.Site(site.SiteId).OpenAlerts(Ct));
        Assert.Equal("paused", await LotStatusAsync(lotId));

        // The read model GET /sites/{siteId}/alerts serves shows it closed with reason paused.
        await identity.SiloServices.GetProjectionRunner<AlertsProjector>().CatchUpAsync(Ct);
        var alerts = identity.SiloServices.GetRequiredService<AlertsReadModel>();
        var listed = Assert.Single((await alerts.ListAlertsAsync(site.SiteId, Now - AlertsReadModel.ClosedRetention, null, 50, Ct)).Alerts);
        Assert.Equal((threshold, (DateTimeOffset?)Now, "paused"), (listed.AlertId, listed.ClosedAt, listed.Reason));
    }

    [Fact]
    public async Task AFrameOfAPausedNodeIsAcknowledgedAndNothingIsKeptOrCounted()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, "Beans");
        var nodeId = node.DeviceId.ToString();
        var stream = $"device/{nodeId}";
        var lastReport = Now;
        Assert.Equal(DevicePauseOutcome.Changed, (await identity.Device(nodeId).SetPause(site.SiteId, null, Ct)).Outcome);
        var journaled = await identity.AliasesAsync(stream);
        var readings = await CountAsync("readings", node);
        var reports = await CountAsync("device_reports", node);

        // Four frames that would open a battery Alert and cross a Threshold: answered as ever, and discarded.
        for (var frame = 0; frame < 4; frame++)
        {
            identity.Time.Advance(Interval);
            var sent = node.Wake(Now, 9, ChargeStatus.NotCharging);
            var result = await IngestAsync(node, sent);

            Assert.Equal(DeviceIngestStatus.Stored, result.Status);
            var downlink = node.OpenDownlink(SealedEnvelope.Parser.ParseFrom(result.Downlink));
            Assert.Equal(SimulatedDevice.ReadingSeqs(sent), SimulatedDevice.AckedReadings(downlink));
        }

        Assert.Equal((readings, reports), (await CountAsync("readings", node), await CountAsync("device_reports", node)));
        Assert.Equal(journaled, await identity.AliasesAsync(stream));
        Assert.Equal((DateTimeOffset?)lastReport, (await identity.DeviceStateAsync(nodeId)).LastReportedAt);
        Assert.Empty(await identity.Site(site.SiteId).OpenAlerts(Ct));
    }

    [Fact]
    public async Task APauseWithAnEndLiftsOnTheFirstWakeAtItsDueAtAndTheSilenceWindowCountsFromThere()
    {
        var site = await SiteAsync();
        var (node, lotId) = await NodeAsync(site, "Beans");
        var nodeId = node.DeviceId.ToString();
        var stream = $"device/{nodeId}";
        var until = Now.AddDays(19);
        Assert.Equal((DevicePauseOutcome.Changed, (DateTimeOffset?)until), Answer(await identity.Device(nodeId).SetPause(site.SiteId, until, Ct)));

        // A second before the due-at, and quiet for 19 days: paused, nothing journaled.
        var journaled = await identity.AliasesAsync(stream);
        identity.Time.SetUtcNow(until - Second);
        await identity.WakeDeviceAsync(nodeId);
        Assert.Equal(journaled, await identity.AliasesAsync(stream));
        Assert.Equal("paused", await LotStatusAsync(lotId));

        // At the due-at the wake lifts it. The Node is not reported silent the moment its Pause ends.
        identity.Time.Advance(Second);
        await identity.WakeDeviceAsync(nodeId);

        Assert.Equal([Resumed], (await identity.AliasesAsync(stream)).Skip(journaled.Count));
        var state = await identity.DeviceStateAsync(nodeId);
        Assert.Equal((false, (DateTimeOffset?)until), (state.IsPaused, state.SilenceFrom));
        Assert.False((await SensorStateAsync(node.SensorId(0, Quantity.SoilMoisture))).Context!.Paused);
        Assert.Equal("needsCalibration", await LotStatusAsync(lotId));
        Assert.DoesNotContain(await identity.Site(site.SiteId).OpenAlerts(Ct), alert => alert.Kind == AlertKind.Silent);

        // A full Silence Window after the resume, and not a second before, it is.
        identity.Time.Advance(NodeWindow - Second);
        await identity.WakeDeviceAsync(nodeId);
        Assert.DoesNotContain(SilenceStarted, await identity.AliasesAsync(stream));

        identity.Time.Advance(Second);
        await identity.WakeDeviceAsync(nodeId);
        Assert.Contains(await identity.Site(site.SiteId).OpenAlerts(Ct), alert => alert.AlertId == AlertIds.Silent(nodeId, 1));
    }

    [Fact]
    public async Task APauseWhoseEndPassedWhileTheServerWasDownLiftsWhenTheDeviceIsActivated()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, "Beans");
        var nodeId = node.DeviceId.ToString();
        var stream = $"device/{nodeId}";
        var until = Now.AddDays(10);
        Assert.Equal(DevicePauseOutcome.Changed, (await identity.Device(nodeId).SetPause(site.SiteId, until, Ct)).Outcome);

        var readings = await CountAsync("readings", node);

        // The Server comes back three days after the end, and the first thing that reaches the Node's grain is a
        // frame: the due-at is in the journal, the Pause is lifted before the frame is looked at, and the frame
        // is kept.
        var restartedAt = until.AddDays(3);
        await identity.RestartSiloAsync(restartedAt);
        var first = await IngestAsync(node, node.Wake(Now));

        Assert.Equal(DeviceIngestStatus.Stored, first.Status);
        Assert.Equal(readings + 4, await CountAsync("readings", node));
        Assert.Single(await identity.AliasesAsync(stream), alias => alias == Resumed);
        var state = await identity.DeviceStateAsync(nodeId);
        Assert.Equal((false, (DateTimeOffset?)restartedAt, (DateTimeOffset?)restartedAt), (state.IsPaused, state.SilenceFrom, state.LastReportedAt));

        // Thirteen quiet days are no silence: only a full window after the resume is.
        identity.Time.Advance(NodeWindow - Second);
        await identity.WakeDeviceAsync(nodeId);
        Assert.DoesNotContain(SilenceStarted, await identity.AliasesAsync(stream));

        identity.Time.Advance(Second);
        await identity.WakeDeviceAsync(nodeId);
        Assert.Contains(SilenceStarted, await identity.AliasesAsync(stream));
    }

    [Fact]
    public async Task AFrameOrAHeartbeatThatArrivesAfterTheEndAndBeforeAnyWakeLiftsThePauseFirst()
    {
        var site = await SiteAsync();
        var hub = await HubAsync(site);
        var hubId = hub.DeviceId.ToString();
        var (node, lotId) = await NodeAsync(site, "Beans");
        var nodeId = node.DeviceId.ToString();
        var until = Now.AddDays(5);
        Assert.Equal(DevicePauseOutcome.Changed, (await identity.Device(nodeId).SetPause(site.SiteId, until, Ct)).Outcome);
        Assert.Equal(DevicePauseOutcome.Changed, (await identity.Device(hubId).SetPause(site.SiteId, until, Ct)).Outcome);
        var readings = await CountAsync("readings", node);

        // A second before the end the Node's frame is still discarded.
        identity.Time.SetUtcNow(until - Second);
        Assert.Equal(DeviceIngestStatus.Stored, (await IngestAsync(node, node.Wake(Now))).Status);
        Assert.Equal(readings, await CountAsync("readings", node));

        // At the end, with no wake in between: the frame is kept, the Sensor and the Lot are told.
        identity.Time.Advance(Second);
        Assert.Equal(DeviceIngestStatus.Stored, (await IngestAsync(node, node.Wake(Now))).Status);

        Assert.Equal(readings + 4, await CountAsync("readings", node));
        Assert.Single(await identity.AliasesAsync($"device/{nodeId}"), alias => alias == Resumed);
        var state = await identity.DeviceStateAsync(nodeId);
        Assert.Equal((false, (DateTimeOffset?)until), (state.IsPaused, state.LastReportedAt));
        Assert.False((await SensorStateAsync(node.SensorId(0, Quantity.SoilMoisture))).Context!.Paused);
        Assert.Equal("needsCalibration", await LotRowStatusAsync(lotId));

        // The Hub's heartbeat does the same for the Hub.
        await HeartbeatAsync(hub);

        Assert.Single(await identity.AliasesAsync($"device/{hubId}"), alias => alias == Resumed);
        Assert.False((await identity.DeviceStateAsync(hubId)).IsPaused);
    }

    // The guard against a Site's Pause that arrives late has two halves: what this activation was told, also by
    // a delivery that journaled nothing, and what the Device journaled, which a new activation reads.
    [Fact]
    public async Task ARevisionOlderThanOneTheDeviceWasToldOrJournaledIsStale()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, "Beans");
        var nodeId = node.DeviceId.ToString();
        var stream = $"device/{nodeId}";
        var device = identity.Device(nodeId);

        Assert.Equal(DeviceSitePauseOutcome.Applied, await device.SetSitePause(site.SiteId, new SitePause(true, null, 1), Ct));

        // Revision 3 says what the Device holds: nothing is journaled, and the Device has heard it all the same.
        var journaled = await identity.AliasesAsync(stream);
        Assert.Equal(DeviceSitePauseOutcome.Unchanged, await device.SetSitePause(site.SiteId, new SitePause(true, null, 3), Ct));
        Assert.Equal(1, (await identity.DeviceStateAsync(nodeId)).SitePauseRevision);

        // The resume of revision 2 arrives after it: stale, by what this activation was told.
        Assert.Equal(DeviceSitePauseOutcome.Stale, await device.SetSitePause(site.SiteId, new SitePause(false, null, 2), Ct));
        Assert.Equal(journaled, await identity.AliasesAsync(stream));
        Assert.True((await identity.DeviceStateAsync(nodeId)).IsPaused);

        // Revision 4 resumes it, journaled with its revision. A new silo knows nothing but the journal.
        Assert.Equal(DeviceSitePauseOutcome.Applied, await device.SetSitePause(site.SiteId, new SitePause(false, null, 4), Ct));
        Assert.Equal(4, (await identity.DeviceStateAsync(nodeId)).SitePauseRevision);
        journaled = await identity.AliasesAsync(stream);
        await identity.RestartSiloAsync(Now);

        Assert.Equal(DeviceSitePauseOutcome.Stale, await identity.Device(nodeId).SetSitePause(site.SiteId, new SitePause(true, null, 3), Ct));
        Assert.Equal(journaled.Count(alias => alias is Paused or Resumed), (await identity.AliasesAsync(stream)).Count(alias => alias is Paused or Resumed));
        Assert.False((await identity.DeviceStateAsync(nodeId)).IsPaused);
    }

    [Fact]
    public async Task APausedHubsClosedAlertIsNoRecoveryForTheNodesItRelays()
    {
        var site = await SiteAsync();
        var hub = await HubAsync(site);
        var hubId = hub.DeviceId.ToString();
        await HeartbeatAsync(hub);
        var (node, _) = await NodeAsync(site, "Beans", hubId);
        var nodeId = node.DeviceId.ToString();
        var lastReport = Now;

        // The Hub goes silent, and the Node's window ends behind it: one Alert, the Hub's.
        identity.Time.Advance(NodeWindow);
        await identity.WakeDeviceAsync(hubId);
        await identity.WakeDeviceAsync(nodeId);
        var hubAlert = AlertIds.Silent(hubId, 1);
        Assert.Equal([hubAlert], SilentAlerts(await identity.Site(site.SiteId).OpenAlerts(Ct)));

        // The Hub is paused: its Alert closes as paused, which is not the Hub reporting again.
        Assert.Equal(DevicePauseOutcome.Changed, (await identity.Device(hubId).SetPause(site.SiteId, null, Ct)).Outcome);
        var closed = (await identity.Alert(hubAlert).Describe(Ct))!;
        Assert.Equal((AlertLifecycle.Closed, (AlertCloseReason?)AlertCloseReason.Paused), (closed.Lifecycle, closed.Reason));
        Assert.Null((await identity.Device(hubId).Silence(Ct))!.LastRecoveredAt);

        // The Node is not paused with its Hub and is no longer excused by it: its own Alert opens, and its clock
        // was never restarted.
        await identity.WakeDeviceAsync(nodeId);

        Assert.Equal([AlertIds.Silent(nodeId, 1)], SilentAlerts(await identity.Site(site.SiteId).OpenAlerts(Ct)));
        var state = await identity.DeviceStateAsync(nodeId);
        Assert.Equal((false, (DateTimeOffset?)lastReport), (state.IsPaused, state.SilenceFrom));
        Assert.DoesNotContain("device.silence-clock-restarted", await identity.AliasesAsync($"device/{nodeId}"));
    }

    [Fact]
    public async Task ADeviceOfAnotherSiteAnUnenrolledOneAndAnEndThatIsNotAheadAreRefusedAndJournalNothing()
    {
        var site = await SiteAsync();
        var other = await SiteAsync();
        var (node, _) = await NodeAsync(site, "Beans");
        var nodeId = node.DeviceId.ToString();
        var stream = $"device/{nodeId}";
        var journaled = await identity.AliasesAsync(stream);
        var unenrolled = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

        Assert.Equal(DevicePauseOutcome.NotFound, (await identity.Device(nodeId).SetPause(other.SiteId, null, Ct)).Outcome);
        Assert.Equal(DevicePauseOutcome.NotFound, (await identity.Device(nodeId).LiftPause(other.SiteId, Ct)).Outcome);
        Assert.Equal(DevicePauseOutcome.NotFound, (await identity.Device(unenrolled).SetPause(site.SiteId, null, Ct)).Outcome);
        Assert.Equal(DevicePauseOutcome.InvalidEnd, (await identity.Device(nodeId).SetPause(site.SiteId, Now, Ct)).Outcome);
        Assert.Equal(DevicePauseOutcome.InvalidEnd, (await identity.Device(nodeId).SetPause(site.SiteId, Now.AddDays(-1), Ct)).Outcome);

        Assert.Equal(journaled, await identity.AliasesAsync(stream));
        Assert.Empty(await identity.AliasesAsync($"device/{unenrolled}"));
        Assert.False((await identity.DeviceStateAsync(nodeId)).IsPaused);
    }

    private static (DevicePauseOutcome Outcome, DateTimeOffset? PausedUntil) Answer(DevicePauseResult result) => (result.Outcome, result.PausedUntil);

    private static List<Guid> SilentAlerts(IEnumerable<SiteAlert> alerts) =>
        [.. alerts.Where(alert => alert.Kind == AlertKind.Silent).Select(alert => alert.AlertId)];

    private static SimulatedReading Soil(int percent) => new(0, Quantity.SoilMoisture, DryRaw - ((DryRaw - WetRaw) * percent / 100));

    private static ulong SeqOf(NodeFrame frame) => frame.Readings.Single(reading => reading.Slot == 0).ReadingSeq;

    // An Active Site created by its Owner, whose User grain therefore holds the Site and is told of its Alerts.
    private async Task<(string SiteId, string OwnerId)> SiteAsync()
    {
        var ownerId = Guid.NewGuid().ToString();
        var created = await identity.User(ownerId).CreateSite("k1", "Home", Ct);
        Assert.Equal(SiteCreationOutcome.Created, created.Outcome);
        return (created.Site!.Id, ownerId);
    }

    // An enrolled Hub of the Site. It has not sent a heartbeat yet.
    private async Task<SimulatedDevice> HubAsync((string SiteId, string OwnerId) site)
    {
        var hub = SimulatedDevice.Create(ProtocolKind.Hub);
        var enrol = new EnrolDevice(site.SiteId, DeviceKind.Hub, identity.Vault.Wrap(hub.DeviceId, hub.Keys.DeviceKey), site.OwnerId, Guid.NewGuid().ToString("N"));
        Assert.Equal(DeviceEnrolmentOutcome.Enrolled, (await identity.Device(hub.DeviceId.ToString()).Enrol(enrol, Ct)).Outcome);
        return hub;
    }

    // A Node enrolled on a new Lot of the Site that declared its Sensors through `hubId`: its last report is now.
    private async Task<(SimulatedDevice Node, string LotId)> NodeAsync((string SiteId, string OwnerId) site, string lotName, string hubId = UnknownHub)
    {
        var lotId = Guid.CreateVersion7().ToString();
        Assert.True(await identity.Store.AppendAsync($"lot/{lotId}", 0, [new LotCreated(site.SiteId, lotName)], Ct));

        var node = SimulatedDevice.Create(ProtocolKind.Node);
        var enrol = new EnrolDevice(site.SiteId, DeviceKind.Node, identity.Vault.Wrap(node.DeviceId, node.Keys.DeviceKey), site.OwnerId, Guid.NewGuid().ToString("N"), lotId);
        Assert.Equal(DeviceEnrolmentOutcome.Enrolled, (await identity.Device(node.DeviceId.ToString()).Enrol(enrol, Ct)).Outcome);

        Assert.True((await ReportAsync(node, node.Wake(Now), hubId)).SpecificationsUnknown);
        Assert.False((await ReportAsync(node, node.Wake(Now), hubId)).SpecificationsUnknown);

        return (node, lotId);
    }

    // One wake, 15 minutes after the last one, stored and acknowledged.
    private async Task<NodeFrame> ReadAsync(SimulatedDevice node, params SimulatedReading[] readings)
    {
        identity.Time.Advance(Interval);
        var frame = node.Wake(Now, readings: readings);
        await ReportAsync(node, frame);
        return frame;
    }

    private async Task<Downlink> ReportAsync(SimulatedDevice node, NodeFrame frame, string hubId = UnknownHub)
    {
        var result = await IngestAsync(node, frame, hubId);

        Assert.Equal(DeviceIngestStatus.Stored, result.Status);
        return node.OpenDownlink(SealedEnvelope.Parser.ParseFrom(result.Downlink));
    }

    private Task<DeviceIngestResult> IngestAsync(SimulatedDevice node, NodeFrame frame, string hubId = UnknownHub)
    {
        var envelope = node.SealFrame(frame);
        return identity.Device(node.DeviceId.ToString()).Ingest(
            new DeviceIngest(envelope.ProtocolVersion, envelope.Counter, envelope.Ciphertext.ToByteArray(), hubId),
            Ct);
    }

    // One signed heartbeat of the Hub, stamped now, as the Edge API hands it to the grain.
    private async Task HeartbeatAsync(SimulatedDevice hub)
    {
        var body = SimulatedDevice.HeartbeatBody(61_000);
        var nonce = RandomNumberGenerator.GetBytes(CryptoSpec.HeartbeatNonceLength);
        var timestampMs = Now.ToUnixTimeMilliseconds();
        var headers = hub.SignHeartbeat("POST", SimulatedDevice.HeartbeatPath, body, timestampMs, nonce);

        var result = await identity.Device(hub.DeviceId.ToString()).Heartbeat(
            new DeviceHeartbeat(
                "POST",
                SimulatedDevice.HeartbeatPath,
                body,
                timestampMs,
                nonce,
                Convert.FromHexString(headers[CryptoSpec.HeartbeatSignatureHeader]),
                61_000),
            Ct);

        Assert.Equal(DeviceHeartbeatOutcome.Accepted, result.Outcome);
    }

    private async Task<long> CountAsync(string table, SimulatedDevice device) =>
        await identity.Database.ScalarAsync<long>($"SELECT COUNT(*) FROM {table} WHERE device_id = @id", ("id", device.DeviceId.ToString()));

    private async Task<SensorState> SensorStateAsync(Guid sensorId)
    {
        var state = new SensorState();

        foreach (var @event in await identity.Store.ReadStreamAsync($"sensor/{sensorId}", Ct))
        {
            ((dynamic)state).Apply((dynamic)@event.Data);
        }

        return state;
    }

    // The Lot's status as the lots row holds it right now: nothing here brings the projection up to date.
    private async Task<string> LotRowStatusAsync(string lotId) =>
        (await identity.Database.ScalarAsync<string>("SELECT status FROM lots WHERE lot_id = @lot_id", ("lot_id", lotId)))!;

    // The Lot's status, once the lots projection has caught up.
    private async Task<string> LotStatusAsync(string lotId)
    {
        await identity.SiloServices.GetProjectionRunner<LotsProjector>().CatchUpAsync(Ct);

        return (await identity.Database.ScalarAsync<string>("SELECT status FROM lots WHERE lot_id = @lot_id", ("lot_id", lotId)))!;
    }
}
