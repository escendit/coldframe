using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Lots;
using Coldframe.Contracts.Sites;
using Coldframe.DeviceSimulator;
using Coldframe.Protocol.Device.V1;
using Coldframe.Server.Alerts;
using Coldframe.Server.Devices;
using Coldframe.Server.IntegrationTests.Devices;
using Coldframe.Server.Journal;
using Coldframe.Server.Lots;
using ProtocolKind = Coldframe.Protocol.Setup.V1.DeviceKind;

namespace Coldframe.Server.IntegrationTests.Identity;

/// <summary>
/// Site Pause on a TestCluster with a fake clock (Story 8.1; AD-6, AD-8): the Site grain owns the Site's Pause and
/// its end date, and delivers it to every Device on its roster from persisted pending state until each has
/// acknowledged: after a Device that could not be reached, after a journal write that failed, and after a
/// restart across the end date. A Device enrolled meanwhile reads it in the join reply. The Site's reminder is
/// fired by hand (<see cref="IdentityCluster.WakeSiteAsync"/>).
/// </summary>
[Collection(IngestSuites.Name)]
public sealed class SitePauseGrainTests(IdentityCluster identity) : IClassFixture<IdentityCluster>
{
    // A relay Hub no Site enrolled: it suppresses nothing.
    private const string UnknownHub = "92064422c012f481";

    private const string SitePaused = "site.paused";

    private const string SiteResumed = "site.resumed";

    private const string PauseDelivered = "site.pause-delivered";

    private const string DevicePaused = "device.paused";

    private const string DeviceResumed = "device.resumed";

    private static readonly TimeSpan NodeWindow = TimeSpan.FromHours(6);

    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTimeOffset Now => identity.Time.GetUtcNow();

    [Fact]
    public async Task ASitePauseReachesEveryDeviceOnceAndItsResumeLiftsItFromEveryDevice()
    {
        var site = await SiteAsync();
        var hub = await HubAsync(site);
        var (beans, beansLot) = await NodeAsync(site, "Beans");
        var (lettuce, lettuceLot) = await NodeAsync(site, "Lettuce");
        string[] devices = [hub.DeviceId.ToString(), beans.DeviceId.ToString(), lettuce.DeviceId.ToString()];
        var grain = identity.Site(site.SiteId);
        var until = Now.AddDays(140);
        var journaled = await identity.AliasesAsync($"site/{site.SiteId}");

        // The Site answers once it has journaled; delivery goes on from its pending state.
        var paused = await grain.SetPause(until, Ct);

        Assert.Equal(new SitePauseResult(SitePauseOutcome.Changed, new SitePause(true, until, 1)), paused);
        var delivered = await grain.DeliverPause(Ct);
        Assert.Equal(new SitePause(true, until, 1), delivered.Pause);
        Assert.Empty(delivered.PendingDevices);

        foreach (var deviceId in devices)
        {
            var state = await identity.DeviceStateAsync(deviceId);
            Assert.Equal(new Dictionary<DevicePauseSource, DateTimeOffset?> { [DevicePauseSource.Site] = until }, state.PausedBy);
            Assert.Single(await identity.AliasesAsync($"device/{deviceId}"), alias => alias == DevicePaused);
        }

        // The Site's stream also takes the closes its Devices' Alerts report while they are being told: the Site
        // answers them in between, because its delivery is no turn that other calls wait behind.
        var since = (await identity.AliasesAsync($"site/{site.SiteId}")).Skip(journaled.Count).ToList();
        Assert.Equal([SitePaused, PauseDelivered, PauseDelivered, PauseDelivered], since.Where(alias => alias != "site.alert-closed"));
        Assert.Equal(2, since.Count(alias => alias == "site.alert-closed"));
        Assert.Equal(("paused", "paused"), (await LotStatusAsync(beansLot), await LotStatusAsync(lettuceLot)));

        // Every Alert of the Site closed as paused: the uncalibrated Alert each Node on a Lot had.
        Assert.Empty(await grain.OpenAlerts(Ct));

        // The same Pause again, and another wake: nothing more is journaled, on the Site or on any Device.
        var streams = await AliasesAsync(site.SiteId, devices);
        Assert.Equal(new SitePauseResult(SitePauseOutcome.Unchanged, new SitePause(true, until, 1)), await grain.SetPause(until, Ct));
        await identity.WakeSiteAsync(site.SiteId);
        Assert.Equal(streams, await AliasesAsync(site.SiteId, devices));

        // Resumed: the next revision, delivered the same way.
        identity.Time.Advance(TimeSpan.FromDays(30));
        Assert.Equal(new SitePauseResult(SitePauseOutcome.Changed, new SitePause(false, null, 2)), await grain.LiftPause(Ct));
        Assert.Empty((await grain.DeliverPause(Ct)).PendingDevices);

        foreach (var deviceId in devices)
        {
            var state = await identity.DeviceStateAsync(deviceId);
            Assert.Equal((false, (DateTimeOffset?)Now), (state.IsPaused, state.SilenceFrom));
            Assert.Single(await identity.AliasesAsync($"device/{deviceId}"), alias => alias == DeviceResumed);
        }

        Assert.Equal(("needsCalibration", "needsCalibration"), (await LotStatusAsync(beansLot), await LotStatusAsync(lettuceLot)));

        // Resuming a Site that is not paused journals nothing.
        streams = await AliasesAsync(site.SiteId, devices);
        Assert.Equal(new SitePauseResult(SitePauseOutcome.Unchanged, new SitePause(false, null, 2)), await grain.LiftPause(Ct));
        await identity.WakeSiteAsync(site.SiteId);
        Assert.Equal(streams, await AliasesAsync(site.SiteId, devices));
    }

