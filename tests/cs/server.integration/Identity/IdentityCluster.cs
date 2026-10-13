using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Lots;
using Coldframe.Contracts.Sensors;
using Coldframe.Contracts.Sites;
using Coldframe.Server.Alerts;
using Coldframe.Server.Devices;
using Coldframe.Server.Identity;
using Coldframe.Server.IntegrationTests.Journal;
using Coldframe.Server.Journal;
using Coldframe.Server.Lots;
using Coldframe.Server.Notifications;
using Coldframe.Server.Notifications.Push;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Orleans.Hosting;
using Orleans.Streams;
using Orleans.TestingHost;

namespace Coldframe.Server.IntegrationTests.Identity;

/// <summary>
/// A one-silo <see cref="TestCluster"/> with the User, Site, Lot, Device, Sensor and Alert grains, Device enrolment keys, the identity and lots projectors, a
/// <see cref="FakePhaseTwoOrganizations"/>, the Notifier with a <see cref="RecordingNotificationChannel"/> and the push channels over <see cref="PushProviderStubs"/>, and a <see cref="FakeTimeProvider"/>. Hints are off and the poll
/// interval is 10 minutes of fake time, so only read-your-writes can bring the projection up to date. A Device's
/// <c>silence-window</c> reminder (Story 7.1) ticks on the real clock, as every reminder does; the cluster drops
/// those ticks (<see cref="SilenceWakes"/>), so only <see cref="WakeDeviceAsync"/> wakes a Device and the fake
/// clock alone decides what is overdue. The same holds for a Site's <c>site-pause</c> reminder (Story 8.1) and
/// <see cref="WakeSiteAsync"/>.
/// </summary>
public sealed class IdentityCluster(AppHostFixture fixture) : IAsyncLifetime
{
    public static readonly DateTimeOffset Start = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    public static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(10);

    private const string ConnectionStringKey = "Coldframe:Tests:ConnectionString";

    private const string DeviceKekKey = "Coldframe:Tests:DeviceKek";

    // Where the fake clock of a cluster's next silo starts, by the cluster's connection string; Start without one.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset> ClockStarts = new(StringComparer.Ordinal);

    private TestCluster? _cluster;

    public JournalDatabase Database { get; } = new(fixture);

    public TestCluster Cluster => _cluster ?? throw new InvalidOperationException("The cluster has not been started.");

    public IServiceProvider SiloServices => Cluster.GetSiloServiceProvider();

    public FakeTimeProvider Time => (FakeTimeProvider)SiloServices.GetRequiredService<TimeProvider>();

    public FakePhaseTwoOrganizations PhaseTwo => SiloServices.GetRequiredService<FakePhaseTwoOrganizations>();

    public JournalStore Store => SiloServices.GetRequiredService<JournalStore>();

    public IdentityReadModel ReadModel => SiloServices.GetRequiredService<IdentityReadModel>();

    public CapturingLoggerProvider Logs => SiloServices.GetRequiredService<CapturingLoggerProvider>();

    public IUserGrain User(string userId) => Cluster.GrainFactory.GetGrain<IUserGrain>(userId);

    public ISiteGrain Site(string siteId) => Cluster.GrainFactory.GetGrain<ISiteGrain>(siteId);

    public ILotGrain Lot(string lotId) => Cluster.GrainFactory.GetGrain<ILotGrain>(lotId);

    public IDeviceGrain Device(string deviceId) => Cluster.GrainFactory.GetGrain<IDeviceGrain>(deviceId);

    public ISensorGrain Sensor(Guid sensorId) => Cluster.GrainFactory.GetGrain<ISensorGrain>(sensorId.ToString("D"));

    public IAlertGrain Alert(Guid alertId) => Cluster.GrainFactory.GetGrain<IAlertGrain>(alertId.ToString("D"));

    /// <summary>
    /// The silo's Alert call filter, which a test can make fail the next opens and closes of any Alert.
    /// </summary>
    public AlertFaults AlertFaults => SiloServices.GetRequiredService<AlertFaults>();

    /// <summary>
    /// The silo's Site call filter, which a test can make fail the next Alert reports to any Site.
    /// </summary>
    public SiteFaults SiteFaults => SiloServices.GetRequiredService<SiteFaults>();

    /// <summary>
    /// The silo's User call filter, which a test can make fail the Reminder cadence handed to one User.
    /// </summary>
    public UserFaults UserFaults => SiloServices.GetRequiredService<UserFaults>();

    /// <summary>
    /// The silo's one notification channel, which records what the Notifier hands it and can fail a User's sends.
    /// A silo restart starts with an empty one.
    /// </summary>
    public RecordingNotificationChannel Notifications => SiloServices.GetRequiredService<RecordingNotificationChannel>();

