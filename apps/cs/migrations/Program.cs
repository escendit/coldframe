using Coldframe.Migrations;
using FluentMigrator.Runner;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// The migration job (AD-22). Without arguments it applies every pending migration, then ensures the
// monthly partitions of the ingestion tables, and exits 0, or exits 1 when either fails. It runs before
// the silo starts: in the AppHost the Server waits for its completion, and in a deployment it becomes the
// Kubernetes Job image. `partitions` ensures the partitions only (the daily CronJob); `advance-replay`
// is the restore step that advances every Device's replay window (AD-15, AD-17). Unknown arguments exit 2
// before anything is touched.
var command = JobCommand.Parse(args, out var usageError);

if (command is null)
{
    await Console.Error.WriteLineAsync(usageError).ConfigureAwait(false);
    await Console.Error.WriteLineAsync(JobCommand.Usage).ConfigureAwait(false);
    return 2;
}

// The arguments are the job's own; they are not configuration.
var builder = Host.CreateApplicationBuilder();

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
    switch (command.Kind)
    {
        case JobCommandKind.Migrate:
            using (var scope = host.Services.CreateScope())
            {
                scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
            }

            await EnsurePartitionsAsync().ConfigureAwait(false);
            break;

        case JobCommandKind.Partitions:
            await EnsurePartitionsAsync().ConfigureAwait(false);
            break;

        case JobCommandKind.AdvanceReplay:
            var devices = await ReadingsMaintenance
                .AdvanceReplayAsync(connectionString, command.UplinkMargin, command.DownlinkMargin)
                .ConfigureAwait(false);
            MigrationLog.ReplayAdvanced(logger, devices, command.UplinkMargin, command.DownlinkMargin);
            break;

        default:
            throw new InvalidOperationException($"Unexpected command {command.Kind}.");
    }
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

async Task EnsurePartitionsAsync()
{
    var created = await ReadingsMaintenance
        .EnsurePartitionsAsync(connectionString, TimeProvider.System.GetUtcNow(), command.MonthsAhead)
        .ConfigureAwait(false);
    MigrationLog.PartitionsEnsured(logger, created, command.MonthsAhead);
}
