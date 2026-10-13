using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Lots;
using Coldframe.Contracts.Notifications;
using Coldframe.Contracts.Sensors;
using Coldframe.Contracts.Sites;
using Coldframe.DeviceSimulator;
using Coldframe.Protocol.Device.V1;
using Coldframe.Server.Alerts;
using Coldframe.Server.Devices;
using Coldframe.Server.IntegrationTests.Identity;
using Coldframe.Server.IntegrationTests.Journal;
using Coldframe.Server.Journal;
using Coldframe.Server.Lots;
using Coldframe.Server.Notifications.Push;
using Coldframe.Server.Sensors;
using Microsoft.Extensions.DependencyInjection;
using ProtocolKind = Coldframe.Protocol.Setup.V1.DeviceKind;

namespace Coldframe.Server.IntegrationTests.Devices;

/// <summary>
/// The uncalibrated Sensor Alert on a TestCluster with a fake clock (Story 7.4; AD-5, AD-8, AD-19): the Device
/// grain tells each of its Sensor grains where the Node is (Site, Lot or none, paused, epoch); the Sensor grain
/// journals that context and opens a Health Alert of kind <c>uncalibrated</c> while it needs calibration on a Lot
/// and is not paused, closes it when a Calibration is saved or the context ends, and closes its Threshold Alert
/// on an unassigned or paused context. Frames come from the Device simulator only, 15 minutes of
/// <c>measured_at</c> apart.
/// </summary>
[Collection(IngestSuites.Name)]
public sealed class UncalibratedAlertGrainTests(IdentityCluster identity) : IClassFixture<IdentityCluster>
{
    private const string HubA = "92064422c012f481";

    private const string ContextChanged = "sensor.context-changed";

    private const string Opened = "sensor.uncalibrated-episode-opened";

    private const string Closed = "sensor.uncalibrated-episode-closed";

    private const string Delivered = "sensor.alert-delivered";

    // The soil probe is calibrated dry 3000, wet 1000: 20 raw counts per percent.
    private const long DryRaw = 3000;

    private const long WetRaw = 1000;

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTimeOffset Now => identity.Time.GetUtcNow();

    [Fact]
    public async Task ANodeOnALotOpensOneUncalibratedAlertWithTheFrameThatDeclaresItsSoilSensor()
    {
        var site = await SiteAsync();
        var lotId = await SeedLotAsync(site.SiteId, "Peppers");
        var node = await EnrolAsync(site, lotId);
        var nodeId = node.DeviceId.ToString();
        var soil = node.SensorId(0, Quantity.SoilMoisture);

        // The first frame declares nothing: the Server does not know the Node's Sensors yet.
        Assert.True((await ReportAsync(node, node.Wake(Now))).SpecificationsUnknown);
        Assert.Empty(await identity.Site(site.SiteId).OpenAlerts(Ct));
        Assert.Empty(await identity.AliasesAsync($"sensor/{soil}"));

        // The next one carries the Specification set: declared, told where the Node is, and the Alert is open.
        identity.Time.Advance(Interval);
        Assert.False((await ReportAsync(node, node.Wake(Now))).SpecificationsUnknown);
        var openedAt = Now;

        var alertId = AlertIds.Uncalibrated(soil, 1);
        var stream = await identity.Store.ReadStreamAsync($"sensor/{soil}", Ct);
        Assert.Equal(["sensor.declared", ContextChanged, Opened, Delivered], await identity.AliasesAsync($"sensor/{soil}"));
        Assert.Equal(new SensorContextChanged(site.SiteId, lotId, false, 1, openedAt), stream[1].Data);
        Assert.Equal(new SensorUncalibratedEpisodeOpened(1, alertId, site.SiteId, lotId, 1, openedAt), stream[2].Data);

        // The Alert carries its Site, Lot, Sensor, quantity, Device and Device kind, and no side.
        Assert.Equal(
            new AlertSnapshot(alertId, AlertKind.Uncalibrated, AlertLifecycle.Open, null, site.SiteId, lotId, soil, nodeId, "soil_moisture", openedAt, DeviceKind: DeviceKind.Node),
            await identity.Alert(alertId).Describe(Ct));
        Assert.Equal(["alert.opened", "alert.site-notified"], await identity.AliasesAsync($"alert/{alertId}"));

        // The episode is in the journal before the Alert is.
        Assert.True(
            await PositionAsync($"sensor/{soil}", Opened) < await PositionAsync($"alert/{alertId}", "alert.opened"),
            "The Sensor journals the episode before the Alert grain journals the Alert.");

        // The Site lists it and its member was told.
        Assert.Equal(
            new SiteAlert(alertId, AlertKind.Uncalibrated, null, lotId, soil, nodeId, "soil_moisture", openedAt, DeviceKind.Node),
            Assert.Single(await identity.Site(site.SiteId).OpenAlerts(Ct)));
        Assert.Equal(alertId, Assert.Single((await identity.UserStateAsync(site.OwnerId)).TrackedAlerts.Values).Alert.AlertId);

        // The Node's other Sensors require no Calibration: they hold the context and open nothing.
        foreach (var (slot, quantity) in new[] { (1u, Quantity.AirTemperature), (2u, Quantity.RelativeHumidity), (3u, Quantity.GasResistance) })
        {
            var other = node.SensorId(slot, quantity);
            Assert.Equal(["sensor.declared", ContextChanged], await identity.AliasesAsync($"sensor/{other}"));
            Assert.Empty(await identity.AliasesAsync($"alert/{AlertIds.Uncalibrated(other, 1)}"));
        }

        // Later reports change nothing: one context per epoch, one Alert per episode.
        await ReadAsync(node);
        await ReadAsync(node);
        Assert.Equal(["sensor.declared", ContextChanged, Opened, Delivered], await identity.AliasesAsync($"sensor/{soil}"));
        Assert.Single(await identity.Site(site.SiteId).OpenAlerts(Ct));
    }

