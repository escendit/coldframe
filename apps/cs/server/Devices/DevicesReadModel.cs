using Npgsql;

namespace Coldframe.Server.Devices;

/// <summary>
/// A Device as the devices projection holds it.
/// </summary>
/// <param name="DeviceId">The Device ID.</param>
/// <param name="Kind">The contract's <c>DeviceKind</c>: <c>hub</c> or <c>node</c>.</param>
/// <param name="LotId">The Lot a Node is on, or <see langword="null"/>.</param>
/// <param name="LastSeenAt">When the Server accepted the last heartbeat, or <see langword="null"/> before the first.</param>
public sealed record DeviceView(string DeviceId, string Kind, string? LotId, DateTimeOffset? LastSeenAt);

/// <summary>
/// Reads the devices projection. Edge API handlers read Devices only from here; grains never read it.
/// </summary>
public sealed class DevicesReadModel(NpgsqlDataSource dataSource)
{
    private const string ListSql =
        """
        SELECT device_id, kind, lot_id, last_seen_at
        FROM devices
        WHERE site_id = @site_id
        ORDER BY device_id
        """;

    /// <summary>
    /// Returns every enrolled Device of the Site, Hubs and Nodes, ordered by Device ID.
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
                await reader.IsDBNullAsync(3, cancellationToken).ConfigureAwait(false) ? null : reader.GetFieldValue<DateTimeOffset>(3)));
        }

        return devices;
    }
}
