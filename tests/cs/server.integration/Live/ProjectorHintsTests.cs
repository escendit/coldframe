using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Lots;
using Coldframe.Contracts.Sites;
using Coldframe.Server.Alerts;
using Coldframe.Server.Devices;
using Coldframe.Server.Identity;
using Coldframe.Server.IntegrationTests.Journal;
using Coldframe.Server.Journal;
using Coldframe.Server.Lots;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Coldframe.Server.IntegrationTests.Live;

/// <summary>
/// What the four projectors report to the hub (Story 6.6), on a migrated database with the real
/// <see cref="ProjectionRunner"/>: every hint names the Site, the resource and the ID of a row the batch
/// changed, its version is the journal position of the event, and it is handed on only once the batch is
/// committed. A batch that fails publishes nothing; a publisher that fails neither fails nor repeats the batch.
/// </summary>
public sealed class ProjectorHintsTests(AppHostFixture fixture) : IAsyncLifetime
{
    private const string Ada = "user-ada";
    private const string Ben = "user-ben";
    private const string Node = "5a4b3c2d1e0f7c20";

    // Of the five Device events below, the ones that change the row.
    private static readonly int[] ChangedRows = [0, 1, 2, 4];

    // Of the eleven silence events of a Hub below, the ones that change the row.
    private static readonly int[] SilenceRows = [0, 1, 6, 8, 10];

    private readonly JournalDatabase _database = new(fixture);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => _database.InitializeAsync();

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task TheIdentityProjectorReportsTheSiteAndTheUserOfAMembership()
    {
        var site = Guid.CreateVersion7().ToString();
        var hints = new RecordingHints(_database.DataSource, IdentityProjector.ProjectorName);

        var positions = await ApplyAsync(
            new IdentityProjector(),
            hints,
            $"site/{site}",
            new SiteCreated("Home garden", Ada),
            new MembershipGranted(Ada, SiteRole.Owner),
            new MembershipGranted(Ben, SiteRole.Member),
            new SiteRenamed("Allotment"),
            new MembershipRevoked(Ben),
            new SiteOwnerlessEditRefused([Ada]),
            new SiteDeleted());

        Assert.Equal(
            [
                new ReadModelChange(site, "sites", site, positions[0]),
                new ReadModelChange(site, "sites", site, positions[1], Ada),
                new ReadModelChange(site, "sites", site, positions[2], Ben),
                new ReadModelChange(site, "sites", site, positions[3]),
                new ReadModelChange(site, "sites", site, positions[4], Ben),
                new ReadModelChange(site, "sites", site, positions[6]),
            ],
            hints.Published);
        Assert.Equal([positions[^1]], hints.CheckpointsSeen);
    }

    [Fact]
    public async Task TheLotsAlertsAndDevicesProjectorsReportTheRowsTheyChangedWithTheirSite()
    {
        var site = Guid.CreateVersion7().ToString();
        var lot = Guid.CreateVersion7().ToString();
        var alert = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 10, 10, 0, 40, 0, TimeSpan.Zero);
        var wrapped = new WrappedDeviceKey("0000000000000000", new byte[12], new byte[48]);
        var opened = new AlertOpened(AlertKind.Threshold, ThresholdSide.Low, site, lot, Guid.NewGuid(), Node, "soil_moisture", 1, at);

        var lots = new RecordingHints(_database.DataSource, LotsProjector.ProjectorName);
        var lotPositions = await ApplyAsync(new LotsProjector(), lots, $"lot/{lot}", new LotCreated(site, "Tomatoes"), new LotClaimed(Node), new LotRenamed("Beans"));
        var alertPosition = (await ApplyAsync(new LotsProjector(), lots, $"alert/{alert:D}", opened))[0];
        var removed = (await ApplyAsync(new LotsProjector(), lots, $"lot/{lot}", 3, new LotReleased(Node), new LotRemoved(), new LotRemoved()))[..2];

        // The Alert's Node holds the Lot, so the Lot is evaluated again and reported; a second remove changes no row.
        Assert.Equal(
            [.. lotPositions.Append(alertPosition).Concat(removed).Select(position => new ReadModelChange(site, "lots", lot, position))],
            lots.Published);

