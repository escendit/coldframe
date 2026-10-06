using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Coldframe.Migrations;
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
        "readings",
        "readings_default",
        "device_reports",
        "device_reports_default",
        "reading_keys",
        "device_replay",
        "lot_status_devices",
        "lot_status_sensors",
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
    public async Task TheLotStatusMigrationEmptiesLotsAndDropsTheirCheckpointSoTheProjectorRebuildsThem()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = new JournalDatabase(fixture);

        // A database from before Story 4.7, with a projected Lot and the projector's checkpoint.
        await database.InitializeAsync(upTo: 20261006150000);
        await database.ExecuteAsync(
            """
            INSERT INTO lots (lot_id, site_id, name, status, claimed_by, created_at, removed_at)
            VALUES ('0192f3a4-8a00-7c3d-8e4f-5a6b7c8d9e02', 'site-1', 'Tomatoes', 'unknown', '7c19', now(), NULL);
            INSERT INTO projection_checkpoints (projector, position, updated_at) VALUES ('lots', 42, now()), ('devices', 42, now());
            """);

        Assert.True(JournalDatabase.Migrate(database.ConnectionString), "The Lot status migration was not pending.");

        Assert.Equal(0L, await database.ScalarAsync<long>("SELECT COUNT(*) FROM lots"));
        Assert.Equal(0L, await database.ScalarAsync<long>("SELECT COUNT(*) FROM projection_checkpoints WHERE projector = 'lots'"));
        Assert.Equal(42L, await database.ScalarAsync<long>("SELECT position FROM projection_checkpoints WHERE projector = 'devices'"));

        var tables = await ReadTablesAsync(database.ConnectionString, cancellationToken);
        Assert.Contains("lot_status_devices", tables);
        Assert.Contains("lot_status_sensors", tables);

        var columns = new List<string>();
        await using (var command = database.DataSource.CreateCommand(
            "SELECT column_name || ' ' || is_nullable FROM information_schema.columns WHERE table_name = 'lots' ORDER BY column_name"))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                columns.Add(reader.GetString(0));
            }
        }

        Assert.Subset(
            columns.ToHashSet(StringComparer.Ordinal),
            new HashSet<string>(["status_since NO", "claimed_at YES", "unknown_cause YES", "paused_by YES", "paused_until YES"], StringComparer.Ordinal));
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

    [Fact]
    public async Task AfterTheMigrationJobThePartitionsReachAtLeastTwoMonthsAheadWithADefault()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(AppHostFixture.ResourceTimeout);

        await fixture.WaitForHealthyAsync(ServerResource, timeout.Token);
        var connectionString = await GetConnectionStringAsync(timeout.Token);
        var now = TimeProvider.System.GetUtcNow().UtcDateTime;

        foreach (var table in ReadingsMaintenance.PartitionedTables)
        {
            var partitions = await ReadPartitionsAsync(connectionString, table, timeout.Token);

            Assert.Equal("DEFAULT", partitions[$"{table}_default"]);
            foreach (var offset in new[] { 0, 1, 2 })
            {
                var month = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(offset);
                var next = month.AddMonths(1);
                Assert.Equal(
                    $"FOR VALUES FROM ('{month:yyyy-MM-dd} 00:00:00+00') TO ('{next:yyyy-MM-dd} 00:00:00+00')",
                    partitions[ReadingsMaintenance.PartitionName(table, month.Year, month.Month)]);
            }
        }
    }

    // Each partition of a table with its bound, as PostgreSQL prints it in UTC.
    internal static async Task<Dictionary<string, string>> ReadPartitionsAsync(string connectionString, string table, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using (var utc = new NpgsqlCommand("SET TIME ZONE 'UTC'", connection))
        {
            await utc.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = new NpgsqlCommand(
            """
            SELECT child.relname, pg_get_expr(child.relpartbound, child.oid)
            FROM pg_inherits
            JOIN pg_class AS parent ON parent.oid = pg_inherits.inhparent
            JOIN pg_class AS child ON child.oid = pg_inherits.inhrelid
            WHERE parent.relname = @table
            """,
            connection);
        command.Parameters.AddWithValue("table", table);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var partitions = new Dictionary<string, string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            partitions.Add(reader.GetString(0), reader.GetString(1));
        }

        return partitions;
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
