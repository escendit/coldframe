using System.Buffers.Text;
using System.Globalization;
using System.Text;
using Coldframe.Server.Sensors;
using Npgsql;

namespace Coldframe.Server.Lots;

/// <summary>
/// The Node a Lot holds and since when, as the lots projection records it.
/// </summary>
/// <param name="NodeId">The Node's Device ID.</param>
/// <param name="ClaimedAt">When the Node took the Lot; the Readings that count start here.</param>
public sealed record LotClaim(string NodeId, DateTimeOffset ClaimedAt);

/// <summary>
/// The newest Reading of one Sensor of a Node, as stored (not yet converted).
/// </summary>
/// <param name="Slot">The Sensor slot.</param>
/// <param name="Quantity">The quantity token: <c>soil_moisture</c>, <c>air_temperature</c>, <c>relative_humidity</c> or <c>gas_resistance</c>.</param>
/// <param name="RawValue">The stored value: raw count, milli-°C, milli-% or Ω.</param>
/// <param name="MeasuredAt">When the Reading was taken.</param>
/// <param name="Calibration">The points of the Calibration the Reading was stored with, or <see langword="null"/> without one.</param>
/// <param name="SensorId">The Sensor ID the Reading carries.</param>
public sealed record StoredReading(int Slot, string Quantity, long RawValue, DateTimeOffset MeasuredAt, CalibrationPoints? Calibration = null, Guid? SensorId = null);

/// <summary>
/// A Node's newest device report.
/// </summary>
/// <param name="BatteryPercent">The battery charge, or <see langword="null"/> when it was not read.</param>
/// <param name="Charging">The stored charger token: <c>charging</c>, <c>not_charging</c> or <c>unknown</c>.</param>
/// <param name="MeasuredAt">When the report was taken.</param>
public sealed record StoredReport(short? BatteryPercent, string Charging, DateTimeOffset MeasuredAt);

/// <summary>
/// One UTC day of a Lot's history in stored units.
/// </summary>
/// <param name="Day">The UTC date.</param>
/// <param name="Low">The day's lowest stored value.</param>
/// <param name="High">The day's highest stored value.</param>
/// <param name="ReadingCount">How many Readings the day has.</param>
/// <param name="CalibratedLow">The day's lowest percentage among its Readings stored with a Calibration, or <see langword="null"/> without one.</param>
/// <param name="CalibratedHigh">The day's highest percentage among those Readings, or <see langword="null"/> without one.</param>
/// <param name="CalibratedCount">How many of the day's Readings were stored with a Calibration.</param>
public sealed record StoredDay(DateOnly Day, long Low, long High, int ReadingCount, long? CalibratedLow = null, long? CalibratedHigh = null, int CalibratedCount = 0);

/// <summary>
/// Reads what Lot detail adds to a Lot (Story 4.8): the newest Reading per Sensor, the Node's newest
/// device report and the daily history. It queries <c>readings</c> and <c>device_reports</c> directly; both
/// are append-only and never deleted (FR8).
/// </summary>
public sealed class LotDetailReadModel(NpgsqlDataSource dataSource)
{
    private const string ClaimSql = "SELECT claimed_by, claimed_at FROM lots WHERE site_id = @site_id AND lot_id = @lot_id AND claimed_by IS NOT NULL AND claimed_at IS NOT NULL";

    private const string LatestSql =
        """
        SELECT DISTINCT ON (r.slot, r.quantity) r.slot, r.quantity, r.raw_value, r.measured_at, c.dry_raw, c.wet_raw, r.sensor_id
        FROM readings r
        LEFT JOIN calibrations c ON c.calibration_id = r.calibration_id
        WHERE r.device_id = @device_id AND r.measured_at >= @since
        ORDER BY r.slot, r.quantity, r.measured_at DESC
        """;

    private const string ReportSql =
        """
        SELECT battery_percent, charging, measured_at
        FROM device_reports
        WHERE device_id = @device_id
        ORDER BY measured_at DESC
        LIMIT 1
        """;

