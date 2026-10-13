using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Lots;
using Coldframe.Contracts.Notifications;
using Coldframe.Contracts.Sites;
using Coldframe.DeviceSimulator;
using Coldframe.Protocol.Device.V1;
using Coldframe.Server.Alerts;
using Coldframe.Server.Devices;
using Coldframe.Server.IntegrationTests.Identity;
using Coldframe.Server.Journal;
using Coldframe.Server.Lots;
using Coldframe.Server.Notifications.Push;
using Microsoft.Extensions.DependencyInjection;
using ProtocolKind = Coldframe.Protocol.Setup.V1.DeviceKind;

namespace Coldframe.Server.IntegrationTests.Devices;

/// <summary>
/// Low-battery Alerts on a TestCluster with a fake clock (Story 7.3): the Device grain counts a Node's device
/// reports, opens a Health Alert of kind <c>battery</c> with the third consecutive low report (battery known and
/// below 20 % while not charging) and closes it with the third consecutive recovered one. One test per server row
/// of the story's matrix, plus what must not change: the Lot's status and the Node's silence evaluation. Frames
/// come from the Device simulator only.
/// </summary>
[Collection(IngestSuites.Name)]
public sealed class BatteryAlertGrainTests(IdentityCluster identity) : IClassFixture<IdentityCluster>
{
    // A relay Hub no Site enrolled: it suppresses nothing.
    private const string UnknownHub = "92064422c012f481";

    private const string StreakChanged = "device.battery-streak-changed";

    private const string Started = "device.battery-low-started";

    private const string Ended = "device.battery-low-ended";

    private const string Delivered = "device.alert-delivered";

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    private static readonly TimeSpan NodeWindow = TimeSpan.FromHours(6);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTimeOffset Now => identity.Time.GetUtcNow();

    [Fact]
    public async Task ExactlyThreeLowReportsOpenOneBatteryAlertWithTheLevelOfTheThird()
    {
        var site = await SiteAsync();
        var (node, lotId) = await NodeAsync(site, "Lettuce");
        var nodeId = node.DeviceId.ToString();
        var stream = $"device/{nodeId}";
        var before = await LotAsync(lotId!);
        var journaled = await identity.AliasesAsync(stream);

        // 19 % and 14 %, not charging: two low reports open nothing.
        await ReportAsync(node, 19);
        await ReportAsync(node, 14);

        var counting = await identity.DeviceStateAsync(nodeId);
        Assert.Equal((2, 0, (DeviceBatteryAlert?)null), (counting.BatteryLowStreak, counting.BatteryEpisode, counting.OpenBatteryAlert));
        Assert.Equal((DateTimeOffset?)Now, counting.BatteryCountedAt);
        Assert.Empty(await OpenAlertsAsync(site.SiteId));
        Assert.Equal([StreakChanged, StreakChanged], Battery(await identity.AliasesAsync(stream), journaled.Count));

        // The third: episode 1, journaled before the Alert it opens, with the level of this report.
        await ReportAsync(node, 14);

        var alertId = AlertIds.Battery(nodeId, 1);
        var state = await identity.DeviceStateAsync(nodeId);
        Assert.Equal((1, new DeviceBatteryAlert(alertId, 1, Now, 14)), (state.BatteryEpisode, state.OpenBatteryAlert));
        Assert.Equal(new BatteryStreaks(true, 0, 0), state.BatteryStreaks);
        Assert.Empty(state.PendingAlertDeliveries);
        Assert.Equal([StreakChanged, StreakChanged, Started, Delivered], Battery(await identity.AliasesAsync(stream), journaled.Count));
        Assert.True(await PositionAsync(stream, Started) < await PositionAsync($"alert/{alertId}", "alert.opened"));

        // The Alert is the Node's own: no side, no Sensor, no quantity and no silent-since; the Lot the Node is
        // on and the level it opened with.
        Assert.Equal(
            new AlertSnapshot(alertId, AlertKind.Battery, AlertLifecycle.Open, null, site.SiteId, lotId, null, nodeId, null, Now, DeviceKind: DeviceKind.Node, BatteryPercent: 14),
            await identity.Alert(alertId).Describe(Ct));
        Assert.Equal(["alert.opened", "alert.site-notified"], await identity.AliasesAsync($"alert/{alertId}"));

        // The Site lists it and its member was told.
        var listed = Assert.Single(await OpenAlertsAsync(site.SiteId));
        Assert.Equal(new SiteAlert(alertId, AlertKind.Battery, null, lotId, null, nodeId, null, Now, DeviceKind.Node, null, 14), listed);
        Assert.Equal(listed, Assert.Single(await TrackedAlertsAsync(site.OwnerId)).Alert);

        // Lot status unchanged: a battery Alert never turns a Lot unknown.
        Assert.Equal(before, await LotAsync(lotId!));

        // Silence evaluation unchanged: no silent Alert, and the window counts from the Node's last report.
        Assert.Equal((0, (DeviceSilentAlert?)null), (state.SilenceEpisode, state.OpenSilentAlert));
        Assert.Equal((Now, Now + NodeWindow), (state.SilenceFrom, state.SilenceDueAt));

        // A Node that stays low journals nothing more and keeps its one Alert.
        var open = await identity.AliasesAsync(stream);
        await ReportAsync(node, 12);
        await ReportAsync(node, 11);
        Assert.Empty(Battery(await identity.AliasesAsync(stream), open.Count));
        Assert.Equal(alertId, Assert.Single(await OpenAlertsAsync(site.SiteId)).AlertId);
        Assert.Equal(state.OpenBatteryAlert, (await identity.Device(nodeId).Silence(Ct))!.OpenBatteryAlert);
    }

