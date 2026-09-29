using Coldframe.Contracts.Lots;
using Coldframe.Server.Journal;
using Npgsql;

namespace Coldframe.Server.Lots;

/// <summary>
/// Projects Lots into <c>lots</c>, the read model of Garden and Site settings. It is the only writer of that
/// table. The status is the Server's (AD-14): <c>noNode</c> while no Node occupies the Lot, <c>unknown</c>
/// once one does and no Reading has been judged yet. Removal sets <c>removed_at</c> and keeps the row.
/// </summary>
public sealed class LotsProjector : IProjector
{
    /// <summary>
    /// The projector's stable name; its checkpoint is stored under it.
    /// </summary>
    public const string ProjectorName = "lots";

    /// <summary>
    /// The status of a Lot without a Node.
    /// </summary>
    public const string NoNodeStatus = "noNode";

    /// <summary>
    /// The status of a Lot with a Node and no judged Reading.
    /// </summary>
    public const string UnknownStatus = "unknown";

    private const string LotStreamPrefix = "lot/";

    private const string CreateSql =
        """
        INSERT INTO lots (lot_id, site_id, name, status, claimed_by, created_at, removed_at)
        VALUES (@lot_id, @site_id, @name, @status, NULL, @created_at, NULL)
        ON CONFLICT (lot_id) DO NOTHING
        """;

    private const string RenameSql = "UPDATE lots SET name = @name WHERE lot_id = @lot_id";

    private const string ClaimSql = "UPDATE lots SET claimed_by = @node_id, status = @status WHERE lot_id = @lot_id";

    private const string ReleaseSql =
        "UPDATE lots SET claimed_by = NULL, status = @status WHERE lot_id = @lot_id AND claimed_by = @node_id";

    private const string RemoveSql = "UPDATE lots SET removed_at = @removed_at WHERE lot_id = @lot_id AND removed_at IS NULL";

    /// <inheritdoc />
    public string Name => ProjectorName;

    /// <summary>
    /// Returns the Lot ID of a Lot stream (<c>lot/{id}</c>), or <see langword="null"/> for any other stream.
    /// </summary>
    public static string? LotIdOf(string streamId)
    {
        ArgumentNullException.ThrowIfNull(streamId);

        return streamId.StartsWith(LotStreamPrefix, StringComparison.Ordinal) ? streamId[LotStreamPrefix.Length..] : null;
    }

    /// <inheritdoc />
    public Task ApplyAsync(JournalEvent journalEvent, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(journalEvent);
        ArgumentNullException.ThrowIfNull(transaction);

        if (LotIdOf(journalEvent.StreamId) is not { } lotId)
        {
            return Task.CompletedTask;
        }

        return journalEvent.Data switch
        {
            LotCreated created => ExecuteAsync(
                transaction,
                CreateSql,
                cancellationToken,
                ("lot_id", lotId),
                ("site_id", created.SiteId),
                ("name", created.Name),
                ("status", NoNodeStatus),
                ("created_at", journalEvent.RecordedAt)),
            LotRenamed renamed => ExecuteAsync(transaction, RenameSql, cancellationToken, ("lot_id", lotId), ("name", renamed.Name)),
            LotClaimed claimed => ExecuteAsync(
                transaction,
                ClaimSql,
                cancellationToken,
                ("lot_id", lotId),
                ("node_id", claimed.NodeId),
                ("status", UnknownStatus)),
            LotReleased released => ExecuteAsync(
                transaction,
                ReleaseSql,
                cancellationToken,
                ("lot_id", lotId),
                ("node_id", released.NodeId),
                ("status", NoNodeStatus)),
            LotRemoved => ExecuteAsync(transaction, RemoveSql, cancellationToken, ("lot_id", lotId), ("removed_at", journalEvent.RecordedAt)),
            _ => Task.CompletedTask,
        };
    }

    private static async Task ExecuteAsync(
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, transaction.Connection, transaction);

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