    /// <summary>
    /// The stub APNs and FCM the silo's push channels send to (Story 6.5): they record every request and answer
    /// what a test says. A silo restart starts with empty ones.
    /// </summary>
    public PushProviderStubs Push => SiloServices.GetRequiredService<PushProviderStubs>();

    /// <summary>
    /// The silo's Sensor call filter, which a test can make fail the declarations of one Sensor and the next evaluations.
    /// </summary>
    public SensorFaults SensorFaults => SiloServices.GetRequiredService<SensorFaults>();

    /// <summary>
    /// The silo's Lot call filter, which a test can make fail the releases of one Lot.
    /// </summary>
    public LotFaults LotFaults => SiloServices.GetRequiredService<LotFaults>();

    /// <summary>
    /// The silo's Device call filter, which a test can make fail the Calibrations set on one Device.
    /// </summary>
    public DeviceFaults DeviceFaults => SiloServices.GetRequiredService<DeviceFaults>();

    public DeviceKeyVault Vault => SiloServices.GetRequiredService<DeviceKeyVault>();

    public LotsReadModel Lots => SiloServices.GetRequiredService<LotsReadModel>();

    /// <summary>
    /// The silo's ingestion store, which a test can make fail a commit.
    /// </summary>
    public FaultyIngestionStore Ingestion => (FaultyIngestionStore)SiloServices.GetRequiredService<DeviceIngestionStore>();

    /// <summary>
    /// Stops the silo and starts it again on the same database and with the same key-encryption key: every
    /// activation and everything a grain held in memory is gone, as after a crash. The clock starts at
    /// <see cref="Start"/> again.
    /// </summary>
    public async Task RestartSiloAsync()
    {
        ClockStarts.TryRemove(Database.ConnectionString, out _);
        await Cluster.RestartSiloAsync(Cluster.Primary ?? throw new InvalidOperationException("The cluster has no silo."));
        await Cluster.WaitForLivenessToStabilizeAsync();
        await ReminderTableReadyAsync();
    }

    /// <summary>
    /// Stops the silo and starts it again like <see cref="RestartSiloAsync()"/>, with the fake clock starting at
    /// <paramref name="startAt"/>: the Server was down until then, and that is the time it reads as its start
    /// (Story 7.1).
    /// </summary>
    public async Task RestartSiloAsync(DateTimeOffset startAt)
    {
        ClockStarts[Database.ConnectionString] = startAt;
        await Cluster.RestartSiloAsync(Cluster.Primary ?? throw new InvalidOperationException("The cluster has no silo."));
        await Cluster.WaitForLivenessToStabilizeAsync();
        await ReminderTableReadyAsync();
    }

