using System.Text.Json;
using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Lots;
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

        // The Site's set of open Alerts holds the second only.
        Assert.Equal([secondAlert], site.OpenAlerts.Keys);
        Assert.Equal("0192f3a4-8a00-7c3d-8e4f-5a6b7c8d9e02", site.OpenAlerts[secondAlert].LotId);

        // Assigned, then paused twice and resumed once: the fourth evaluation epoch.
        Assert.Equal(4, node.EvaluationEpoch);

        // The Node caches the first Calibration of the soil Sensor.
        Assert.Equal(Guid.Parse("0192f3a4-9000-7000-8000-000000000001"), node.CalibrationOf(Guid.Parse("dac4e7fe-93b1-56fc-a363-51235a586394")));

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
