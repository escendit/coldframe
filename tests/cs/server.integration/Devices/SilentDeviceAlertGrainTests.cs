using System.Security.Cryptography;
using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Lots;
using Coldframe.Contracts.Notifications;
using Coldframe.Contracts.Sites;
using Coldframe.Crypto;
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
/// Silent Device Alerts on a TestCluster with a fake clock (Story 7.1; AD-6, AD-7, AD-14): the Device grain
/// measures its own silence against a journaled expiry, opens a Health Alert of kind <c>silent</c> through the
/// Alert grain and closes it when the Device reports again; a Node behind a silent Hub opens nothing; and the lots
/// projector turns an open <c>silent</c> Alert into Lot status <c>unknown</c>. One test per row of the story's
/// matrix. Frames and heartbeats come from the Device simulator only; the Device's reminder is fired by hand.
/// </summary>
[Collection(IngestSuites.Name)]
public sealed class SilentDeviceAlertGrainTests(IdentityCluster identity) : IClassFixture<IdentityCluster>
{
    // A relay Hub no Site enrolled: it suppresses nothing.
    private const string UnknownHub = "92064422c012f481";

    private const string Started = "device.silence-started";

    private const string Ended = "device.silence-ended";

    private const string Delivered = "device.alert-delivered";

    private const string ClockRestarted = "device.silence-clock-restarted";

    private static readonly TimeSpan NodeWindow = TimeSpan.FromHours(6);

    private static readonly TimeSpan HubWindow = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTimeOffset Now => identity.Time.GetUtcNow();

    [Fact]
    public async Task ANodeThatGoesQuietOpensOneSilentAlertAtItsWindowAndNotASecondBefore()
    {
        var site = await SiteAsync();
        var (node, lotId) = await NodeAsync(site, "Beans", UnknownHub);
        var nodeId = node.DeviceId.ToString();
        var lastReport = Now;
        var before = await LotAsync(lotId);
        Assert.Equal((lastReport, lastReport + NodeWindow), await ClockAsync(nodeId));

        // Before the window: nothing is journaled.
        var journaled = await identity.AliasesAsync($"device/{nodeId}");
        identity.Time.Advance(NodeWindow - Second);
        await identity.WakeDeviceAsync(nodeId);

        Assert.Equal(journaled, await identity.AliasesAsync($"device/{nodeId}"));
        Assert.Empty(await identity.Site(site.SiteId).OpenAlerts(Ct));

        // At the window: episode 1, journaled before the Alert it opens.
        identity.Time.Advance(Second);
        await identity.WakeDeviceAsync(nodeId);
        await identity.WakeDeviceAsync(nodeId);

        var alertId = AlertIds.Silent(nodeId, 1);
        var state = await identity.DeviceStateAsync(nodeId);
        Assert.Equal((1, new DeviceSilentAlert(alertId, 1, Now)), (state.SilenceEpisode, state.OpenSilentAlert));
        Assert.Empty(state.PendingAlertDeliveries);
        Assert.Equal([Started, Delivered], (await identity.AliasesAsync($"device/{nodeId}")).Skip(journaled.Count));
        Assert.True(await PositionAsync($"device/{nodeId}", Started) < await PositionAsync($"alert/{alertId}", "alert.opened"));

        // The Alert is the Node's own: no side, no Sensor, no quantity, the Lot the Node is on. It says that it
        // is a Node's and when the Node was last heard: its last accepted measured_at, six hours ago (Story 7.2).
        Assert.Equal(
            new AlertSnapshot(alertId, AlertKind.Silent, AlertLifecycle.Open, null, site.SiteId, lotId, null, nodeId, null, Now, DeviceKind: DeviceKind.Node, SilentSince: lastReport),
            await identity.Alert(alertId).Describe(Ct));
        Assert.Equal(["alert.opened", "alert.site-notified"], await identity.AliasesAsync($"alert/{alertId}"));

        // The Site lists it and its member was told.
        var listed = Assert.Single(await identity.Site(site.SiteId).OpenAlerts(Ct));
        Assert.Equal(new SiteAlert(alertId, AlertKind.Silent, null, lotId, null, nodeId, null, Now, DeviceKind.Node, lastReport), listed);
        Assert.Equal(listed, Assert.Single((await identity.UserStateAsync(site.OwnerId)).TrackedAlerts.Values).Alert);

        // The Lot cannot be read: unknown, because of its Node.
        Assert.NotEqual("unknown", before.Status);
        Assert.Equal(("unknown", "node"), await LotAsync(lotId));
    }

