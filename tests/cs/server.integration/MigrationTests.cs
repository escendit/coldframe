using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Coldframe.Server.IntegrationTests.Journal;
using Npgsql;

namespace Coldframe.Server.IntegrationTests;

/// <summary>
/// The migration job runs before the silo (AD-22), and the silo clusters through the ADO.NET tables it creates.
/// </summary>
public sealed class MigrationTests(AppHostFixture fixture)
{
    private const string MigrationsResource = "migrations";
    private const string ServerResource = "server";
    private const string DatabaseResource = "coldframe";

    private static readonly string[] OrleansTables =
    [
        "orleansquery",
        "orleansstorage",
        "orleansmembershiptable",
        "orleansmembershipversiontable",
        "orleansreminderstable",
    ];

    private static readonly string[] ColdframeTables =
    [
        "journal_events",
        "journal_outbox",
        "projection_checkpoints",
        "identity_sites",
        "identity_memberships",
        "lots",
        "devices",
    ];

    [Fact]
    public async Task TheMigrationJobFinishesWithExitCodeZeroBeforeTheServerStarts()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(AppHostFixture.ResourceTimeout);

        fixture.GetResource(MigrationsResource);

        var migrations = await fixture.App.ResourceNotifications.WaitForResourceAsync(
            MigrationsResource,
            resource => KnownResourceStates.TerminalStates.Contains(resource.Snapshot.State?.Text),
            timeout.Token);

        Assert.Equal(KnownResourceStates.Finished, migrations.Snapshot.State?.Text);
        Assert.Equal(0, migrations.Snapshot.ExitCode);

        // The AppHost holds the Server back until the job has completed successfully.
        var waits = fixture.GetResource(ServerResource).Annotations.OfType<WaitAnnotation>();
        Assert.Contains(waits, wait => wait.Resource.Name == MigrationsResource && wait.WaitType == WaitType.WaitForCompletion);

        // And it did: the silo joined the cluster only after the last migration was applied.
        await fixture.WaitForHealthyAsync(ServerResource, timeout.Token);

        await using var connection = new NpgsqlConnection(await GetConnectionStringAsync(timeout.Token));
        await connection.OpenAsync(timeout.Token);
        await using var command = new NpgsqlCommand(
            "SELECT (SELECT MAX(applied_on) FROM versions), (SELECT MIN(starttime) FROM orleansmembershiptable)",
            connection);
        await using var reader = await command.ExecuteReaderAsync(timeout.Token);
        Assert.True(await reader.ReadAsync(timeout.Token));

        var lastMigration = reader.GetDateTime(0);
        var siloStart = reader.GetDateTime(1);
        Assert.True(
            siloStart >= lastMigration,
            $"The silo started at {siloStart:O}, before the last migration was applied at {lastMigration:O}.");
    }

    [Fact]
    public async Task TheOrleansAndColdframeTablesExist()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(AppHostFixture.ResourceTimeout);

        await fixture.WaitForHealthyAsync(ServerResource, timeout.Token);

        var tables = await ReadTablesAsync(await GetConnectionStringAsync(timeout.Token), timeout.Token);

        Assert.All(OrleansTables.Concat(ColdframeTables), table => Assert.Contains(table, tables));
    }

    [Fact]
    public async Task TheHealthySiloIsAnActiveMemberInTheAdoNetMembershipTable()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(AppHostFixture.ResourceTimeout);

        await fixture.WaitForHealthyAsync(ServerResource, timeout.Token);

        await using var connection = new NpgsqlConnection(await GetConnectionStringAsync(timeout.Token));
        await connection.OpenAsync(timeout.Token);

        // SiloStatus.Active is 3.
        await using var command = new NpgsqlCommand("SELECT COUNT(*) FROM orleansmembershiptable WHERE status = 3", connection);
        var active = (long)(await command.ExecuteScalarAsync(timeout.Token))!;

        Assert.True(active >= 1, "No silo is active in orleansmembershiptable.");
    }

    [Fact]
    public async Task ARerunOnAMigratedDatabaseAppliesNothing()
    {
        await using var database = new JournalDatabase(fixture);
        await database.InitializeAsync();

        // InitializeAsync has migrated the fresh database once.
        var tables = await ReadTablesAsync(database.ConnectionString, TestContext.Current.CancellationToken);
        Assert.All(OrleansTables.Concat(ColdframeTables), table => Assert.Contains(table, tables));

        var versions = await database.ScalarAsync<long>("SELECT COUNT(*) FROM versions");

        Assert.False(JournalDatabase.Migrate(database.ConnectionString), "A second run found pending migrations.");
        Assert.Equal(versions, await database.ScalarAsync<long>("SELECT COUNT(*) FROM versions"));
    }

    private async Task<string> GetConnectionStringAsync(CancellationToken cancellationToken) =>
        await fixture.App.GetConnectionStringAsync(DatabaseResource, cancellationToken)
            ?? throw new InvalidOperationException($"The AppHost gives '{DatabaseResource}' no connection string.");

    private static async Task<HashSet<string>> ReadTablesAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var tables = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }
}
