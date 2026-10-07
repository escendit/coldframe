using Npgsql;
using NpgsqlTypes;

namespace Coldframe.Server.Devices;

/// <summary>
/// A Device's replay state as <c>device_replay</c> holds it (AD-17).
/// </summary>
/// <param name="HighWater">The highest accepted frame counter.</param>
/// <param name="Seen">The 64-bit window bitmap below it.</param>
public sealed record StoredReplay(ulong HighWater, ulong Seen);

/// <summary>
/// One Reading of a frame, ready to store.
/// </summary>
/// <param name="SensorId">The Sensor ID (AD-19).</param>
/// <param name="ReadingSeq">The Reading's <c>reading_seq</c>.</param>
/// <param name="Slot">The Sensor slot.</param>
/// <param name="Quantity">The quantity token.</param>
/// <param name="RawValue">The raw value.</param>
/// <param name="CalibrationId">The Calibration in force for the Sensor when the Reading is stored, or <see langword="null"/> (Story 5.1).</param>
public sealed record ReadingWrite(Guid SensorId, ulong ReadingSeq, int Slot, string Quantity, long RawValue, Guid? CalibrationId = null);

/// <summary>
/// The device report of a frame, ready to store.
/// </summary>
/// <param name="ReportSeq">The report's <c>reading_seq</c>.</param>
/// <param name="BatteryPercent">The battery charge, when it was read.</param>
/// <param name="Charging">The charger status token.</param>
public sealed record DeviceReportWrite(ulong ReportSeq, short? BatteryPercent, string Charging);

/// <summary>
/// The rows of one frame: its Readings and its device report, which share the time (AD-9, AD-11).
/// </summary>
/// <param name="MeasuredAt">When the Readings were taken, rebased for an unsynced Node.</param>
/// <param name="ReceivedAt">When the Server received the frame.</param>
/// <param name="TimeUnsynced">Whether the Node's clock was not set.</param>
/// <param name="BootId">The boot counter of an unsynced measurement.</param>
/// <param name="UptimeMs">The uptime of an unsynced measurement.</param>
/// <param name="Readings">The Readings, distinct by Sensor and <c>reading_seq</c>.</param>
/// <param name="Report">The device report.</param>
public sealed record FrameRows(
    DateTimeOffset MeasuredAt,
    DateTimeOffset ReceivedAt,
    bool TimeUnsynced,
    ulong? BootId,
    ulong? UptimeMs,
    IReadOnlyList<ReadingWrite> Readings,
    DeviceReportWrite Report);

/// <summary>
/// What one frame commits.
/// </summary>
/// <param name="DeviceId">The Node.</param>
/// <param name="Replay">The replay window after this frame's counter.</param>
/// <param name="ReserveDownlink">Whether the frame is acknowledged, so a downlink counter is reserved.</param>
/// <param name="Rows">The rows to store, or <see langword="null"/> for a frame that stores none.</param>
public sealed record FrameCommit(string DeviceId, StoredReplay Replay, bool ReserveDownlink, FrameRows? Rows);

/// <summary>
/// What a commit did.
/// </summary>
/// <param name="NewKeys">How many Reading keys were new, the device report's included.</param>
/// <param name="DownlinkCounter">The reserved downlink counter, when one was asked for.</param>
public sealed record FrameCommitted(int NewKeys, ulong? DownlinkCounter);

/// <summary>
/// The ingestion tables (AD-9, AD-17): <c>readings</c>, <c>device_reports</c>, <c>reading_keys</c> and
/// <c>device_replay</c>. Only the Device grain calls it, so the grain stays the only writer (AD-1). It never
/// updates or deletes a Reading or a device report, and it runs no DDL (AD-22).
/// </summary>
public class DeviceIngestionStore(NpgsqlDataSource dataSource)
{
    private const string LoadReplaySql = "SELECT high_water, seen FROM device_replay WHERE device_id = @device_id";

    // The stored downlink counter is the next one to use: the first frame uses 0 and leaves 1. A stored
    // high-water mark above the one being written means the window was moved underneath the writer
    // (advance-replay, or another activation): no row comes back, and the commit fails.
    private const string SaveReplaySql =
        """
        INSERT INTO device_replay (device_id, high_water, seen, downlink_counter)
        VALUES (@device_id, @high_water, @seen, @reserve)
        ON CONFLICT (device_id) DO UPDATE
        SET high_water = EXCLUDED.high_water, seen = EXCLUDED.seen, downlink_counter = device_replay.downlink_counter + @reserve
        WHERE device_replay.high_water <= EXCLUDED.high_water
        RETURNING downlink_counter - @reserve
        """;

    // A Reading row is inserted only for a key that was new (exactly-once across partitions).
    private const string InsertReadingsSql =
        """
        WITH fresh AS (
            INSERT INTO reading_keys (device_id, sensor_id, reading_seq)
            SELECT @device_id, reading.sensor_id, reading.reading_seq
            FROM unnest(@sensor_ids, @reading_seqs) AS reading(sensor_id, reading_seq)
            ON CONFLICT DO NOTHING
            RETURNING sensor_id, reading_seq
        )
        INSERT INTO readings (
            device_id, sensor_id, reading_seq, measured_at, slot, quantity, raw_value, calibration_id,
            time_unsynced, boot_id, uptime_ms, received_at)
        SELECT @device_id, reading.sensor_id, reading.reading_seq, @measured_at, reading.slot, reading.quantity,
            reading.raw_value, reading.calibration_id, @time_unsynced, @boot_id, @uptime_ms, @received_at
        FROM unnest(@sensor_ids, @reading_seqs, @slots, @quantities, @raw_values, @calibration_ids)
            AS reading(sensor_id, reading_seq, slot, quantity, raw_value, calibration_id)
        JOIN fresh USING (sensor_id, reading_seq)
        """;