    // The in-memory reminder table is a grain of the silo. Right after a restart its placement can time out for
    // 30 s at a time (Orleans' own timeout: "Grain placement operation timed out", then "ReminderService failed
    // initial load of reminders"), and every grain that registers or reads a reminder meanwhile waits that long:
    // a User grain does so in the turn that tells it of an Alert, which a Device's wake or frame awaits, so the
    // test's own call times out. The restart therefore reads the table before it hands back: a stall is spent
    // here. The wait never fails a test by itself: on CI four reads in a row timed out (DW-110), in a test that
    // needs no reminder, so after a few reads the restart is over as it was before this wait existed, and a test
    // that does need the table fails on its own call.
    private async Task ReminderTableReadyAsync()
    {
        var table = SiloServices.GetRequiredService<IReminderTable>();

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await table.ReadRows(GrainId.Create("device", "0000000000000000"));
                return;
            }
#pragma warning disable CA1031 // Whatever kept the table from answering, it is asked again, a few times.
            catch (Exception)
#pragma warning restore CA1031
            {
                // Asked again, and then left alone.
            }
        }
    }

    /// <summary>
    /// Wakes the Device grain the way its <c>silence-window</c> reminder does (Story 7.1), and returns once the
    /// wake has processed everything overdue. The reminder's own ticks are dropped in this cluster.
    /// </summary>
    public async Task WakeDeviceAsync(string deviceId)
    {
        RequestContext.Set(SilenceWakes.ByHandKey, true);

        try
        {
            await Device(deviceId).AsReference<IRemindable>().ReceiveReminder(DeviceGrain.SilenceReminderName, default);
        }
        finally
        {
            RequestContext.Remove(SilenceWakes.ByHandKey);
        }
    }

    /// <summary>
    /// Wakes the Site grain the way its <c>site-pause</c> reminder does (Story 8.1), and returns once the Site has
    /// ended a Pause that is past its end date and tried every pending delivery once. The reminder's own ticks are
    /// dropped in this cluster. The reminder only starts the delivery, which runs in turns of its own, so the wake
    /// waits for one attempt through <see cref="ISiteGrain.DeliverPause"/>.
    /// </summary>
    public async Task WakeSiteAsync(string siteId)
    {
        RequestContext.Set(SilenceWakes.ByHandKey, true);

        try
        {
            await Site(siteId).AsReference<IRemindable>().ReceiveReminder(SiteGrain.PauseReminderName, default);
        }
        finally
        {
            RequestContext.Remove(SilenceWakes.ByHandKey);
        }

        await Site(siteId).DeliverPause(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Whether the Site grain has its Pause reminder registered.
    /// </summary>
    public async Task<bool> HasSitePauseReminderAsync(string siteId)
    {
        var rows = await SiloServices.GetRequiredService<IReminderTable>().ReadRows(GrainId.Create("site", siteId));
        return rows.Reminders.Any(reminder => string.Equals(reminder.ReminderName, SiteGrain.PauseReminderName, StringComparison.Ordinal));
    }

    /// <summary>
    /// Makes the next append to the journal stream <paramref name="streamId"/> fail in the database, once, the way
    /// a lost write does: a trigger on <c>journal_events</c> raises for the first row of that stream. Test-only,
    /// and nothing of the Server knows it. Disposing removes the trigger.
    /// </summary>
    public async Task<IAsyncDisposable> FailNextJournalWriteAsync(string streamId)
    {
        ArgumentException.ThrowIfNullOrEmpty(streamId);

        // A sequence is not transactional: the failed insert still uses up the one failure.
        await using var command = Database.DataSource.CreateCommand(
            $$"""
            CREATE SEQUENCE test_journal_fault;
            CREATE FUNCTION test_journal_fault() RETURNS trigger LANGUAGE plpgsql AS $fault$
            BEGIN
                IF NEW.stream_id = '{{streamId.Replace("'", "''", StringComparison.Ordinal)}}' AND nextval('test_journal_fault') = 1 THEN
                    RAISE EXCEPTION 'The test failed this journal write.';
                END IF;
                RETURN NEW;
            END
            $fault$;
            CREATE TRIGGER test_journal_fault BEFORE INSERT ON journal_events FOR EACH ROW EXECUTE FUNCTION test_journal_fault();
            """);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        return new JournalFault(Database.DataSource);
    }

    private sealed class JournalFault(NpgsqlDataSource dataSource) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await using var command = dataSource.CreateCommand(
                "DROP TRIGGER test_journal_fault ON journal_events; DROP FUNCTION test_journal_fault(); DROP SEQUENCE test_journal_fault;");
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Whether the Device grain has its silence reminder registered.
    /// </summary>
    public async Task<bool> HasSilenceReminderAsync(string deviceId)
    {
        var rows = await SiloServices.GetRequiredService<IReminderTable>().ReadRows(GrainId.Create("device", deviceId));
        return rows.Reminders.Any(reminder => string.Equals(reminder.ReminderName, DeviceGrain.SilenceReminderName, StringComparison.Ordinal));
    }

    /// <summary>
    /// The Device grain's state, replayed from its journal stream.
    /// </summary>
    public async Task<DeviceState> DeviceStateAsync(string deviceId)
    {
        var state = new DeviceState();

        foreach (var @event in await Store.ReadStreamAsync($"device/{deviceId}", TestContext.Current.CancellationToken))
        {
            ((dynamic)state).Apply((dynamic)@event.Data);
        }

        return state;
    }

    /// <summary>
    /// The User grain's Site set, replayed from its journal stream.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, SiteRole>> UserSitesAsync(string userId) => (await UserStateAsync(userId)).Sites;

    /// <summary>
    /// The User grain's state, replayed from its journal stream.
    /// </summary>
    public async Task<UserState> UserStateAsync(string userId)
    {
        var state = new UserState();

        foreach (var @event in await Store.ReadStreamAsync($"user/{userId}", TestContext.Current.CancellationToken))
        {
            ((dynamic)state).Apply((dynamic)@event.Data);
        }

        return state;
    }

    /// <summary>
    /// Wakes the User grain the way its <c>deliver-notifications</c> reminder does, and returns once the wake has
    /// processed everything overdue. The fake clock fires no reminder by itself.
    /// </summary>
    public Task WakeUserAsync(string userId) =>
        User(userId).AsReference<IRemindable>().ReceiveReminder(UserGrain.WakeReminderName, default);

    /// <summary>
    /// Whether the User grain has its wake-up reminder registered.
    /// </summary>
    public async Task<bool> HasWakeReminderAsync(string userId)
    {
        var rows = await SiloServices.GetRequiredService<IReminderTable>().ReadRows(GrainId.Create("user", userId));
        return rows.Reminders.Any(reminder => string.Equals(reminder.ReminderName, UserGrain.WakeReminderName, StringComparison.Ordinal));
    }

    public async ValueTask InitializeAsync()
    {
        await Database.InitializeAsync();

        var builder = new TestClusterBuilder(1);
        builder.Properties[ConnectionStringKey] = Database.ConnectionString;

        // One key-encryption key for the life of the cluster, so wrapped keys survive a silo restart.
        builder.Properties[DeviceKekKey] = Guid.NewGuid().ToString("N");
        builder.AddSiloBuilderConfigurator<SiloConfigurator>();

        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_cluster is not null)
        {
            await _cluster.StopAllSilosAsync();
            await _cluster.DisposeAsync();
        }

        await Database.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// The aliases of a stream's events, in version order.
    /// </summary>
    public async Task<List<string>> AliasesAsync(string streamId)
    {
        var aliases = new List<string>();
        await using var command = Database.DataSource.CreateCommand(
            "SELECT type_alias FROM journal_events WHERE stream_id = @stream_id ORDER BY version");
        command.Parameters.AddWithValue("stream_id", streamId);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            aliases.Add(reader.GetString(0));
        }

        return aliases;
    }

    private sealed class SiloConfigurator : ISiloConfigurator
    {
        private static readonly string[] OrleansClocks =
        [
            TimeProviderNames.Grains,
            TimeProviderNames.Messaging,
            TimeProviderNames.SystemTimers,
            TimeProviderNames.Membership,
            TimeProviderNames.ActivationManagement,
            TimeProviderNames.GrainDirectory,
            StreamingTimeProviderNames.Streaming,
        ];

        public void Configure(ISiloBuilder siloBuilder)
        {
            var connectionString = siloBuilder.Configuration[ConnectionStringKey]
                ?? throw new InvalidOperationException("The test cluster has no connection string.");

            // Registered first: the Server's registrations add TimeProvider.System only with TryAdd.
            siloBuilder.Services.AddSingleton<TimeProvider>(
                new FakeTimeProvider(ClockStarts.TryGetValue(connectionString, out var startAt) ? startAt : Start));

            // Orleans' own runtime keeps the real clock; only Coldframe code sees the fake one.
            foreach (var clock in OrleansClocks)
            {
                siloBuilder.Services.AddKeyedSingleton(clock, TimeProvider.System);
            }

            // Reminders (a Device's pending Lot releases) need a service; in memory is enough for one silo.
            siloBuilder.UseInMemoryReminderService();

            siloBuilder.Services.AddSingleton<CapturingLoggerProvider>();
            siloBuilder.Services.AddSingleton<ILoggerProvider>(provider => provider.GetRequiredService<CapturingLoggerProvider>());

            siloBuilder.Services.AddJournal(connectionString, options => options.PollInterval = PollInterval);
            siloBuilder.Services.AddProjector<IdentityProjector>();
            siloBuilder.Services.AddSingleton<IdentityReadModel>();
            siloBuilder.Services.AddLots();
            siloBuilder.Services.AddAlerts();

            // The real Notifier over one recording channel: the User grain decides, the channel only records.
            siloBuilder.Services.AddNotifications();
            siloBuilder.Services.AddSingleton<RecordingNotificationChannel>();
            siloBuilder.Services.AddSingleton<INotificationChannel>(provider => provider.GetRequiredService<RecordingNotificationChannel>());

            // Push (Story 6.5): the real APNs and FCM channels with generated credentials, against stub providers.
            // A User without a registration is sent nothing by them, so the other suites do not notice.
            siloBuilder.Services.AddSingleton<PushProviderStubs>();
            siloBuilder.Services.AddOptions<PushOptions>().Configure(PushProviderStubs.Configure);
            siloBuilder.Services.AddApnsChannel();
            siloBuilder.Services.AddFcmChannel();
            siloBuilder.Services
                .AddHttpClient(ApnsChannel.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(provider => provider.GetRequiredService<PushProviderStubs>().CreateHandler());
            siloBuilder.Services
                .AddHttpClient(FcmServiceAccount.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(provider => provider.GetRequiredService<PushProviderStubs>().CreateHandler());

            // Registered before AddDevices, which adds the real store only when there is none.
            siloBuilder.Services.AddSingleton<DeviceIngestionStore, FaultyIngestionStore>();
            var deviceKek = siloBuilder.Configuration[DeviceKekKey]
                ?? throw new InvalidOperationException("The test cluster has no Device key-encryption key.");
            siloBuilder.Services.AddDevices().Configure(options =>
            {
                options.PrivateKeyPem = EnrolmentKeyring.ToPrivateKeyPem(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
                options.DeviceKeyEncryptionKey = deviceKek;
            });
            siloBuilder.Services.AddSingleton<IIncomingGrainCallFilter, SilenceWakes>();
            siloBuilder.Services.AddSingleton<SensorFaults>();
            siloBuilder.Services.AddSingleton<IIncomingGrainCallFilter>(provider => provider.GetRequiredService<SensorFaults>());
            siloBuilder.Services.AddSingleton<LotFaults>();
            siloBuilder.Services.AddSingleton<IIncomingGrainCallFilter>(provider => provider.GetRequiredService<LotFaults>());
            siloBuilder.Services.AddSingleton<DeviceFaults>();
            siloBuilder.Services.AddSingleton<IIncomingGrainCallFilter>(provider => provider.GetRequiredService<DeviceFaults>());
            siloBuilder.Services.AddSingleton<AlertFaults>();
            siloBuilder.Services.AddSingleton<IIncomingGrainCallFilter>(provider => provider.GetRequiredService<AlertFaults>());
            siloBuilder.Services.AddSingleton<UserFaults>();
            siloBuilder.Services.AddSingleton<IIncomingGrainCallFilter>(provider => provider.GetRequiredService<UserFaults>());
            siloBuilder.Services.AddSingleton<SiteFaults>();
            siloBuilder.Services.AddSingleton<IIncomingGrainCallFilter>(provider => provider.GetRequiredService<SiteFaults>());
            siloBuilder.Services.AddOptions<KeycloakOptions>();
            siloBuilder.Services.AddSingleton<FakePhaseTwoOrganizations>();
            siloBuilder.Services.AddSingleton<IPhaseTwoOrganizations>(provider => provider.GetRequiredService<FakePhaseTwoOrganizations>());

            // No hint stream: projectors converge by polling alone, every 10 minutes of fake time.
            siloBuilder.AddJournalGrains(hintStream: null);
        }
    }
}

/// <summary>
/// The real ingestion store, except that a test can make the next commits fail the way a lost database does:
/// the transaction is rolled back instead of committed, and the store throws.
/// </summary>
public sealed class FaultyIngestionStore(NpgsqlDataSource dataSource) : DeviceIngestionStore(dataSource)
{
    private int _failures;
    private int _loadFailures;

    /// <summary>
    /// How many commits were attempted, failed ones included.
    /// </summary>
    public int Commits { get; private set; }

    /// <summary>
    /// Makes the next <paramref name="commits"/> commits fail.
    /// </summary>
    public void FailNextCommits(int commits) => Interlocked.Exchange(ref _failures, commits);

    /// <summary>
    /// Makes the next <paramref name="loads"/> reads of a replay window fail.
    /// </summary>
    public void FailNextLoads(int loads) => Interlocked.Exchange(ref _loadFailures, loads);

    public override Task<StoredReplay?> LoadReplayAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Decrement(ref _loadFailures) >= 0)
        {
            throw new NpgsqlException("The test failed this read.");
        }

        Interlocked.Exchange(ref _loadFailures, 0);
        return base.LoadReplayAsync(deviceId, cancellationToken);
    }

    protected override async Task CommitTransactionAsync(NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        Commits++;

        if (Interlocked.Decrement(ref _failures) >= 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new NpgsqlException("The test failed this commit.");
        }

        Interlocked.Exchange(ref _failures, 0);
        await base.CommitTransactionAsync(transaction, cancellationToken);
    }
}

/// <summary>
/// Passes every grain call on, except that a test can make <see cref="ISensorGrain.Declare"/> throw for chosen
/// Sensors and the next <see cref="ISensorGrain.Evaluate"/> or <see cref="ISensorGrain.SetContext"/> calls throw,
/// the way a Sensor grain that cannot be reached or cannot write its journal does. Nothing reaches the grain then.
/// </summary>
public sealed class SensorFaults : IIncomingGrainCallFilter
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _failing = new(StringComparer.Ordinal);
    private int _evaluationFailures;
    private int _contextFailures;

    /// <summary>
    /// How many evaluation contexts were failed (Story 7.4).
    /// </summary>
    public int FailedContexts => _failedContexts;

    /// <summary>
    /// How many evaluation contexts reached a Sensor grain (Story 7.4): what the Device sent and nothing failed.
    /// </summary>
    public int PassedContexts => _passedContexts;

    private int _failedContexts;
    private int _passedContexts;

    /// <summary>
    /// Makes the next <paramref name="calls"/> evaluation contexts fail, whichever Sensor they are for (Story 7.4);
    /// 0 lets them through again.
    /// </summary>
    public void FailNextContexts(int calls) => Interlocked.Exchange(ref _contextFailures, calls);

    /// <summary>
    /// Makes the next <paramref name="calls"/> evaluations fail, whichever Sensor they are for.
    /// </summary>
    public void FailNextEvaluations(int calls) => Interlocked.Exchange(ref _evaluationFailures, calls);

    /// <summary>
    /// Makes every declaration of <paramref name="sensorId"/> fail until <see cref="Restore"/>.
    /// </summary>
    public void FailDeclarations(Guid sensorId) => _failing[sensorId.ToString("D")] = true;

    /// <summary>
    /// Lets the declarations of <paramref name="sensorId"/> through again.
    /// </summary>
    public void Restore(Guid sensorId) => _failing.TryRemove(sensorId.ToString("D"), out _);

    public Task Invoke(IIncomingGrainCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Grain is ISensorGrain
            && string.Equals(context.MethodName, nameof(ISensorGrain.Declare), StringComparison.Ordinal)
            && _failing.ContainsKey(context.TargetId.Key.ToString()!))
        {
            throw new InvalidOperationException("The test failed this declaration.");
        }

        if (context.Grain is ISensorGrain && string.Equals(context.MethodName, nameof(ISensorGrain.SetContext), StringComparison.Ordinal))
        {
            int left;
            while ((left = Volatile.Read(ref _contextFailures)) > 0)
            {
                if (Interlocked.CompareExchange(ref _contextFailures, left - 1, left) == left)
                {
                    Interlocked.Increment(ref _failedContexts);
                    throw new InvalidOperationException("The test failed this evaluation context.");
                }
            }

            Interlocked.Increment(ref _passedContexts);
        }

        if (context.Grain is ISensorGrain && string.Equals(context.MethodName, nameof(ISensorGrain.Evaluate), StringComparison.Ordinal))
        {
            // Takes one of the failures that are left, and never writes over a count set meanwhile.
            int left;
            while ((left = Volatile.Read(ref _evaluationFailures)) > 0)
            {
                if (Interlocked.CompareExchange(ref _evaluationFailures, left - 1, left) == left)
                {
                    throw new InvalidOperationException("The test failed this evaluation.");
                }
            }
        }

        return context.Invoke();
    }
}

