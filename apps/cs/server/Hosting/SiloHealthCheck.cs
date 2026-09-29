using Microsoft.Extensions.Diagnostics.HealthChecks;
using Orleans.Runtime;

namespace Coldframe.Server.Hosting;

/// <summary>
/// Healthy while the local silo is an active member of the cluster.
/// </summary>
internal sealed class SiloHealthCheck(ISiloStatusOracle siloStatusOracle) : IHealthCheck
{
    public const string Name = "silo";

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var status = siloStatusOracle.CurrentStatus;

        return Task.FromResult(status == SiloStatus.Active
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy($"The silo is {status}."));
    }
}