        var alerts = new RecordingHints(_database.DataSource, AlertsProjector.ProjectorName);
        var alertPositions = await ApplyAsync(new AlertsProjector(), alerts, $"alert/{alert:D}", 1, new AlertClosed(AlertCloseReason.Recovered, at.AddHours(5)), new AlertSiteNotified(AlertLifecycle.Closed, at.AddHours(5)));

        // The open (appended above) and the close; the acknowledgement is not projected.
        Assert.Equal(
            [new ReadModelChange(site, "alerts", alert.ToString("D"), alertPosition), new ReadModelChange(site, "alerts", alert.ToString("D"), alertPositions[0])],
            alerts.Published);

        var devices = new RecordingHints(_database.DataSource, DevicesProjector.ProjectorName);
        var devicePositions = await ApplyAsync(
            new DevicesProjector(),
            devices,
            $"device/{Node}",
            new DeviceEnrolled(site, DeviceKind.Node, wrapped, at),
            new DeviceAssigned(site, lot, at),
            new DeviceSeen(at.AddMinutes(1), 1, null),
            new DeviceSeen(at, 2, null),
            new DeviceUnassigned(site, lot, at.AddMinutes(2)));

        // A heartbeat that is older than the last one changes no row and is not reported.
        Assert.Equal(
            [.. ChangedRows.Select(index => new ReadModelChange(site, "devices", Node, devicePositions[index]))],
            devices.Published.Where(change => change.Resource == "devices"));
        Assert.All(lots.Published.Concat(alerts.Published).Concat(devices.Published), change => Assert.True(change.Version > 0));
    }

    // Story 7.2: the overview and the Devices page read again when a Device goes silent, recovers, or gets
    // another Silence Window; an event that changes no row is not reported.
    [Fact]
    public async Task TheDevicesProjectorReportsADeviceThatGoesSilentRecoversOrGetsAnotherSilenceWindow()
    {
        var site = Guid.CreateVersion7().ToString();
        var hub = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8));
        var alert = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 10, 12, 7, 0, 0, TimeSpan.Zero);
        var wrapped = new WrappedDeviceKey("0000000000000000", new byte[12], new byte[48]);
        var started = new DeviceSilenceStarted(1, alert, site, null, at, at.AddSeconds(40), at.AddMinutes(-5));

        var devices = new RecordingHints(_database.DataSource, DevicesProjector.ProjectorName);
        var positions = await ApplyAsync(
            new DevicesProjector(),
            devices,
            $"device/{hub}",
            new DeviceEnrolled(site, DeviceKind.Hub, wrapped, at.AddDays(-1)),
            started,
            started,
            new DeviceReported(at, at),
            new DeviceSilenceClockRestarted(at, SilenceClockRestartCause.ServerStarted, null, at),
            new DeviceAlertDelivered(alert, AlertLifecycle.Open, at.AddSeconds(41)),
            new DeviceSilenceWindowSet(600, at.AddMinutes(1)),
            new DeviceSilenceWindowSet(600, at.AddMinutes(2)),
            new DeviceSilenceEnded(1, alert, AlertCloseReason.Recovered, at.AddMinutes(20)),
            new DeviceSilenceEnded(1, alert, AlertCloseReason.Recovered, at.AddMinutes(20)),
            new DeviceSilenceWindowSet(null, at.AddMinutes(30)));

        // The enrolment, the start of the silence, the window, the recovery and the cleared window; the same
        // silence, the same window and the same recovery applied again change nothing, and the other silence
        // events are not projected.
        Assert.Equal(
            [.. SilenceRows.Select(index => new ReadModelChange(site, "devices", hub, positions[index]))],
            devices.Published.Where(change => change.Id == hub));
    }

    [Fact]
    public async Task ABatchThatFailsPublishesNothingAndAPublisherThatFailsNeitherFailsNorRepeatsTheBatch()
    {
        var site = Guid.CreateVersion7().ToString();
        var store = CreateStore();
        Assert.True(await store.AppendAsync($"site/{site}", 0, [new SiteCreated("Home garden", Ada), new MembershipGranted(Ada, SiteRole.Owner)], Ct));
        var batch = await store.ReadFromAsync(0, 1000, Ct);

        // The second event of the batch throws after the first reported its row: nothing is committed or told.
        var failing = new RecordingHints(_database.DataSource, "failing");
        var faulty = new FailingProjector("failing", failOn: batch[^1].Position);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Runner(faulty, failing).ApplyAsync(batch, Ct));
        Assert.Empty(failing.Published);
        Assert.Equal(0L, await CheckpointAsync("failing"));

        // The publisher throws after the commit: the batch stays applied, and applying it again does nothing.
        var throwing = new RecordingHints(_database.DataSource, "counting") { Failure = new InvalidOperationException("The hub is gone.") };
        var counting = new FailingProjector("counting", failOn: null);
        var runner = Runner(counting, throwing);
        Assert.True(await runner.ApplyAsync(batch, readAfter: null, Ct));
        Assert.Equal(batch[^1].Position, await CheckpointAsync("counting"));
        Assert.Equal(batch.Count, counting.Applied);
        Assert.True(await runner.ApplyAsync(batch, readAfter: null, Ct));
        Assert.Equal(batch.Count, counting.Applied);
        Assert.Equal(1, throwing.Calls);
    }

    private Task<long[]> ApplyAsync(IProjector projector, RecordingHints hints, string streamId, params object[] events) =>
        ApplyAsync(projector, hints, streamId, 0, events);

    // Appends the events and has the projector's runner catch up. Returns the positions of the appended events.
    private async Task<long[]> ApplyAsync(IProjector projector, RecordingHints hints, string streamId, int expectedVersion, params object[] events)
    {
        var store = CreateStore();
        Assert.True(await store.AppendAsync(streamId, expectedVersion, events, Ct));
        await Runner(projector, hints).CatchUpAsync(Ct);

        return [.. (await store.ReadStreamAsync(streamId, Ct)).Skip(expectedVersion).Select(journalEvent => journalEvent.Position)];
    }

    private ProjectionRunner Runner(IProjector projector, IReadModelHints hints) =>
        new(
            projector,
            _database.DataSource,
            CreateStore(),
            new ProjectionWakeup(),
            TimeProvider.System,
            Options.Create(new JournalOptions()),
            NullLogger<ProjectionRunner>.Instance,
            hints);

    private JournalStore CreateStore() =>
        new(
            _database.DataSource,
            new JournalSerializer(new EventTypeRegistry(new JournalOptions().EventAssemblies)),
            TimeProvider.System,
            new OutboxWakeup());

    private async Task<long> CheckpointAsync(string projector) =>
        await _database.ScalarAsync<long?>(
            "SELECT position FROM projection_checkpoints WHERE projector = @projector",
            ("projector", projector)) ?? 0;

    /// <summary>
    /// Records what is published and, at that moment, the committed checkpoint as another connection sees it.
    /// </summary>
    private sealed class RecordingHints(NpgsqlDataSource dataSource, string projector) : IReadModelHints
    {
        public List<ReadModelChange> Published { get; } = [];

        public List<long> CheckpointsSeen { get; } = [];

        public Exception? Failure { get; set; }

        public int Calls { get; private set; }

        public void Publish(IReadOnlyList<ReadModelChange> changes)
        {
            Calls++;

            if (Failure is not null)
            {
                throw Failure;
            }

            using var command = dataSource.CreateCommand("SELECT position FROM projection_checkpoints WHERE projector = @projector");
            command.Parameters.AddWithValue("projector", projector);
            var checkpoint = (long)command.ExecuteScalar()!;

            // After the commit, never before: the checkpoint another connection reads covers every version told.
            Assert.All(changes, change => Assert.True(change.Version <= checkpoint));
            CheckpointsSeen.Add(checkpoint);
            Published.AddRange(changes);
        }
    }

    private sealed class FailingProjector(string name, long? failOn) : IProjector
    {
        public string Name => name;

        public int Applied { get; private set; }

        public Task ApplyAsync(JournalEvent journalEvent, NpgsqlTransaction transaction, ReadModelChanges changes, CancellationToken cancellationToken)
        {
            if (journalEvent.Position == failOn)
            {
                throw new InvalidOperationException("The projector fails as the test asked.");
            }

            Applied++;
            changes.Touch("site", ReadModelResources.Sites, journalEvent.StreamId);

            return Task.CompletedTask;
        }
    }
}