    [Fact]
    public async Task ExactlyThreeRecoveredReportsCloseTheAlertAndTheNextLowEpisodeIsAnotherAlert()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, "Lettuce");
        var nodeId = node.DeviceId.ToString();
        var stream = $"device/{nodeId}";
        var first = await LowAsync(node);
        var journaled = await identity.AliasesAsync(stream);

        // 25 % not charging, then 12 % charging: both recovered, and still open after the second.
        await ReportAsync(node, 25);
        await ReportAsync(node, 12, ChargeStatus.Charging);

        Assert.Equal(new BatteryStreaks(true, 0, 2), (await identity.DeviceStateAsync(nodeId)).BatteryStreaks);
        Assert.Equal(AlertLifecycle.Open, (await identity.Alert(first).Describe(Ct))?.Lifecycle);

        // 30 % charging: the third closes it as recovered.
        await ReportAsync(node, 30, ChargeStatus.Charging);

        var closed = await identity.Alert(first).Describe(Ct);
        Assert.Equal((AlertLifecycle.Closed, AlertCloseReason.Recovered, (DateTimeOffset?)Now), (closed!.Lifecycle, closed.Reason, closed.ClosedAt));
        Assert.Equal(["alert.opened", "alert.site-notified", "alert.closed", "alert.site-notified"], await identity.AliasesAsync($"alert/{first}"));
        Assert.Empty(await OpenAlertsAsync(site.SiteId));
        Assert.Empty(await TrackedAlertsAsync(site.OwnerId));
        Assert.Equal([StreakChanged, StreakChanged, Ended, Delivered], Battery(await identity.AliasesAsync(stream), journaled.Count));

        var state = await identity.DeviceStateAsync(nodeId);
        Assert.Equal((1, (DeviceBatteryAlert?)null, new BatteryStreaks(false, 0, 0)), (state.BatteryEpisode, state.OpenBatteryAlert, state.BatteryStreaks));
        Assert.Empty(state.PendingAlertDeliveries);

        // The next low episode is episode 2, with another Alert ID.
        var second = await LowAsync(node);
        Assert.Equal(AlertIds.Battery(nodeId, 2), second);
        Assert.NotEqual(first, second);
        Assert.Equal(second, Assert.Single(await OpenAlertsAsync(site.SiteId)).AlertId);
        Assert.Equal([Started, Ended, Started], (await identity.AliasesAsync(stream)).Where(alias => alias is Started or Ended));
    }

    [Fact]
    public async Task ALowLevelWhileChargingNeverAlerts()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, "Lettuce");
        var stream = $"device/{node.DeviceId}";
        var journaled = await identity.AliasesAsync(stream);

        for (var report = 0; report < 6; report++)
        {
            await ReportAsync(node, 10, ChargeStatus.Charging);
        }

        // Nothing of the battery is journaled for a Node whose reports move no streak.
        Assert.Empty(Battery(await identity.AliasesAsync(stream), journaled.Count));
        Assert.Empty(await OpenAlertsAsync(site.SiteId));
    }

    [Fact]
    public async Task EnoughChargeWhileNotChargingNeverAlertsAndTwentyIsNotBelowTwenty()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, "Lettuce");
        var stream = $"device/{node.DeviceId}";
        var journaled = await identity.AliasesAsync(stream);

        foreach (var percent in new uint[] { 20, 55, 20, 55, 20 })
        {
            await ReportAsync(node, percent);
        }

        Assert.Empty(Battery(await identity.AliasesAsync(stream), journaled.Count));
        Assert.Empty(await OpenAlertsAsync(site.SiteId));
    }

    [Fact]
    public async Task ANodeFlappingAroundTwentyPercentAlertsOnlyWithItsThirdConsecutiveLowReport()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, "Lettuce");
        var nodeId = node.DeviceId.ToString();
        uint[] levels = [19, 19, 20, 19, 19, 21, 19, 19];

        foreach (var percent in levels)
        {
            await ReportAsync(node, percent);
            Assert.Empty(await OpenAlertsAsync(site.SiteId));
        }

        await ReportAsync(node, 19);

        Assert.Equal(AlertIds.Battery(nodeId, 1), Assert.Single(await OpenAlertsAsync(site.SiteId)).AlertId);
        Assert.Single(await identity.AliasesAsync($"device/{nodeId}"), alias => alias == Started);
    }

    [Fact]
    public async Task AnOpenAlertThatFlapsClosesOnlyWithItsThirdConsecutiveRecoveredReport()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, "Lettuce");
        var alertId = await LowAsync(node);

        // Recovered, recovered, low, recovered, recovered: still open.
        foreach (var percent in new uint[] { 40, 40, 12, 40, 40 })
        {
            await ReportAsync(node, percent);
            Assert.Equal(AlertLifecycle.Open, (await identity.Alert(alertId).Describe(Ct))?.Lifecycle);
        }

        await ReportAsync(node, 40);

        Assert.Equal(AlertLifecycle.Closed, (await identity.Alert(alertId).Describe(Ct))?.Lifecycle);
        Assert.Empty(await OpenAlertsAsync(site.SiteId));
    }

    [Fact]
    public async Task AReportWhoseBatteryCouldNotBeReadEndsTheStreakAndStartsNone()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, "Lettuce");
        var nodeId = node.DeviceId.ToString();

        await ReportAsync(node, 14);
        await ReportAsync(node, 14);
        await ReportAsync(node, batteryPercent: null);
        Assert.Equal(0, (await identity.DeviceStateAsync(nodeId)).BatteryLowStreak);

        // The streak restarts at the fourth report: no Alert.
        await ReportAsync(node, 14);

        Assert.Equal(1, (await identity.DeviceStateAsync(nodeId)).BatteryLowStreak);
        Assert.Empty(await OpenAlertsAsync(site.SiteId));

        // A charger status that could not be read below 20 % is neither low nor recovered.
        await ReportAsync(node, 14);
        await ReportAsync(node, 14, ChargeStatus.Unspecified);
        Assert.Equal(0, (await identity.DeviceStateAsync(nodeId)).BatteryLowStreak);
        Assert.Empty(await OpenAlertsAsync(site.SiteId));
    }

    [Fact]
    public async Task AFrameSentAgainIsCountedOnce()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, "Lettuce");
        var nodeId = node.DeviceId.ToString();

        // Two low reports, each delivered twice: a streak of two, no Alert.
        for (var report = 0; report < 2; report++)
        {
            identity.Time.Advance(Interval);
            var frame = node.Wake(Now, 14, ChargeStatus.NotCharging);
            Assert.Equal(DeviceIngestStatus.Stored, (await IngestAsync(node, frame)).Status);
            identity.Time.Advance(TimeSpan.FromSeconds(30));
            Assert.Equal(DeviceIngestStatus.Duplicate, (await IngestAsync(node, frame)).Status);
        }

        Assert.Equal(2, (await identity.DeviceStateAsync(nodeId)).BatteryLowStreak);
        Assert.Empty(await OpenAlertsAsync(site.SiteId));

        // The third low report, delivered twice: one Alert.
        identity.Time.Advance(Interval);
        var third = node.Wake(Now, 13, ChargeStatus.NotCharging);
        Assert.Equal(DeviceIngestStatus.Stored, (await IngestAsync(node, third)).Status);
        Assert.Equal(DeviceIngestStatus.Duplicate, (await IngestAsync(node, third)).Status);

        var state = await identity.DeviceStateAsync(nodeId);
        Assert.Equal((1, AlertIds.Battery(nodeId, 1), 13), (state.BatteryEpisode, state.OpenBatteryAlert?.AlertId, state.OpenBatteryAlert?.BatteryPercent));
        Assert.Equal(new BatteryStreaks(true, 0, 0), state.BatteryStreaks);
        Assert.Single(await OpenAlertsAsync(site.SiteId));
        Assert.Single(await identity.AliasesAsync($"device/{nodeId}"), alias => alias == Started);
    }

    // A Node without synced time gets "receive time - age" as measured_at, which every resend moves later. The
    // report is counted at the time stored with its key at first delivery, so the resend is still no new report.
    [Fact]
    public async Task AnUnsyncedFrameSentAgainIsCountedOnceAtTheTimeOfItsFirstDelivery()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, "Lettuce");
        var nodeId = node.DeviceId.ToString();

        identity.Time.Advance(Interval);
        var frame = node.WakeUnsynced(node.UptimeMs, 14, ChargeStatus.NotCharging);
        var firstDelivery = Now;
        Assert.Equal(DeviceIngestStatus.Stored, (await IngestAsync(node, frame)).Status);

        identity.Time.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(DeviceIngestStatus.Duplicate, (await IngestAsync(node, frame)).Status);
        identity.Time.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(DeviceIngestStatus.Duplicate, (await IngestAsync(node, frame)).Status);

        var state = await identity.DeviceStateAsync(nodeId);
        Assert.Equal((1, (DateTimeOffset?)firstDelivery), (state.BatteryLowStreak, state.BatteryCountedAt));
        Assert.Empty(await OpenAlertsAsync(site.SiteId));
    }

    [Fact]
    public async Task AnOlderReportArrivingLateIsIgnored()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, "Lettuce");
        var nodeId = node.DeviceId.ToString();
        var stream = $"device/{nodeId}";

        await ReportAsync(node, 14);
        await ReportAsync(node, 14);
        var counted = Now;
        var journaled = await identity.AliasesAsync(stream);

        // A low report measured before the last one counted arrives late: stored, and not counted.
        identity.Time.Advance(Interval);
        var late = await IngestAsync(node, node.Wake(counted.AddMinutes(-5), 14, ChargeStatus.NotCharging));

        Assert.Equal(DeviceIngestStatus.Stored, late.Status);
        Assert.Equal(journaled, await identity.AliasesAsync(stream));
        Assert.Equal((2, (DateTimeOffset?)counted), ((await identity.DeviceStateAsync(nodeId)).BatteryLowStreak, (await identity.DeviceStateAsync(nodeId)).BatteryCountedAt));
        Assert.Empty(await OpenAlertsAsync(site.SiteId));

        // Also behind a report that changed nothing and so journaled nothing: a healthy report ends the streak,
        // a second one leaves the state as it is, and two low ones measured just before that one arrive late.
        await ReportAsync(node, 80);
        var reset = await identity.AliasesAsync(stream);
        await ReportAsync(node, 80);
        Assert.Empty(Battery(await identity.AliasesAsync(stream), reset.Count));
        var healthy = Now;
        identity.Time.Advance(Interval);
        Assert.Equal(DeviceIngestStatus.Stored, (await IngestAsync(node, node.Wake(healthy.AddMinutes(-2), 14, ChargeStatus.NotCharging))).Status);
        Assert.Equal(DeviceIngestStatus.Stored, (await IngestAsync(node, node.Wake(healthy.AddMinutes(-1), 14, ChargeStatus.NotCharging))).Status);

        Assert.Equal(0, (await identity.DeviceStateAsync(nodeId)).BatteryLowStreak);
        Assert.Empty(await OpenAlertsAsync(site.SiteId));
    }

    // The streak is journaled state: a new silo goes on from it.
    [Fact]
    public async Task AJournaledStreakOfTwoOpensTheAlertWithTheNextLowReportAfterARestart()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, "Lettuce");
        var nodeId = node.DeviceId.ToString();

        await ReportAsync(node, 14);
        await ReportAsync(node, 14);
        await identity.RestartSiloAsync(Now);
        Assert.Empty(await OpenAlertsAsync(site.SiteId));

        await ReportAsync(node, 13);

        var state = await identity.DeviceStateAsync(nodeId);
        Assert.Equal((1, AlertIds.Battery(nodeId, 1), 13), (state.BatteryEpisode, state.OpenBatteryAlert?.AlertId, state.OpenBatteryAlert?.BatteryPercent));
        Assert.Equal(AlertIds.Battery(nodeId, 1), Assert.Single(await OpenAlertsAsync(site.SiteId)).AlertId);

        // And an open episode: after another restart three recovered reports close it.
        await ReportAsync(node, 60);
        await identity.RestartSiloAsync(Now);
        await ReportAsync(node, 60);
        Assert.Single(await OpenAlertsAsync(site.SiteId));
        await ReportAsync(node, 60);
        Assert.Empty(await OpenAlertsAsync(site.SiteId));
    }

    // A healthy report journals nothing of the battery, so the last counted time is not in the battery state. The
    // Node's journaled liveness is: after a restart a frame older than it is still no new report.
    [Fact]
    public async Task OlderLowReportsArrivingLateAfterARestartAreIgnored()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, "Lettuce");
        var nodeId = node.DeviceId.ToString();
        var stream = $"device/{nodeId}";
        var journaled = await identity.AliasesAsync(stream);
        var first = Now;

        await ReportAsync(node, 80);
        await ReportAsync(node, 80);
        await ReportAsync(node, 80);
        var healthy = Now;
        Assert.Empty(Battery(await identity.AliasesAsync(stream), journaled.Count));
        Assert.Null((await identity.DeviceStateAsync(nodeId)).BatteryCountedAt);

        await identity.RestartSiloAsync(healthy.AddMinutes(1));

        // Three low reports measured between the healthy ones, delivered late: stored, and none counted.
        for (var minute = 1; minute <= 3; minute++)
        {
            var late = await IngestAsync(node, node.Wake(first.AddMinutes(minute), 9, ChargeStatus.NotCharging));
            Assert.Equal(DeviceIngestStatus.Stored, late.Status);
        }

        Assert.Empty(Battery(await identity.AliasesAsync(stream), journaled.Count));
        Assert.Equal(0, (await identity.DeviceStateAsync(nodeId)).BatteryEpisode);
        Assert.Empty(await OpenAlertsAsync(site.SiteId));

        // A report newer than the last one accepted counts as ever.
        await ReportAsync(node, 9);
        Assert.Equal(1, (await identity.DeviceStateAsync(nodeId)).BatteryLowStreak);
    }

    // The write of step 7 fails once in the database. The log-consistency adaptor repeats a failed write until it
    // lands, inside the grain's turn, so the frame is not answered with retry: it waits, and its report is
    // counted exactly once, also when the Node sends the frame again.
    [Fact]
    public async Task AReportWhoseJournalWriteFailsOnceIsCountedExactlyOnce()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, "Lettuce");
        var nodeId = node.DeviceId.ToString();
        var stream = $"device/{nodeId}";
        var journaled = await identity.AliasesAsync(stream);

        identity.Time.Advance(Interval);
        var frame = node.Wake(Now, 14, ChargeStatus.NotCharging);
        DeviceIngestResult first;

        await using (await identity.FailNextJournalWriteAsync(stream))
        {
            first = await IngestAsync(node, frame);
        }

        Assert.Equal(DeviceIngestStatus.Stored, first.Status);
        Assert.Equal([StreakChanged], Battery(await identity.AliasesAsync(stream), journaled.Count));

        Assert.Equal(DeviceIngestStatus.Duplicate, (await IngestAsync(node, frame)).Status);
        Assert.Equal(DeviceIngestStatus.Duplicate, (await IngestAsync(node, frame)).Status);

        Assert.Equal([StreakChanged], Battery(await identity.AliasesAsync(stream), journaled.Count));
        Assert.Equal(1, (await identity.DeviceStateAsync(nodeId)).BatteryLowStreak);
    }

    [Fact]
    public async Task ANodeOnNoLotGetsAnAlertWithoutALot()
    {
        var site = await SiteAsync();
        var (node, lotId) = await NodeAsync(site, lotName: null);
        var nodeId = node.DeviceId.ToString();
        Assert.Null(lotId);

        var alertId = await LowAsync(node);

        var alert = await identity.Alert(alertId).Describe(Ct);
        Assert.Equal((AlertKind.Battery, (string?)null, (int?)14, (DeviceKind?)DeviceKind.Node), (alert!.Kind, alert.LotId, alert.BatteryPercent, alert.DeviceKind));
        Assert.Equal((alertId, (string?)null), (Assert.Single(await OpenAlertsAsync(site.SiteId)).AlertId, Assert.Single(await OpenAlertsAsync(site.SiteId)).LotId));
        Assert.Equal(AlertIds.Battery(nodeId, 1), alertId);
    }

    // The Alert keeps the Lot the Node had when it opened.
    [Fact]
    public async Task AnAlertKeepsTheLotItsNodeHadWhenItOpened()
    {
        var site = await SiteAsync();
        var (node, lotId) = await NodeAsync(site, "Lettuce");
        var nodeId = node.DeviceId.ToString();
        var alertId = await LowAsync(node);

        Assert.Equal(DeviceAssignmentOutcome.Unassigned, (await identity.Device(nodeId).Unassign(site.SiteId, Ct)).Outcome);
        await ReportAsync(node, 12);

        Assert.Equal(lotId, (await identity.Alert(alertId).Describe(Ct))!.LotId);
        Assert.Equal(lotId, Assert.Single(await OpenAlertsAsync(site.SiteId)).LotId);
    }

    [Fact]
    public async Task AnUndeliveredOpenAndAnUndeliveredCloseAreDeliveredByTheNextWakeOrReport()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, "Lettuce");
        var nodeId = node.DeviceId.ToString();
        var alertId = AlertIds.Battery(nodeId, 1);

        // The Alert grain cannot be reached: the frame is stored and acknowledged, the episode is journaled, the
        // open stays pending.
        await ReportAsync(node, 14);
        await ReportAsync(node, 14);
        identity.AlertFaults.FailNext(1);
        await ReportAsync(node, 14);

        var pending = await identity.DeviceStateAsync(nodeId);
        Assert.Equal((1, alertId), (pending.BatteryEpisode, pending.OpenBatteryAlert?.AlertId));
        var open = Assert.Single(pending.PendingAlertDeliveries);
        Assert.Equal((AlertLifecycle.Open, AlertKind.Battery, (int?)14), (open.Change, open.Kind, open.BatteryPercent));
        Assert.Empty(await identity.AliasesAsync($"alert/{alertId}"));

        // The next wake delivers it: one episode, one Alert.
        await identity.WakeDeviceAsync(nodeId);

        Assert.Equal(alertId, Assert.Single(await OpenAlertsAsync(site.SiteId)).AlertId);
        Assert.Equal(14, (await identity.Alert(alertId).Describe(Ct))!.BatteryPercent);
        Assert.Single(await identity.AliasesAsync($"device/{nodeId}"), alias => alias == Started);

        // The same for the close, which the next report delivers.
        await ReportAsync(node, 60);
        await ReportAsync(node, 60);
        identity.AlertFaults.FailNext(1);
        await ReportAsync(node, 60);

        var closing = await identity.DeviceStateAsync(nodeId);
        Assert.Null(closing.OpenBatteryAlert);
        Assert.Equal(AlertLifecycle.Closed, Assert.Single(closing.PendingAlertDeliveries).Change);
        Assert.Equal(AlertLifecycle.Open, (await identity.Alert(alertId).Describe(Ct))?.Lifecycle);

        await ReportAsync(node, 60);

        Assert.Equal(AlertLifecycle.Closed, (await identity.Alert(alertId).Describe(Ct))?.Lifecycle);
        Assert.Empty(await OpenAlertsAsync(site.SiteId));
        Assert.Empty((await identity.DeviceStateAsync(nodeId)).PendingAlertDeliveries);
        Assert.Equal(
            [Started, Delivered, Ended, Delivered],
            (await identity.AliasesAsync($"device/{nodeId}")).Where(alias => alias is Started or Ended or Delivered));
    }

    [Fact]
    public async Task OnlyANodeOpensABatteryAlertAndOnlyTheNodeThatOpenedItClosesIt()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, "Lettuce");
        var nodeId = node.DeviceId.ToString();
        var alertId = await LowAsync(node);
        var alert = identity.Alert(alertId);

        // A client is not the Device grain that opened the Alert.
        Assert.Equal(new AlertResult(AlertOutcome.Refused), await alert.Close(new CloseAlert(AlertCloseReason.Recovered, Now), Ct));
        Assert.Equal(AlertLifecycle.Open, (await alert.Describe(Ct))?.Lifecycle);

        // No Alert for a Hub, none for a Device that does not say what it is, and none under an ID the request
        // does not derive (the silent Alert of the same episode is another ID).
        var hubId = "3f2a9c0d1e4b5a67";
        var ofHub = identity.Alert(AlertIds.Battery(hubId, 1));
        Assert.Equal(AlertOutcome.Refused, (await ofHub.OpenForDevice(new OpenDeviceAlert(AlertKind.Battery, hubId, 1, site.SiteId, null, Now, DeviceKind.Hub, BatteryPercent: 14), Ct)).Outcome);
        Assert.Equal(AlertOutcome.Refused, (await ofHub.OpenForDevice(new OpenDeviceAlert(AlertKind.Battery, hubId, 1, site.SiteId, null, Now, BatteryPercent: 14), Ct)).Outcome);
        Assert.Null(await ofHub.Describe(Ct));

        var asSilent = identity.Alert(AlertIds.Silent(nodeId, 2));
        Assert.Equal(AlertOutcome.Refused, (await asSilent.OpenForDevice(new OpenDeviceAlert(AlertKind.Battery, nodeId, 2, site.SiteId, null, Now, DeviceKind.Node, BatteryPercent: 14), Ct)).Outcome);
        Assert.Null(await asSilent.Describe(Ct));

        // Opening it again journals nothing and keeps the level it opened with.
        var again = await alert.OpenForDevice(new OpenDeviceAlert(AlertKind.Battery, nodeId, 1, site.SiteId, null, Now, DeviceKind.Node, BatteryPercent: 3), Ct);
        Assert.Equal(new AlertResult(AlertOutcome.Open, Reported: true), again);
        Assert.Equal(["alert.opened", "alert.site-notified"], await identity.AliasesAsync($"alert/{alertId}"));
        Assert.Equal(14, (await alert.Describe(Ct))!.BatteryPercent);
    }

    [Fact]
    public async Task APausedNodeCountsNoReport()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, "Lettuce");
        var nodeId = node.DeviceId.ToString();
        var stream = $"device/{nodeId}";
        var lastReport = Now;

        // Pause has no command before Epic 8: its event is journaled here, and a new silo reads it.
        var version = (await identity.Store.ReadStreamAsync(stream, Ct)).Count;
        Assert.True(await identity.Store.AppendAsync(stream, version, [new DevicePaused(DevicePauseSource.Device, null, lastReport)], Ct));
        await identity.RestartSiloAsync(lastReport);
        var journaled = await identity.AliasesAsync(stream);

        // A paused Node's frames are acknowledged and store no report: nothing is counted.
        for (var report = 0; report < 4; report++)
        {
            await ReportAsync(node, 9);
        }

        Assert.Empty(Battery(await identity.AliasesAsync(stream), journaled.Count));
        Assert.Equal(0, (await identity.DeviceStateAsync(nodeId)).BatteryEpisode);
        Assert.Empty(await OpenAlertsAsync(site.SiteId));
    }

    // What the Device grain states once as its Alert opens is what the Alerts list and the notifications say; the
    // Lot keeps its status.
    [Fact]
    public async Task ABatteryAlertShowsInTheAlertsReadModelWithItsLevelAndLeavesTheLotAsItWas()
    {
        var site = await SiteAsync();
        var (node, lotId) = await NodeAsync(site, "Lettuce");
        var (other, otherLot) = await NodeAsync(site, "Beans");
        var nodeId = node.DeviceId.ToString();
        await CatchUpAsync();
        var lots = identity.SiloServices.GetRequiredService<LotsReadModel>();
        var before = (await lots.ListLotsAsync(site.SiteId, Ct)).ToDictionary(lot => lot.LotId, StringComparer.Ordinal);

        var alertId = await LowAsync(node);
        var opened = Now;
        await CatchUpAsync();

        // The Alerts list: no Threshold Alert, one Health Alert with the level, counted as open.
        var alerts = identity.SiloServices.GetRequiredService<AlertsReadModel>();
        var closedSince = Now - AlertsReadModel.ClosedRetention;
        Assert.Empty((await alerts.ListAlertsAsync(site.SiteId, closedSince, null, 50, Ct)).Alerts);
        // Counted with the uncalibrated Alerts of the two Nodes on Lots (Story 7.4), which are Health Alerts too.
        Assert.Equal(1 + 2, await alerts.CountOpenAsync(site.SiteId, Ct));
        Assert.Equal(
            [new HealthAlertView(alertId, "battery", nodeId, "node", lotId, "Lettuce", null, opened, null, null, 14)],
            await DeviceHealthAlertsAsync(alerts, site.SiteId, closedSince));

        // The Lots: every Lot has the status, the cause and the silence members it had without the Alert.
        var after = (await lots.ListLotsAsync(site.SiteId, Ct)).ToDictionary(lot => lot.LotId, StringComparer.Ordinal);
        foreach (var lot in new[] { lotId!, otherLot! })
        {
            Assert.Equal(
                (before[lot].Status, before[lot].UnknownCause, before[lot].SilentHubId, before[lot].SilentSince),
                (after[lot].Status, after[lot].UnknownCause, after[lot].SilentHubId, after[lot].SilentSince));
            Assert.NotEqual("unknown", after[lot].Status);
        }

        // The Devices list says nothing of the Alert: no Node is silent.
        var devices = identity.SiloServices.GetRequiredService<DevicesReadModel>();
        Assert.All(await devices.ListDevicesAsync(site.SiteId, Ct), device => Assert.Null(device.SilentSince));

        // The notification text, written from what the Alert carries and the real read models.
        var push = identity.SiloServices.GetRequiredService<PushContentBuilder>();
        var listed = Assert.Single(await OpenAlertsAsync(site.SiteId));
        var entry = new NotificationEntry(alertId, listed.Kind, null, listed.LotId, null, listed.DeviceId, null, listed.OpenedAt, listed.DeviceKind, listed.SilentSince, listed.BatteryPercent);
        var content = await push.BuildAsync(new Notification(site.OwnerId, site.SiteId, NotificationKind.Alert, Now, null, [entry]), Now);
        Assert.Equal(("Node battery 14 %, not charging", "Lot 'Lettuce'."), (content!.Title, content.Body));

        // Recovered: the Alert stays in the list as closed, with the level it opened with.
        for (var report = 0; report < 3; report++)
        {
            await ReportAsync(node, 70, ChargeStatus.Charging);
        }

        await CatchUpAsync();
        Assert.Equal(0 + 2, await alerts.CountOpenAsync(site.SiteId, Ct));
        Assert.Equal(
            [new HealthAlertView(alertId, "battery", nodeId, "node", lotId, "Lettuce", null, opened, Now, "recovered", 14)],
            await DeviceHealthAlertsAsync(alerts, site.SiteId, Now - AlertsReadModel.ClosedRetention));
        Assert.NotNull(other);
    }

    [Fact]
    public async Task ABatteryAlertIsDeliveredLikeAnyAlertAndRemindsNoSoonerThanADayLater()
    {
        // Inside the Notification Window of a User who set none (07:00 to 22:00, UTC), with room for the reports.
        GoTo(11, 0);
        var site = await SiteAsync();
        var (node, lotId) = await NodeAsync(site, "Lettuce");
        var nodeId = node.DeviceId.ToString();

        var alertId = await LowAsync(node);
        var opened = Now;

        await identity.WakeUserAsync(site.OwnerId);
        var sent = Assert.Single(NotificationsOf(site.OwnerId, alertId));
        Assert.Equal(NotificationKind.Alert, sent.Notification.Kind);
        Assert.Equal(
            new NotificationEntry(alertId, AlertKind.Battery, null, lotId, null, nodeId, null, opened, DeviceKind.Node, null, 14),
            Assert.Single(sent.Notification.Entries));

        // The Reminder comes 24 h after the notification and not a minute before. That a Health Alert never
        // reminds more often, whatever the cadence, is shown in ReminderIntervalRuleTests. The Node keeps
        // reporting low meanwhile, so it is neither silent nor recovered.
        for (var hour = 0; hour < 23; hour++)
        {
            identity.Time.Advance(TimeSpan.FromHours(1));
            Assert.Equal(DeviceIngestStatus.Stored, (await IngestAsync(node, node.Wake(Now, 12, ChargeStatus.NotCharging))).Status);
        }

        identity.Time.SetUtcNow(opened + TimeSpan.FromHours(24) - TimeSpan.FromMinutes(1));
        await identity.WakeUserAsync(site.OwnerId);
        Assert.Single(NotificationsOf(site.OwnerId, alertId));

        identity.Time.Advance(TimeSpan.FromMinutes(1));
        await identity.WakeUserAsync(site.OwnerId);
        var reminded = NotificationsOf(site.OwnerId, alertId);
        Assert.Equal(2, reminded.Count);
        Assert.Equal((NotificationKind.Reminder, alertId), (reminded[1].Notification.Kind, Assert.Single(reminded[1].Notification.Entries).AlertId));
        Assert.Equal(14, Assert.Single(reminded[1].Notification.Entries).BatteryPercent);

        // Still one Alert, and never a silent one.
        Assert.Equal(alertId, Assert.Single(await OpenAlertsAsync(site.SiteId)).AlertId);
    }

    // The battery events of a Device stream after its first `skip` events.
    private static List<string> Battery(IEnumerable<string> aliases, int skip) =>
        [.. aliases.Skip(skip).Where(alias => alias is StreakChanged or Started or Ended or Delivered)];

    // An Active Site created by its Owner, whose User grain therefore holds the Site and is told of its Alerts.
    private async Task<(string SiteId, string OwnerId)> SiteAsync()
    {
        var ownerId = Guid.NewGuid().ToString();
        var created = await identity.User(ownerId).CreateSite("k1", "Home", Ct);
        Assert.Equal(SiteCreationOutcome.Created, created.Outcome);
        return (created.Site!.Id, ownerId);
    }

    // A Node of the Site that declared its Sensors, on a new Lot when `lotName` is given: its last report is now,
    // charging at 87 %.
    private async Task<(SimulatedDevice Node, string? LotId)> NodeAsync((string SiteId, string OwnerId) site, string? lotName)
    {
        string? lotId = null;

        if (lotName is not null)
        {
            lotId = Guid.CreateVersion7().ToString();
            Assert.True(await identity.Store.AppendAsync($"lot/{lotId}", 0, [new LotCreated(site.SiteId, lotName)], Ct));
        }

        var node = SimulatedDevice.Create(ProtocolKind.Node);
        var enrol = new EnrolDevice(site.SiteId, DeviceKind.Node, identity.Vault.Wrap(node.DeviceId, node.Keys.DeviceKey), site.OwnerId, Guid.NewGuid().ToString("N"), lotId);
        Assert.Equal(DeviceEnrolmentOutcome.Enrolled, (await identity.Device(node.DeviceId.ToString()).Enrol(enrol, Ct)).Outcome);

        Assert.True((await AcknowledgedAsync(node, node.Wake(Now))).SpecificationsUnknown);
        Assert.False((await AcknowledgedAsync(node, node.Wake(Now))).SpecificationsUnknown);

        return (node, lotId);
    }

    // The Node's next wake, a quarter of an hour after the last: stored and acknowledged.
    private async Task ReportAsync(SimulatedDevice node, uint? batteryPercent, ChargeStatus charging = ChargeStatus.NotCharging)
    {
        identity.Time.Advance(Interval);
        await AcknowledgedAsync(node, node.Wake(Now, batteryPercent, charging));
    }

    private async Task<Downlink> AcknowledgedAsync(SimulatedDevice node, NodeFrame frame)
    {
        var result = await IngestAsync(node, frame);

        Assert.Equal(DeviceIngestStatus.Stored, result.Status);
        return node.OpenDownlink(SealedEnvelope.Parser.ParseFrom(result.Downlink));
    }

    private Task<DeviceIngestResult> IngestAsync(SimulatedDevice node, NodeFrame frame)
    {
        var envelope = node.SealFrame(frame);
        return identity.Device(node.DeviceId.ToString()).Ingest(
            new DeviceIngest(envelope.ProtocolVersion, envelope.Counter, envelope.Ciphertext.ToByteArray(), UnknownHub),
            Ct);
    }

    // Three reports at 14 %, not charging: the Node's next battery Alert is open.
    private async Task<Guid> LowAsync(SimulatedDevice node)
    {
        var nodeId = node.DeviceId.ToString();
        var episode = (await identity.DeviceStateAsync(nodeId)).BatteryEpisode + 1;

        for (var report = 0; report < 3; report++)
        {
            await ReportAsync(node, 14);
        }

        var alertId = AlertIds.Battery(nodeId, episode);
        Assert.Equal(AlertLifecycle.Open, (await identity.Alert(alertId).Describe(Ct))?.Lifecycle);
        return alertId;
    }

    // Moves the clock on to the next hh:mm UTC, today or tomorrow: the clock only moves forward.
    private void GoTo(int hour, int minute)
    {
        var target = new DateTimeOffset(Now.UtcDateTime.Date, TimeSpan.Zero).AddHours(hour).AddMinutes(minute);
        identity.Time.SetUtcNow(target > Now ? target : target.AddDays(1));
    }

    // The Site's open Alerts without the uncalibrated Alert every Node on a Lot has for its soil Sensor from its
    // first declaration on (Story 7.4). UncalibratedAlertGrainTests pins that Alert; this suite is about the
    // Alerts a Device opens for itself, and counts those.
    private async Task<List<SiteAlert>> OpenAlertsAsync(string siteId) =>
        [.. (await identity.Site(siteId).OpenAlerts(Ct)).Where(alert => alert.Kind != AlertKind.Uncalibrated)];

    // The Health Alerts the Alerts list holds, without those uncalibrated Alerts.
    private static async Task<List<HealthAlertView>> DeviceHealthAlertsAsync(AlertsReadModel alerts, string siteId, DateTimeOffset closedSince) =>
        [.. (await alerts.ListHealthAlertsAsync(siteId, closedSince, Ct)).Where(alert => alert.Kind != "uncalibrated")];

    // What the User grain tracks, without those uncalibrated Alerts.
    private async Task<List<Coldframe.Server.Identity.TrackedAlert>> TrackedAlertsAsync(string userId) =>
        [.. (await identity.UserStateAsync(userId)).TrackedAlerts.Values.Where(tracked => tracked.Alert.Kind != AlertKind.Uncalibrated)];

    // The notifications a User was sent about one Alert: the uncalibrated Alert of a Node on a Lot has its own.
    private List<RecordedNotification> NotificationsOf(string userId, Guid alertId) =>
        [.. identity.Notifications.For(userId).Where(sent => sent.Notification.Entries.Any(entry => entry.AlertId == alertId))];

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

    // The Lot's status and its cause when unknown, once the lots projection has caught up.
    private async Task<(string Status, string? Cause)> LotAsync(string lotId)
    {
        await identity.SiloServices.GetProjectionRunner<LotsProjector>().CatchUpAsync(Ct);

        await using var command = identity.Database.DataSource.CreateCommand("SELECT status, unknown_cause FROM lots WHERE lot_id = @lot_id");
        command.Parameters.AddWithValue("lot_id", lotId);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        Assert.True(await reader.ReadAsync(Ct));

        return (reader.GetString(0), await reader.IsDBNullAsync(1, Ct) ? null : reader.GetString(1));
    }
}