/// <summary>
/// Passes every grain call on, except the ticks of a Device's <c>silence-window</c> reminder that no test sent
/// (Story 7.1), and those of a Site's <c>site-pause</c> reminder (Story 8.1). Reminders tick on the real clock, a
/// minute after they were registered, while the tests move a fake clock by hours: a tick at an arbitrary moment
/// would open Alerts, or end a Pause, in the middle of another test. A wake a test sends through
/// <see cref="IdentityCluster.WakeDeviceAsync"/> or <see cref="IdentityCluster.WakeSiteAsync"/> carries
/// <see cref="ByHandKey"/> and passes.
/// </summary>
public sealed class SilenceWakes : IIncomingGrainCallFilter
{
    /// <summary>
    /// The request-context key of a wake a test sent.
    /// </summary>
    public const string ByHandKey = "coldframe.tests.silence-wake";

    public Task Invoke(IIncomingGrainCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.Equals(context.MethodName, nameof(IRemindable.ReceiveReminder), StringComparison.Ordinal)
            && ((context.Grain is IDeviceGrain && context.Request.GetArgument(0) is DeviceGrain.SilenceReminderName)
                || (context.Grain is ISiteGrain && context.Request.GetArgument(0) is SiteGrain.PauseReminderName))
            && RequestContext.Get(ByHandKey) is not true)
        {
            return Task.CompletedTask;
        }

