using Coldframe.Migrations;
using FluentMigrator.Runner;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// The migration job (AD-22). It applies every pending migration and exits 0, or exits 1 when a
// migration fails. It runs before the silo starts: in the AppHost the Server waits for its completion,
// and in a deployment it becomes the Kubernetes Job image.
var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

var connectionString = builder.Configuration.GetConnectionString(MigrationRunnerRegistration.ConnectionStringName);

if (string.IsNullOrWhiteSpace(connectionString))
{
    await Console.Error.WriteLineAsync(
        $"No connection string '{MigrationRunnerRegistration.ConnectionStringName}' is configured.").ConfigureAwait(false);
    return 1;
}

builder.Services.AddColdframeMigrations(connectionString);

using var host = builder.Build();
await host.StartAsync().ConfigureAwait(false);

var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Coldframe.Migrations");
var exitCode = 0;

try
{
    using var scope = host.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
}
#pragma warning disable CA1031 // Any failure must end the job with a non-zero exit code.
catch (Exception exception)
#pragma warning restore CA1031
{
    MigrationLog.Failed(logger, exception);
    exitCode = 1;
}

await host.StopAsync().ConfigureAwait(false);
return exitCode;
