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
/// <see cref="FakePhaseTwoOrganizations"/> and a <see cref="FakeTimeProvider"/>. Hints are off and the poll
/// interval is 10 minutes of fake time, so only read-your-writes can bring the projection up to date.
/// </summary>
public sealed class IdentityCluster(AppHostFixture fixture) : IAsyncLifetime
{
    public static readonly DateTimeOffset Start = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    public static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(10);

    private const string ConnectionStringKey = "Coldframe:Tests:ConnectionString";

    private const string DeviceKekKey = "Coldframe:Tests:DeviceKek";

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
        await Cluster.RestartSiloAsync(Cluster.Primary ?? throw new InvalidOperationException("The cluster has no silo."));
        await Cluster.WaitForLivenessToStabilizeAsync();
    }

    /// <summary>
    /// The User grain's Site set, replayed from its journal stream.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, SiteRole>> UserSitesAsync(string userId)
    {
        var state = new UserState();

        foreach (var @event in await Store.ReadStreamAsync($"user/{userId}", TestContext.Current.CancellationToken))
        {
            ((dynamic)state).Apply((dynamic)@event.Data);
        }

        return state.Sites;
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
            siloBuilder.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));

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

            // Registered before AddDevices, which adds the real store only when there is none.
            siloBuilder.Services.AddSingleton<DeviceIngestionStore, FaultyIngestionStore>();
            var deviceKek = siloBuilder.Configuration[DeviceKekKey]
                ?? throw new InvalidOperationException("The test cluster has no Device key-encryption key.");
            siloBuilder.Services.AddDevices().Configure(options =>
            {
                options.PrivateKeyPem = EnrolmentKeyring.ToPrivateKeyPem(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
                options.DeviceKeyEncryptionKey = deviceKek;
            });
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
/// Sensors and the next <see cref="ISensorGrain.Evaluate"/> calls throw, the way a Sensor grain that cannot be
/// reached or cannot write its journal does. Nothing reaches the grain then.
/// </summary>
public sealed class SensorFaults : IIncomingGrainCallFilter
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _failing = new(StringComparer.Ordinal);
    private int _evaluationFailures;

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
/// Passes every grain call on, except that a test can make <see cref="IDeviceGrain.SetCalibration"/> throw for
/// chosen Devices, the way a Device grain that cannot be reached or cannot write its journal does. Nothing
/// reaches the grain then.
/// </summary>
public sealed class DeviceFaults : IIncomingGrainCallFilter
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _failing = new(StringComparer.Ordinal);

    /// <summary>
    /// How many calls were failed.
    /// </summary>
    public int Failed { get; private set; }

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

        return context.Invoke();
    }
}

/// <summary>
/// Passes every grain call on, except that a test can make the next <see cref="IAlertGrain.Open"/> and
/// <see cref="IAlertGrain.Close"/> calls throw, the way an Alert grain that cannot be reached or cannot write its
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

        if (context.Grain is IAlertGrain && context.MethodName is nameof(IAlertGrain.Open) or nameof(IAlertGrain.Close))
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
/// <see cref="ISiteGrain.AlertClosed"/> calls throw, the way a Site grain that cannot be reached does. Nothing
/// reaches the grain then.
/// </summary>
public sealed class SiteFaults : IIncomingGrainCallFilter
{
    private int _failures;

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

        return context.Invoke();
    }
}

/// <summary>
/// Passes every grain call on, except that a test can make <see cref="IUserGrain.SyncSiteReminderCadence"/> throw
/// for chosen Users, the way a User grain that cannot be reached or cannot write its journal does. Nothing
/// reaches the grain then.
/// </summary>
public sealed class UserFaults : IIncomingGrainCallFilter
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _failing = new(StringComparer.Ordinal);

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

        return context.Invoke();
    }
}