        return context.Invoke();
    }
}

/// <summary>
/// Passes every grain call on, except that a test can make <see cref="ILotGrain.Release"/> throw for chosen
/// Lots, the way a Lot grain that cannot be reached does. Nothing reaches the grain then.
/// </summary>
public sealed class LotFaults : IIncomingGrainCallFilter
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _failing = new(StringComparer.Ordinal);

    /// <summary>
    /// Makes every release of <paramref name="lotId"/> fail until <see cref="Restore"/>.
    /// </summary>
    public void FailReleases(string lotId) => _failing[lotId] = true;

    /// <summary>
    /// Lets the releases of <paramref name="lotId"/> through again.
    /// </summary>
    public void Restore(string lotId) => _failing.TryRemove(lotId, out _);

    public Task Invoke(IIncomingGrainCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Grain is ILotGrain
            && string.Equals(context.MethodName, nameof(ILotGrain.Release), StringComparison.Ordinal)
            && _failing.ContainsKey(context.TargetId.Key.ToString()!))
        {
            throw new InvalidOperationException("The test failed this release.");
        }

        return context.Invoke();
    }
}

/// <summary>
/// Passes every grain call on, except that a test can make <see cref="IDeviceGrain.SetCalibration"/>,
/// <see cref="IDeviceGrain.Silence"/> or <see cref="IDeviceGrain.SetSitePause"/> throw for chosen Devices, the way
/// a Device grain that cannot be reached or cannot write its journal does. Nothing reaches the grain then.
/// </summary>
public sealed class DeviceFaults : IIncomingGrainCallFilter
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _failing = new(StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _failingSilence = new(StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _failingSitePause = new(StringComparer.Ordinal);

    /// <summary>
    /// Makes every delivery of its Site's Pause to <paramref name="deviceId"/> fail until
    /// <see cref="RestoreSitePause"/>: a Device the Site grain cannot reach (Story 8.1).
    /// </summary>
    public void FailSitePause(string deviceId) => _failingSitePause[deviceId] = true;

    /// <summary>
    /// Lets its Site's Pause through to <paramref name="deviceId"/> again.
    /// </summary>
    public void RestoreSitePause(string deviceId) => _failingSitePause.TryRemove(deviceId, out _);

    /// <summary>
    /// How many calls were failed.
    /// </summary>
    public int Failed { get; private set; }

    /// <summary>
    /// Makes every read of the silence of <paramref name="deviceId"/> fail until <see cref="RestoreSilence"/>:
    /// a relay Hub that cannot be reached (Story 7.1).
    /// </summary>
    public void FailSilence(string deviceId) => _failingSilence[deviceId] = true;

    /// <summary>
    /// Lets the silence of <paramref name="deviceId"/> be read again.
    /// </summary>
    public void RestoreSilence(string deviceId) => _failingSilence.TryRemove(deviceId, out _);

    /// <summary>
    /// Makes every Calibration set on <paramref name="deviceId"/> fail until <see cref="Restore"/>.
    /// </summary>
    public void FailCalibrations(string deviceId) => _failing[deviceId] = true;

    /// <summary>
    /// Lets the Calibrations of <paramref name="deviceId"/> through again.
    /// </summary>
    public void Restore(string deviceId) => _failing.TryRemove(deviceId, out _);

    public Task Invoke(IIncomingGrainCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Grain is IDeviceGrain
            && string.Equals(context.MethodName, nameof(IDeviceGrain.SetCalibration), StringComparison.Ordinal)
            && _failing.ContainsKey(context.TargetId.Key.ToString()!))
        {
            Failed++;
            throw new InvalidOperationException("The test failed this Calibration.");
        }

        if (context.Grain is IDeviceGrain
            && string.Equals(context.MethodName, nameof(IDeviceGrain.Silence), StringComparison.Ordinal)
            && _failingSilence.ContainsKey(context.TargetId.Key.ToString()!))
        {
            throw new InvalidOperationException("The test failed this read of a Device's silence.");
        }

        if (context.Grain is IDeviceGrain
            && string.Equals(context.MethodName, nameof(IDeviceGrain.SetSitePause), StringComparison.Ordinal)
            && _failingSitePause.ContainsKey(context.TargetId.Key.ToString()!))
        {
            throw new InvalidOperationException("The test failed this delivery of a Site's Pause.");
        }

        return context.Invoke();
    }
}

