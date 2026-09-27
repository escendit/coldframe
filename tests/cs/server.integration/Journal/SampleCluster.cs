using Coldframe.Server.Journal;
using Coldframe.Server.Tests.Samples;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Orleans.Hosting;
using Orleans.Streams;
using Orleans.TestingHost;

namespace Coldframe.Server.IntegrationTests.Journal;

/// <summary>
/// A one-silo <see cref="TestCluster"/> on its own <see cref="JournalDatabase"/>, with the journal,
/// the sample projector and a <see cref="FakeTimeProvider"/> registered before the Server's registrations.
/// </summary>
public abstract class SampleCluster(AppHostFixture fixture, bool hintStream) : IAsyncLifetime
{
    public static readonly DateTimeOffset Start = new(2026, 4, 1, 9, 15, 0, TimeSpan.Zero);

    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    private const string ConnectionStringKey = "Coldframe:Tests:ConnectionString";
    private const string HintStreamKey = "Coldframe:Tests:HintStream";

    private TestCluster? _cluster;

    public JournalDatabase Database { get; } = new(fixture);

    public TestCluster Cluster => _cluster ?? throw new InvalidOperationException("The cluster has not been started.");

    public IServiceProvider SiloServices => Cluster.GetSiloServiceProvider();

    public FakeTimeProvider Time => (FakeTimeProvider)SiloServices.GetRequiredService<TimeProvider>();

    public JournalStore Store => SiloServices.GetRequiredService<JournalStore>();

    public ProjectionWakeup Wakeup => SiloServices.GetRequiredService<ProjectionWakeup>();

    public ProjectionRunner Runner => SiloServices.GetServices<IHostedService>()
        .OfType<ProjectionRunner>()
        .Single(runner => runner.ProjectorName == SampleProjector.ProjectorName);

    public ISampleGrain Grain(string key) => Cluster.GrainFactory.GetGrain<ISampleGrain>(key);

    public async ValueTask InitializeAsync()
    {
        await Database.InitializeAsync();
        await Database.ExecuteAsync(SampleProjector.CreateTablesSql);

        var builder = new TestClusterBuilder(1);
        builder.Properties[ConnectionStringKey] = Database.ConnectionString;
        builder.Properties[HintStreamKey] = hintStream.ToString();
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
    /// The last global position in the journal.
    /// </summary>
    public async Task<long> LastPositionAsync() =>
        await Database.ScalarAsync<long>("SELECT COALESCE(MAX(position), 0) FROM journal_events");

    /// <summary>
    /// The sample projector's checkpoint, or 0 without one.
    /// </summary>
    public async Task<long> CheckpointAsync() =>
        await Database.ScalarAsync<long>(
            "SELECT COALESCE((SELECT position FROM projection_checkpoints WHERE projector = @projector), 0)",
            ("projector", SampleProjector.ProjectorName));

    /// <summary>
    /// Every row of the sample read model, ordered by stream.
    /// </summary>
    public async Task<List<(string StreamId, string? Name, int NoteCount, int TotalWeight)>> ReadModelAsync()
    {
        var rows = new List<(string, string?, int, int)>();
        await using var command = Database.DataSource.CreateCommand(
            "SELECT stream_id, name, note_count, total_weight FROM sample_read_model ORDER BY stream_id");
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            rows.Add((reader.GetString(0), await reader.IsDBNullAsync(1) ? null : reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3)));
        }

        return rows;
    }

    /// <summary>
    /// Every position the sample projector applied, in the order it applied them.
    /// </summary>
    public async Task<List<long>> AppliedPositionsAsync()
    {
        var positions = new List<long>();
        await using var command = Database.DataSource.CreateCommand("SELECT position FROM sample_applied ORDER BY sequence");
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            positions.Add(reader.GetInt64(0));
        }

        return positions;
    }

    /// <summary>
    /// Every global position in the journal, ascending.
    /// </summary>
    public async Task<List<long>> JournalPositionsAsync()
    {
        var positions = new List<long>();
        await using var command = Database.DataSource.CreateCommand("SELECT position FROM journal_events ORDER BY position");
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            positions.Add(reader.GetInt64(0));
        }

        return positions;
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
            var withHintStream = bool.Parse(siloBuilder.Configuration[HintStreamKey] ?? bool.FalseString);

            // Registered first: the Server's registrations add TimeProvider.System only with TryAdd.
            siloBuilder.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));

            // Orleans resolves its own clocks by key and falls back to the unkeyed provider. Its runtime
            // (grain timers, stream pulling agents, membership) keeps the real clock, so only Coldframe
            // code sees the fake one.
            foreach (var clock in OrleansClocks)
            {
                siloBuilder.Services.AddKeyedSingleton(clock, TimeProvider.System);
            }

            siloBuilder.Services.AddJournal(connectionString, options =>
            {
                options.EventAssemblies.Add(typeof(SampleCreated).Assembly);
                options.PollInterval = PollInterval;
            });
            siloBuilder.Services.AddProjector<SampleProjector>();

            Action<ISiloBuilder>? hintStream = withHintStream
                ? silo => silo.AddMemoryStreams(
                    JournalHints.StreamProvider,
                    streams => streams.ConfigureStreamPubSub(StreamPubSubType.ImplicitOnly))
                : null;

            siloBuilder.AddJournalGrains(hintStream);
        }
    }
}

/// <summary>
/// The sample cluster with no stream provider: projectors converge by polling alone.
/// </summary>
public sealed class SampleClusterWithoutStreams(AppHostFixture fixture) : SampleCluster(fixture, hintStream: false);

/// <summary>
/// The sample cluster with the hint stream on memory streams.
/// </summary>
public sealed class SampleClusterWithHints(AppHostFixture fixture) : SampleCluster(fixture, hintStream: true);