    [Fact]
    public async Task AHubThatGoesQuietOpensOneSilentAlertWithoutALotAndItsMemberIsNotified()
    {
        // The window ends at noon, inside the Notification Window of a User who set none (07:00 to 22:00, UTC).
        GoTo(11, 55);
        var site = await SiteAsync();
        var hub = await HubAsync(site);
        var hubId = hub.DeviceId.ToString();
        await HeartbeatAsync(hub);
        var journaled = await identity.AliasesAsync($"device/{hubId}");

        identity.Time.Advance(HubWindow - Second);
        await identity.WakeDeviceAsync(hubId);
        Assert.Equal(journaled, await identity.AliasesAsync($"device/{hubId}"));

        identity.Time.Advance(Second);
        await identity.WakeDeviceAsync(hubId);

        // Story 7.2: the Hub states once, as its Alert opens, what it is and when it was last heard: its last
        // accepted heartbeat, five minutes ago. The Alert, the Site and the member's notification carry both.
        var alertId = AlertIds.Silent(hubId, 1);
        var opened = Now;
        var lastHeard = opened - HubWindow;
        Assert.Equal(
            new AlertSnapshot(alertId, AlertKind.Silent, AlertLifecycle.Open, null, site.SiteId, null, null, hubId, null, opened, DeviceKind: DeviceKind.Hub, SilentSince: lastHeard),
            await identity.Alert(alertId).Describe(Ct));
        var listed = Assert.Single(await identity.Site(site.SiteId).OpenAlerts(Ct));
        Assert.Equal(((DeviceKind?)DeviceKind.Hub, (DateTimeOffset?)lastHeard), (listed.DeviceKind, listed.SilentSince));
        Assert.Equal(alertId, Assert.Single(await identity.Site(site.SiteId).OpenAlerts(Ct)).AlertId);

        // Delivered through the same fan-out as a Threshold Alert, and nothing throws for an Alert without a Lot.
        await identity.WakeUserAsync(site.OwnerId);
        var sent = Assert.Single(identity.Notifications.For(site.OwnerId));
        Assert.Equal(NotificationKind.Alert, sent.Notification.Kind);
        Assert.Equal(
            new NotificationEntry(alertId, AlertKind.Silent, null, null, null, hubId, null, opened, DeviceKind.Hub, lastHeard),
            Assert.Single(sent.Notification.Entries));

        // The Reminder comes 24 h after the notification and not a minute before. That a Health Alert never
        // reminds more often, whatever the cadence, is shown in ReminderIntervalRuleTests.
        identity.Time.Advance(TimeSpan.FromHours(24) - TimeSpan.FromMinutes(1));
        await identity.WakeUserAsync(site.OwnerId);
        Assert.Single(identity.Notifications.For(site.OwnerId));

        identity.Time.Advance(TimeSpan.FromMinutes(1));
        await identity.WakeUserAsync(site.OwnerId);
        var reminded = identity.Notifications.For(site.OwnerId);
        Assert.Equal(2, reminded.Count);
        Assert.Equal((NotificationKind.Reminder, alertId), (reminded[1].Notification.Kind, Assert.Single(reminded[1].Notification.Entries).AlertId));

        // A day of wakes later it is still one Alert.
        await identity.WakeDeviceAsync(hubId);
        Assert.Single(await identity.AliasesAsync($"device/{hubId}"), alias => alias == Started);
    }

    [Fact]
    public async Task ADeviceThatNeverReportsIsSilentOneWindowAfterItsEnrolment()
    {
        var site = await SiteAsync();
        var hub = await HubAsync(site);
        var hubId = hub.DeviceId.ToString();
        var enrolledAt = Now;
        Assert.True(await identity.HasSilenceReminderAsync(hubId));
        Assert.Equal((enrolledAt, enrolledAt + HubWindow), await ClockAsync(hubId));

        identity.Time.Advance(HubWindow - Second);
        await identity.WakeDeviceAsync(hubId);
        Assert.Empty(await identity.Site(site.SiteId).OpenAlerts(Ct));

        identity.Time.Advance(Second);
        await identity.WakeDeviceAsync(hubId);
        Assert.Equal(AlertIds.Silent(hubId, 1), Assert.Single(await identity.Site(site.SiteId).OpenAlerts(Ct)).AlertId);
    }

