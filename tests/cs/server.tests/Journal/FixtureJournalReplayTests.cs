using System.Text.Json;
using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Lots;
using Coldframe.Contracts.Notifications;
using Coldframe.Contracts.Sensors;
using Coldframe.Contracts.Sites;
using Coldframe.Server.Alerts;
using Coldframe.Server.Devices;
using Coldframe.Server.Identity;
using Coldframe.Server.Journal;
using Coldframe.Server.Lots;
using Coldframe.Server.Sensors;
using Coldframe.Server.Tests.Samples;

namespace Coldframe.Server.Tests.Journal;

/// <summary>
/// Replays <c>Fixtures/journal.json</c> through the same registry and serializer the Server uses (AD-21, AD-24).
/// </summary>
/// <remarks>
/// A new event contract or schema version fails <see cref="EveryRegisteredAliasAndSchemaVersionAppearsInTheFixture"/>
/// until the fixture holds a row for it. An event must never become unreadable, so rows are only ever added.
/// </remarks>
public sealed class FixtureJournalReplayTests
{
    private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "journal.json");

    // Each alias prefix names the state its events apply to. A new aggregate adds its state here.
    private static readonly Dictionary<string, Func<object>> States = new(StringComparer.Ordinal)
    {
        ["alert"] = () => new AlertState(),
        ["device"] = () => new DeviceState(),
        ["lot"] = () => new LotState(),
        ["sample"] = () => new SampleState(),
        ["sensor"] = () => new SensorState(),
        ["site"] = () => new SiteState(),
        ["user"] = () => new UserState(),
    };

    [Fact]
    public void EveryRowDeserializesAndAppliesToItsState()
    {
        var serializer = new JournalSerializer(CreateRegistry());
        var states = new Dictionary<string, object>(StringComparer.Ordinal);

        foreach (var row in ReadFixture())
        {
            var @event = serializer.Deserialize(row.Alias, row.SchemaVersion, row.Payload.GetRawText());

            if (!states.TryGetValue(row.StreamId, out var state))
            {
                var prefix = row.Alias.Split('.')[0];
                Assert.True(
                    States.TryGetValue(prefix, out var create),
                    $"The replay test has no state for alias prefix '{prefix}' (row {row.StreamId} v{row.Version}).");
                state = create();
                states.Add(row.StreamId, state);
            }

            // Journaled grains apply events the same way: through a public Apply overload.
            ((dynamic)state).Apply((dynamic)@event);
        }

        var sample = Assert.IsType<SampleState>(states["sample/fixture-a"]);
        Assert.Equal("Fixture A", sample.Name);
        Assert.Equal(2, sample.NoteCount);
        Assert.Equal(SampleNotedUpcaster.ImplicitWeight + 4, sample.TotalWeight);

        var site = Assert.IsType<SiteState>(states["site/0192f3a4-7c1e-7d2b-9a51-3f7e2c9b1d00"]);
        Assert.Equal(SiteLifecycle.Active, site.Lifecycle);
        Assert.Equal("Home", site.Name);
        Assert.Equal(["5b0c7c1e-2a4d-4f1b-8e3a-9d7f6c5b4a31"], site.Owners);
        Assert.Equal(SiteRole.Member, site.Members["8f1e2d3c-4b5a-4968-8776-5a4b3c2d1e0f"]);
        var lotCreation = site.LotCreations["5b0c7c1e-2a4d-4f1b-8e3a-9d7f6c5b4a31:lk1"];
        Assert.Equal(("0192f3a4-8a00-7c3d-8e4f-5a6b7c8d9e01", "Tomatoes", true), (lotCreation.LotId, lotCreation.Name, lotCreation.Completed));

        // A Hub joined the roster, then enrolled with its key wrapped.
        Assert.Equal(DeviceKind.Hub, site.Devices["92064422c012f481"]);
        Assert.Equal("92064422c012f481", site.DeviceRegistrations["5b0c7c1e-2a4d-4f1b-8e3a-9d7f6c5b4a31:dk1"].DeviceId);

        // Site Pause (Story 8.1): paused with an end, paused again without one, resumed, and the one Device on
        // the roster acknowledged the resume. SiteStatePauseTests pins the pending set.
        Assert.Equal(new SitePause(false, null, 3), site.Pause);
        Assert.Empty(site.PendingPauseDeliveries);

        // Story 8.1 on a Device stream: a Node with an open battery and an open silent Alert was told the Site's
        // Pause of revision 1, which ended both episodes as paused (the battery one without a report to stamp
        // it), was paused by itself as well, and was told the Site's resume of revision 3. It is still paused, by
        // itself; a Pause is no recovery and restarts no silence clock; both closes are still to be delivered.
        var pausedNode = Assert.IsType<DeviceState>(states["device/4b5c6d7e8f901a22"]);
        Assert.Equal([DevicePauseSource.Device], pausedNode.PauseSources);
        Assert.Null(pausedNode.PausedBy[DevicePauseSource.Device]);
        Assert.Equal((3L, 3L), (pausedNode.SitePauseRevision, pausedNode.EvaluationEpoch));
        Assert.Equal(((DeviceSilentAlert?)null, (DeviceBatteryAlert?)null, (DateTimeOffset?)null), (pausedNode.OpenSilentAlert, pausedNode.OpenBatteryAlert, pausedNode.LastRecoveredAt));
        Assert.Equal(pausedNode.EnrolledAt, pausedNode.SilenceFrom);
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.Zero), pausedNode.BatteryCountedAt);
        var pausedAt = new DateTimeOffset(2026, 10, 20, 8, 0, 1, TimeSpan.Zero);
        Assert.Equal(
            [(AlertKind.Silent, (AlertCloseReason?)AlertCloseReason.Paused, pausedAt), (AlertKind.Battery, AlertCloseReason.Paused, pausedAt)],
            pausedNode.PendingAlertDeliveries.Where(delivery => delivery.Change == AlertLifecycle.Closed).Select(delivery => (delivery.Kind, delivery.Reason, delivery.At)));

        var device = Assert.IsType<DeviceState>(states["device/92064422c012f481"]);
        Assert.Equal(("0192f3a4-7c1e-7d2b-9a51-3f7e2c9b1d00", DeviceKind.Hub), (device.SiteId, device.Kind));
        Assert.Equal("0123456789abcdef", device.WrappedKey?.KekId);
        Assert.Equal(12, device.WrappedKey?.Nonce.Length);
        Assert.Equal(48, device.WrappedKey?.Sealed.Length);

        // Two heartbeats, the second without an uptime: last seen at the second.
        Assert.Equal(new DateTimeOffset(2026, 9, 29, 8, 1, 15, 500, TimeSpan.Zero), device.LastSeenAt);
        Assert.Equal(1_790_668_875_100, device.LastHeartbeatTimestampMs);

        // A Node enrolled and assigned to a Lot in one step.
        var node = Assert.IsType<DeviceState>(states["device/5a4b3c2d1e0f7c20"]);
        Assert.Equal(("0192f3a4-7c1e-7d2b-9a51-3f7e2c9b1d00", DeviceKind.Node), (node.SiteId, node.Kind));
        Assert.Equal("0192f3a4-8a00-7c3d-8e4f-5a6b7c8d9e02", node.LotId);
        Assert.Null(device.LotId);

        // Assigned, moved (its old Lot released at once), then unassigned: on no Lot, and the unassigned Lot
        // is still pending release.
        var moved = Assert.IsType<DeviceState>(states["device/5a4b3c2d1e0f7c21"]);
        Assert.Null(moved.LotId);
        Assert.Equal(["0192f3a4-8a00-7c3d-8e4f-5a6b7c8d9e04"], moved.PendingReleases);

        // Silence (Story 7.1): the Hub's clock runs from its last heartbeat with the default window. The moved
        // Node reported, got a window of its own, had its clock restarted by a Server start and by its relay
        // Hub's recovery, went silent (episode 1, delivered), reported again (recovered, the close not delivered
        // yet), and its window was cleared: the default again, counted from its newest measured_at.
        Assert.Equal(device.LastSeenAt, device.SilenceFrom);
        Assert.Equal(device.LastSeenAt + SilenceRule.HubWindow, device.SilenceDueAt);
        Assert.Null(device.OpenSilentAlert);
        var silentAlert = Guid.Parse("dfa1c207-f801-5c34-9795-559203085894");
        var recovered = new DateTimeOffset(2026, 10, 14, 14, 0, 0, TimeSpan.Zero);
        Assert.Equal(silentAlert, AlertIds.Silent("5a4b3c2d1e0f7c21", 1));
        Assert.Equal((recovered, recovered), (moved.LastReportedAt, moved.SilenceFrom));
        Assert.Null(moved.SilenceWindowOverride);
        Assert.Equal(recovered + SilenceRule.NodeWindow, moved.SilenceDueAt);
        Assert.Equal((1, (DeviceSilentAlert?)null, recovered.AddSeconds(3)), (moved.SilenceEpisode, moved.OpenSilentAlert, moved.LastRecoveredAt));
        var undeliveredClose = Assert.Single(moved.PendingAlertDeliveries);
        Assert.Equal((silentAlert, AlertLifecycle.Closed, AlertKind.Silent), (undeliveredClose.AlertId, undeliveredClose.Change, undeliveredClose.Kind));

        // Its Alert has no side, Sensor, quantity or Lot: the rows carry none of them.
        var silent = Assert.IsType<AlertState>(states[$"alert/{silentAlert}"]);
        Assert.Equal((AlertLifecycle.Closed, AlertKind.Silent, "5a4b3c2d1e0f7c21", false), (silent.Lifecycle, silent.Kind, silent.DeviceId, silent.ReportPending));
        Assert.Equal(((ThresholdSide?)null, (string?)null, (Guid?)null, (string?)null), (silent.Side, silent.LotId, silent.SensorId, silent.Quantity));

        // Story 7.2: a second Hub went silent after that story. Its episode says when it was last heard (its
        // last heartbeat, not the expiry), and the Alert, the Site and the User carry that and the Device kind.
        // The rows above, journaled before, carry neither and still read.
        var hubAlert = Guid.Parse("2cf5a632-8d4e-54a8-a58c-cb4e24408eb6");
        var lastHeartbeat = new DateTimeOffset(2026, 10, 15, 6, 55, 0, TimeSpan.Zero);
        var silentHub = Assert.IsType<DeviceState>(states["device/3f2a9c0d1e4b5a67"]);
        Assert.Equal(hubAlert, AlertIds.Silent("3f2a9c0d1e4b5a67", 1));
        Assert.Equal((lastHeartbeat, hubAlert), (silentHub.LastHeardAt, silentHub.OpenSilentAlert?.AlertId));
        var undeliveredOpen = Assert.Single(silentHub.PendingAlertDeliveries);
        Assert.Equal((AlertLifecycle.Open, (DateTimeOffset?)lastHeartbeat), (undeliveredOpen.Change, undeliveredOpen.SilentSince));
        var hubSilent = Assert.IsType<AlertState>(states[$"alert/{hubAlert}"]);
        Assert.Equal(((DeviceKind?)DeviceKind.Hub, (DateTimeOffset?)lastHeartbeat), (hubSilent.DeviceKind, hubSilent.SilentSince));
        Assert.Equal(((DeviceKind?)null, (DateTimeOffset?)null), (silent.DeviceKind, silent.SilentSince));
        Assert.Equal((DateTimeOffset?)null, Assert.Single(moved.PendingAlertDeliveries).SilentSince);

        // Story 7.3: a Node on no Lot sent three low device reports (episode 1, opened at 14 %, delivered), then
        // three recovered ones (the close not delivered yet), then one low report again. The Alert carries the
        // level; every row journaled before that story carries none and still reads.
        var batteryAlert = Guid.Parse("ae0cf71d-0fe1-50b9-b548-f4b9e57572b7");
        var lowNode = Assert.IsType<DeviceState>(states["device/9c0d1e2f3a4b5c68"]);
        Assert.Equal(batteryAlert, AlertIds.Battery("9c0d1e2f3a4b5c68", 1));
        Assert.Equal((1, (DeviceBatteryAlert?)null), (lowNode.BatteryEpisode, lowNode.OpenBatteryAlert));
        Assert.Equal(
            (1, 0, (DateTimeOffset?)new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero)),
            (lowNode.BatteryLowStreak, lowNode.BatteryRecoveredStreak, lowNode.BatteryCountedAt));
        var batteryClose = Assert.Single(lowNode.PendingAlertDeliveries);
        Assert.Equal((batteryAlert, AlertLifecycle.Closed, AlertKind.Battery), (batteryClose.AlertId, batteryClose.Change, batteryClose.Kind));
        Assert.Equal(0, lowNode.SilenceEpisode);
        var battery = Assert.IsType<AlertState>(states[$"alert/{batteryAlert}"]);
        Assert.Equal(
            (AlertLifecycle.Closed, AlertKind.Battery, (DeviceKind?)DeviceKind.Node, (int?)14, false),
            (battery.Lifecycle, battery.Kind, battery.DeviceKind, battery.BatteryPercent, battery.ReportPending));
        Assert.Equal(((int?)null, (int?)null), (silent.BatteryPercent, hubSilent.BatteryPercent));
        Assert.Equal((0, 0, (DateTimeOffset?)null, 0), (moved.BatteryLowStreak, moved.BatteryRecoveredStreak, moved.BatteryCountedAt, moved.BatteryEpisode));

        // Relayed by the Hub; paused by its Site and by itself, then the Site resumed: still paused.
        Assert.Equal("92064422c012f481", node.LastRelayHubId);
        Assert.True(node.IsPaused);
        Assert.Equal([DevicePauseSource.Device], node.PausedBy.Keys);
        Assert.Null(node.PausedBy[DevicePauseSource.Device]);
        Assert.False(device.IsPaused);
        Assert.Null(device.LastRelayHubId);

        // Two Specification sets were accepted, the second with a changed soil Specification: the known hash
        // is the second one's, and the Sensor list is the same two Sensors.
        Assert.Equal(32, node.SpecHash?.Length);
        Assert.Equal((byte)0xA1, node.SpecHash![0]);
        Assert.Equal(
            [
                new DeclaredSensor(0, "soil_moisture", Guid.Parse("dac4e7fe-93b1-56fc-a363-51235a586394")),
                new DeclaredSensor(1, "air_temperature", Guid.Parse("e62a2dbe-b439-5906-a842-35abfb36458a")),
            ],
            node.Sensors);
        Assert.Null(device.SpecHash);
        Assert.Empty(device.Sensors);

        // Declared, its Thresholds set, then redeclared: the override and the cleared side survive.
        var soil = Assert.IsType<SensorState>(states["sensor/dac4e7fe-93b1-56fc-a363-51235a586394"]);
        Assert.Equal(("5a4b3c2d1e0f7c20", 0), (soil.DeviceId, soil.Slot));
        Assert.Equal(new SensorSpecification("soil_moisture", SensorUnit.RawCount, 0, 8191, true, 25, 75), soil.Specification);
        Assert.Equal((new ThresholdSetting(ThresholdKind.Override, 40), new ThresholdSetting(ThresholdKind.Cleared)), (soil.Low, soil.High));
        Assert.Equal((40L, (long?)null), (soil.EffectiveLow, soil.EffectiveHigh));

        // Calibrated (its dry point kept first), delivered to the Device, then recalibrated: a wet point kept, and
        // the second Calibration in force (revision 2) and not delivered yet.
        Assert.Equal(
            new SensorCalibration(Guid.Parse("0192f3a4-9000-7000-8000-000000000002"), 2, 3100, 1100, new DateTimeOffset(2026, 10, 7, 12, 1, 0, TimeSpan.Zero)),
            soil.Calibration);
        Assert.Equal(Guid.Parse("0192f3a4-9000-7000-8000-000000000001"), soil.DeliveredCalibrationId);
        Assert.True(soil.DeliveryPending);
        Assert.Equal(((long?)null, (long?)null), (soil.PendingDryRaw, soil.PendingWetRaw));

        // Evaluated (Story 6.1): a streak of two, episode 1 opened, delivered, recovered and delivered, then
        // episode 2 opened and not delivered yet, and one Reading within since.
        var firstAlert = Guid.Parse("0760cb39-dfed-5779-9344-f44689933ee4");
        var secondAlert = Guid.Parse("4321deb0-d3de-52ca-acfb-c3b31ebddd27");
        Assert.Equal((firstAlert, secondAlert), (AlertIds.Threshold(Guid.Parse("dac4e7fe-93b1-56fc-a363-51235a586394"), 1), AlertIds.Threshold(Guid.Parse("dac4e7fe-93b1-56fc-a363-51235a586394"), 2)));
        Assert.Equal(2, soil.Episode);
        Assert.Equal(new SensorOpenAlert(secondAlert, 2, ThresholdSide.Low), soil.OpenAlert);
        Assert.Equal(new StreakState(ThresholdSide.Low, ThresholdPosition.Within, 1), soil.Streak);
        Assert.Equal((4L, new DateTimeOffset(2026, 10, 8, 15, 15, 0, TimeSpan.Zero)), (soil.EvaluationEpoch, soil.LastEvaluatedAt));
        var undelivered = Assert.Single(soil.PendingAlertDeliveries);
        Assert.Equal((secondAlert, AlertLifecycle.Open), (undelivered.AlertId, undelivered.Change));

        // The first Alert was opened, closed as recovered, and its Site knows both; the second is open and
        // its Site was not told by the Alert grain yet.
        var closedAlert = Assert.IsType<AlertState>(states[$"alert/{firstAlert}"]);
        Assert.Equal((AlertLifecycle.Closed, AlertCloseReason.Recovered, false), (closedAlert.Lifecycle, closedAlert.Reason, closedAlert.ReportPending));
        var openAlert = Assert.IsType<AlertState>(states[$"alert/{secondAlert}"]);
        Assert.Equal(
            (AlertLifecycle.Open, (ThresholdSide?)ThresholdSide.Low, "5a4b3c2d1e0f7c20", "soil_moisture", true),
            (openAlert.Lifecycle, openAlert.Side, openAlert.DeviceId, openAlert.Quantity, openAlert.ReportPending));
        Assert.Equal(Guid.Parse("dac4e7fe-93b1-56fc-a363-51235a586394"), openAlert.SensorId);

        // The Site's set of open Alerts holds the second, and the silent Hub's with its kind and time (Story 7.2).
        Assert.Equal(new[] { secondAlert, hubAlert }.Order(), site.OpenAlerts.Keys.Order());
        Assert.Equal(
            ((DeviceKind?)DeviceKind.Hub, (DateTimeOffset?)new DateTimeOffset(2026, 10, 15, 6, 55, 0, TimeSpan.Zero)),
            (site.OpenAlerts[hubAlert].DeviceKind, site.OpenAlerts[hubAlert].SilentSince));
        Assert.Equal(((DeviceKind?)null, (DateTimeOffset?)null), (site.OpenAlerts[secondAlert].DeviceKind, site.OpenAlerts[secondAlert].SilentSince));
        Assert.Equal("0192f3a4-8a00-7c3d-8e4f-5a6b7c8d9e02", site.OpenAlerts[secondAlert].LotId);

        // Assigned, then paused twice and resumed once: the fourth evaluation epoch.
        Assert.Equal(4, node.EvaluationEpoch);

        // The Node caches the first Calibration of the soil Sensor.
        Assert.Equal(Guid.Parse("0192f3a4-9000-7000-8000-000000000001"), node.CalibrationOf(Guid.Parse("dac4e7fe-93b1-56fc-a363-51235a586394")));

        // Story 7.4: the rows above were journaled before that story. They hold no evaluation context, so the
        // Sensor has no uncalibrated episode and no Alert of that kind until a context arrives.
        Assert.Equal(((SensorContext?)null, 0, (SensorUncalibratedAlert?)null), (soil.Context, soil.UncalibratedEpisode, soil.OpenUncalibratedAlert));
        Assert.Equal((AlertKind.Threshold, (ThresholdSide?)ThresholdSide.Low), (undelivered.Kind, undelivered.Side));

        // A soil Sensor of another Node (whose own stream is not in the fixture) was declared while its Node was
        // on no Lot (no Alert), then the Node was assigned: uncalibrated episode 1, delivered. The Node moved:
        // the Alert of the Lot it left closed as unassigned (delivered), and episode 2 names the new Lot and is
        // not delivered yet.
        var uncalibratedSensor = Guid.Parse("1549a7b9-e608-5016-9526-384730ad21c4");
        var firstUncalibrated = Guid.Parse("b53cd77e-ef30-5b22-a9b1-c4dfe37d5236");
        var secondUncalibrated = Guid.Parse("b0981698-8399-5254-80ab-c006e203641a");
        Assert.Equal(
            (firstUncalibrated, secondUncalibrated),
            (AlertIds.Uncalibrated(uncalibratedSensor, 1), AlertIds.Uncalibrated(uncalibratedSensor, 2)));
        var uncalibrated = Assert.IsType<SensorState>(states[$"sensor/{uncalibratedSensor}"]);
        Assert.Equal(new SensorContext("0192f3a4-7c1e-7d2b-9a51-3f7e2c9b1d00", "0192f3a4-8a00-7c3d-8e4f-5a6b7c8d9e06", false, 2), uncalibrated.Context);
        Assert.True(uncalibrated.NeedsCalibration);
        Assert.Equal(
            (2, new SensorUncalibratedAlert(secondUncalibrated, 2, "0192f3a4-8a00-7c3d-8e4f-5a6b7c8d9e06")),
            (uncalibrated.UncalibratedEpisode, uncalibrated.OpenUncalibratedAlert));
        Assert.Equal((0, (SensorOpenAlert?)null), (uncalibrated.Episode, uncalibrated.OpenAlert));
        var undeliveredUncalibrated = Assert.Single(uncalibrated.PendingAlertDeliveries);
        Assert.Equal(
            (secondUncalibrated, AlertLifecycle.Open, AlertKind.Uncalibrated, (ThresholdSide?)null, "0192f3a4-8a00-7c3d-8e4f-5a6b7c8d9e06"),
            (undeliveredUncalibrated.AlertId, undeliveredUncalibrated.Change, undeliveredUncalibrated.Kind, undeliveredUncalibrated.Side, undeliveredUncalibrated.LotId));

        // Its first Alert records the Sensor (which alone closes it), the quantity, the Lot and the Device kind,
        // and no side; the Site and the User heard of its open and of its close.
        var uncalibratedAlert = Assert.IsType<AlertState>(states[$"alert/{firstUncalibrated}"]);
        Assert.Equal(
            (AlertLifecycle.Closed, AlertKind.Uncalibrated, AlertCloseReason.Unassigned, false),
            (uncalibratedAlert.Lifecycle, uncalibratedAlert.Kind, uncalibratedAlert.Reason, uncalibratedAlert.ReportPending));
        Assert.Equal(
            ((ThresholdSide?)null, uncalibratedSensor, "soil_moisture", "0192f3a4-8a00-7c3d-8e4f-5a6b7c8d9e05", (DeviceKind?)DeviceKind.Node, "7e1f2a3b4c5d6e70"),
            (uncalibratedAlert.Side, uncalibratedAlert.SensorId, uncalibratedAlert.Quantity, uncalibratedAlert.LotId, uncalibratedAlert.DeviceKind, uncalibratedAlert.DeviceId));
        Assert.DoesNotContain(firstUncalibrated, site.OpenAlerts.Keys);

        // Only watched: no defaults, so both sides follow a default that is absent.
        var air = Assert.IsType<SensorState>(states["sensor/e62a2dbe-b439-5906-a842-35abfb36458a"]);
        Assert.Equal(new SensorSpecification("air_temperature", SensorUnit.MilliDegreeCelsius, -40_000, 85_000, false), air.Specification);
        Assert.Equal((ThresholdSetting.Default, ThresholdSetting.Default), (air.Low, air.High));
        Assert.Equal(((long?)null, (long?)null), (air.EffectiveLow, air.EffectiveHigh));

        // Created, renamed, claimed and released by a Node, then removed: the tombstone keeps name and Site.
        var removed = Assert.IsType<LotState>(states["lot/0192f3a4-8a00-7c3d-8e4f-5a6b7c8d9e01"]);
        Assert.Equal(LotLifecycle.Removed, removed.Lifecycle);
        Assert.Equal("Tomatoes east", removed.Name);
        Assert.Equal("0192f3a4-7c1e-7d2b-9a51-3f7e2c9b1d00", removed.SiteId);
        Assert.Null(removed.ClaimedBy);

        var claimed = Assert.IsType<LotState>(states["lot/0192f3a4-8a00-7c3d-8e4f-5a6b7c8d9e02"]);
        Assert.Equal(LotLifecycle.Active, claimed.Lifecycle);
        Assert.Equal("7C20", claimed.ClaimedBy);

        // Reconciled from Keycloak: renamed, an ownerless episode refused and resolved, a member revoked,
        // then deleted. Deletion keeps the Members.
        var reconciled = Assert.IsType<SiteState>(states["site/0192f3a4-7c1e-7d2b-9a51-3f7e2c9b1d01"]);
        Assert.Equal(SiteLifecycle.Deleted, reconciled.Lifecycle);
        Assert.Equal("Allotment 12", reconciled.Name);
        Assert.False(reconciled.OwnerlessEditRefused);
        Assert.Equal(["5b0c7c1e-2a4d-4f1b-8e3a-9d7f6c5b4a31"], reconciled.Members.Keys);
        Assert.Equal(["8f1e2d3c-4b5a-4968-8776-5a4b3c2d1e0f"], reconciled.FormerMembers);

        var user = Assert.IsType<UserState>(states["user/5b0c7c1e-2a4d-4f1b-8e3a-9d7f6c5b4a31"]);
        Assert.True(user.SiteCreations["k1"].Completed);
        Assert.Equal(SiteRole.Owner, user.Sites["0192f3a4-7c1e-7d2b-9a51-3f7e2c9b1d00"]);

        // Notification settings (Story 6.3): the window, the chosen zone over the detected one, and per Site
        // the mute, a personal cadence taken back, and the Site's cadence.
        Assert.Equal(new NotificationWindow(390, 1320), user.NotificationWindow);
        Assert.Equal(("Europe/Vienna", "Europe/Zurich"), (user.ChosenTimeZone, user.DetectedTimeZone));
        Assert.Equal(
            new UserSiteNotifications(Muted: true, ReminderCadence: null, SiteReminderCadence: ReminderCadence.Every2Days),
            user.SiteNotificationsOf("0192f3a4-7c1e-7d2b-9a51-3f7e2c9b1d00"));
        Assert.Equal(ReminderCadence.Every2Days, Assert.IsType<SiteState>(states["site/0192f3a4-7c1e-7d2b-9a51-3f7e2c9b1d00"]).ReminderCadence);

        // Delivery timing (Story 6.4): the Site was pulled; one Alert was learned by pull and dropped, the other
        // told at night, held, released by the morning summary and reminded two days later, at the cadence in
        // force (the Site's, every 2 days), counted from its own due-at.
        Assert.Empty(user.PendingPulls);
        Assert.Null(user.WindowOpensAt);
        Assert.Equal(2, user.TrackedAlerts.Count);
        var tracked = Assert.Single(user.TrackedAlerts.Values, alert => alert.Alert.AlertId == secondAlert);
        var trackedHub = Assert.Single(user.TrackedAlerts.Values, alert => alert.Alert.AlertId == hubAlert);
        Assert.Equal(
            (AlertKind.Silent, (DeviceKind?)DeviceKind.Hub, (DateTimeOffset?)new DateTimeOffset(2026, 10, 15, 6, 55, 0, TimeSpan.Zero)),
            (trackedHub.Alert.Kind, trackedHub.Alert.DeviceKind, trackedHub.Alert.SilentSince));
        Assert.Equal(
            (secondAlert, new DateTimeOffset(2026, 10, 11, 21, 30, 0, TimeSpan.Zero), false, (DateTimeOffset?)null),
            (tracked.Alert.AlertId, tracked.PreviousDueAt, tracked.OpeningPending, tracked.HeldFrom));
        Assert.Equal(new DateTimeOffset(2026, 10, 13, 21, 30, 0, TimeSpan.Zero), user.DueAt(tracked));

        // Push registrations (Story 6.5): an iPhone and an Android phone registered; APNs then reported the
        // iPhone's token as invalid, so only the Android phone is left.
        var registration = Assert.Single(user.PushRegistrations.Values);
        Assert.Equal(
            new PushRegistration(
                "c0ldf4a3-2222-4a6b-9c2d-000000000002",
                PushPlatform.Fcm,
                "dQw4w9WgXcQ:APA91bExampleRegistrationToken",
                Environment: null,
                new DateTimeOffset(2026, 10, 12, 8, 5, 0, TimeSpan.Zero)),
            registration);

        var former = Assert.IsType<UserState>(states["user/8f1e2d3c-4b5a-4968-8776-5a4b3c2d1e0f"]);
        Assert.Empty(former.Sites);
    }

    [Fact]
    public void EveryRegisteredAliasAndSchemaVersionAppearsInTheFixture()
    {
        var registry = CreateRegistry();
        var covered = ReadFixture().Select(row => (row.Alias, row.SchemaVersion)).ToHashSet();

        var missing = registry.EventTypes
            .Where(type => !covered.Contains((type.Alias, type.SchemaVersion)))
            .Select(type => $"{type.Alias} v{type.SchemaVersion}")
            .ToList();

        Assert.True(missing.Count == 0, $"The fixture journal has no row for: {string.Join(", ", missing)}.");
    }

    [Fact]
    public void TheRegistryCoversTheServerContracts()
    {
        var options = new JournalOptions();

        Assert.Contains(typeof(Coldframe.Contracts.Events.EventTypeAttribute).Assembly, options.EventAssemblies);
    }

    private static EventTypeRegistry CreateRegistry()
    {
        // The Server's own configuration plus the test-only sample events.
        var options = new JournalOptions();
        options.EventAssemblies.Add(typeof(SampleCreated).Assembly);

        return new EventTypeRegistry(options.EventAssemblies);
    }

    private static List<FixtureRow> ReadFixture()
    {
        using var stream = File.OpenRead(FixturePath);
        var rows = JsonSerializer.Deserialize<List<FixtureRow>>(stream, JsonSerializerOptions.Web);

        Assert.NotNull(rows);
        Assert.NotEmpty(rows);
        return rows;
    }

    private sealed record FixtureRow(string StreamId, int Version, string Alias, int SchemaVersion, JsonElement Payload);
}
