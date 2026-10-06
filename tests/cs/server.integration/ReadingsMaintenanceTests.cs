using Coldframe.Contracts.Devices;
using Coldframe.Migrations;
using Coldframe.Server.IntegrationTests.Devices;
using Coldframe.Server.IntegrationTests.Journal;
using Coldframe.Server.Journal;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Coldframe.Server.IntegrationTests;

/// <summary>
/// What the migration job does to the ingestion tables besides migrating (Story 4.5; AD-15, AD-17, AD-22):
/// the monthly partitions and the restore step <c>advance-replay</c>, each on a fresh database migrated the
/// way the job migrates it, and the job's own commands as a process.
/// </summary>
[Collection(IngestSuites.Name)]
public sealed class ReadingsMaintenanceTests(AppHostFixture fixture)
{
    [Fact]
    public async Task EnsuringPartitionsIsIdempotentAndCarriesTheParentsIndexes()
    {
        await using var database = new JournalDatabase(fixture);
        await database.InitializeAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = new DateTimeOffset(2026, 11, 30, 23, 59, 59, TimeSpan.Zero);

        // The migrations alone create the default partitions only.
        Assert.Equal(["readings_default"], (await MigrationTests.ReadPartitionsAsync(database.ConnectionString, "readings", cancellationToken)).Keys);

        Assert.Equal(8, await ReadingsMaintenance.EnsurePartitionsAsync(database.ConnectionString, now, monthsAhead: 3, cancellationToken));
        Assert.Equal(0, await ReadingsMaintenance.EnsurePartitionsAsync(database.ConnectionString, now, monthsAhead: 3, cancellationToken));

        // A longer horizon, and the next month's run, only add what is missing; a year change is just a month.
        Assert.Equal(2, await ReadingsMaintenance.EnsurePartitionsAsync(database.ConnectionString, now, monthsAhead: 4, cancellationToken));
        Assert.Equal(2, await ReadingsMaintenance.EnsurePartitionsAsync(database.ConnectionString, now.AddSeconds(1), monthsAhead: 4, cancellationToken));

        foreach (var table in ReadingsMaintenance.PartitionedTables)
        {
            Assert.Equal(
                [$"{table}_default", $"{table}_y2026m11", $"{table}_y2026m12", $"{table}_y2027m01", $"{table}_y2027m02", $"{table}_y2027m03", $"{table}_y2027m04"],
                (await MigrationTests.ReadPartitionsAsync(database.ConnectionString, table, cancellationToken)).Keys.Order(StringComparer.Ordinal));
        }

        // Every index of the parent exists on a partition created later.
        Assert.Equal(2L, await database.ScalarAsync<long>("SELECT COUNT(*) FROM pg_indexes WHERE tablename = 'readings_y2027m04'"));
        Assert.Equal(1L, await database.ScalarAsync<long>("SELECT COUNT(*) FROM pg_indexes WHERE tablename = 'device_reports_y2027m04'"));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => ReadingsMaintenance.EnsurePartitionsAsync(database.ConnectionString, now, monthsAhead: 1, cancellationToken));
    }

    [Fact]
    public async Task ThePartitionRunMovesTheRowsOfItsMonthOutOfTheDefaultPartition()
    {
        await using var database = new JournalDatabase(fixture);
        await database.InitializeAsync();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Rows of three months, all in the default partitions: no month is prepared yet.
        foreach (var measuredAt in new[] { "2026-12-31T23:59:59.999Z", "2027-01-01T00:00:00Z", "2027-01-31T23:59:59.999Z", "2027-05-01T00:00:00Z" })
        {
            await database.ExecuteAsync(
                $"""
                INSERT INTO readings (device_id, sensor_id, reading_seq, measured_at, slot, quantity, raw_value, time_unsynced, received_at)
                VALUES ('92064422c012f481', gen_random_uuid(), 1, '{measuredAt}', 0, 'soil_moisture', 1873, false, '{measuredAt}');
                INSERT INTO device_reports (device_id, reading_seq, measured_at, battery_percent, charging, time_unsynced, received_at)
                VALUES ('92064422c012f481', 2, '{measuredAt}', 87, 'charging', false, '{measuredAt}');
                """);
        }

        Assert.Equal("readings_default:4", await RowsByPartitionAsync(database, "readings"));

        // January's run: January and the three months after it, and the months with rows waiting in the
        // default partition, December (already past) and May (beyond the horizon). Nothing stays behind.
        var created = await ReadingsMaintenance.EnsurePartitionsAsync(
            database.ConnectionString,
            new DateTimeOffset(2027, 1, 15, 3, 17, 0, TimeSpan.Zero),
            cancellationToken: cancellationToken);

        Assert.Equal(12, created);
        Assert.Equal("readings_y2026m12:1,readings_y2027m01:2,readings_y2027m05:1", await RowsByPartitionAsync(database, "readings"));
        Assert.Equal(
            "device_reports_y2026m12:1,device_reports_y2027m01:2,device_reports_y2027m05:1",
            await RowsByPartitionAsync(database, "device_reports"));
        Assert.Equal(4L, await database.ScalarAsync<long>("SELECT COUNT(*) FROM readings"));

        // New rows of a prepared month go straight to its partition.
        await database.ExecuteAsync(
            """
            INSERT INTO readings (device_id, sensor_id, reading_seq, measured_at, slot, quantity, raw_value, time_unsynced, received_at)
            VALUES ('92064422c012f481', gen_random_uuid(), 3, '2027-04-30T23:59:59Z', 0, 'soil_moisture', 1, false, now())
            """);
        Assert.Equal(
            "readings_y2026m12:1,readings_y2027m01:2,readings_y2027m04:1,readings_y2027m05:1",
            await RowsByPartitionAsync(database, "readings"));
    }

    [Fact]
    public async Task ARowOfAMonthLongPastLeavesTheDefaultPartitionOnTheNextRun()
    {
        await using var database = new JournalDatabase(fixture);
        await database.InitializeAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = new FakeTimeProvider(new DateTimeOffset(2027, 5, 1, 0, 0, 0, TimeSpan.Zero)).GetUtcNow();
        Assert.Equal(6, await ReadingsMaintenance.EnsurePartitionsAsync(database.ConnectionString, now, monthsAhead: 2, cancellationToken));

        // A Node hands in Readings it buffered years ago, and one of last month: no partition holds them.
        foreach (var measuredAt in new[] { "2019-03-14T06:30:00Z", "2019-03-31T23:59:59.999Z", "2027-04-30T23:59:59.999Z" })
        {
            await database.ExecuteAsync(
                $"""
                INSERT INTO readings (device_id, sensor_id, reading_seq, measured_at, slot, quantity, raw_value, time_unsynced, received_at)
                VALUES ('92064422c012f481', gen_random_uuid(), 1, '{measuredAt}', 0, 'soil_moisture', 1873, false, now());
                """);
        }

        await database.ExecuteAsync(
            """
            INSERT INTO device_reports (device_id, reading_seq, measured_at, battery_percent, charging, time_unsynced, received_at)
            VALUES ('92064422c012f481', 2, '2019-03-14T06:30:00Z', 87, 'charging', false, now())
            """);
        Assert.Equal("readings_default:3", await RowsByPartitionAsync(database, "readings"));

        // The next run, same month: only the partitions of the waiting months are new.
        Assert.Equal(3, await ReadingsMaintenance.EnsurePartitionsAsync(database.ConnectionString, now, monthsAhead: 2, cancellationToken));

        Assert.Equal("readings_y2019m03:2,readings_y2027m04:1", await RowsByPartitionAsync(database, "readings"));
        Assert.Equal("device_reports_y2019m03:1", await RowsByPartitionAsync(database, "device_reports"));
        Assert.Equal(0, await ReadingsMaintenance.EnsurePartitionsAsync(database.ConnectionString, now, monthsAhead: 2, cancellationToken));
    }

    [Fact]
    public async Task AdvanceReplayMovesEveryDevicesWindowAndDownlinkCounterOrNothingAtAll()
    {
        await using var database = new JournalDatabase(fixture);
        await database.InitializeAsync();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Two Devices with replay state, one at the top of the counter range; a third enrolled without a frame.
        await database.ExecuteAsync(
            """
            INSERT INTO device_replay (device_id, high_water, seen, downlink_counter) VALUES
                ('92064422c012f481', 200, 5, 17),
                ('b485999a177ebbf3', 18446744073709551614, 1, 0)
            """);
        var store = new JournalStore(
            database.DataSource,
            new JournalSerializer(new EventTypeRegistry(new JournalOptions().EventAssemblies)),
            TimeProvider.System,
            new OutboxWakeup());
        var key = new WrappedDeviceKey("0123456789abcdef", new byte[12], new byte[48]);
        Assert.True(await store.AppendAsync(
            "device/5a4b3c2d1e0f7c20",
            0,
            [new DeviceEnrolled(Guid.CreateVersion7().ToString(), DeviceKind.Node, key, TimeProvider.System.GetUtcNow())],
            cancellationToken));

        // A failure in the middle changes nothing: the journal is gone, so the second statement fails.
        await database.ExecuteAsync("ALTER TABLE journal_events RENAME TO journal_events_away");
        await Assert.ThrowsAsync<PostgresException>(
            () => ReadingsMaintenance.AdvanceReplayAsync(database.ConnectionString, cancellationToken: cancellationToken));
        await database.ExecuteAsync("ALTER TABLE journal_events_away RENAME TO journal_events");
        Assert.Equal("92064422c012f481:200:5:17,b485999a177ebbf3:18446744073709551614:1:0", await ReplayRowsAsync(database));

        Assert.Equal(3, await ReadingsMaintenance.AdvanceReplayAsync(database.ConnectionString, cancellationToken: cancellationToken));
        Assert.Equal(
            "5a4b3c2d1e0f7c20:63:-1:1048576,92064422c012f481:264:-1:1048593,b485999a177ebbf3:18446744073709551615:-1:1048576",
            await ReplayRowsAsync(database));

        // Other margins, on top.
        Assert.Equal(3, await ReadingsMaintenance.AdvanceReplayAsync(database.ConnectionString, 100, 7, cancellationToken));
        Assert.Equal(
            "5a4b3c2d1e0f7c20:163:-1:1048583,92064422c012f481:364:-1:1048600,b485999a177ebbf3:18446744073709551615:-1:1048583",
            await ReplayRowsAsync(database));
    }

    [Fact]
    public async Task TheJobRunsItsCommandsAgainstADatabase()
    {
        await using var database = new JournalDatabase(fixture);
        await database.InitializeAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        await database.ExecuteAsync("INSERT INTO device_replay (device_id, high_water, seen, downlink_counter) VALUES ('92064422c012f481', 10, 1, 2)");

        // Without arguments: nothing to migrate, and the partitions of this month and three more.
        Assert.Equal(0, await RunJobAsync(database.ConnectionString, cancellationToken));
        Assert.Equal(5, (await MigrationTests.ReadPartitionsAsync(database.ConnectionString, "readings", cancellationToken)).Count);
        Assert.Equal(5, (await MigrationTests.ReadPartitionsAsync(database.ConnectionString, "device_reports", cancellationToken)).Count);

        Assert.Equal(0, await RunJobAsync(database.ConnectionString, cancellationToken, "partitions", "--months-ahead", "5"));
        Assert.Equal(7, (await MigrationTests.ReadPartitionsAsync(database.ConnectionString, "readings", cancellationToken)).Count);

        Assert.Equal(0, await RunJobAsync(database.ConnectionString, cancellationToken, "advance-replay", "--uplink-margin", "128"));
        Assert.Equal("92064422c012f481:138:-1:1048578", await ReplayRowsAsync(database));

        // An unknown argument changes nothing.
        Assert.Equal(2, await RunJobAsync(database.ConnectionString, cancellationToken, "advance-replay", "--margin", "1"));
        Assert.Equal("92064422c012f481:138:-1:1048578", await ReplayRowsAsync(database));
    }

    private static async Task<int> RunJobAsync(string connectionString, CancellationToken cancellationToken, params string[] arguments)
    {
        // The job is built next to the tests, because this project references it.
        var start = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(typeof(MigrationRunnerRegistration).Assembly.Location);
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["ConnectionStrings__coldframe"] = connectionString;
        start.Environment["DOTNET_ENVIRONMENT"] = "Production";
        start.Environment.Remove("OTEL_EXPORTER_OTLP_ENDPOINT");

        using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("The migration job did not start.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        await Task.WhenAll(output, error);
        TestContext.Current.TestOutputHelper?.WriteLine(await output + await error);
        return process.ExitCode;
    }

    private static async Task<string?> RowsByPartitionAsync(JournalDatabase database, string table) =>
        await database.ScalarAsync<string>(
            $"""
            SELECT string_agg(partition || ':' || rows, ',' ORDER BY partition)
            FROM (SELECT tableoid::regclass::text AS partition, COUNT(*) AS rows FROM {table} GROUP BY 1) AS counted
            """);

    private static async Task<string?> ReplayRowsAsync(JournalDatabase database) =>
        await database.ScalarAsync<string>(
            "SELECT string_agg(device_id || ':' || high_water || ':' || seen || ':' || downlink_counter, ',' ORDER BY device_id) FROM device_replay");
}