    [Fact]
    public async Task ANodeDeclaredOnNoLotOpensNothingUntilItIsAssigned()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, lotName: null);
        var soil = node.SensorId(0, Quantity.SoilMoisture);

        // On no Lot: the Sensor knows it, and nobody is alerted.
        Assert.Equal(["sensor.declared", ContextChanged], await identity.AliasesAsync($"sensor/{soil}"));
        Assert.Equal(new SensorContext(site.SiteId, null, false, 0), (await SensorStateAsync(soil)).Context);
        await ReadAsync(node);
        Assert.Empty(await identity.Site(site.SiteId).OpenAlerts(Ct));

        // Assigned: one Alert, naming the Lot.
        var peppers = await SeedLotAsync(site.SiteId, "Peppers");
        Assert.Equal(DeviceAssignmentOutcome.Moved, (await identity.Device(node.DeviceId.ToString()).Move(site.SiteId, peppers, Ct)).Outcome);

        var open = Assert.Single(await identity.Site(site.SiteId).OpenAlerts(Ct));
        Assert.Equal((AlertIds.Uncalibrated(soil, 1), AlertKind.Uncalibrated, peppers), (open.AlertId, open.Kind, open.LotId));
        Assert.Equal(new SensorContext(site.SiteId, peppers, false, 1), (await SensorStateAsync(soil)).Context);
    }

    [Fact]
    public async Task ANodeGivenItsLotByASecondEnrolmentTellsItsSensorsAtOnce()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, lotName: null);
        var nodeId = node.DeviceId.ToString();
        var soil = node.SensorId(0, Quantity.SoilMoisture);
        Assert.Empty(await identity.Site(site.SiteId).OpenAlerts(Ct));

        // Enrolled again, now with a Lot: the enrolment claims it, and the Node has Sensors to tell.
        var peppers = await SeedLotAsync(site.SiteId, "Peppers");
        var enrol = new EnrolDevice(site.SiteId, DeviceKind.Node, identity.Vault.Wrap(node.DeviceId, node.Keys.DeviceKey), site.OwnerId, Guid.NewGuid().ToString("N"), peppers);
        var enrolled = await identity.Device(nodeId).Enrol(enrol, Ct);
        Assert.Equal((DeviceEnrolmentOutcome.Enrolled, peppers), (enrolled.Outcome, enrolled.Device?.LotId));

        // No frame and no wake: the enrolment itself sent the context.
        Assert.Equal(new SensorContext(site.SiteId, peppers, false, 1), (await SensorStateAsync(soil)).Context);
        var open = Assert.Single(await identity.Site(site.SiteId).OpenAlerts(Ct));
        Assert.Equal((AlertIds.Uncalibrated(soil, 1), AlertKind.Uncalibrated, peppers), (open.AlertId, open.Kind, open.LotId));
    }

    [Fact]
    public async Task ACloseTheAlertGrainDidNotTakeLeavesTheCalibrationAnswerAsItIsAndIsDeliveredAgainByTheSensor()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, "Peppers");
        var soil = node.SensorId(0, Quantity.SoilMoisture);
        var alertId = AlertIds.Uncalibrated(soil, 1);
        var dry = await ReadAsync(node, new SimulatedReading(0, Quantity.SoilMoisture, DryRaw));
        var wet = await ReadAsync(node, new SimulatedReading(0, Quantity.SoilMoisture, WetRaw));

        // The Alert grain cannot be reached while the Calibration is saved.
        identity.AlertFaults.FailNext(int.MaxValue);

        try
        {
            var calibrated = await identity.Sensor(soil).Calibrate(
                new CalibrateSensor(new CalibrationPointRef(SeqOf(dry)), new CalibrationPointRef(SeqOf(wet))),
                Ct);

            // The Calibration is saved and in force on the Device; the episode ended with its close pending.
            Assert.Equal(SensorCalibrationOutcome.Calibrated, calibrated.Outcome);
            var state = await SensorStateAsync(soil);
            Assert.Null(state.OpenUncalibratedAlert);
            var pending = Assert.Single(state.PendingAlertDeliveries);
            Assert.Equal(
                (alertId, AlertLifecycle.Closed, AlertKind.Uncalibrated, (AlertCloseReason?)AlertCloseReason.Calibrated),
                (pending.AlertId, pending.Change, pending.Kind, pending.Reason));
            Assert.Equal(AlertLifecycle.Open, (await identity.Alert(alertId).Describe(Ct))!.Lifecycle);
        }
        finally
        {
            identity.AlertFaults.FailNext(0);
        }

        // Nobody calls the Sensor: its delivery timer does.
        await JournalWait.UntilAsync(
            async () => (await identity.Site(site.SiteId).OpenAlerts(Ct)).Count == 0
                && (await SensorStateAsync(soil)).PendingAlertDeliveries.Count == 0,
            "the Sensor delivered the close of its uncalibrated Alert by itself");

        var closed = (await identity.Alert(alertId).Describe(Ct))!;
        Assert.Equal((AlertLifecycle.Closed, AlertCloseReason.Calibrated), (closed.Lifecycle, closed.Reason));
        Assert.Equal(["alert.opened", "alert.site-notified", "alert.closed", "alert.site-notified"], await identity.AliasesAsync($"alert/{alertId}"));
        Assert.Single(await identity.AliasesAsync($"sensor/{soil}"), alias => alias == Closed);
    }

    [Fact]
    public async Task TheSamePairSentAgainDeliversTheCloseTheAlertGrainDidNotTake()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, "Peppers");
        var soil = node.SensorId(0, Quantity.SoilMoisture);
        var alertId = AlertIds.Uncalibrated(soil, 1);
        var dry = await ReadAsync(node, new SimulatedReading(0, Quantity.SoilMoisture, DryRaw));
        var wet = await ReadAsync(node, new SimulatedReading(0, Quantity.SoilMoisture, WetRaw));
        var pair = new CalibrateSensor(new CalibrationPointRef(SeqOf(dry)), new CalibrationPointRef(SeqOf(wet)));

        identity.AlertFaults.FailNext(int.MaxValue);

        try
        {
            Assert.Equal(SensorCalibrationOutcome.Calibrated, (await identity.Sensor(soil).Calibrate(pair, Ct)).Outcome);
            Assert.Single((await SensorStateAsync(soil)).PendingAlertDeliveries);
        }
        finally
        {
            identity.AlertFaults.FailNext(0);
        }

        // The request is sent again, as a client does after a slow answer: it saves nothing, and it delivers the
        // close before it answers, without waiting for the Sensor's timer.
        Assert.Equal(SensorCalibrationOutcome.Calibrated, (await identity.Sensor(soil).Calibrate(pair, Ct)).Outcome);

        Assert.Empty((await SensorStateAsync(soil)).PendingAlertDeliveries);
        var closed = (await identity.Alert(alertId).Describe(Ct))!;
        Assert.Equal((AlertLifecycle.Closed, AlertCloseReason.Calibrated), (closed.Lifecycle, closed.Reason));
        Assert.Empty(await identity.Site(site.SiteId).OpenAlerts(Ct));
        Assert.Single(await identity.AliasesAsync($"sensor/{soil}"), alias => alias == "sensor.calibrated");
    }

    [Fact]
    public async Task AContextLostBeforeASiloRestartIsSentAgainOnceTheDeviceIsActivatedWithNobodyWakingIt()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, "Peppers");
        var nodeId = node.DeviceId.ToString();
        var soil = node.SensorId(0, Quantity.SoilMoisture);
        var alertId = AlertIds.Uncalibrated(soil, 1);

        identity.SensorFaults.FailNextContexts(int.MaxValue);

        try
        {
            Assert.Equal(DeviceAssignmentOutcome.Unassigned, (await identity.Device(nodeId).Unassign(site.SiteId, Ct)).Outcome);
            Assert.Equal(AlertLifecycle.Open, (await identity.Alert(alertId).Describe(Ct))!.Lifecycle);
        }
        finally
        {
            identity.SensorFaults.FailNextContexts(0);
        }

        // Which Sensor confirmed the context was only in memory. The new silo activates the Device with a read
        // that sends nothing itself: no wake and no frame. The activation sends the context a moment later.
        await identity.RestartSiloAsync(Now);
        Assert.Null((await identity.Device(nodeId).Describe(Ct))!.LotId);

        await JournalWait.UntilAsync(
            async () => (await identity.Alert(alertId).Describe(Ct))?.Lifecycle == AlertLifecycle.Closed
                && (await SensorStateAsync(soil)).PendingAlertDeliveries.Count == 0,
            "the activated Device sent its Sensors the context it had lost");

        Assert.Equal(AlertCloseReason.Unassigned, (await identity.Alert(alertId).Describe(Ct))!.Reason);
        Assert.Equal(new SensorContext(site.SiteId, null, false, 2), (await SensorStateAsync(soil)).Context);
        Assert.Empty(await identity.Site(site.SiteId).OpenAlerts(Ct));
    }

    [Fact]
    public async Task SavingTheCalibrationClosesTheAlertAsCalibratedAndASinglePointLeavesItOpen()
    {
        var site = await SiteAsync();
        var (node, lotId) = await NodeAsync(site, "Peppers");
        var soil = node.SensorId(0, Quantity.SoilMoisture);
        var alertId = AlertIds.Uncalibrated(soil, 1);
        var dry = await ReadAsync(node, new SimulatedReading(0, Quantity.SoilMoisture, DryRaw));
        var wet = await ReadAsync(node, new SimulatedReading(0, Quantity.SoilMoisture, WetRaw));

        // One point: the Sensor is as uncalibrated as it was.
        var point = await identity.Sensor(soil).Calibrate(new CalibrateSensor(Dry: new CalibrationPointRef(SeqOf(dry))), Ct);
        Assert.Equal(SensorCalibrationOutcome.PointRecorded, point.Outcome);
        Assert.Equal(AlertLifecycle.Open, (await identity.Alert(alertId).Describe(Ct))!.Lifecycle);
        Assert.Equal(alertId, Assert.Single(await identity.Site(site.SiteId).OpenAlerts(Ct)).AlertId);

        // The second point saves the Calibration: the Alert closes with it.
        identity.Time.Advance(TimeSpan.FromMinutes(1));
        var closedAt = Now;
        var calibrated = await identity.Sensor(soil).Calibrate(new CalibrateSensor(Wet: new CalibrationPointRef(SeqOf(wet))), Ct);
        Assert.Equal(SensorCalibrationOutcome.Calibrated, calibrated.Outcome);

        var alert = (await identity.Alert(alertId).Describe(Ct))!;
        Assert.Equal((AlertLifecycle.Closed, AlertCloseReason.Calibrated, closedAt, lotId), (alert.Lifecycle, alert.Reason, alert.ClosedAt, alert.LotId));
        Assert.Equal(["alert.opened", "alert.site-notified", "alert.closed", "alert.site-notified"], await identity.AliasesAsync($"alert/{alertId}"));
        Assert.Empty(await identity.Site(site.SiteId).OpenAlerts(Ct));
        Assert.Empty((await identity.UserStateAsync(site.OwnerId)).TrackedAlerts);

        // The Calibration and the end of the episode are one write.
        var aliases = await identity.AliasesAsync($"sensor/{soil}");
        Assert.Equal(["sensor.calibrated", Closed, "sensor.calibration-delivered", Delivered], aliases[^4..]);
        Assert.Equal(new SensorUncalibratedEpisodeClosed(1, alertId, AlertCloseReason.Calibrated, closedAt), (await identity.Store.ReadStreamAsync($"sensor/{soil}", Ct))[^3].Data);
        var state = await SensorStateAsync(soil);
        Assert.Equal((1, (SensorUncalibratedAlert?)null, false), (state.UncalibratedEpisode, state.OpenUncalibratedAlert, state.NeedsCalibration));
        Assert.Empty(state.PendingAlertDeliveries);

        // The same pair again changes nothing.
        var again = await identity.Sensor(soil).Calibrate(new CalibrateSensor(new CalibrationPointRef(SeqOf(dry)), new CalibrationPointRef(SeqOf(wet))), Ct);
        Assert.Equal((SensorCalibrationOutcome.Calibrated, calibrated.Calibration), (again.Outcome, again.Calibration));
        Assert.Equal(aliases, await identity.AliasesAsync($"sensor/{soil}"));
        Assert.Equal(4, (await identity.AliasesAsync($"alert/{alertId}")).Count);
    }

    [Fact]
    public async Task UnassigningTheNodeClosesTheAlertAsUnassignedAndAssigningItAgainOpensTheNextEpisode()
    {
        var site = await SiteAsync();
        var (node, lotId) = await NodeAsync(site, "Peppers");
        var nodeId = node.DeviceId.ToString();
        var soil = node.SensorId(0, Quantity.SoilMoisture);
        var first = AlertIds.Uncalibrated(soil, 1);

        identity.Time.Advance(TimeSpan.FromMinutes(5));
        var closedAt = Now;
        Assert.Equal(DeviceAssignmentOutcome.Unassigned, (await identity.Device(nodeId).Unassign(site.SiteId, Ct)).Outcome);

        var closed = (await identity.Alert(first).Describe(Ct))!;
        Assert.Equal((AlertLifecycle.Closed, AlertCloseReason.Unassigned, closedAt), (closed.Lifecycle, closed.Reason, closed.ClosedAt));
        Assert.Empty(await identity.Site(site.SiteId).OpenAlerts(Ct));
        Assert.Equal([ContextChanged, Closed, Delivered], (await identity.AliasesAsync($"sensor/{soil}"))[^3..]);
        Assert.Equal(new SensorContext(site.SiteId, null, false, 2), (await SensorStateAsync(soil)).Context);

        // On no Lot: its frames are stored, and no Alert opens.
        await ReadAsync(node);
        await ReadAsync(node);
        Assert.Empty(await identity.Site(site.SiteId).OpenAlerts(Ct));
        Assert.Equal(1, (await SensorStateAsync(soil)).UncalibratedEpisode);

        // Assigned again and still uncalibrated: the next episode, another Alert.
        Assert.Equal(DeviceAssignmentOutcome.Moved, (await identity.Device(nodeId).Move(site.SiteId, lotId!, Ct)).Outcome);
        var second = AlertIds.Uncalibrated(soil, 2);
        Assert.NotEqual(first, second);
        Assert.Equal((second, lotId), ((await identity.Site(site.SiteId).OpenAlerts(Ct)).Single().AlertId, (await identity.Alert(second).Describe(Ct))!.LotId));
        Assert.Equal(AlertLifecycle.Closed, (await identity.Alert(first).Describe(Ct))!.Lifecycle);
        Assert.Equal((2, new SensorUncalibratedAlert(second, 2, lotId!)), ((await SensorStateAsync(soil)).UncalibratedEpisode, (await SensorStateAsync(soil)).OpenUncalibratedAlert));
    }

    [Fact]
    public async Task MovingTheNodeClosesTheAlertOfTheLotItLeftAndOpensOneNamingTheNewLot()
    {
        var site = await SiteAsync();
        var (node, tomatoes) = await NodeAsync(site, "Tomatoes");
        var soil = node.SensorId(0, Quantity.SoilMoisture);
        var first = AlertIds.Uncalibrated(soil, 1);
        var peppers = await SeedLotAsync(site.SiteId, "Peppers");

        identity.Time.Advance(TimeSpan.FromMinutes(5));
        var movedAt = Now;
        Assert.Equal(DeviceAssignmentOutcome.Moved, (await identity.Device(node.DeviceId.ToString()).Move(site.SiteId, peppers, Ct)).Outcome);

        var old = (await identity.Alert(first).Describe(Ct))!;
        Assert.Equal((AlertLifecycle.Closed, AlertCloseReason.Unassigned, movedAt, tomatoes), (old.Lifecycle, old.Reason, old.ClosedAt, old.LotId));

        var second = AlertIds.Uncalibrated(soil, 2);
        var opened = (await identity.Alert(second).Describe(Ct))!;
        Assert.Equal((AlertLifecycle.Open, AlertKind.Uncalibrated, peppers, movedAt), (opened.Lifecycle, opened.Kind, opened.LotId, opened.OpenedAt));
        Assert.Equal(second, Assert.Single(await identity.Site(site.SiteId).OpenAlerts(Ct)).AlertId);

        // One context, the close before the open, each delivered in that order.
        Assert.Equal([ContextChanged, Closed, Opened, Delivered, Delivered], (await identity.AliasesAsync($"sensor/{soil}"))[^5..]);
        var events = await identity.Store.ReadStreamAsync($"sensor/{soil}", Ct);
        Assert.Equal(new SensorAlertDelivered(first, AlertLifecycle.Closed, movedAt), events[^2].Data);
        Assert.Equal(new SensorAlertDelivered(second, AlertLifecycle.Open, movedAt), events[^1].Data);
    }

    [Fact]
    public async Task ACalibratedSensorOpensNoAlertWhenItsNodeIsAssignedOrMoved()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, "Tomatoes");
        var nodeId = node.DeviceId.ToString();
        var soil = node.SensorId(0, Quantity.SoilMoisture);
        await CalibrateAsync(node);
        Assert.Empty(await identity.Site(site.SiteId).OpenAlerts(Ct));

        var peppers = await SeedLotAsync(site.SiteId, "Peppers");
        Assert.Equal(DeviceAssignmentOutcome.Moved, (await identity.Device(nodeId).Move(site.SiteId, peppers, Ct)).Outcome);
        Assert.Equal(DeviceAssignmentOutcome.Unassigned, (await identity.Device(nodeId).Unassign(site.SiteId, Ct)).Outcome);
        Assert.Equal(DeviceAssignmentOutcome.Moved, (await identity.Device(nodeId).Move(site.SiteId, peppers, Ct)).Outcome);
        await ReadAsync(node);

        // It holds every context, and its one episode stays the one its Calibration closed.
        var state = await SensorStateAsync(soil);
        Assert.Equal((new SensorContext(site.SiteId, peppers, false, 4), 1, (SensorUncalibratedAlert?)null), (state.Context, state.UncalibratedEpisode, state.OpenUncalibratedAlert));
        Assert.Empty(await identity.Site(site.SiteId).OpenAlerts(Ct));
        Assert.Empty(await identity.AliasesAsync($"alert/{AlertIds.Uncalibrated(soil, 2)}"));
    }

    [Fact]
    public async Task APausedNodeClosesItsAlertAsPausedOpensNoneWhilePausedAndTheNextEpisodeOnResume()
    {
        var site = await SiteAsync();
        var (node, lotId) = await NodeAsync(site, "Peppers");
        var nodeId = node.DeviceId.ToString();
        var soil = node.SensorId(0, Quantity.SoilMoisture);
        var first = AlertIds.Uncalibrated(soil, 1);

        // Pause has no command before Epic 8: its events are journaled here, and a new silo reads them. The
        // Device grain then sends its Sensors the context with its next wake.
        await AppendAsync($"device/{nodeId}", new DevicePaused(DevicePauseSource.Device, null, Now));
        await identity.RestartSiloAsync(Now);
        identity.Time.Advance(TimeSpan.FromMinutes(5));
        var pausedAt = Now;
        await identity.WakeDeviceAsync(nodeId);

        var closed = (await identity.Alert(first).Describe(Ct))!;
        Assert.Equal((AlertLifecycle.Closed, AlertCloseReason.Paused, pausedAt), (closed.Lifecycle, closed.Reason, closed.ClosedAt));
        Assert.Empty(await identity.Site(site.SiteId).OpenAlerts(Ct));
        Assert.Equal(new SensorContext(site.SiteId, lotId, true, 2), (await SensorStateAsync(soil)).Context);

        // While paused: frames are acknowledged, wakes come and go, and nothing opens.
        await ReadAsync(node);
        await identity.WakeDeviceAsync(nodeId);
        Assert.Empty(await identity.Site(site.SiteId).OpenAlerts(Ct));
        Assert.Equal(1, (await SensorStateAsync(soil)).UncalibratedEpisode);

        // Resumed and still uncalibrated: the next episode.
        await AppendAsync($"device/{nodeId}", new DeviceResumed(DevicePauseSource.Device, Now));
        await identity.RestartSiloAsync(Now);
        await identity.WakeDeviceAsync(nodeId);

        var second = AlertIds.Uncalibrated(soil, 2);
        Assert.Equal(second, Assert.Single(await identity.Site(site.SiteId).OpenAlerts(Ct)).AlertId);
        Assert.Equal(new SensorContext(site.SiteId, lotId, false, 3), (await SensorStateAsync(soil)).Context);
        Assert.Equal(AlertLifecycle.Closed, (await identity.Alert(first).Describe(Ct))!.Lifecycle);
    }

    [Fact]
    public async Task AContextThatIsNotNewerIsIgnoredAndJournalsNothing()
    {
        var site = await SiteAsync();
        var (node, lotId) = await NodeAsync(site, "Peppers");
        var soil = node.SensorId(0, Quantity.SoilMoisture);
        var before = await identity.AliasesAsync($"sensor/{soil}");

        // The epoch the Sensor holds, and an older one that would unassign it.
        Assert.Equal(new SensorContextResult(SensorContextOutcome.NotNewer), await identity.Sensor(soil).SetContext(new SensorContext(site.SiteId, null, false, 1), Ct));
        Assert.Equal(new SensorContextResult(SensorContextOutcome.NotNewer), await identity.Sensor(soil).SetContext(new SensorContext(site.SiteId, lotId, true, 0), Ct));

        Assert.Equal(before, await identity.AliasesAsync($"sensor/{soil}"));
        Assert.Equal(new SensorContext(site.SiteId, lotId, false, 1), (await SensorStateAsync(soil)).Context);
        Assert.Equal(AlertIds.Uncalibrated(soil, 1), Assert.Single(await identity.Site(site.SiteId).OpenAlerts(Ct)).AlertId);

        // A Sensor that was never declared holds nothing.
        var undeclared = node.SensorId(7, Quantity.SoilMoisture);
        Assert.Equal(new SensorContextResult(SensorContextOutcome.NotDeclared), await identity.Sensor(undeclared).SetContext(new SensorContext(site.SiteId, lotId, false, 9), Ct));
        Assert.Empty(await identity.AliasesAsync($"sensor/{undeclared}"));
    }

    [Fact]
    public async Task ASpecificationThatStartsRequiringCalibrationOpensTheAlertAndOneThatStopsClosesItAsRemoved()
    {
        var site = await SiteAsync();
        var lotId = await SeedLotAsync(site.SiteId, "Peppers");
        var node = await EnrolAsync(site, lotId);
        var soil = node.SensorId(0, Quantity.SoilMoisture);

        // A firmware whose soil probe reports without a Calibration: assigned, declared, and nothing to calibrate.
        node.Specifications = Specifications(calibration: false);
        await DeclareAsync(node);
        Assert.Equal(["sensor.declared", ContextChanged], await identity.AliasesAsync($"sensor/{soil}"));
        Assert.Empty(await identity.Site(site.SiteId).OpenAlerts(Ct));

        // The next firmware requires one: the Sensor is on a Lot, so the Alert opens with the declaration.
        node.Specifications = Specifications(calibration: true);
        await DeclareAsync(node);
        var openedAt = Now;
        var alertId = AlertIds.Uncalibrated(soil, 1);
        Assert.Equal(["sensor.specification-changed", Opened, Delivered], (await identity.AliasesAsync($"sensor/{soil}"))[^3..]);
        var open = Assert.Single(await identity.Site(site.SiteId).OpenAlerts(Ct));
        Assert.Equal((alertId, lotId, openedAt), (open.AlertId, open.LotId, open.OpenedAt));

        // And back: such a Sensor refuses a Calibration, so nothing else could close the Alert.
        node.Specifications = Specifications(calibration: false);
        await DeclareAsync(node);
        var closed = (await identity.Alert(alertId).Describe(Ct))!;
        Assert.Equal((AlertLifecycle.Closed, AlertCloseReason.Removed, Now), (closed.Lifecycle, closed.Reason, closed.ClosedAt));
        Assert.Equal(["sensor.specification-changed", Closed, Delivered], (await identity.AliasesAsync($"sensor/{soil}"))[^3..]);
        Assert.Empty(await identity.Site(site.SiteId).OpenAlerts(Ct));
    }

    [Fact]
    public async Task UnassigningANodeClosesItsOpenThresholdAlertAsUnassignedAndResetsTheStreak()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, "Tomatoes");
        var nodeId = node.DeviceId.ToString();
        var soil = node.SensorId(0, Quantity.SoilMoisture);
        await CalibrateAsync(node);

        // Three Readings below the default low of 30 %: the Threshold Alert is open; one more starts a streak.
        for (var reading = 0; reading < 3; reading++)
        {
            await ReadAsync(node, Soil(20));
        }

        var alertId = AlertIds.Threshold(soil, 1);
        Assert.Equal(alertId, Assert.Single(await identity.Site(site.SiteId).OpenAlerts(Ct)).AlertId);
        await ReadAsync(node, Soil(40));
        Assert.Equal(new StreakState(ThresholdSide.Low, ThresholdPosition.Within, 1), (await SensorStateAsync(soil)).Streak);

        identity.Time.Advance(TimeSpan.FromMinutes(5));
        var closedAt = Now;
        Assert.Equal(DeviceAssignmentOutcome.Unassigned, (await identity.Device(nodeId).Unassign(site.SiteId, Ct)).Outcome);

        // No Reading will recover it: the context closes it.
        var closed = (await identity.Alert(alertId).Describe(Ct))!;
        Assert.Equal((AlertLifecycle.Closed, AlertCloseReason.Unassigned, closedAt), (closed.Lifecycle, closed.Reason, closed.ClosedAt));
        Assert.Empty(await identity.Site(site.SiteId).OpenAlerts(Ct));
        Assert.Equal(
            new SensorThresholdEpisodeClosed(1, alertId, AlertCloseReason.Unassigned, 2, null, closedAt),
            (await identity.Store.ReadStreamAsync($"sensor/{soil}", Ct))[^2].Data);
        var state = await SensorStateAsync(soil);
        Assert.Equal(((SensorOpenAlert?)null, new StreakState(null, ThresholdPosition.Within, 0)), (state.OpenAlert, state.Streak));
        Assert.Empty(state.PendingAlertDeliveries);
    }

    [Fact]
    public async Task PausingANodeClosesItsOpenThresholdAlertAsPausedAndAMoveLeavesItOpen()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, "Tomatoes");
        var nodeId = node.DeviceId.ToString();
        var soil = node.SensorId(0, Quantity.SoilMoisture);
        await CalibrateAsync(node);

        for (var reading = 0; reading < 3; reading++)
        {
            await ReadAsync(node, Soil(20));
        }

        var alertId = AlertIds.Threshold(soil, 1);

        // A move starts a new epoch and leaves the Threshold Alert as it is (DW-85).
        var peppers = await SeedLotAsync(site.SiteId, "Peppers");
        Assert.Equal(DeviceAssignmentOutcome.Moved, (await identity.Device(nodeId).Move(site.SiteId, peppers, Ct)).Outcome);
        Assert.Equal(AlertLifecycle.Open, (await identity.Alert(alertId).Describe(Ct))!.Lifecycle);
        Assert.Equal(alertId, Assert.Single(await identity.Site(site.SiteId).OpenAlerts(Ct)).AlertId);

        await AppendAsync($"device/{nodeId}", new DevicePaused(DevicePauseSource.Site, null, Now));
        await identity.RestartSiloAsync(Now);
        await identity.WakeDeviceAsync(nodeId);

        var closed = (await identity.Alert(alertId).Describe(Ct))!;
        Assert.Equal((AlertLifecycle.Closed, AlertCloseReason.Paused), (closed.Lifecycle, closed.Reason));
        Assert.Empty(await identity.Site(site.SiteId).OpenAlerts(Ct));
        Assert.Null((await SensorStateAsync(soil)).OpenAlert);
    }

    [Fact]
    public async Task AnUncalibratedSensorOpensNoThresholdAlertHoweverDryItReads()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, "Peppers");
        var soil = node.SensorId(0, Quantity.SoilMoisture);

        // Raw counts that any Calibration would read far below the default low of 30 %.
        for (var reading = 0; reading < 3; reading++)
        {
            await ReadAsync(node, new SimulatedReading(0, Quantity.SoilMoisture, 2900));
        }

        var state = await SensorStateAsync(soil);
        Assert.Equal((0, (SensorOpenAlert?)null, new StreakState(null, ThresholdPosition.Within, 0)), (state.Episode, state.OpenAlert, state.Streak));
        Assert.Empty(await identity.AliasesAsync($"alert/{AlertIds.Threshold(soil, 1)}"));

        // The uncalibrated Alert stays the only one.
        var only = Assert.Single(await identity.Site(site.SiteId).OpenAlerts(Ct));
        Assert.Equal((AlertIds.Uncalibrated(soil, 1), AlertKind.Uncalibrated), (only.AlertId, only.Kind));
        Assert.Equal(["sensor.declared", ContextChanged, Opened, Delivered], await identity.AliasesAsync($"sensor/{soil}"));
    }

    [Fact]
    public async Task TheOpenAlertSurvivesASiloRestartAndStaysTheOnlyOne()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, "Peppers");
        var nodeId = node.DeviceId.ToString();
        var soil = node.SensorId(0, Quantity.SoilMoisture);
        var alertId = AlertIds.Uncalibrated(soil, 1);
        var before = await identity.AliasesAsync($"sensor/{soil}");

        // Everything held in memory is gone, also which Sensor confirmed the context: the Device sends it again,
        // and the Sensor, which holds that epoch, ignores it.
        await identity.RestartSiloAsync(Now);
        await identity.WakeDeviceAsync(nodeId);
        await ReadAsync(node);
        await ReadAsync(node);

        Assert.Equal(before, await identity.AliasesAsync($"sensor/{soil}"));
        Assert.Equal(alertId, Assert.Single(await identity.Site(site.SiteId).OpenAlerts(Ct)).AlertId);
        Assert.Equal(["alert.opened", "alert.site-notified"], await identity.AliasesAsync($"alert/{alertId}"));

        // The episode still ends where it should.
        await CalibrateAsync(node);
        Assert.Equal(AlertCloseReason.Calibrated, (await identity.Alert(alertId).Describe(Ct))!.Reason);
        Assert.Empty(await identity.Site(site.SiteId).OpenAlerts(Ct));
        Assert.Equal(1, (await SensorStateAsync(soil)).UncalibratedEpisode);
    }

    [Fact]
    public async Task AContextThatDidNotReachTheSensorsIsSentAgainWithTheNextWake()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, "Peppers");
        var nodeId = node.DeviceId.ToString();
        var soil = node.SensorId(0, Quantity.SoilMoisture);
        var alertId = AlertIds.Uncalibrated(soil, 1);
        var failedBefore = identity.SensorFaults.FailedContexts;

        // No Sensor can be reached while the Node is unassigned: the unassignment answers as it always did.
        identity.SensorFaults.FailNextContexts(int.MaxValue);

        try
        {
            Assert.Equal(DeviceAssignmentOutcome.Unassigned, (await identity.Device(nodeId).Unassign(site.SiteId, Ct)).Outcome);
            Assert.Equal(4, identity.SensorFaults.FailedContexts - failedBefore);
            Assert.Null((await identity.DeviceStateAsync(nodeId)).LotId);
            Assert.Equal(AlertLifecycle.Open, (await identity.Alert(alertId).Describe(Ct))!.Lifecycle);
        }
        finally
        {
            identity.SensorFaults.FailNextContexts(0);
        }

        // No Reading follows an unassignment: the Device sends the context again when it is next woken.
        await identity.WakeDeviceAsync(nodeId);

        var closed = (await identity.Alert(alertId).Describe(Ct))!;
        Assert.Equal((AlertLifecycle.Closed, AlertCloseReason.Unassigned), (closed.Lifecycle, closed.Reason));
        Assert.Empty(await identity.Site(site.SiteId).OpenAlerts(Ct));

        // Confirmed: another wake sends nothing. No Sensor is called, which its journal alone would not show.
        var journaled = await identity.AliasesAsync($"sensor/{soil}");
        var sentBefore = identity.SensorFaults.PassedContexts;
        await identity.WakeDeviceAsync(nodeId);
        Assert.Equal(sentBefore, identity.SensorFaults.PassedContexts);
        Assert.Equal(journaled, await identity.AliasesAsync($"sensor/{soil}"));
    }

    [Fact]
    public async Task AContextThatDidNotReachTheSensorsIsSentAgainWithTheNodesNextReport()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, "Peppers");
        var nodeId = node.DeviceId.ToString();
        var soil = node.SensorId(0, Quantity.SoilMoisture);
        var alertId = AlertIds.Uncalibrated(soil, 1);

        identity.SensorFaults.FailNextContexts(int.MaxValue);

        try
        {
            Assert.Equal(DeviceAssignmentOutcome.Unassigned, (await identity.Device(nodeId).Unassign(site.SiteId, Ct)).Outcome);
            Assert.Equal(AlertLifecycle.Open, (await identity.Alert(alertId).Describe(Ct))!.Lifecycle);
        }
        finally
        {
            identity.SensorFaults.FailNextContexts(0);
        }

        // Nobody wakes the Device and no silo restarts: the Node reports. On no Lot its Readings are not
        // evaluated, so only the context the frame sends again can end the episode.
        var sentBefore = identity.SensorFaults.PassedContexts;
        await ReadAsync(node);

        Assert.Equal(4, identity.SensorFaults.PassedContexts - sentBefore);
        var closed = (await identity.Alert(alertId).Describe(Ct))!;
        Assert.Equal((AlertLifecycle.Closed, AlertCloseReason.Unassigned), (closed.Lifecycle, closed.Reason));
        Assert.Empty(await identity.Site(site.SiteId).OpenAlerts(Ct));
        Assert.Equal(new SensorContext(site.SiteId, null, false, 2), (await SensorStateAsync(soil)).Context);

        // Confirmed: the next report sends nothing.
        await ReadAsync(node);
        Assert.Equal(4, identity.SensorFaults.PassedContexts - sentBefore);
    }

    [Fact]
    public async Task ASensorThatHoldsNoContextTakesItsFirstOneFromAReading()
    {
        var site = await SiteAsync();
        var lotId = await SeedLotAsync(site.SiteId, "Peppers");
        var node = await EnrolAsync(site, lotId);
        var soil = node.SensorId(0, Quantity.SoilMoisture);

        // No context the Device sends reaches the Sensors, from the frame that declares them on: the Sensor is
        // as one journaled before Story 7.4, declared and without a context, when that frame's Reading arrives.
        var failedBefore = identity.SensorFaults.FailedContexts;
        var sentBefore = identity.SensorFaults.PassedContexts;
        identity.SensorFaults.FailNextContexts(int.MaxValue);

        try
        {
            await DeclareAsync(node);
        }
        finally
        {
            identity.SensorFaults.FailNextContexts(0);
        }

        // The Reading said the Site, the Lot and the epoch.
        Assert.Equal(4, identity.SensorFaults.FailedContexts - failedBefore);
        Assert.Equal(sentBefore, identity.SensorFaults.PassedContexts);
        Assert.Equal(new SensorContext(site.SiteId, lotId, false, 1), (await SensorStateAsync(soil)).Context);
        var open = Assert.Single(await identity.Site(site.SiteId).OpenAlerts(Ct));
        Assert.Equal((AlertIds.Uncalibrated(soil, 1), AlertKind.Uncalibrated, lotId), (open.AlertId, open.Kind, open.LotId));
        var aliases = await identity.AliasesAsync($"sensor/{soil}");
        Assert.Single(aliases, alias => alias == ContextChanged);
        Assert.Single(aliases, alias => alias == Opened);
    }

    [Fact]
    public async Task AMoveWhoseContextWasLostHealsWithTheNextReadingBecauseReadingsCarryTheEpoch()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, "Tomatoes");
        var nodeId = node.DeviceId.ToString();
        var soil = node.SensorId(0, Quantity.SoilMoisture);
        var peppers = await SeedLotAsync(site.SiteId, "Peppers");

        identity.SensorFaults.FailNextContexts(int.MaxValue);

        try
        {
            Assert.Equal(DeviceAssignmentOutcome.Moved, (await identity.Device(nodeId).Move(site.SiteId, peppers, Ct)).Outcome);
            Assert.Equal(AlertIds.Uncalibrated(soil, 1), Assert.Single(await identity.Site(site.SiteId).OpenAlerts(Ct)).AlertId);

            // The next frame: its context is lost too, but its Reading says the epoch and the Lot.
            await ReadAsync(node);
        }
        finally
        {
            identity.SensorFaults.FailNextContexts(0);
        }

        var open = Assert.Single(await identity.Site(site.SiteId).OpenAlerts(Ct));
        Assert.Equal((AlertIds.Uncalibrated(soil, 2), peppers), (open.AlertId, open.LotId));
        Assert.Equal(AlertCloseReason.Unassigned, (await identity.Alert(AlertIds.Uncalibrated(soil, 1)).Describe(Ct))!.Reason);
        Assert.Equal(new SensorContext(site.SiteId, peppers, false, 2), (await SensorStateAsync(soil)).Context);
    }

    [Fact]
    public async Task AnOpenTheAlertGrainDidNotTakeIsDeliveredAgainByTheSensorItself()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, lotName: null);
        var nodeId = node.DeviceId.ToString();
        var soil = node.SensorId(0, Quantity.SoilMoisture);
        var alertId = AlertIds.Uncalibrated(soil, 1);
        var peppers = await SeedLotAsync(site.SiteId, "Peppers");

        // The Alert grain cannot be reached while the Node is assigned: the assignment answers all the same, and
        // the episode is journaled with its open pending.
        identity.AlertFaults.FailNext(int.MaxValue);

        try
        {
            Assert.Equal(DeviceAssignmentOutcome.Moved, (await identity.Device(nodeId).Move(site.SiteId, peppers, Ct)).Outcome);

            var pending = Assert.Single((await SensorStateAsync(soil)).PendingAlertDeliveries);
            Assert.Equal((alertId, AlertLifecycle.Open, AlertKind.Uncalibrated), (pending.AlertId, pending.Change, pending.Kind));
            Assert.Null(await identity.Alert(alertId).Describe(Ct));
            Assert.Empty(await identity.Site(site.SiteId).OpenAlerts(Ct));
        }
        finally
        {
            identity.AlertFaults.FailNext(0);
        }

        // Nobody calls the Sensor: its delivery timer does.
        await JournalWait.UntilAsync(
            async () => (await identity.Site(site.SiteId).OpenAlerts(Ct)).Count == 1
                && (await SensorStateAsync(soil)).PendingAlertDeliveries.Count == 0,
            "the Sensor delivered its open uncalibrated Alert by itself");

        Assert.Equal(alertId, Assert.Single(await identity.Site(site.SiteId).OpenAlerts(Ct)).AlertId);
        Assert.Equal(["alert.opened", "alert.site-notified"], await identity.AliasesAsync($"alert/{alertId}"));
        Assert.Single(await identity.AliasesAsync($"sensor/{soil}"), alias => alias == Opened);
        Assert.Single(await identity.AliasesAsync($"site/{site.SiteId}"), alias => alias == "site.alert-opened");
    }

    [Fact]
    public async Task AReportTheSiteDidNotTakeAnswersRetryAndTheResentFrameCompletesWithOneAlert()
    {
        var site = await SiteAsync();
        var lotId = await SeedLotAsync(site.SiteId, "Peppers");
        var node = await EnrolAsync(site, lotId);
        var soil = node.SensorId(0, Quantity.SoilMoisture);
        var alertId = AlertIds.Uncalibrated(soil, 1);
        Assert.True((await ReportAsync(node, node.Wake(Now))).SpecificationsUnknown);

        // The Site cannot be reached, neither for the context's delivery nor for the frame's evaluation.
        identity.SiteFaults.FailNextAlertReports(2);
        identity.Time.Advance(Interval);
        var declaring = node.Wake(Now);

        // The Alert is open, but its Site does not know it: the frame is not acknowledged.
        Assert.Equal(DeviceIngestStatus.Retry, (await IngestAsync(node, declaring)).Status);
        Assert.Equal(AlertLifecycle.Open, (await identity.Alert(alertId).Describe(Ct))!.Lifecycle);

        // The Node resends: a duplicate, whose evaluation finishes the delivery.
        Assert.Equal(DeviceIngestStatus.Duplicate, (await IngestAsync(node, declaring)).Status);

        Assert.Equal(alertId, Assert.Single(await identity.Site(site.SiteId).OpenAlerts(Ct)).AlertId);
        Assert.Equal(["alert.opened", "alert.site-notified"], await identity.AliasesAsync($"alert/{alertId}"));
        Assert.Equal(["sensor.declared", ContextChanged, Opened, Delivered], await identity.AliasesAsync($"sensor/{soil}"));
        Assert.Single(await identity.AliasesAsync($"site/{site.SiteId}"), alias => alias == "site.alert-opened");
    }

    [Fact]
    public async Task OnlyTheSensorThatOpenedTheAlertClosesItAndNoOtherKindOpensThisWay()
    {
        var site = await SiteAsync();
        var (node, lotId) = await NodeAsync(site, "Peppers");
        var nodeId = node.DeviceId.ToString();
        var soil = node.SensorId(0, Quantity.SoilMoisture);
        var alertId = AlertIds.Uncalibrated(soil, 1);
        var alert = identity.Alert(alertId);

        // A client is not the Sensor grain that opened the Alert.
        Assert.Equal(new AlertResult(AlertOutcome.Refused), await alert.Close(new CloseAlert(AlertCloseReason.Calibrated, Now), Ct));
        Assert.Equal(AlertLifecycle.Open, (await alert.Describe(Ct))?.Lifecycle);

        // No Alert of another kind this way, none under an ID the request does not derive (the Threshold Alert of
        // the same episode is another ID), and no episode 0.
        var asThreshold = identity.Alert(AlertIds.Threshold(soil, 2));
        Assert.Equal(AlertOutcome.Refused, (await asThreshold.OpenForSensor(new OpenSensorAlert(AlertKind.Uncalibrated, soil, 2, site.SiteId, lotId!, nodeId, "soil_moisture", Now), Ct)).Outcome);
        Assert.Null(await asThreshold.Describe(Ct));

        var second = identity.Alert(AlertIds.Uncalibrated(soil, 2));
        Assert.Equal(AlertOutcome.Refused, (await second.OpenForSensor(new OpenSensorAlert(AlertKind.Threshold, soil, 2, site.SiteId, lotId!, nodeId, "soil_moisture", Now), Ct)).Outcome);
        Assert.Equal(AlertOutcome.Refused, (await second.OpenForSensor(new OpenSensorAlert(AlertKind.Battery, soil, 2, site.SiteId, lotId!, nodeId, "soil_moisture", Now), Ct)).Outcome);
        Assert.Equal(AlertOutcome.Refused, (await second.Open(new OpenAlert(soil, 2, ThresholdSide.Low, site.SiteId, lotId!, nodeId, "soil_moisture", Now), Ct)).Outcome);
        Assert.Null(await second.Describe(Ct));

        var zero = identity.Alert(AlertIds.Uncalibrated(soil, 0));
        Assert.Equal(AlertOutcome.Refused, (await zero.OpenForSensor(new OpenSensorAlert(AlertKind.Uncalibrated, soil, 0, site.SiteId, lotId!, nodeId, "soil_moisture", Now), Ct)).Outcome);

        // Opening it again journals nothing.
        var again = await alert.OpenForSensor(new OpenSensorAlert(AlertKind.Uncalibrated, soil, 1, site.SiteId, lotId!, nodeId, "soil_moisture", Now.AddHours(1)), Ct);
        Assert.Equal(new AlertResult(AlertOutcome.Open, Reported: true), again);
        Assert.Equal(["alert.opened", "alert.site-notified"], await identity.AliasesAsync($"alert/{alertId}"));
        Assert.Equal(alertId, Assert.Single(await identity.Site(site.SiteId).OpenAlerts(Ct)).AlertId);
    }

    // What the Sensor grain states as its Alert opens is what the Alerts list and the notifications say. The Lot's
    // status is computed from the Sensor's state, not from the Alert.
    [Fact]
    public async Task TheAlertShowsInTheAlertsReadModelAsAHealthAlertOfItsNodeAndLeavesTheLotStatusToTheSensor()
    {
        var site = await SiteAsync();
        var (node, lotId) = await NodeAsync(site, "Peppers");
        var nodeId = node.DeviceId.ToString();
        var soil = node.SensorId(0, Quantity.SoilMoisture);
        var alertId = AlertIds.Uncalibrated(soil, 1);
        var opened = Now;
        await CatchUpAsync();

        // The Alerts list: no Threshold Alert, one Health Alert with the Node and the Lot, counted as open.
        var alerts = identity.SiloServices.GetRequiredService<AlertsReadModel>();
        var closedSince = Now - AlertsReadModel.ClosedRetention;
        Assert.Empty((await alerts.ListAlertsAsync(site.SiteId, closedSince, null, 50, Ct)).Alerts);
        Assert.Equal(1, await alerts.CountOpenAsync(site.SiteId, Ct));
        Assert.Equal(
            [new HealthAlertView(alertId, "uncalibrated", nodeId, "node", lotId, "Peppers", null, opened, null, null, null)],
            await alerts.ListHealthAlertsAsync(site.SiteId, closedSince, Ct));

        // The Lot needs calibration because its Sensor has none, and that is the time of the declaration.
        Assert.Equal("needsCalibration", await LotStatusAsync(lotId!));

        // The notification text, written from what the Alert carries and the real read models; it routes to the Lot.
        var push = identity.SiloServices.GetRequiredService<PushContentBuilder>();
        var listed = Assert.Single(await identity.Site(site.SiteId).OpenAlerts(Ct));
        var entry = new NotificationEntry(alertId, listed.Kind, listed.Side, listed.LotId, listed.SensorId, listed.DeviceId, listed.Quantity, listed.OpenedAt, listed.DeviceKind);
        var content = await push.BuildAsync(new Notification(site.OwnerId, site.SiteId, NotificationKind.Alert, Now, null, [entry]), Now);
        Assert.Equal(
            ("Soil sensor on Lot 'Peppers' needs calibration", "An Owner or Administrator can calibrate it.", lotId),
            (content!.Title, content.Body, content.LotId));

        // Calibrated: the Lot leaves needs calibration, and the Alert stays in the list as closed.
        await CalibrateAsync(node);
        var closedAt = Now;
        await CatchUpAsync();
        Assert.NotEqual("needsCalibration", await LotStatusAsync(lotId!));
        Assert.Equal(0, await alerts.CountOpenAsync(site.SiteId, Ct));
        Assert.Equal(
            [new HealthAlertView(alertId, "uncalibrated", nodeId, "node", lotId, "Peppers", null, opened, closedAt, "calibrated", null)],
            await alerts.ListHealthAlertsAsync(site.SiteId, Now - AlertsReadModel.ClosedRetention, Ct));
    }

    [Fact]
    public async Task TheAlertIsDeliveredLikeAnyAlertAndRemindsNoSoonerThanADayLater()
    {
        // Inside the Notification Window of a User who set none (07:00 to 22:00, UTC), with room for the reports.
        GoTo(11, 0);
        var site = await SiteAsync();
        var (node, lotId) = await NodeAsync(site, "Peppers");
        var nodeId = node.DeviceId.ToString();
        var soil = node.SensorId(0, Quantity.SoilMoisture);
        var alertId = AlertIds.Uncalibrated(soil, 1);
        var opened = Now;

        await identity.WakeUserAsync(site.OwnerId);
        var sent = Assert.Single(identity.Notifications.For(site.OwnerId));
        Assert.Equal(NotificationKind.Alert, sent.Notification.Kind);
        Assert.Equal(
            new NotificationEntry(alertId, AlertKind.Uncalibrated, null, lotId, soil, nodeId, "soil_moisture", opened, DeviceKind.Node),
            Assert.Single(sent.Notification.Entries));

        // The Reminder comes 24 h after the notification and not a minute before. The Node keeps reporting
        // meanwhile, so it is not silent.
        for (var hour = 0; hour < 23; hour++)
        {
            identity.Time.Advance(TimeSpan.FromHours(1));
            Assert.Equal(DeviceIngestStatus.Stored, (await IngestAsync(node, node.Wake(Now))).Status);
        }

        identity.Time.SetUtcNow(opened + TimeSpan.FromHours(24) - TimeSpan.FromMinutes(1));
        await identity.WakeUserAsync(site.OwnerId);
        Assert.Single(identity.Notifications.For(site.OwnerId));

        identity.Time.Advance(TimeSpan.FromMinutes(1));
        await identity.WakeUserAsync(site.OwnerId);
        var reminded = identity.Notifications.For(site.OwnerId);
        Assert.Equal(2, reminded.Count);
        Assert.Equal((NotificationKind.Reminder, alertId), (reminded[1].Notification.Kind, Assert.Single(reminded[1].Notification.Entries).AlertId));

        // Still the one Alert.
        Assert.Equal(alertId, Assert.Single(await identity.Site(site.SiteId).OpenAlerts(Ct)).AlertId);
    }

    private static SimulatedReading Soil(int percent) => new(0, Quantity.SoilMoisture, DryRaw - ((DryRaw - WetRaw) * percent / 100));

    // The default set, with the soil probe requiring a Calibration or not. Without one its default Thresholds are
    // in its own unit, inside its range.
    private static SpecificationSet Specifications(bool calibration)
    {
        var set = SimulatedDevice.DefaultSpecifications();
        set.Specifications[0].Calibration = calibration;

        if (!calibration)
        {
            set.Specifications[0].DefaultLow = 1200;
            set.Specifications[0].DefaultHigh = 3200;
        }

        return set;
    }

    // An Active Site created by its Owner, whose User grain therefore holds the Site and is told of its Alerts.
    private async Task<(string SiteId, string OwnerId)> SiteAsync()
    {
        var ownerId = Guid.NewGuid().ToString();
        var created = await identity.User(ownerId).CreateSite("k1", "Home", Ct);
        Assert.Equal(SiteCreationOutcome.Created, created.Outcome);
        return (created.Site!.Id, ownerId);
    }

    private async Task<string> SeedLotAsync(string siteId, string name)
    {
        var lotId = Guid.CreateVersion7().ToString();
        Assert.True(await identity.Store.AppendAsync($"lot/{lotId}", 0, [new LotCreated(siteId, name)], Ct));
        return lotId;
    }

    // A Node enrolled on the Site, on the Lot when one is given; it has not reported yet.
    private async Task<SimulatedDevice> EnrolAsync((string SiteId, string OwnerId) site, string? lotId)
    {
        var node = SimulatedDevice.Create(ProtocolKind.Node);
        var enrol = new EnrolDevice(site.SiteId, DeviceKind.Node, identity.Vault.Wrap(node.DeviceId, node.Keys.DeviceKey), site.OwnerId, Guid.NewGuid().ToString("N"), lotId);
        Assert.Equal(DeviceEnrolmentOutcome.Enrolled, (await identity.Device(node.DeviceId.ToString()).Enrol(enrol, Ct)).Outcome);
        return node;
    }

    // A Node of the Site that declared its four Sensors, on a new Lot when `lotName` is given.
    private async Task<(SimulatedDevice Node, string? LotId)> NodeAsync((string SiteId, string OwnerId) site, string? lotName)
    {
        var lotId = lotName is null ? null : await SeedLotAsync(site.SiteId, lotName);
        var node = await EnrolAsync(site, lotId);
        await DeclareAsync(node);
        return (node, lotId);
    }

    // Two wakes: the Server asks for the Specification set, and the Node's next frame carries it.
    private async Task DeclareAsync(SimulatedDevice node)
    {
        identity.Time.Advance(Interval);
        Assert.True((await ReportAsync(node, node.Wake(Now))).SpecificationsUnknown);
        identity.Time.Advance(Interval);
        Assert.False((await ReportAsync(node, node.Wake(Now))).SpecificationsUnknown);
    }

    // The soil probe's two points from two Readings, saved in one request.
    private async Task CalibrateAsync(SimulatedDevice node)
    {
        var dry = await ReadAsync(node, new SimulatedReading(0, Quantity.SoilMoisture, DryRaw));
        var wet = await ReadAsync(node, new SimulatedReading(0, Quantity.SoilMoisture, WetRaw));
        var calibrated = await identity.Sensor(node.SensorId(0, Quantity.SoilMoisture)).Calibrate(
            new CalibrateSensor(new CalibrationPointRef(SeqOf(dry)), new CalibrationPointRef(SeqOf(wet))),
            Ct);
        Assert.Equal(SensorCalibrationOutcome.Calibrated, calibrated.Outcome);
    }

    // One wake, 15 minutes after the last one, stored and acknowledged; the Node's four default Readings unless
    // others are given.
    private async Task<NodeFrame> ReadAsync(SimulatedDevice node, params SimulatedReading[] readings)
    {
        identity.Time.Advance(Interval);
        var frame = node.Wake(Now, readings: readings.Length == 0 ? null : readings);
        await ReportAsync(node, frame);
        return frame;
    }

    private async Task<Downlink> ReportAsync(SimulatedDevice node, NodeFrame frame)
    {
        var result = await IngestAsync(node, frame);

        Assert.Equal(DeviceIngestStatus.Stored, result.Status);
        return node.OpenDownlink(SealedEnvelope.Parser.ParseFrom(result.Downlink));
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

    private async Task AppendAsync(string streamId, object @event)
    {
        var version = (await identity.Store.ReadStreamAsync(streamId, Ct)).Count;
        Assert.True(await identity.Store.AppendAsync(streamId, version, [@event], Ct));
    }

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

    private async Task CatchUpAsync()
    {
        await identity.SiloServices.GetProjectionRunner<AlertsProjector>().CatchUpAsync(Ct);
        await identity.SiloServices.GetProjectionRunner<DevicesProjector>().CatchUpAsync(Ct);
        await identity.SiloServices.GetProjectionRunner<LotsProjector>().CatchUpAsync(Ct);
    }

    private async Task<string> LotStatusAsync(string lotId)
    {
        await identity.SiloServices.GetProjectionRunner<LotsProjector>().CatchUpAsync(Ct);
        return (await identity.Database.ScalarAsync<string>("SELECT status FROM lots WHERE lot_id = @lot_id", ("lot_id", lotId)))!;
    }

    // Moves the clock on to the next hh:mm UTC, today or tomorrow: the clock only moves forward.
    private void GoTo(int hour, int minute)
    {
        var target = new DateTimeOffset(Now.UtcDateTime.Date, TimeSpan.Zero).AddHours(hour).AddMinutes(minute);
        identity.Time.SetUtcNow(target > Now ? target : target.AddDays(1));
    }
}
