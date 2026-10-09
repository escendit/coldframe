using Coldframe.Contracts.Alerts;
using Coldframe.Server.Journal;
using Npgsql;
using NpgsqlTypes;

namespace Coldframe.Server.Alerts;

/// <summary>
/// Projects the Alert streams into <c>alerts</c>, the read model of the Alerts list (Story 6.2). It is the only
/// writer of that table (AD-21). <c>alert.opened</c> creates the row and <c>alert.closed</c> sets its close; a
/// closed Alert keeps its row, so an event applied again changes nothing and a rebuild from position 0 gives the
/// same rows. <c>alert.site-notified</c> is not projected.
/// </summary>
public sealed class AlertsProjector : IProjector
{
    /// <summary>
    /// The projector's stable name; its checkpoint is stored under it.
    /// </summary>
    public const string ProjectorName = "alerts";

    private const string AlertStreamPrefix = "alert/";

    // The first event of an Alert stream. An open applied again, also after its close, keeps the row as it is.
    private const string OpenSql =
        """
        INSERT INTO alerts (alert_id, site_id, lot_id, device_id, sensor_id, kind, side, quantity, opened_at)
        VALUES (@alert_id, @site_id, @lot_id, @device_id, @sensor_id, @kind, @side, @quantity, @opened_at)
        ON CONFLICT (alert_id) DO NOTHING
        """;

    // Final: a second close changes nothing.
    private const string CloseSql =
        """
        UPDATE alerts SET closed_at = @closed_at, reason = @reason
        WHERE alert_id = @alert_id AND closed_at IS NULL
        """;

    /// <inheritdoc />
    public string Name => ProjectorName;

    /// <summary>
    /// Returns the Alert ID of an Alert stream (<c>alert/{id}</c>), or <see langword="null"/> for any other stream.
    /// </summary>
    /// <param name="streamId">The stream ID.</param>
    public static Guid? AlertIdOf(string streamId)
    {
        ArgumentNullException.ThrowIfNull(streamId);

        return streamId.StartsWith(AlertStreamPrefix, StringComparison.Ordinal)
            && Guid.TryParseExact(streamId.AsSpan(AlertStreamPrefix.Length), "D", out var alertId)
                ? alertId
                : null;
    }

    /// <inheritdoc />
    public async Task ApplyAsync(JournalEvent journalEvent, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(journalEvent);
        ArgumentNullException.ThrowIfNull(transaction);

        if (AlertIdOf(journalEvent.StreamId) is not { } alertId)
        {
            return;
        }

        switch (journalEvent.Data)
        {
            case AlertOpened opened:
                await using (var command = new NpgsqlCommand(OpenSql, transaction.Connection, transaction))
                {
                    command.Parameters.AddWithValue("alert_id", alertId);
                    command.Parameters.AddWithValue("site_id", opened.SiteId);
                    command.Parameters.AddWithValue("lot_id", opened.LotId);
                    command.Parameters.AddWithValue("device_id", opened.DeviceId);
                    command.Parameters.AddWithValue("sensor_id", opened.SensorId);
                    command.Parameters.AddWithValue("kind", AlertNames.Kind(opened.Kind));
                    command.Parameters.AddWithValue("side", NpgsqlDbType.Text, (object?)AlertNames.Side(opened.Side) ?? DBNull.Value);
                    command.Parameters.AddWithValue("quantity", opened.Quantity);
                    command.Parameters.AddWithValue("opened_at", opened.OpenedAt.ToUniversalTime());
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                break;
            case AlertClosed closed:
                await using (var command = new NpgsqlCommand(CloseSql, transaction.Connection, transaction))
                {
                    command.Parameters.AddWithValue("alert_id", alertId);
                    command.Parameters.AddWithValue("closed_at", closed.ClosedAt.ToUniversalTime());
                    command.Parameters.AddWithValue("reason", AlertNames.Reason(closed.Reason));
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                break;
            default:
                break;
        }
    }
}
