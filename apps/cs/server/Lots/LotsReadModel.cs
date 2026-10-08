using Coldframe.Server.Sensors;
using Npgsql;

namespace Coldframe.Server.Lots;

/// <summary>
/// A Lot as the lots projection holds it.
/// </summary>
/// <param name="LotId">The Lot ID.</param>
/// <param name="Name">The Lot name.</param>
/// <param name="Status">The Server's status of the Lot (AD-14), a <c>LotStatus</c> value of the contract.</param>
/// <param name="StatusSince">When the Lot got that status.</param>
/// <param name="Removed">Whether the Lot was removed.</param>
/// <param name="LastReadingAt">
/// The newest <c>measured_at</c> of the Readings of the Lot's Node since it claimed the Lot, or
/// <see langword="null"/> without a Node or before its first Reading.
/// </param>
/// <param name="UnknownCause"><c>node</c> or <c>hub</c> for an <c>unknown</c> Lot; otherwise <see langword="null"/>.</param>
/// <param name="PausedBy">The Pause sources of a <c>paused</c> Lot, <c>device</c> before <c>site</c>; otherwise <see langword="null"/>.</param>
/// <param name="PausedUntil">When the Pause of a <c>paused</c> Lot ends, or <see langword="null"/> when it has no end.</param>
/// <param name="MoisturePercent">
/// The soil moisture of the newest soil-moisture Reading since the Lot took its Node, in percent of the Calibration
/// that Reading was stored with; <see langword="null"/> when there is none or it was stored without a Calibration.
/// </param>
/// <param name="SoilSensorId">The Sensor ID of that Reading, which names the Sensor whose low Threshold the Lot shows.</param>
public sealed record LotView(
    string LotId,
    string Name,
    string Status,
    DateTimeOffset StatusSince,
    bool Removed,
    DateTimeOffset? LastReadingAt = null,
    string? UnknownCause = null,
    IReadOnlyList<string>? PausedBy = null,
    DateTimeOffset? PausedUntil = null,
    int? MoisturePercent = null,
    Guid? SoilSensorId = null);

/// <summary>
/// Reads the lots projection. Edge API handlers read Lots only from here; grains never read it.
/// </summary>
/// <remarks>
/// <c>lastReadingAt</c> is not projected: both queries read it from <c>readings</c> through
/// <c>ix_readings_device_id_measured_at</c>, so it is the newest stored Reading at the moment of the answer.
/// </remarks>
public sealed class LotsReadModel(NpgsqlDataSource dataSource)
{
    /// <summary>
    /// The AD-14 status order: what needs attention first, a Lot without a Node last.
    /// </summary>
    public static readonly IReadOnlyList<string> StatusOrder = ["needsWater", "needsCalibration", "unknown", "ok", "paused", "noNode"];

    private const string ListSql =
        """
        SELECT l.lot_id, l.name, l.removed_at IS NOT NULL, l.status, l.status_since, l.unknown_cause, l.paused_by, l.paused_until,
               (SELECT max(r.measured_at) FROM readings r WHERE r.device_id = l.claimed_by AND r.measured_at >= l.claimed_at),
               s.sensor_id, s.raw_value, s.dry_raw, s.wet_raw
        FROM lots l
        LEFT JOIN LATERAL (
            SELECT r.sensor_id, r.raw_value, c.dry_raw, c.wet_raw
            FROM readings r
            LEFT JOIN calibrations c ON c.calibration_id = r.calibration_id
            WHERE r.device_id = l.claimed_by AND r.quantity = 'soil_moisture' AND r.measured_at >= l.claimed_at
            ORDER BY r.measured_at DESC
            LIMIT 1) s ON true
        WHERE l.site_id = @site_id AND l.removed_at IS NULL
        ORDER BY array_position(@status_order, l.status), l.created_at, l.lot_id
        """;

    private const string FindSql =
        """
        SELECT l.lot_id, l.name, l.removed_at IS NOT NULL, l.status, l.status_since, l.unknown_cause, l.paused_by, l.paused_until,
               (SELECT max(r.measured_at) FROM readings r WHERE r.device_id = l.claimed_by AND r.measured_at >= l.claimed_at),
               s.sensor_id, s.raw_value, s.dry_raw, s.wet_raw
        FROM lots l
        LEFT JOIN LATERAL (
            SELECT r.sensor_id, r.raw_value, c.dry_raw, c.wet_raw
            FROM readings r
            LEFT JOIN calibrations c ON c.calibration_id = r.calibration_id
            WHERE r.device_id = l.claimed_by AND r.quantity = 'soil_moisture' AND r.measured_at >= l.claimed_at
            ORDER BY r.measured_at DESC
            LIMIT 1) s ON true
        WHERE l.site_id = @site_id AND l.lot_id = @lot_id
        """;

    /// <summary>
    /// Returns the Site's live Lots in the Server's order: status (AD-14), then creation time, then Lot ID.
    /// Clients show them in this order and never re-sort (UX-DR20).
    /// </summary>
    /// <param name="siteId">The Site ID, in its canonical form.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<IReadOnlyList<LotView>> ListLotsAsync(string siteId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(siteId);

        await using var command = dataSource.CreateCommand(ListSql);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("status_order", StatusOrder.ToArray());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var lots = new List<LotView>();

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            lots.Add(await ReadAsync(reader, cancellationToken).ConfigureAwait(false));
        }

        return lots;
    }

    /// <summary>
    /// Returns the Lot of the Site, removed or not, or <see langword="null"/> when the Site has no such Lot.
    /// </summary>
    /// <param name="siteId">The Site ID, in its canonical form.</param>
    /// <param name="lotId">The Lot ID, in its canonical form.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<LotView?> FindLotAsync(string siteId, string lotId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(siteId);
        ArgumentNullException.ThrowIfNull(lotId);

        await using var command = dataSource.CreateCommand(FindSql);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("lot_id", lotId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? await ReadAsync(reader, cancellationToken).ConfigureAwait(false)
            : null;
    }

    private static async Task<LotView> ReadAsync(NpgsqlDataReader reader, CancellationToken cancellationToken)
    {
        // The newest soil-moisture Reading is a percentage only when it was stored with a Calibration whose points are projected.
        var calibrated = !await reader.IsDBNullAsync(11, cancellationToken).ConfigureAwait(false);

        return new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(3),
            reader.GetFieldValue<DateTimeOffset>(4),
            reader.GetBoolean(2),
            await reader.IsDBNullAsync(8, cancellationToken).ConfigureAwait(false) ? null : reader.GetFieldValue<DateTimeOffset>(8),
            await reader.IsDBNullAsync(5, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(5),
            await reader.IsDBNullAsync(6, cancellationToken).ConfigureAwait(false) ? null : reader.GetFieldValue<string[]>(6),
            await reader.IsDBNullAsync(7, cancellationToken).ConfigureAwait(false) ? null : reader.GetFieldValue<DateTimeOffset>(7),
            calibrated ? CalibrationMath.Percent(reader.GetInt64(10), reader.GetInt64(11), reader.GetInt64(12)) : null,
            await reader.IsDBNullAsync(9, cancellationToken).ConfigureAwait(false) ? null : reader.GetGuid(9));
    }
}