    // The percentage of a Reading is derived here exactly as CalibrationMath.Percent does: linear between the points of
    // the Calibration it was stored with, clamped to 0-100, rounded to the nearest 5 (numeric rounds a half away from zero).
    private const string HistorySql =
        """
        WITH readings_of_day AS (
            SELECT (r.measured_at AT TIME ZONE 'UTC')::date AS day, r.raw_value,
                   CASE WHEN c.dry_raw IS NULL THEN NULL
                        ELSE round(LEAST(100, GREATEST(0, 100.0 * (r.raw_value - c.dry_raw) / (c.wet_raw - c.dry_raw))) / 5) * 5 END AS percent
            FROM readings r
            LEFT JOIN calibrations c ON c.calibration_id = r.calibration_id
            WHERE r.device_id = @device_id AND r.quantity = @quantity
              AND r.measured_at >= @from AND r.measured_at <= @to
              AND (@after::date IS NULL OR (r.measured_at AT TIME ZONE 'UTC')::date > @after::date))
        SELECT day, min(raw_value), max(raw_value), count(*), min(percent), max(percent), count(percent)
        FROM readings_of_day
        GROUP BY day
        ORDER BY day
        LIMIT @take
        """;

    private const string HasCalibratedSql =
        """
        SELECT EXISTS (
            SELECT 1
            FROM readings r
            JOIN calibrations c ON c.calibration_id = r.calibration_id
            WHERE r.device_id = @device_id AND r.quantity = @quantity
              AND r.measured_at >= @from AND r.measured_at <= @to)
        """;

    /// <summary>
    /// Returns the Node the Lot holds and when it took the Lot, or <see langword="null"/> without a Node.
    /// </summary>
    /// <param name="siteId">The Site ID, in its canonical form.</param>
    /// <param name="lotId">The Lot ID, in its canonical form.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<LotClaim?> FindClaimAsync(string siteId, string lotId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(siteId);
        ArgumentNullException.ThrowIfNull(lotId);

        await using var command = dataSource.CreateCommand(ClaimSql);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("lot_id", lotId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new LotClaim(reader.GetString(0), reader.GetFieldValue<DateTimeOffset>(1))
            : null;
    }

    /// <summary>
    /// Returns the newest Reading of every <c>(slot, quantity)</c> of the Node since <paramref name="since"/>,
    /// by slot then quantity.
    /// </summary>
    /// <param name="nodeId">The Node's Device ID.</param>
    /// <param name="since">When the Node took the Lot.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<IReadOnlyList<StoredReading>> LatestReadingsAsync(string nodeId, DateTimeOffset since, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodeId);

        await using var command = dataSource.CreateCommand(LatestSql);
        command.Parameters.AddWithValue("device_id", nodeId);
        command.Parameters.AddWithValue("since", since.ToUniversalTime());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var readings = new List<StoredReading>();

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            // A Reading stored with a Calibration whose points are not projected yet reads as raw until they are.
            var calibration = await reader.IsDBNullAsync(4, cancellationToken).ConfigureAwait(false)
                ? null
                : new CalibrationPoints(reader.GetInt64(4), reader.GetInt64(5));

