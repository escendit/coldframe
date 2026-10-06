namespace Coldframe.Server.IntegrationTests.Devices;

/// <summary>
/// The ingestion suites run one after the other, after the suites that run in parallel: each brings its own
/// cluster, databases or job processes, and the AppHost's PostgreSQL has a fixed number of connections,
/// which the parallel suites already come close to.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class IngestSuites
{
    public const string Name = "Ingestion";
}
