using Npgsql;

namespace Coldframe.Server.Devices;

/// <summary>
/// A Device as the devices projection holds it.
/// </summary>
/// <param name="DeviceId">The Device ID.</param>
/// <param name="Kind">The contract's <c>DeviceKind</c>: <c>hub</c> or <c>node</c>.</param>
/// <param name="LotId">The Lot a Node is on, or <see langword="null"/>.</param>
/// <param name="LastSeenAt">When the Server accepted the last heartbeat, or <see langword="null"/> before the first.</param>
/// <param name="LotName">The name of the Lot a Node is on, or <see langword="null"/>.</param>
/// <param name="BatteryPercent">A Node's battery charge in its newest device report, or <see langword="null"/> when unknown.</param>
/// <param name="Charging">A Node's stored charger token in its newest device report (<c>charging</c>, <c>not_charging</c>, <c>unknown</c>), or <see langword="null"/> without a report.</param>
/// <param name="ReportedAt">When a Node's newest device report was taken, or <see langword="null"/> without one.</param>
public sealed record DeviceView(
    string DeviceId,
    string Kind,
    string? LotId,
    DateTimeOffset? LastSeenAt,
    string? LotName = null,
    short? BatteryPercent = null,
    string? Charging = null,
    DateTimeOffset? ReportedAt = null);

/// <summary>
/// Reads the devices projection. Edge API handlers read Devices only from here; grains never read it.
/// </summary>
public sealed class DevicesReadModel(NpgsqlDataSource dataSource)
{
    private const string ListSql =
        """
        SELECT d.device_id, d.kind, d.lot_id, d.last_seen_at, l.name, r.battery_percent, r.charging, r.measured_at
        FROM devices d
        LEFT JOIN lots l ON l.site_id = d.site_id AND l.lot_id = d.lot_id
        LEFT JOIN LATERAL (
            SELECT battery_percent, charging, measured_at
            FROM device_reports
            WHERE device_id = d.device_id AND d.kind = 'node'
            ORDER BY measured_at DESC
            LIMIT 1) r ON true
        WHERE d.site_id = @site_id
        ORDER BY (d.kind = 'node'), lower(l.name) COLLATE "C" NULLS LAST, l.name COLLATE "C", d.device_id
        """;

    /// <summary>
    /// Returns every enrolled Device of the Site: Hubs by Device ID, then Nodes by Lot name (unassigned last) and Device ID.
    /// </summary>
    /// <param name="siteId">The Site ID, in its canonical form.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<IReadOnlyList<DeviceView>> ListDevicesAsync(string siteId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(siteId);

        await using var command = dataSource.CreateCommand(ListSql);
        command.Parameters.AddWithValue("site_id", siteId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var devices = new List<DeviceView>();

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            devices.Add(new DeviceView(
                reader.GetString(0),
                reader.GetString(1),
                await reader.IsDBNullAsync(2, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(2),
                await reader.IsDBNullAsync(3, cancellationToken).ConfigureAwait(false) ? null : reader.GetFieldValue<DateTimeOffset>(3),
                await reader.IsDBNullAsync(4, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(4),
                await reader.IsDBNullAsync(5, cancellationToken).ConfigureAwait(false) ? null : reader.GetInt16(5),
                await reader.IsDBNullAsync(6, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(6),
                await reader.IsDBNullAsync(7, cancellationToken).ConfigureAwait(false) ? null : reader.GetFieldValue<DateTimeOffset>(7)));
        }

        return devices;
    }
}