/// <summary>
/// Passes every grain call on, except that a test can make the next <see cref="IAlertGrain.Open"/>,
/// <see cref="IAlertGrain.OpenForDevice"/>, <see cref="IAlertGrain.OpenForSensor"/> and <see cref="IAlertGrain.Close"/> calls throw, the way an Alert grain that cannot be reached or cannot write its
/// journal does. Nothing reaches the grain then.
/// </summary>
public sealed class AlertFaults : IIncomingGrainCallFilter
{
    private int _failures;

    /// <summary>
    /// Makes the next <paramref name="calls"/> opens and closes fail, whichever Alert they are for.
    /// </summary>
    public void FailNext(int calls) => Interlocked.Exchange(ref _failures, calls);

    public Task Invoke(IIncomingGrainCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Grain is IAlertGrain && context.MethodName is nameof(IAlertGrain.Open) or nameof(IAlertGrain.OpenForDevice) or nameof(IAlertGrain.OpenForSensor) or nameof(IAlertGrain.Close))
        {
            if (Interlocked.Decrement(ref _failures) >= 0)
            {
                throw new InvalidOperationException("The test failed this Alert call.");
            }

            Interlocked.Exchange(ref _failures, 0);
        }

        return context.Invoke();
    }
}

/// <summary>
/// Passes every grain call on, except that a test can make the next <see cref="ISiteGrain.AlertOpened"/> and
/// <see cref="ISiteGrain.AlertClosed"/> calls throw, and every <see cref="ISiteGrain.OpenAlerts"/> of chosen
/// Sites, the way a Site grain that cannot be reached does. Nothing reaches the grain then.
/// </summary>
public sealed class SiteFaults : IIncomingGrainCallFilter
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _failingPulls = new(StringComparer.Ordinal);
    private int _failures;

    /// <summary>
    /// Makes every read of the open Alerts of <paramref name="siteId"/> fail until <see cref="RestoreOpenAlerts"/>.
    /// </summary>
    public void FailOpenAlerts(string siteId) => _failingPulls[siteId] = true;

    /// <summary>
    /// Lets the open Alerts of <paramref name="siteId"/> be read again.
    /// </summary>
    public void RestoreOpenAlerts(string siteId) => _failingPulls.TryRemove(siteId, out _);

    /// <summary>
    /// Makes the next <paramref name="calls"/> Alert reports fail, whichever Site they are for.
    /// </summary>
    public void FailNextAlertReports(int calls) => Interlocked.Exchange(ref _failures, calls);

    public Task Invoke(IIncomingGrainCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Grain is ISiteGrain && context.MethodName is nameof(ISiteGrain.AlertOpened) or nameof(ISiteGrain.AlertClosed))
        {
            if (Interlocked.Decrement(ref _failures) >= 0)
            {
                throw new InvalidOperationException("The test failed this Alert report.");
            }

            Interlocked.Exchange(ref _failures, 0);
        }

        if (context.Grain is ISiteGrain
            && string.Equals(context.MethodName, nameof(ISiteGrain.OpenAlerts), StringComparison.Ordinal)
            && _failingPulls.ContainsKey(context.TargetId.Key.ToString()!))
        {
            throw new InvalidOperationException("The test failed this read of the open Alerts.");
        }

        return context.Invoke();
    }
}