    [Fact]
    public async Task APerDeviceWindowReplacesTheDefaultAndClearingItReturnsToTheDefault()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, "Beans", UnknownHub);
        var (other, _) = await NodeAsync(site, "Peas", UnknownHub);
        var nodeId = node.DeviceId.ToString();
        var otherId = other.DeviceId.ToString();
        var grain = identity.Device(nodeId);
        var lastReport = Now;
        var eightHours = TimeSpan.FromHours(8);

        // Outside 1 min to 30 days, or of another Site: refused, and nothing is journaled.
        var journaled = await identity.AliasesAsync($"device/{nodeId}");
        Assert.Equal(DeviceSilenceWindowOutcome.OutOfRange, (await grain.SetSilenceWindow(site.SiteId, TimeSpan.FromSeconds(59), Ct)).Outcome);
        Assert.Equal(DeviceSilenceWindowOutcome.OutOfRange, (await grain.SetSilenceWindow(site.SiteId, TimeSpan.FromDays(30) + Second, Ct)).Outcome);
        Assert.Equal(DeviceSilenceWindowOutcome.NotFound, (await grain.SetSilenceWindow(Guid.CreateVersion7().ToString(), eightHours, Ct)).Outcome);
        Assert.Equal(journaled, await identity.AliasesAsync($"device/{nodeId}"));

        Assert.Equal(new DeviceSilenceWindowResult(DeviceSilenceWindowOutcome.Set, eightHours), await grain.SetSilenceWindow(site.SiteId, eightHours, Ct));
        Assert.Equal(new DeviceSilenceWindowResult(DeviceSilenceWindowOutcome.Unchanged, eightHours), await grain.SetSilenceWindow(site.SiteId, eightHours, Ct));
        Assert.Equal(DeviceSilenceWindowOutcome.Set, (await identity.Device(otherId).SetSilenceWindow(site.SiteId, eightHours, Ct)).Outcome);
        Assert.Equal(["device.silence-window-set"], (await identity.AliasesAsync($"device/{nodeId}")).Skip(journaled.Count));

        var silence = await grain.Silence(Ct);
        Assert.Equal(
            new DeviceSilence(DeviceKind.Node, eightHours, WindowOverridden: true, lastReport, lastReport + eightHours, Paused: false),
            silence);

        // No Alert at 6 h.
        identity.Time.Advance(NodeWindow);
        await identity.WakeDeviceAsync(nodeId);
        await identity.WakeDeviceAsync(otherId);
        Assert.Empty(await identity.Site(site.SiteId).OpenAlerts(Ct));

        // Clearing the override returns to 6 h, which has passed: the next wake opens the Alert.
        Assert.Equal(new DeviceSilenceWindowResult(DeviceSilenceWindowOutcome.Set, NodeWindow), await identity.Device(otherId).SetSilenceWindow(site.SiteId, null, Ct));
        await identity.WakeDeviceAsync(otherId);
        Assert.Equal(AlertIds.Silent(otherId, 1), Assert.Single(await identity.Site(site.SiteId).OpenAlerts(Ct)).AlertId);

        // With the override: not a second before 8 h, and at 8 h.
        identity.Time.Advance(eightHours - NodeWindow - Second);
        await identity.WakeDeviceAsync(nodeId);
        Assert.Single(await identity.Site(site.SiteId).OpenAlerts(Ct));

        identity.Time.Advance(Second);
        await identity.WakeDeviceAsync(nodeId);
        Assert.Equal(
            new[] { AlertIds.Silent(nodeId, 1), AlertIds.Silent(otherId, 1) }.Order(),
            (await identity.Site(site.SiteId).OpenAlerts(Ct)).Select(alert => alert.AlertId).Order());
    }

    [Fact]
    public async Task ASilentNodeThatReportsAgainRecoversAndItsNextSilenceIsAnotherAlert()
    {
        var site = await SiteAsync();
        var (node, lotId) = await NodeAsync(site, "Beans", UnknownHub);
        var nodeId = node.DeviceId.ToString();
        var before = await LotAsync(lotId);
        var first = await SilentAsync(nodeId, NodeWindow);
        Assert.Equal(("unknown", "node"), await LotAsync(lotId));

        // A frame measured now, inside the window: the Alert closes as recovered.
        identity.Time.Advance(TimeSpan.FromMinutes(30));
        await ReportAsync(node, UnknownHub);

        var closed = await identity.Alert(first).Describe(Ct);
        Assert.Equal((AlertLifecycle.Closed, AlertCloseReason.Recovered, (DateTimeOffset?)Now), (closed!.Lifecycle, closed.Reason, closed.ClosedAt));
        Assert.Equal(["alert.opened", "alert.site-notified", "alert.closed", "alert.site-notified"], await identity.AliasesAsync($"alert/{first}"));
        Assert.Empty(await identity.Site(site.SiteId).OpenAlerts(Ct));
        Assert.Empty((await identity.UserStateAsync(site.OwnerId)).TrackedAlerts);

        var state = await identity.DeviceStateAsync(nodeId);
        Assert.Equal((1, (DeviceSilentAlert?)null, (DateTimeOffset?)Now), (state.SilenceEpisode, state.OpenSilentAlert, state.LastRecoveredAt));
        Assert.Empty(state.PendingAlertDeliveries);

        // The Lot is what it was before the silence.
        Assert.Equal(before, await LotAsync(lotId));

        // The next silence is episode 2, with another Alert ID.
        var second = await SilentAsync(nodeId, NodeWindow);
        Assert.Equal(AlertIds.Silent(nodeId, 2), second);
        Assert.NotEqual(first, second);
        Assert.Equal([Started, Ended, Started], (await identity.AliasesAsync($"device/{nodeId}")).Where(alias => alias is Started or Ended));
        Assert.Equal(second, Assert.Single(await identity.Site(site.SiteId).OpenAlerts(Ct)).AlertId);
    }

    [Fact]
    public async Task ASilentHubRecoversWithItsNextHeartbeat()
    {
        var site = await SiteAsync();
        var hub = await HubAsync(site);
        var hubId = hub.DeviceId.ToString();
        await HeartbeatAsync(hub);
        var alertId = await SilentAsync(hubId, HubWindow);

        identity.Time.Advance(TimeSpan.FromMinutes(7));
        await HeartbeatAsync(hub);

        Assert.Equal(AlertLifecycle.Closed, (await identity.Alert(alertId).Describe(Ct))?.Lifecycle);
        Assert.Empty(await identity.Site(site.SiteId).OpenAlerts(Ct));
        Assert.Equal(Now, (await identity.Device(hubId).Silence(Ct))?.LastRecoveredAt);
    }

    [Fact]
    public async Task AnOldBacklogDoesNotCloseTheAlertOfASilentNode()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, "Beans", UnknownHub);
        var nodeId = node.DeviceId.ToString();
        var lastReport = Now;
        var alertId = await SilentAsync(nodeId, NodeWindow);

        // Half an hour into the silence the Node delivers Readings it buffered ten minutes after its last report:
        // accepted and stored, newer than anything accepted before, and still older than the window.
        identity.Time.Advance(TimeSpan.FromMinutes(30));
        var journaled = await identity.AliasesAsync($"device/{nodeId}");
        var result = await IngestAsync(node, node.Wake(lastReport.AddMinutes(10)), UnknownHub);

        Assert.Equal(DeviceIngestStatus.Stored, result.Status);
        Assert.Equal(["device.reported"], (await identity.AliasesAsync($"device/{nodeId}")).Skip(journaled.Count));
        Assert.Equal(lastReport.AddMinutes(10), (await identity.DeviceStateAsync(nodeId)).LastReportedAt);
        Assert.Equal(AlertLifecycle.Open, (await identity.Alert(alertId).Describe(Ct))?.Lifecycle);
        Assert.Equal(alertId, Assert.Single(await identity.Site(site.SiteId).OpenAlerts(Ct)).AlertId);
    }

    [Fact]
    public async Task AnUndeliveredOpenAndAnUndeliveredCloseAreDeliveredByTheNextWake()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, "Beans", UnknownHub);
        var nodeId = node.DeviceId.ToString();
        var alertId = AlertIds.Silent(nodeId, 1);

        // The Alert grain cannot be reached: the episode is journaled, the open stays pending.
        identity.Time.Advance(NodeWindow);
        identity.AlertFaults.FailNext(1);
        await identity.WakeDeviceAsync(nodeId);

        var pending = await identity.DeviceStateAsync(nodeId);
        Assert.Equal((1, alertId), (pending.SilenceEpisode, pending.OpenSilentAlert?.AlertId));
        Assert.Equal(AlertLifecycle.Open, Assert.Single(pending.PendingAlertDeliveries).Change);
        Assert.Empty(await identity.AliasesAsync($"alert/{alertId}"));

        // The next wake delivers it: one episode, one Alert.
        identity.Time.Advance(TimeSpan.FromMinutes(1));
        await identity.WakeDeviceAsync(nodeId);

        Assert.Equal(alertId, Assert.Single(await identity.Site(site.SiteId).OpenAlerts(Ct)).AlertId);
        Assert.Single(await identity.AliasesAsync($"device/{nodeId}"), alias => alias == Started);
        Assert.Empty(await identity.AliasesAsync($"alert/{AlertIds.Silent(nodeId, 2)}"));

        // The same for the close: the frame is acknowledged, the close stays pending and the next wake delivers it.
        identity.Time.Advance(TimeSpan.FromMinutes(1));
        identity.AlertFaults.FailNext(1);
        await ReportAsync(node, UnknownHub);

        var closing = await identity.DeviceStateAsync(nodeId);
        Assert.Null(closing.OpenSilentAlert);
        Assert.Equal(AlertLifecycle.Closed, Assert.Single(closing.PendingAlertDeliveries).Change);
        Assert.Equal(AlertLifecycle.Open, (await identity.Alert(alertId).Describe(Ct))?.Lifecycle);

        await identity.WakeDeviceAsync(nodeId);

        Assert.Equal(AlertLifecycle.Closed, (await identity.Alert(alertId).Describe(Ct))?.Lifecycle);
        Assert.Empty(await identity.Site(site.SiteId).OpenAlerts(Ct));
        Assert.Empty((await identity.DeviceStateAsync(nodeId)).PendingAlertDeliveries);
        Assert.Equal([Started, Delivered, Ended, Delivered], (await identity.AliasesAsync($"device/{nodeId}")).Where(alias => alias is Started or Ended or Delivered));
    }

    [Fact]
    public async Task OnlyTheDeviceThatOpenedASilentAlertClosesItAndARequestForAnotherAlertIsRefused()
    {
        var site = await SiteAsync();
        var hub = await HubAsync(site);
        var hubId = hub.DeviceId.ToString();
        var alertId = await SilentAsync(hubId, HubWindow);
        var alert = identity.Alert(alertId);

        // A client is not the Device grain that opened the Alert.
        Assert.Equal(new AlertResult(AlertOutcome.Refused), await alert.Close(new CloseAlert(AlertCloseReason.Recovered, Now), Ct));
        Assert.Equal(AlertLifecycle.Open, (await alert.Describe(Ct))?.Lifecycle);

        // An open that does not derive the Alert ID, or of a kind no Device opens, journals nothing.
        var other = identity.Alert(AlertIds.Silent(hubId, 2));
        Assert.Equal(AlertOutcome.Refused, (await other.OpenForDevice(new OpenDeviceAlert(AlertKind.Silent, hubId, 3, site.SiteId, null, Now), Ct)).Outcome);
        Assert.Equal(AlertOutcome.Refused, (await other.OpenForDevice(new OpenDeviceAlert(AlertKind.Threshold, hubId, 2, site.SiteId, null, Now), Ct)).Outcome);
        Assert.Null(await other.Describe(Ct));

        // Opening it again journals nothing.
        var again = await alert.OpenForDevice(new OpenDeviceAlert(AlertKind.Silent, hubId, 1, site.SiteId, null, Now), Ct);
        Assert.Equal(new AlertResult(AlertOutcome.Open, Reported: true), again);
        Assert.Equal(["alert.opened", "alert.site-notified"], await identity.AliasesAsync($"alert/{alertId}"));
    }

    [Fact]
    public async Task AServerRestartIsNoSilenceAndTheWindowCountsFromTheStart()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, "Beans", UnknownHub);
        var nodeId = node.DeviceId.ToString();
        var lastReport = Now;

        // The silo is down from T + 5 h and back at T + 7 h, an hour after the window would have ended.
        await identity.RestartSiloAsync(lastReport.AddHours(7));
        Assert.Equal(lastReport.AddHours(7), Now);
        await identity.WakeDeviceAsync(nodeId);

        Assert.Empty(await identity.Site(site.SiteId).OpenAlerts(Ct));
        var restarted = Assert.IsType<DeviceSilenceClockRestarted>((await identity.Store.ReadStreamAsync($"device/{nodeId}", Ct))[^1].Data);
        Assert.Equal(
            (lastReport.AddHours(7), SilenceClockRestartCause.ServerStarted, (string?)null),
            (restarted.From, restarted.Cause, restarted.HubId));
        Assert.Equal((lastReport.AddHours(7), lastReport.AddHours(13)), await ClockAsync(nodeId));

        // Journaled once per start, however often the Device is woken.
        await identity.WakeDeviceAsync(nodeId);
        Assert.Single(await identity.AliasesAsync($"device/{nodeId}"), alias => alias == ClockRestarted);

        // Only at T + 13 h, 6 h after the start.
        identity.Time.SetUtcNow(lastReport.AddHours(13) - Second);
        await identity.WakeDeviceAsync(nodeId);
        Assert.Empty(await identity.Site(site.SiteId).OpenAlerts(Ct));

        identity.Time.Advance(Second);
        await identity.WakeDeviceAsync(nodeId);
        Assert.Equal(AlertIds.Silent(nodeId, 1), Assert.Single(await identity.Site(site.SiteId).OpenAlerts(Ct)).AlertId);
    }

    [Fact]
    public async Task FiveNodesBehindASilentHubOpenOneAlertAndTheirWindowsRestartWhenTheHubRecovers()
    {
        var site = await SiteAsync();
        var hub = await HubAsync(site);
        var hubId = hub.DeviceId.ToString();
        await HeartbeatAsync(hub);

        var nodes = new List<(SimulatedDevice Node, string LotId)>();
        for (var index = 0; index < 5; index++)
        {
            nodes.Add(await NodeAsync(site, $"Lot {index}", hubId));
        }

        await CatchUpLotsAsync();
        var before = new List<(string Status, string? Cause)>();
        foreach (var (_, lotId) in nodes)
        {
            before.Add(await LotAsync(lotId));
        }

        // Everything goes quiet. The Nodes' reminders fire first: each asks the Hub, which processes its own
        // expiry before it answers, so exactly one Alert opens, the Hub's.
        identity.Time.Advance(NodeWindow);
        foreach (var (node, _) in nodes)
        {
            await identity.WakeDeviceAsync(node.DeviceId.ToString());
        }

        var hubAlert = AlertIds.Silent(hubId, 1);
        Assert.Equal(hubAlert, Assert.Single(await identity.Site(site.SiteId).OpenAlerts(Ct)).AlertId);
        foreach (var (node, lotId) in nodes)
        {
            Assert.DoesNotContain(Started, await identity.AliasesAsync($"device/{node.DeviceId}"));
            Assert.Equal(("unknown", "hub"), await LotAsync(lotId));
        }

        // Still one Alert after the Hub's own wake and another round of the Nodes'.
        identity.Time.Advance(TimeSpan.FromMinutes(1));
        await identity.WakeDeviceAsync(hubId);
        foreach (var (node, _) in nodes)
        {
            await identity.WakeDeviceAsync(node.DeviceId.ToString());
        }

        Assert.Equal(hubAlert, Assert.Single(await identity.Site(site.SiteId).OpenAlerts(Ct)).AlertId);

        // The Hub heartbeats again at R: its Alert closes and the Lots behind it are what they were.
        identity.Time.Advance(TimeSpan.FromHours(1));
        await HeartbeatAsync(hub);
        var recoveredAt = Now;

        Assert.Empty(await identity.Site(site.SiteId).OpenAlerts(Ct));
        for (var index = 0; index < nodes.Count; index++)
        {
            Assert.Equal(before[index], await LotAsync(nodes[index].LotId));
        }

        // Each Node's window restarts at R: no Node Alert, though every Node is long past its own 6 h.
        identity.Time.Advance(TimeSpan.FromMinutes(1));
        foreach (var (node, _) in nodes)
        {
            var nodeId = node.DeviceId.ToString();
            await identity.WakeDeviceAsync(nodeId);

            var restarted = Assert.IsType<DeviceSilenceClockRestarted>((await identity.Store.ReadStreamAsync($"device/{nodeId}", Ct))[^1].Data);
            Assert.Equal((recoveredAt, SilenceClockRestartCause.RelayHubRecovered, hubId), (restarted.From, restarted.Cause, restarted.HubId));
            Assert.Equal((recoveredAt, recoveredAt + NodeWindow), await ClockAsync(nodeId));
        }

        Assert.Empty(await identity.Site(site.SiteId).OpenAlerts(Ct));

        // One Node reports again; the Hub keeps heartbeating; the others stay quiet.
        identity.Time.Advance(TimeSpan.FromHours(1));
        await ReportAsync(nodes[0].Node, hubId);
        await HeartbeatAsync(hub);

        identity.Time.SetUtcNow(recoveredAt + NodeWindow - Second);
        await HeartbeatAsync(hub);
        foreach (var (node, _) in nodes)
        {
            await identity.WakeDeviceAsync(node.DeviceId.ToString());
        }

        Assert.Empty(await identity.Site(site.SiteId).OpenAlerts(Ct));

        // At R + 6 h every Node that is still quiet opens its own Alert, and its Lot names the Node as the cause.
        identity.Time.Advance(Second);
        await HeartbeatAsync(hub);
        foreach (var (node, _) in nodes)
        {
            await identity.WakeDeviceAsync(node.DeviceId.ToString());
        }

        Assert.Equal(
            nodes.Skip(1).Select(entry => AlertIds.Silent(entry.Node.DeviceId.ToString(), 1)).Order(),
            (await identity.Site(site.SiteId).OpenAlerts(Ct)).Select(alert => alert.AlertId).Order());
        Assert.Equal(before[0], await LotAsync(nodes[0].LotId));
        foreach (var (_, lotId) in nodes.Skip(1))
        {
            Assert.Equal(("unknown", "node"), await LotAsync(lotId));
        }
    }

    [Fact]
    public async Task ANodeWhoseSilentAlertWasOpenBeforeItsHubWentSilentKeepsItAndItsLotNamesTheNode()
    {
        var site = await SiteAsync();
        var hub = await HubAsync(site);
        var hubId = hub.DeviceId.ToString();
        await HeartbeatAsync(hub);
        var (node, lotId) = await NodeAsync(site, "Beans", hubId);
        var nodeId = node.DeviceId.ToString();

        // The Hub stays alive until the Node's window has ended, then goes quiet too.
        identity.Time.Advance(NodeWindow);
        await HeartbeatAsync(hub);
        await identity.WakeDeviceAsync(nodeId);
        Assert.Equal(AlertIds.Silent(nodeId, 1), Assert.Single(await identity.Site(site.SiteId).OpenAlerts(Ct)).AlertId);

        identity.Time.Advance(HubWindow);
        await identity.WakeDeviceAsync(hubId);
        await identity.WakeDeviceAsync(nodeId);

        Assert.Equal(
            new[] { AlertIds.Silent(nodeId, 1), AlertIds.Silent(hubId, 1) }.Order(),
            (await identity.Site(site.SiteId).OpenAlerts(Ct)).Select(alert => alert.AlertId).Order());
        Assert.Equal(("unknown", "node"), await LotAsync(lotId));
    }

    [Fact]
    public async Task ARelayHubThatCannotBeReachedOpensNothingAndTheNextWakeAsksAgain()
    {
        var site = await SiteAsync();
        var hub = await HubAsync(site);
        var hubId = hub.DeviceId.ToString();
        var (node, _) = await NodeAsync(site, "Beans", hubId);
        var nodeId = node.DeviceId.ToString();

        // The Hub is alive, the Node's window has ended, and the Hub's grain does not answer.
        identity.Time.Advance(NodeWindow);
        await HeartbeatAsync(hub);
        var journaled = await identity.AliasesAsync($"device/{nodeId}");
        identity.DeviceFaults.FailSilence(hubId);

        try
        {
            await identity.WakeDeviceAsync(nodeId);

            Assert.Equal(journaled, await identity.AliasesAsync($"device/{nodeId}"));
            Assert.Empty(await identity.Site(site.SiteId).OpenAlerts(Ct));
        }
        finally
        {
            identity.DeviceFaults.RestoreSilence(hubId);
        }

        // It answers again: the Hub is not silent, so the Node's own Alert opens.
        await identity.WakeDeviceAsync(nodeId);
        Assert.Equal(AlertIds.Silent(nodeId, 1), Assert.Single(await identity.Site(site.SiteId).OpenAlerts(Ct)).AlertId);
    }

    [Fact]
    public async Task APausedDeviceOpensNoAlertAndItsWindowRestartsAtTheResume()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, "Beans", UnknownHub);
        var nodeId = node.DeviceId.ToString();
        var stream = $"device/{nodeId}";
        var lastReport = Now;

        // Pause has no command before Epic 8: its events are journaled here, and a new silo reads them. The clock
        // of every new silo starts where this one stands, so that no Server start moves the Node's window.
        await AppendAsync(stream, new DevicePaused(DevicePauseSource.Device, null, lastReport.AddHours(1)));
        await identity.RestartSiloAsync(lastReport);

        // The window ends while the Node is paused: no Alert, however often it is woken.
        identity.Time.SetUtcNow(lastReport.AddHours(8));
        await identity.WakeDeviceAsync(nodeId);
        Assert.True((await identity.Device(nodeId).Silence(Ct))!.Paused);
        Assert.DoesNotContain(Started, await identity.AliasesAsync(stream));
        Assert.Empty(await identity.Site(site.SiteId).OpenAlerts(Ct));

        // Resumed at P = T + 9 h: the window restarts there.
        var resumedAt = lastReport.AddHours(9);
        await AppendAsync(stream, new DeviceResumed(DevicePauseSource.Device, resumedAt));
        await identity.RestartSiloAsync(lastReport);
        Assert.Equal((resumedAt, resumedAt + NodeWindow), await ClockAsync(nodeId));

        identity.Time.SetUtcNow(resumedAt + NodeWindow - Second);
        await identity.WakeDeviceAsync(nodeId);
        Assert.Empty(await identity.Site(site.SiteId).OpenAlerts(Ct));

        identity.Time.Advance(Second);
        await identity.WakeDeviceAsync(nodeId);
        Assert.Equal(AlertIds.Silent(nodeId, 1), Assert.Single(await identity.Site(site.SiteId).OpenAlerts(Ct)).AlertId);
        Assert.DoesNotContain(ClockRestarted, await identity.AliasesAsync(stream));
    }

    // Story 7.2: what the Device grain states once as its Alert opens is what all three read models show.
    [Fact]
    public async Task ASilentNodeAndASilentHubShowInTheAlertsTheLotsAndTheDevicesReadModelsWithWhenTheyWereLastHeard()
    {
        var site = await SiteAsync();
        var hub = await HubAsync(site);
        var hubId = hub.DeviceId.ToString();
        await HeartbeatAsync(hub);
        var (beansNode, beans) = await NodeAsync(site, "Beans", UnknownHub);
        var (lettuceNode, lettuce) = await NodeAsync(site, "Lettuce", hubId);
        var beansId = beansNode.DeviceId.ToString();
        var lettuceId = lettuceNode.DeviceId.ToString();
        var lastHeard = Now;
        var lettuceBefore = await LotAsync(lettuce);

        // The Hub goes quiet first; six hours after the last frames the Node of Beans does. The Node of Lettuce
        // is behind the silent Hub and opens nothing.
        var hubAlert = await SilentAsync(hubId, HubWindow);
        var hubOpened = Now;
        identity.Time.SetUtcNow(lastHeard + NodeWindow);
        await identity.WakeDeviceAsync(beansId);
        await identity.WakeDeviceAsync(lettuceId);
        var beansAlert = AlertIds.Silent(beansId, 1);
        var beansOpened = Now;
        Assert.Equal(2, (await identity.Site(site.SiteId).OpenAlerts(Ct)).Count);
        Assert.Equal(
            ((DeviceKind?)DeviceKind.Node, (DateTimeOffset?)lastHeard),
            ((await identity.Alert(beansAlert).Describe(Ct))!.DeviceKind, (await identity.Alert(beansAlert).Describe(Ct))!.SilentSince));

        await CatchUpAsync();

        // The Alerts list: no Threshold Alert, two Health Alerts newest first, both counted as open.
        var alerts = identity.SiloServices.GetRequiredService<AlertsReadModel>();
        var closedSince = Now - AlertsReadModel.ClosedRetention;
        Assert.Empty((await alerts.ListAlertsAsync(site.SiteId, closedSince, null, 50, Ct)).Alerts);
        Assert.Equal(2, await alerts.CountOpenAsync(site.SiteId, Ct));
        Assert.Equal(
            [
                new HealthAlertView(beansAlert, "silent", beansId, "node", beans, "Beans", lastHeard, beansOpened, null, null),
                new HealthAlertView(hubAlert, "silent", hubId, "hub", null, null, lastHeard, hubOpened, null, null),
            ],
            await alerts.ListHealthAlertsAsync(site.SiteId, closedSince, Ct));

        // The Lots: Beans is unknown by its Node, Lettuce by the Hub that relays it, each since that Device was
        // last heard; the Hub is listed for the Site.
        var lots = identity.SiloServices.GetRequiredService<LotsReadModel>();
        var listed = (await lots.ListLotsAsync(site.SiteId, Ct)).ToDictionary(lot => lot.LotId, StringComparer.Ordinal);
        Assert.Equal(
            ("unknown", "node", (string?)null, (DateTimeOffset?)lastHeard),
            (listed[beans].Status, listed[beans].UnknownCause, listed[beans].SilentHubId, listed[beans].SilentSince));
        Assert.Equal(
            ("unknown", "hub", (string?)hubId, (DateTimeOffset?)lastHeard),
            (listed[lettuce].Status, listed[lettuce].UnknownCause, listed[lettuce].SilentHubId, listed[lettuce].SilentSince));
        var found = await lots.FindLotAsync(site.SiteId, lettuce, Ct);
        Assert.Equal(((string?)hubId, (DateTimeOffset?)lastHeard), (found!.SilentHubId, found.SilentSince));
        Assert.Equal([new SilentHubView(hubId, lastHeard)], await lots.ListSilentHubsAsync(site.SiteId, Ct));

        // The Devices: the Hub and the Node of Beans are silent, the suppressed Node is not; nobody has a window
        // of its own yet.
        var devices = identity.SiloServices.GetRequiredService<DevicesReadModel>();
        var rows = (await devices.ListDevicesAsync(site.SiteId, Ct)).ToDictionary(device => device.DeviceId, StringComparer.Ordinal);
        Assert.Equal(
            ((DateTimeOffset?)lastHeard, (DateTimeOffset?)lastHeard, (DateTimeOffset?)null),
            (rows[hubId].SilentSince, rows[beansId].SilentSince, rows[lettuceId].SilentSince));
        Assert.All(rows.Values, row => Assert.Null(row.SilenceWindowOverrideSeconds));

        // The notification text, written from what the Alert carries and the real read models: the Node by its
        // Lot with the time of its last Reading (UTC for a User without a zone), the Hub by its short ID.
        var push = identity.SiloServices.GetRequiredService<PushContentBuilder>();
        var open = (await identity.Site(site.SiteId).OpenAlerts(Ct)).ToDictionary(alert => alert.AlertId);
        NotificationEntry Entry(Guid alertId) => new(
            alertId, AlertKind.Silent, null, open[alertId].LotId, null, open[alertId].DeviceId, null, open[alertId].OpenedAt, open[alertId].DeviceKind, open[alertId].SilentSince);
        var ofNode = await push.BuildAsync(new Notification(site.OwnerId, site.SiteId, NotificationKind.Alert, Now, null, [Entry(beansAlert)]), Now);
        var ofHub = await push.BuildAsync(new Notification(site.OwnerId, site.SiteId, NotificationKind.Alert, Now, null, [Entry(hubAlert)]), Now);
        Assert.Equal(
            ("Node on Lot 'Beans' silent for 6 h", $"No Reading since {lastHeard.UtcDateTime:HH:mm}. Check power or range."),
            (ofNode!.Title, ofNode.Body));
        Assert.Equal(($"Hub {hubId[..4].ToUpperInvariant()} silent for 6 h", "Lots behind it can't be read."), (ofHub!.Title, ofHub.Body));

        // A window of its own shows, and clearing it shows the default again.
        Assert.Equal(DeviceSilenceWindowOutcome.Set, (await identity.Device(beansId).SetSilenceWindow(site.SiteId, TimeSpan.FromHours(8), Ct)).Outcome);
        await CatchUpAsync();
        Assert.Equal(28_800, (await devices.ListDevicesAsync(site.SiteId, Ct)).Single(device => device.DeviceId == beansId).SilenceWindowOverrideSeconds);
        Assert.Equal(DeviceSilenceWindowOutcome.Set, (await identity.Device(beansId).SetSilenceWindow(site.SiteId, null, Ct)).Outcome);
        await CatchUpAsync();
        Assert.Null((await devices.ListDevicesAsync(site.SiteId, Ct)).Single(device => device.DeviceId == beansId).SilenceWindowOverrideSeconds);

        // The Hub recovers: it is no longer listed as silent anywhere, Lettuce is readable again, and its Alert
        // stays in the list as closed. Beans keeps its own.
        await HeartbeatAsync(hub);
        await CatchUpAsync();

        Assert.Empty(await lots.ListSilentHubsAsync(site.SiteId, Ct));
        var afterwards = (await lots.ListLotsAsync(site.SiteId, Ct)).ToDictionary(lot => lot.LotId, StringComparer.Ordinal);
        Assert.Equal(
            (lettuceBefore.Status, lettuceBefore.Cause, (string?)null, (DateTimeOffset?)null),
            (afterwards[lettuce].Status, afterwards[lettuce].UnknownCause, afterwards[lettuce].SilentHubId, afterwards[lettuce].SilentSince));
        Assert.Equal(("unknown", (DateTimeOffset?)lastHeard), (afterwards[beans].Status, afterwards[beans].SilentSince));
        Assert.Null((await devices.ListDevicesAsync(site.SiteId, Ct)).Single(device => device.DeviceId == hubId).SilentSince);
        Assert.Equal(1, await alerts.CountOpenAsync(site.SiteId, Ct));
        Assert.Equal(
            [
                new HealthAlertView(beansAlert, "silent", beansId, "node", beans, "Beans", lastHeard, beansOpened, null, null),
                new HealthAlertView(hubAlert, "silent", hubId, "hub", null, null, lastHeard, hubOpened, Now, "recovered"),
            ],
            await alerts.ListHealthAlertsAsync(site.SiteId, Now - AlertsReadModel.ClosedRetention, Ct));
    }

    // A Node that never reported is silent since its enrolment (Story 7.2).
    [Fact]
    public async Task ADeviceThatNeverReportedIsSilentSinceItsEnrolment()
    {
        var site = await SiteAsync();
        var hub = await HubAsync(site);
        var hubId = hub.DeviceId.ToString();
        var enrolledAt = Now;

        var alertId = await SilentAsync(hubId, HubWindow);

        Assert.Equal((DateTimeOffset?)enrolledAt, (await identity.Alert(alertId).Describe(Ct))!.SilentSince);
    }

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
    private async Task<(SimulatedDevice Node, string LotId)> NodeAsync((string SiteId, string OwnerId) site, string lotName, string hubId)
    {
        var lotId = Guid.CreateVersion7().ToString();
        Assert.True(await identity.Store.AppendAsync($"lot/{lotId}", 0, [new LotCreated(site.SiteId, lotName)], Ct));

        var node = SimulatedDevice.Create(ProtocolKind.Node);
        var enrol = new EnrolDevice(site.SiteId, DeviceKind.Node, identity.Vault.Wrap(node.DeviceId, node.Keys.DeviceKey), site.OwnerId, Guid.NewGuid().ToString("N"), lotId);
        Assert.Equal(DeviceEnrolmentOutcome.Enrolled, (await identity.Device(node.DeviceId.ToString()).Enrol(enrol, Ct)).Outcome);

        Assert.True((await ReportAsync(node, hubId)).SpecificationsUnknown);
        Assert.False((await ReportAsync(node, hubId)).SpecificationsUnknown);

        return (node, lotId);
    }

    // One wake of the Node, measured now, stored and acknowledged.
    private async Task<Downlink> ReportAsync(SimulatedDevice node, string hubId)
    {
        var result = await IngestAsync(node, node.Wake(Now), hubId);

        Assert.Equal(DeviceIngestStatus.Stored, result.Status);
        return node.OpenDownlink(SealedEnvelope.Parser.ParseFrom(result.Downlink));
    }

    private Task<DeviceIngestResult> IngestAsync(SimulatedDevice node, NodeFrame frame, string hubId)
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

    // Lets the Device's window pass and wakes it: its next silent Alert is open.
    private async Task<Guid> SilentAsync(string deviceId, TimeSpan window)
    {
        var episode = (await identity.DeviceStateAsync(deviceId)).SilenceEpisode + 1;
        identity.Time.Advance(window);
        await identity.WakeDeviceAsync(deviceId);

        var alertId = AlertIds.Silent(deviceId, episode);
        Assert.Equal(AlertLifecycle.Open, (await identity.Alert(alertId).Describe(Ct))?.Lifecycle);
        return alertId;
    }

    // Where the Device's window counts from and when it ends, as its journal says.
    private async Task<(DateTimeOffset? From, DateTimeOffset? DueAt)> ClockAsync(string deviceId)
    {
        var state = await identity.DeviceStateAsync(deviceId);
        return (state.SilenceFrom, state.SilenceDueAt);
    }

    // Journals an event on a Device stream behind its grain, which reads it when a new silo activates it.
    private async Task AppendAsync(string stream, object @event)
    {
        var version = (await identity.Store.ReadStreamAsync(stream, Ct)).Count;
        Assert.True(await identity.Store.AppendAsync(stream, version, [@event], Ct));
    }

    // Moves the clock on to the next hh:mm UTC, today or tomorrow: the clock only moves forward.
    private void GoTo(int hour, int minute)
    {
        var target = new DateTimeOffset(Now.UtcDateTime.Date, TimeSpan.Zero).AddHours(hour).AddMinutes(minute);
        identity.Time.SetUtcNow(target > Now ? target : target.AddDays(1));
    }

    private async Task<long> PositionAsync(string streamId, string alias) =>
        await identity.Database.ScalarAsync<long>(
            "SELECT MIN(position) FROM journal_events WHERE stream_id = @stream_id AND type_alias = @alias",
            ("stream_id", streamId),
            ("alias", alias));

    private Task CatchUpLotsAsync() => identity.SiloServices.GetProjectionRunner<LotsProjector>().CatchUpAsync(Ct);

    // The three read models that show silence (Story 7.2), each up to the end of the journal.
    private async Task CatchUpAsync()
    {
        await identity.SiloServices.GetProjectionRunner<AlertsProjector>().CatchUpAsync(Ct);
        await identity.SiloServices.GetProjectionRunner<DevicesProjector>().CatchUpAsync(Ct);
        await CatchUpLotsAsync();
    }

    // The Lot's status and its cause when unknown, once the lots projection has caught up.
    private async Task<(string Status, string? Cause)> LotAsync(string lotId)
    {
        await CatchUpLotsAsync();

        await using var command = identity.Database.DataSource.CreateCommand("SELECT status, unknown_cause FROM lots WHERE lot_id = @lot_id");
        command.Parameters.AddWithValue("lot_id", lotId);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        Assert.True(await reader.ReadAsync(Ct));

        return (reader.GetString(0), await reader.IsDBNullAsync(1, Ct) ? null : reader.GetString(1));
    }
}