    private const string InsertReportSql =
        """
        WITH fresh AS (
            INSERT INTO reading_keys (device_id, sensor_id, reading_seq)
            VALUES (@device_id, @sensor_id, @reading_seq)
            ON CONFLICT DO NOTHING
            RETURNING reading_seq
        )
        INSERT INTO device_reports (
            device_id, reading_seq, measured_at, battery_percent, charging, time_unsynced, boot_id, uptime_ms, received_at)
        SELECT @device_id, reading_seq, @measured_at, @battery_percent, @charging, @time_unsynced, @boot_id, @uptime_ms, @received_at
        FROM fresh
        """;

    /// <summary>
    /// Reads a Device's stored replay state, or <see langword="null"/> before its first frame.
    /// </summary>
    public virtual async Task<StoredReplay?> LoadReplayAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(deviceId);

        await using var command = dataSource.CreateCommand(LoadReplaySql);
        command.Parameters.AddWithValue("device_id", deviceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new StoredReplay((ulong)reader.GetDecimal(0), unchecked((ulong)reader.GetInt64(1)))
            : null;
    }

    /// <summary>
    /// Commits one frame in one transaction: the replay window, the reserved downlink counter, and the rows
    /// whose keys are new. Throws when the transaction fails, or when the stored high-water mark is above
    /// the one being written; nothing is stored then.
    /// </summary>
    public virtual async Task<FrameCommitted> CommitAsync(FrameCommit commit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        ulong downlinkCounter;
        await using (var replay = new NpgsqlCommand(SaveReplaySql, connection, transaction))
        {
            replay.Parameters.AddWithValue("device_id", commit.DeviceId);
            replay.Parameters.AddWithValue("high_water", (decimal)commit.Replay.HighWater);
            replay.Parameters.AddWithValue("seen", unchecked((long)commit.Replay.Seen));
            replay.Parameters.Add(new NpgsqlParameter("reserve", NpgsqlDbType.Numeric) { Value = commit.ReserveDownlink ? 1m : 0m });
            if (await replay.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not decimal used)
            {
                // Disposing the transaction rolls it back: nothing of the frame is stored.
                throw new InvalidOperationException(
                    $"The stored replay window of Device {commit.DeviceId} is ahead of the one being written; the frame is retried.");
            }

            downlinkCounter = (ulong)used;
        }

        var newKeys = 0;
        if (commit.Rows is { } rows)
        {
            if (rows.Readings.Count > 0)
            {
                await using var readings = new NpgsqlCommand(InsertReadingsSql, connection, transaction);
                AddShared(readings, commit.DeviceId, rows);
                readings.Parameters.AddWithValue("sensor_ids", rows.Readings.Select(reading => reading.SensorId).ToArray());
                readings.Parameters.AddWithValue("reading_seqs", rows.Readings.Select(reading => (decimal)reading.ReadingSeq).ToArray());
                readings.Parameters.AddWithValue("slots", rows.Readings.Select(reading => reading.Slot).ToArray());
                readings.Parameters.Add(new NpgsqlParameter("quantities", NpgsqlDbType.Array | NpgsqlDbType.Text)
                {
                    Value = rows.Readings.Select(reading => reading.Quantity).ToArray(),
                });
                readings.Parameters.AddWithValue("raw_values", rows.Readings.Select(reading => reading.RawValue).ToArray());
                readings.Parameters.Add(new NpgsqlParameter("calibration_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
                {
                    Value = rows.Readings.Select(reading => reading.CalibrationId).ToArray(),
                });
                newKeys += await readings.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using var report = new NpgsqlCommand(InsertReportSql, connection, transaction);
            AddShared(report, commit.DeviceId, rows);
            report.Parameters.AddWithValue("sensor_id", Coldframe.Crypto.SensorIds.DeviceReport);
            report.Parameters.AddWithValue("reading_seq", (decimal)rows.Report.ReportSeq);
            report.Parameters.Add(new NpgsqlParameter("battery_percent", NpgsqlDbType.Smallint) { Value = (object?)rows.Report.BatteryPercent ?? DBNull.Value });
            report.Parameters.AddWithValue("charging", rows.Report.Charging);
            newKeys += await report.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await CommitTransactionAsync(transaction, cancellationToken).ConfigureAwait(false);

        return new FrameCommitted(newKeys, commit.ReserveDownlink ? downlinkCounter : null);
    }

    /// <summary>
    /// Commits the transaction of a frame. The one step a test replaces to fail a commit.
    /// </summary>
    protected virtual Task CommitTransactionAsync(NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        return transaction.CommitAsync(cancellationToken);
    }

    private static void AddShared(NpgsqlCommand command, string deviceId, FrameRows rows)
    {
        command.Parameters.AddWithValue("device_id", deviceId);
        command.Parameters.AddWithValue("measured_at", rows.MeasuredAt.ToUniversalTime());
        command.Parameters.AddWithValue("received_at", rows.ReceivedAt.ToUniversalTime());
        command.Parameters.AddWithValue("time_unsynced", rows.TimeUnsynced);
        command.Parameters.Add(new NpgsqlParameter("boot_id", NpgsqlDbType.Numeric) { Value = rows.BootId is { } bootId ? (decimal)bootId : DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("uptime_ms", NpgsqlDbType.Numeric) { Value = rows.UptimeMs is { } uptimeMs ? (decimal)uptimeMs : DBNull.Value });
    }
}
