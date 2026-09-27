using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;

namespace Coldframe.Migrations;

/// <summary>
/// Registers the one migration set (AD-22): the Orleans cluster schema from
/// <c>Escendit.Orleans.Migrations.Cluster.PostgreSQL</c> and Coldframe's own migrations, run by one
/// runner against one version table.
/// </summary>
/// <remarks>
/// The migration job and the tests call this same method, so the tests migrate a database exactly
/// the way the job does.
/// </remarks>
public static class MigrationRunnerRegistration
{
    /// <summary>
    /// The name of the connection string the job reads.
    /// </summary>
    public const string ConnectionStringName = "coldframe";

    /// <summary>
    /// Adds the FluentMigrator runner for the Coldframe database.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The connection string of the Coldframe database.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddColdframeMigrations(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // The cluster package registers the runner, the PostgreSQL processor, the connection string
        // and its version table. Coldframe adds its own assembly to the same runner.
        services
            .AddClusterMigrationRunner(connectionString)
            .ConfigureRunner(runner => runner.WithMigrationsIn(typeof(MigrationRunnerRegistration).Assembly));

        return services;
    }
}
