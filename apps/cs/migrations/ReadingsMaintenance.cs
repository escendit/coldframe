using System.Globalization;
using Npgsql;

namespace Coldframe.Migrations;

/// <summary>
/// What the migration job does to the ingestion tables besides migrating: it keeps the monthly partitions
/// of <c>readings</c> and <c>device_reports</c> ahead of the calendar (AD-22), and after a database restore
/// it advances every Device's replay window and downlink counter (AD-15, AD-17).
/// </summary>
public static class ReadingsMaintenance
{
    /// <summary>
    /// How many months after the current one get a partition unless the command says otherwise.
    /// </summary>
    public const int DefaultMonthsAhead = 3;

    /// <summary>
    /// The fewest months ahead the job accepts (AD-22).
    /// </summary>
    public const int MinMonthsAhead = 2;

    /// <summary>
    /// The most months ahead the job accepts: ten years.
    /// </summary>
    public const int MaxMonthsAhead = 120;

    /// <summary>
    /// How far a restore moves every uplink high-water mark: the size of the replay window.
    /// </summary>
    public const ulong DefaultUplinkMargin = 64;

    /// <summary>
    /// How far a restore moves every downlink counter: far above the acknowledgements one Node can receive
    /// between a backup and a restore.
    /// </summary>
    public const ulong DefaultDownlinkMargin = 1_048_576;

    /// <summary>
    /// The largest margin <c>advance-replay</c> accepts: 2^32. A larger one is a typing mistake; it would
    /// push the counters towards the end of their range, where every frame is a replay.
    /// </summary>
    public const ulong MaxMargin = 4_294_967_296;

    /// <summary>
    /// The advisory lock key that serializes maintenance runs ("cf-part" in ASCII).
    /// </summary>
    public const long MaintenanceLockKey = 0x63_66_2D_70_61_72_74;

    private const string LargestCounter = "18446744073709551615";

    /// <summary>
    /// The range-partitioned tables, each with a <c>{table}_default</c> partition.
    /// </summary>
    public static IReadOnlyList<string> PartitionedTables { get; } = ["readings", "device_reports"];

    /// <summary>
    /// The name of the partition of <paramref name="table"/> for a month, such as <c>readings_y2026m10</c>.
    /// </summary>
    public static string PartitionName(string table, int year, int month) =>
        string.Create(CultureInfo.InvariantCulture, $"{table}_y{year:D4}m{month:D2}");