    [Fact]
    public async Task ADeviceThatCouldNotBeReachedStaysPendingAndIsPausedByTheNextWake()
    {
        var site = await SiteAsync();
        var (beans, _) = await NodeAsync(site, "Beans");
        var (lettuce, _) = await NodeAsync(site, "Lettuce");
        var reached = beans.DeviceId.ToString();
        var unreachable = lettuce.DeviceId.ToString();
        var grain = identity.Site(site.SiteId);
        identity.DeviceFaults.FailSitePause(unreachable);

        try
        {
            Assert.Equal(SitePauseOutcome.Changed, (await grain.SetPause(null, Ct)).Outcome);

            // One Device answered, the other did not: it stays pending, also after another try.
            Assert.Equal([unreachable], (await grain.DeliverPause(Ct)).PendingDevices);
            await identity.WakeSiteAsync(site.SiteId);
            Assert.Equal([unreachable], (await grain.GetPause(Ct)).PendingDevices);
            Assert.True((await identity.DeviceStateAsync(reached)).IsPaused);
            Assert.False((await identity.DeviceStateAsync(unreachable)).IsPaused);
        }
        finally
        {
            identity.DeviceFaults.RestoreSitePause(unreachable);
        }

        // The next wake pauses it too, and the Device that had acknowledged journals nothing more.
        var acknowledged = await identity.AliasesAsync($"device/{reached}");
        await identity.WakeSiteAsync(site.SiteId);

        Assert.Empty((await grain.GetPause(Ct)).PendingDevices);
        Assert.True((await identity.DeviceStateAsync(unreachable)).IsPaused);
        Assert.Single(await identity.AliasesAsync($"device/{unreachable}"), alias => alias == DevicePaused);
        Assert.Equal(acknowledged, await identity.AliasesAsync($"device/{reached}"));
        Assert.Equal(2, (await identity.AliasesAsync($"site/{site.SiteId}")).Count(alias => alias == PauseDelivered));
    }

    [Fact]
    public async Task AJournalWriteThatFailsOnTheSiteOrOnADeviceStillPausesEveryDeviceExactlyOnce()
    {
        var site = await SiteAsync();
        var (beans, _) = await NodeAsync(site, "Beans");
        var (lettuce, _) = await NodeAsync(site, "Lettuce");
        string[] devices = [beans.DeviceId.ToString(), lettuce.DeviceId.ToString()];
        var grain = identity.Site(site.SiteId);

        // The Site's next write is lost once.
        await using (await identity.FailNextJournalWriteAsync($"site/{site.SiteId}"))
        {
            Assert.Equal(SitePauseOutcome.Changed, (await grain.SetPause(null, Ct)).Outcome);
            Assert.Empty((await grain.DeliverPause(Ct)).PendingDevices);
        }

        // And then one Device's.
        await using (await identity.FailNextJournalWriteAsync($"device/{devices[0]}"))
        {
            Assert.Equal(SitePauseOutcome.Changed, (await grain.LiftPause(Ct)).Outcome);
            Assert.Empty((await grain.DeliverPause(Ct)).PendingDevices);
        }

        var stream = await identity.AliasesAsync($"site/{site.SiteId}");
        Assert.Equal((1, 1, 4), (stream.Count(alias => alias == SitePaused), stream.Count(alias => alias == SiteResumed), stream.Count(alias => alias == PauseDelivered)));

        foreach (var deviceId in devices)
        {
            var aliases = await identity.AliasesAsync($"device/{deviceId}");
            Assert.Equal((1, 1), (aliases.Count(alias => alias == DevicePaused), aliases.Count(alias => alias == DeviceResumed)));
            Assert.False((await identity.DeviceStateAsync(deviceId)).IsPaused);
        }
    }