            readings.Add(new StoredReading(reader.GetInt32(0), reader.GetString(1), reader.GetInt64(2), reader.GetFieldValue<DateTimeOffset>(3), calibration, reader.GetGuid(6)));
        }

        return readings;
    }

    /// <summary>
    /// Returns the Node's newest device report, or <see langword="null"/> before its first.
    /// </summary>
    /// <param name="nodeId">The Node's Device ID.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<StoredReport?> LatestReportAsync(string nodeId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodeId);

        await using var command = dataSource.CreateCommand(ReportSql);
        command.Parameters.AddWithValue("device_id", nodeId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new StoredReport(
                await reader.IsDBNullAsync(0, cancellationToken).ConfigureAwait(false) ? null : reader.GetInt16(0),
                reader.GetString(1),
                reader.GetFieldValue<DateTimeOffset>(2))
            : null;
    }

    /// <summary>
    /// Returns up to <paramref name="limit"/> UTC days with Readings of one quantity, ascending, and whether more follow.
    /// Every day carries the percentages of its Readings that were stored with a Calibration (Story 5.4); a day is
    /// raw (<c>Low</c> and <c>High</c>) and calibrated at once, and the caller decides which one it shows.
    /// </summary>
    /// <param name="nodeId">The Node's Device ID.</param>
    /// <param name="quantity">The quantity token.</param>
    /// <param name="from">The start of the window, already no earlier than the claim.</param>
    /// <param name="to">The end of the window.</param>
    /// <param name="after">Only days after this one, or <see langword="null"/> for the first page.</param>
    /// <param name="limit">The most days to return.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<(IReadOnlyList<StoredDay> Days, bool HasMore)> HistoryAsync(
        string nodeId,
        string quantity,
        DateTimeOffset from,
        DateTimeOffset to,
        DateOnly? after,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodeId);
        ArgumentNullException.ThrowIfNull(quantity);

        await using var command = dataSource.CreateCommand(HistorySql);
        command.Parameters.AddWithValue("device_id", nodeId);
        command.Parameters.AddWithValue("quantity", quantity);
        command.Parameters.AddWithValue("from", from.ToUniversalTime());
        command.Parameters.AddWithValue("to", to.ToUniversalTime());
        command.Parameters.Add(new NpgsqlParameter("after", NpgsqlTypes.NpgsqlDbType.Text) { Value = after is { } day ? day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : DBNull.Value });
        command.Parameters.AddWithValue("take", limit + 1);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var days = new List<StoredDay>();

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var calibrated = reader.GetInt64(6) > 0;

            days.Add(new StoredDay(
                reader.GetFieldValue<DateOnly>(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                (int)reader.GetInt64(3),
                calibrated ? (long)reader.GetDecimal(4) : null,
                calibrated ? (long)reader.GetDecimal(5) : null,
                (int)reader.GetInt64(6)));
        }

        return days.Count > limit ? (days[..limit], true) : (days, false);
    }

    /// <summary>
    /// Whether any Reading of the window was stored with a Calibration. History asks it once for the whole
    /// window, so every page of a window agrees on its unit.
    /// </summary>
    /// <param name="nodeId">The Node the Lot holds.</param>
    /// <param name="quantity">The Quantity.</param>
    /// <param name="from">The start of the window.</param>
    /// <param name="to">The end of the window.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<bool> HasCalibratedAsync(string nodeId, string quantity, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodeId);
        ArgumentNullException.ThrowIfNull(quantity);

        await using var command = dataSource.CreateCommand(HasCalibratedSql);
        command.Parameters.AddWithValue("device_id", nodeId);
        command.Parameters.AddWithValue("quantity", quantity);
        command.Parameters.AddWithValue("from", from.ToUniversalTime());
        command.Parameters.AddWithValue("to", to.ToUniversalTime());

        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is true;
    }

    /// <summary>
    /// Writes a history cursor: the last day of the page, opaque to clients.
    /// </summary>
    /// <param name="lastDay">The last day of the page just returned.</param>
    public static string EncodeCursor(DateOnly lastDay) =>
        Base64Url.EncodeToString(Encoding.ASCII.GetBytes("d1:" + lastDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));

    /// <summary>
    /// Reads a history cursor, or returns <see langword="null"/> when it is not one this Server wrote.
    /// </summary>
    /// <param name="cursor">The cursor as a client sent it.</param>
    public static DateOnly? DecodeCursor(string cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);

        try
        {
            var text = Encoding.ASCII.GetString(Base64Url.DecodeFromChars(cursor));

            return text.StartsWith("d1:", StringComparison.Ordinal)
                && DateOnly.TryParseExact(text[3..], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                    ? day
                    : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