/// <summary>
/// Passes every grain call on, except that a test can make <see cref="IUserGrain.SyncSiteReminderCadence"/>, or
/// <see cref="IUserGrain.AlertOpened"/> and <see cref="IUserGrain.AlertClosed"/>, throw for chosen Users, the way
/// a User grain that cannot be reached or cannot write its journal does. Nothing reaches the grain then.
/// </summary>
public sealed class UserFaults : IIncomingGrainCallFilter
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _failing = new(StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _failingAlertCalls = new(StringComparer.Ordinal);

    /// <summary>
    /// How many Alert calls were failed.
    /// </summary>
    public int FailedAlertCalls => _failedAlertCalls;

    private int _failedAlertCalls;

    /// <summary>
    /// Makes every Alert open and close told to <paramref name="userId"/> fail until <see cref="RestoreAlertCalls"/>.
    /// </summary>
    public void FailAlertCalls(string userId) => _failingAlertCalls[userId] = true;

    /// <summary>
    /// Lets the Alert opens and closes through to <paramref name="userId"/> again.
    /// </summary>
    public void RestoreAlertCalls(string userId) => _failingAlertCalls.TryRemove(userId, out _);

    /// <summary>
    /// Makes every Reminder cadence handed to <paramref name="userId"/> fail until <see cref="Restore"/>.
    /// </summary>
    public void FailCadenceSyncs(string userId) => _failing[userId] = true;

    /// <summary>
    /// Lets the Reminder cadence through to <paramref name="userId"/> again.
    /// </summary>
    public void Restore(string userId) => _failing.TryRemove(userId, out _);

    public Task Invoke(IIncomingGrainCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Grain is IUserGrain
            && string.Equals(context.MethodName, nameof(IUserGrain.SyncSiteReminderCadence), StringComparison.Ordinal)
            && _failing.ContainsKey(context.TargetId.Key.ToString()!))
        {
            throw new InvalidOperationException("The test failed this Reminder cadence.");
        }

        if (context.Grain is IUserGrain
            && context.MethodName is nameof(IUserGrain.AlertOpened) or nameof(IUserGrain.AlertClosed)
            && _failingAlertCalls.ContainsKey(context.TargetId.Key.ToString()!))
        {
            Interlocked.Increment(ref _failedAlertCalls);
            throw new InvalidOperationException("The test failed this Alert call.");
        }

        return context.Invoke();
    }
}