    [Fact]
    public async Task ASitePauseWhoseEndPassedWhileTheServerWasDownLiftsOnTheSitesWakeAndNoDeviceIsSilentBeforeAFullWindow()
    {
        var site = await SiteAsync();
        var hub = await HubAsync(site);
        var (node, lotId) = await NodeAsync(site, "Beans");
        var nodeId = node.DeviceId.ToString();
        string[] devices = [hub.DeviceId.ToString(), nodeId];
        var until = Now.AddDays(140);
        Assert.Equal(SitePauseOutcome.Changed, (await identity.Site(site.SiteId).SetPause(until, Ct)).Outcome);
        Assert.Empty((await identity.Site(site.SiteId).DeliverPause(Ct)).PendingDevices);
        Assert.True(await identity.HasSitePauseReminderAsync(site.SiteId));

        // The Server is down across the end date. A Device never lifts its site source by itself.
        var restartedAt = until.AddDays(2);
        await identity.RestartSiloAsync(restartedAt);
        await identity.WakeDeviceAsync(nodeId);
        Assert.True((await identity.DeviceStateAsync(nodeId)).IsPaused);

        // The Site's wake finds its due-at overdue, ends the Pause and delivers the resume.
        await identity.WakeSiteAsync(site.SiteId);

        var status = await identity.Site(site.SiteId).GetPause(Ct);
        Assert.Equal((new SitePause(false, null, 2), 0), (status.Pause, status.PendingDevices.Count));
        Assert.Single(await identity.AliasesAsync($"site/{site.SiteId}"), alias => alias == SiteResumed);

        foreach (var deviceId in devices)
        {
            var state = await identity.DeviceStateAsync(deviceId);
            Assert.Equal((false, (DateTimeOffset?)restartedAt), (state.IsPaused, state.SilenceFrom));
        }

        Assert.Equal("needsCalibration", await LotStatusAsync(lotId));

        // The Node was quiet for 142 days: it is silent only a full Silence Window after the resume.
        identity.Time.Advance(NodeWindow - Second);
        await identity.WakeDeviceAsync(nodeId);
        Assert.DoesNotContain(await identity.Site(site.SiteId).OpenAlerts(Ct), alert => alert.DeviceId == nodeId && alert.Kind == AlertKind.Silent);

        identity.Time.Advance(Second);
        await identity.WakeDeviceAsync(nodeId);
        Assert.Contains(await identity.Site(site.SiteId).OpenAlerts(Ct), alert => alert.AlertId == AlertIds.Silent(nodeId, 1));

        // Nothing is left to wake the Site for: its reminder removes itself.
        await identity.WakeSiteAsync(site.SiteId);
        Assert.False(await identity.HasSitePauseReminderAsync(site.SiteId));
    }

