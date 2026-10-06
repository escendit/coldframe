using System.Buffers.Text;
using System.Globalization;
using System.Text;
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
public sealed record StoredReading(int Slot, string Quantity, long RawValue, DateTimeOffset MeasuredAt);

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
public sealed record StoredDay(DateOnly Day, long Low, long High, int ReadingCount);

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
        SELECT DISTINCT ON (slot, quantity) slot, quantity, raw_value, measured_at
        FROM readings
        WHERE device_id = @device_id AND measured_at >= @since
        ORDER BY slot, quantity, measured_at DESC
        """;

    private const string ReportSql =
        """
        SELECT battery_percent, charging, measured_at
        FROM device_reports
        WHERE device_id = @device_id
        ORDER BY measured_at DESC
        LIMIT 1
        """;

    private const string HistorySql =
        """
        SELECT (measured_at AT TIME ZONE 'UTC')::date AS day, min(raw_value), max(raw_value), count(*)
        FROM readings
        WHERE device_id = @device_id AND quantity = @quantity
          AND measured_at >= @from AND measured_at <= @to
          AND (@after::date IS NULL OR (measured_at AT TIME ZONE 'UTC')::date > @after::date)
        GROUP BY 1
        ORDER BY 1
        LIMIT @take
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
            readings.Add(new StoredReading(reader.GetInt32(0), reader.GetString(1), reader.GetInt64(2), reader.GetFieldValue<DateTimeOffset>(3)));
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
            days.Add(new StoredDay(reader.GetFieldValue<DateOnly>(0), reader.GetInt64(1), reader.GetInt64(2), (int)reader.GetInt64(3)));
        }

        return days.Count > limit ? (days[..limit], true) : (days, false);
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
