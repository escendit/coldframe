using Npgsql;

namespace Coldframe.Server.Lots;

/// <summary>
/// A Lot as the lots projection holds it.
/// </summary>
/// <param name="LotId">The Lot ID.</param>
/// <param name="Name">The Lot name.</param>
/// <param name="Status">The Server's status of the Lot (AD-14), a <c>LotStatus</c> value of the contract.</param>
/// <param name="Removed">Whether the Lot was removed.</param>
public sealed record LotView(string LotId, string Name, string Status, bool Removed);

/// <summary>
/// Reads the lots projection. Edge API handlers read Lots only from here; grains never read it.
/// </summary>
public sealed class LotsReadModel(NpgsqlDataSource dataSource)
{
    /// <summary>
    /// The AD-14 status order: what needs attention first, a Lot without a Node last.
    /// </summary>
    public static readonly IReadOnlyList<string> StatusOrder = ["needsWater", "needsCalibration", "unknown", "ok", "paused", "noNode"];

    private const string ListSql =
        """
        SELECT lot_id, name, status
        FROM lots
        WHERE site_id = @site_id AND removed_at IS NULL
        ORDER BY array_position(@status_order, status), created_at, lot_id
        """;

    private const string FindSql =
        """
        SELECT name, status, removed_at IS NOT NULL
        FROM lots
        WHERE site_id = @site_id AND lot_id = @lot_id
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
            lots.Add(new LotView(reader.GetString(0), reader.GetString(1), reader.GetString(2), Removed: false));
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
            ? new LotView(lotId, reader.GetString(0), reader.GetString(1), reader.GetBoolean(2))
            : null;
    }
}