    [Fact]
    public async Task ADeviceEnrolledIntoAPausedSiteStartsPausedAndNoAlertOpensWithItsFirstFrames()
    {
        var site = await SiteAsync();
        var grain = identity.Site(site.SiteId);
        var until = Now.AddDays(140);
        Assert.Equal(SitePauseOutcome.Changed, (await grain.SetPause(until, Ct)).Outcome);

        // The join reply carries the Site's Pause.
        var joining = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8));
        var reply = await grain.RegisterDevice(joining, DeviceKind.Hub, $"{site.OwnerId}:join", Ct);
        Assert.Equal(new SitePause(true, until, 1), reply.Pause);

        // A Node enrolled now starts with the site source and the Site's end, in the write of its enrolment.
        var (node, lotId) = await NodeAsync(site, "Beans");
        var nodeId = node.DeviceId.ToString();

        Assert.Equal(["device.enrolled", "device.assigned", DevicePaused], (await identity.AliasesAsync($"device/{nodeId}")).Take(3));
        var state = await identity.DeviceStateAsync(nodeId);
        Assert.Equal(new Dictionary<DevicePauseSource, DateTimeOffset?> { [DevicePauseSource.Site] = until }, state.PausedBy);
        Assert.Null(state.LastReportedAt);

        // Its first frames declared its Sensors and were discarded: no Reading, no Alert, a paused Lot.
        Assert.Equal(4, state.Sensors.Count);
        Assert.Equal(0L, await identity.Database.ScalarAsync<long>("SELECT COUNT(*) FROM readings WHERE device_id = @id", ("id", nodeId)));
        Assert.Empty(await grain.OpenAlerts(Ct));
        Assert.Equal("paused", await LotStatusAsync(lotId));

        // The Site resumes: the Node is live, and its soil Sensor asks for its Calibration.
        Assert.Equal(SitePauseOutcome.Changed, (await grain.LiftPause(Ct)).Outcome);
        Assert.Empty((await grain.DeliverPause(Ct)).PendingDevices);

        Assert.False((await identity.DeviceStateAsync(nodeId)).IsPaused);
        Assert.Equal(AlertKind.Uncalibrated, Assert.Single(await grain.OpenAlerts(Ct)).Kind);
        Assert.Equal("needsCalibration", await LotStatusAsync(lotId));
    }

    [Fact]
    public async Task PausedResumedAndPausedAgainWhileADeviceWasOutOfReachDeliversOnlyTheLatestState()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, "Beans");
        var nodeId = node.DeviceId.ToString();
        var grain = identity.Site(site.SiteId);
        var until = Now.AddDays(30);
        identity.DeviceFaults.FailSitePause(nodeId);

        try
        {
            Assert.Equal(1, (await grain.SetPause(null, Ct)).Pause.Revision);
            Assert.Equal(2, (await grain.LiftPause(Ct)).Pause.Revision);
            Assert.Equal(3, (await grain.SetPause(until, Ct)).Pause.Revision);
            Assert.Equal([nodeId], (await grain.DeliverPause(Ct)).PendingDevices);
        }
        finally
        {
            identity.DeviceFaults.RestoreSitePause(nodeId);
        }

        await identity.WakeSiteAsync(site.SiteId);

        // One event on the Device: the Pause in force. The states in between never reached it.
        var events = (await identity.Store.ReadStreamAsync($"device/{nodeId}", Ct)).Select(row => row.Data).ToList();
        Assert.Equal(new DevicePaused(DevicePauseSource.Site, until, Now, 3), Assert.Single(events.OfType<DevicePaused>()));
        Assert.Empty(events.OfType<DeviceResumed>());
        Assert.Empty((await grain.GetPause(Ct)).PendingDevices);
        Assert.Single(await identity.AliasesAsync($"site/{site.SiteId}"), alias => alias == PauseDelivered);
    }

    // Nothing here wakes the Site or asks it to deliver: the reminder it keeps for a pending delivery, and the
    // catch-up of its activation after a restart, are all there is.
    [Fact]
    public async Task APendingDeliveryKeepsTheReminderAndIsFinishedByTheSitesActivationAfterARestart()
    {
        var site = await SiteAsync();
        var (node, _) = await NodeAsync(site, "Beans");
        var nodeId = node.DeviceId.ToString();
        identity.DeviceFaults.FailSitePause(nodeId);

        // Without an end date: the reminder is kept for the pending delivery alone.
        Assert.Equal(SitePauseOutcome.Changed, (await identity.Site(site.SiteId).SetPause(null, Ct)).Outcome);
        Assert.Equal([nodeId], (await identity.Site(site.SiteId).GetPause(Ct)).PendingDevices);
        Assert.True(await identity.HasSitePauseReminderAsync(site.SiteId));
        Assert.False((await identity.DeviceStateAsync(nodeId)).IsPaused);

        // A new silo, whose call filters fail nothing: the Device is in reach again from the restart on and not a
        // moment before, so no tick of the old silo's timer can have delivered. The Site's activation goes on from
        // its journaled pending state.
        await identity.RestartSiloAsync(Now);
        Assert.False((await identity.DeviceStateAsync(nodeId)).IsPaused);
        var deadline = TimeProvider.System.GetUtcNow().AddSeconds(30);

        while ((await identity.Site(site.SiteId).GetPause(Ct)).PendingDevices.Count > 0)
        {
            Assert.True(TimeProvider.System.GetUtcNow() < deadline, "The Site did not finish its pending delivery after the restart.");
            await Task.Delay(TimeSpan.FromMilliseconds(100), Ct);
        }

        Assert.Equal(new Dictionary<DevicePauseSource, DateTimeOffset?> { [DevicePauseSource.Site] = null }, (await identity.DeviceStateAsync(nodeId)).PausedBy);
        Assert.Single(await identity.AliasesAsync($"device/{nodeId}"), alias => alias == DevicePaused);
    }

    [Fact]
    public async Task ASiteThatIsNotActiveAndAnEndThatIsNotAheadAreRefusedAndJournalNothing()
    {
        var uncreated = Guid.CreateVersion7().ToString();
        Assert.Equal(SitePauseOutcome.NotFound, (await identity.Site(uncreated).SetPause(null, Ct)).Outcome);
        Assert.Equal(SitePauseOutcome.NotFound, (await identity.Site(uncreated).LiftPause(Ct)).Outcome);
        Assert.Empty(await identity.AliasesAsync($"site/{uncreated}"));

        var site = await SiteAsync();
        var journaled = await identity.AliasesAsync($"site/{site.SiteId}");
        Assert.Equal(SitePauseOutcome.InvalidEnd, (await identity.Site(site.SiteId).SetPause(Now, Ct)).Outcome);
        Assert.Equal(SitePauseOutcome.InvalidEnd, (await identity.Site(site.SiteId).SetPause(Now.AddDays(-1), Ct)).Outcome);

        Assert.Equal(journaled, await identity.AliasesAsync($"site/{site.SiteId}"));
        Assert.Equal(new SitePauseStatus(SitePause.NotPaused, []), await identity.Site(site.SiteId).GetPause(Ct), SitePauseStatusComparer.Instance);
        Assert.False(await identity.HasSitePauseReminderAsync(site.SiteId));
    }

    // The aliases of the Site's stream and of each Device's, in one list.
    private async Task<List<string>> AliasesAsync(string siteId, IEnumerable<string> devices)
    {
        var aliases = await identity.AliasesAsync($"site/{siteId}");

        foreach (var deviceId in devices)
        {
            aliases.Add(deviceId);
            aliases.AddRange(await identity.AliasesAsync($"device/{deviceId}"));
        }

        return aliases;
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

    // A Node enrolled on a new Lot of the Site that sent the two frames that declare its Sensors.
    private async Task<(SimulatedDevice Node, string LotId)> NodeAsync((string SiteId, string OwnerId) site, string lotName)
    {
        var lotId = Guid.CreateVersion7().ToString();
        Assert.True(await identity.Store.AppendAsync($"lot/{lotId}", 0, [new LotCreated(site.SiteId, lotName)], Ct));

        var node = SimulatedDevice.Create(ProtocolKind.Node);
        var enrol = new EnrolDevice(site.SiteId, DeviceKind.Node, identity.Vault.Wrap(node.DeviceId, node.Keys.DeviceKey), site.OwnerId, Guid.NewGuid().ToString("N"), lotId);
        Assert.Equal(DeviceEnrolmentOutcome.Enrolled, (await identity.Device(node.DeviceId.ToString()).Enrol(enrol, Ct)).Outcome);

        Assert.True((await ReportAsync(node)).SpecificationsUnknown);
        Assert.False((await ReportAsync(node)).SpecificationsUnknown);

        return (node, lotId);
    }

    // One wake of the Node, measured now, acknowledged.
    private async Task<Downlink> ReportAsync(SimulatedDevice node)
    {
        var envelope = node.SealFrame(node.Wake(Now));
        var result = await identity.Device(node.DeviceId.ToString()).Ingest(
            new DeviceIngest(envelope.ProtocolVersion, envelope.Counter, envelope.Ciphertext.ToByteArray(), UnknownHub),
            Ct);

        Assert.Equal(DeviceIngestStatus.Stored, result.Status);
        return node.OpenDownlink(SealedEnvelope.Parser.ParseFrom(result.Downlink));
    }

    // The Lot's status, once the lots projection has caught up.
    private async Task<string> LotStatusAsync(string lotId)
    {
        await identity.SiloServices.GetProjectionRunner<LotsProjector>().CatchUpAsync(Ct);

        return (await identity.Database.ScalarAsync<string>("SELECT status FROM lots WHERE lot_id = @lot_id", ("lot_id", lotId)))!;
    }

    // A record's own equality compares its list by reference.
    private sealed class SitePauseStatusComparer : IEqualityComparer<SitePauseStatus>
    {
        public static SitePauseStatusComparer Instance { get; } = new();

        public bool Equals(SitePauseStatus? x, SitePauseStatus? y) =>
            x is not null && y is not null && x.Pause == y.Pause && x.PendingDevices.SequenceEqual(y.PendingDevices, StringComparer.Ordinal);

        public int GetHashCode(SitePauseStatus obj) => obj.Pause.GetHashCode();
    }
}