    /// <summary>
    /// Creates, for both tables, the partition of the month of <paramref name="now"/> (UTC) and of the
    /// <paramref name="monthsAhead"/> months after it, and of every other month that has rows waiting in the
    /// default partition (a Reading from before the first partition, or of a month a run missed), unless it
    /// exists. Rows of a month waiting in the default partition move into its new partition in the
    /// transaction that creates it, so after a run the default partitions are empty.
    /// </summary>
    /// <returns>The number of partitions created.</returns>
    public static async Task<int> EnsurePartitionsAsync(
        string connectionString,
        DateTimeOffset now,
        int monthsAhead = DefaultMonthsAhead,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentOutOfRangeException.ThrowIfLessThan(monthsAhead, MinMonthsAhead);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(monthsAhead, MaxMonthsAhead);

        var utc = now.UtcDateTime;
        var first = new DateTime(utc.Year, utc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var created = 0;

        await using var connection = Open(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        foreach (var table in PartitionedTables)
        {
            var months = Enumerable.Range(0, monthsAhead + 1).Select(offset => first.AddMonths(offset)).ToList();
            months.AddRange(await WaitingMonthsAsync(connection, table, cancellationToken).ConfigureAwait(false));

            foreach (var month in months.Distinct())
            {
                if (await EnsurePartitionAsync(connection, table, month, cancellationToken).ConfigureAwait(false))
                {
                    created++;
                }
            }
        }

        return created;
    }

    /// <summary>
    /// After a database restore, with the apps stopped: moves every Device's uplink high-water mark up by
    /// <paramref name="uplinkMargin"/> with its window marked fully seen, and every downlink counter up by
    /// <paramref name="downlinkMargin"/>. An enrolled Device that never sent a frame gets the same state, so
    /// its first <paramref name="uplinkMargin"/> counters are refused too. One transaction: all or nothing.
    /// </summary>
    /// <returns>The number of Devices whose replay state changed.</returns>
    public static async Task<int> AdvanceReplayAsync(
        string connectionString,
        ulong uplinkMargin = DefaultUplinkMargin,
        ulong downlinkMargin = DefaultDownlinkMargin,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentOutOfRangeException.ThrowIfZero(uplinkMargin);
        ArgumentOutOfRangeException.ThrowIfZero(downlinkMargin);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(uplinkMargin, MaxMargin);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(downlinkMargin, MaxMargin);

        await using var connection = Open(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await TakeLockAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

        int advanced;

        // seen = -1 is the bitmap with all 64 bits set.
        await using (var update = new NpgsqlCommand(
            $"""
            UPDATE device_replay
            SET high_water = LEAST(high_water + @uplink, {LargestCounter}::numeric),
                seen = -1,
                downlink_counter = LEAST(downlink_counter + @downlink, {LargestCounter}::numeric)
            """,
            connection,
            transaction))
        {
            update.Parameters.AddWithValue("uplink", (decimal)uplinkMargin);
            update.Parameters.AddWithValue("downlink", (decimal)downlinkMargin);
            advanced = await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO device_replay (device_id, high_water, seen, downlink_counter)
            SELECT DISTINCT substr(stream_id, 8), @uplink - 1, -1, @downlink
            FROM journal_events
            WHERE type_alias = 'device.enrolled' AND stream_id LIKE 'device/%'
            ON CONFLICT (device_id) DO NOTHING
            """,
            connection,
            transaction))
        {
            insert.Parameters.AddWithValue("uplink", (decimal)uplinkMargin);
            insert.Parameters.AddWithValue("downlink", (decimal)downlinkMargin);
            advanced += await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return advanced;
    }

    // The months (UTC) that have rows in the default partition of a table.
    private static async Task<List<DateTime>> WaitingMonthsAsync(NpgsqlConnection connection, string table, CancellationToken cancellationToken)
    {
        var months = new List<DateTime>();

#pragma warning disable CA2100 // The statement is built from the fixed table names only.
        await using var command = new NpgsqlCommand(
            $"SELECT DISTINCT date_trunc('month', measured_at AT TIME ZONE 'UTC') FROM {table}_default ORDER BY 1",
            connection);
#pragma warning restore CA2100
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            months.Add(DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc));
        }

        return months;
    }

    // One transaction per partition: create it detached, move the month's rows out of the default
    // partition, attach it. Names and bounds are generated here, never taken from input.
    private static async Task<bool> EnsurePartitionAsync(
        NpgsqlConnection connection,
        string table,
        DateTime month,
        CancellationToken cancellationToken)
    {
        var name = PartitionName(table, month.Year, month.Month);
        var from = Bound(month);
        var to = Bound(month.AddMonths(1));

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await TakeLockAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

        await using (var exists = new NpgsqlCommand("SELECT to_regclass(@name) IS NOT NULL", connection, transaction))
        {
            exists.Parameters.AddWithValue("name", name);
            if ((bool)(await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!)
            {
                return false;
            }
        }

#pragma warning disable CA2100 // The statement is built from the fixed table names and generated month bounds only.
        await using (var create = new NpgsqlCommand(
            $"""
            LOCK TABLE {table}_default IN ACCESS EXCLUSIVE MODE;
            CREATE TABLE {name} (LIKE {table} INCLUDING DEFAULTS INCLUDING CONSTRAINTS);
            WITH moved AS (
                DELETE FROM {table}_default WHERE measured_at >= '{from}' AND measured_at < '{to}' RETURNING *
            )
            INSERT INTO {name} SELECT * FROM moved;
            ALTER TABLE {table} ATTACH PARTITION {name} FOR VALUES FROM ('{from}') TO ('{to}');
            """,
            connection,
            transaction))
#pragma warning restore CA2100
        {
            await create.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static async Task TakeLockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var takeLock = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@key)", connection, transaction);
        takeLock.Parameters.AddWithValue("key", MaintenanceLockKey);
        await takeLock.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    // One short-lived connection per run: a job that exits has no use for a pool.
    private static NpgsqlConnection Open(string connectionString) =>
        new(new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString);

    private static string Bound(DateTime month) => month.ToString("yyyy-MM-dd' 00:00:00+00'", CultureInfo.InvariantCulture);
}
