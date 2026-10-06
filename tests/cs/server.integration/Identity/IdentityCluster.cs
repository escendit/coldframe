using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Lots;
using Coldframe.Contracts.Sites;
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
/// A one-silo <see cref="TestCluster"/> with the User, Site, Lot and Device grains, Device enrolment keys, the identity and lots projectors, a
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

            siloBuilder.Services.AddSingleton<CapturingLoggerProvider>();
            siloBuilder.Services.AddSingleton<ILoggerProvider>(provider => provider.GetRequiredService<CapturingLoggerProvider>());

            siloBuilder.Services.AddJournal(connectionString, options => options.PollInterval = PollInterval);
            siloBuilder.Services.AddProjector<IdentityProjector>();
            siloBuilder.Services.AddSingleton<IdentityReadModel>();
            siloBuilder.Services.AddLots();

            // Registered before AddDevices, which adds the real store only when there is none.
            siloBuilder.Services.AddSingleton<DeviceIngestionStore, FaultyIngestionStore>();
            var deviceKek = siloBuilder.Configuration[DeviceKekKey]
                ?? throw new InvalidOperationException("The test cluster has no Device key-encryption key.");
            siloBuilder.Services.AddDevices().Configure(options =>
            {
                options.PrivateKeyPem = EnrolmentKeyring.ToPrivateKeyPem(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
                options.DeviceKeyEncryptionKey = deviceKek;
            });
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
