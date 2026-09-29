using Coldframe.Contracts.Sites;
using Coldframe.Server.Journal;
using Npgsql;

namespace Coldframe.Server.Identity;

/// <summary>
/// Projects Sites and Memberships into <c>identity_sites</c> and <c>identity_memberships</c>, the read
/// model the authorization policy reads (AD-4). It is the only writer of those tables. The ownerless-edit
/// events need no rows.
/// </summary>
public sealed class IdentityProjector : IProjector
{
    /// <summary>
    /// The projector's stable name; its checkpoint is stored under it.
    /// </summary>
    public const string ProjectorName = "identity";

    private const string SiteStreamPrefix = "site/";

    private const string UpsertSiteSql =
        """
        INSERT INTO identity_sites (site_id, name, lifecycle, created_at)
        VALUES (@site_id, @name, @lifecycle, @created_at)
        ON CONFLICT (site_id) DO UPDATE SET name = EXCLUDED.name, lifecycle = EXCLUDED.lifecycle
        """;

    private const string UpsertMembershipSql =
        """
        INSERT INTO identity_memberships (site_id, user_id, role)
        VALUES (@site_id, @user_id, @role)
        ON CONFLICT (site_id, user_id) DO UPDATE SET role = EXCLUDED.role
        """;

    private const string RenameSiteSql = "UPDATE identity_sites SET name = @name WHERE site_id = @site_id";

    // Deletion keeps the rows: the ID stays resolvable and the policy answers 404 for a deleted Site.
    private const string DeleteSiteSql = "UPDATE identity_sites SET lifecycle = @lifecycle WHERE site_id = @site_id";

    // The projection is a read model, not a tombstone: a revoked Membership has no row.
    private const string RevokeMembershipSql = "DELETE FROM identity_memberships WHERE site_id = @site_id AND user_id = @user_id";

    /// <inheritdoc />
    public string Name => ProjectorName;

    /// <summary>
    /// Returns the Site ID of a Site stream (<c>site/{id}</c>), or <see langword="null"/> for any other stream.
    /// </summary>
    public static string? SiteIdOf(string streamId)
    {
        ArgumentNullException.ThrowIfNull(streamId);

        return streamId.StartsWith(SiteStreamPrefix, StringComparison.Ordinal) ? streamId[SiteStreamPrefix.Length..] : null;
    }

    /// <inheritdoc />
    public async Task ApplyAsync(JournalEvent journalEvent, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(journalEvent);
        ArgumentNullException.ThrowIfNull(transaction);

        if (SiteIdOf(journalEvent.StreamId) is not { } siteId)
        {
            return;
        }

        switch (journalEvent.Data)
        {
            case SiteCreated created:
                await using (var command = new NpgsqlCommand(UpsertSiteSql, transaction.Connection, transaction))
                {
                    command.Parameters.AddWithValue("site_id", siteId);
                    command.Parameters.AddWithValue("name", created.Name);
                    command.Parameters.AddWithValue("lifecycle", nameof(SiteLifecycle.Active));
                    command.Parameters.AddWithValue("created_at", journalEvent.RecordedAt);
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                break;
            case MembershipGranted granted:
                await using (var command = new NpgsqlCommand(UpsertMembershipSql, transaction.Connection, transaction))
                {
                    command.Parameters.AddWithValue("site_id", siteId);
                    command.Parameters.AddWithValue("user_id", granted.UserId);
                    command.Parameters.AddWithValue("role", granted.Role.ToString());
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                break;
            case SiteRenamed renamed:
                await ExecuteAsync(transaction, RenameSiteSql, cancellationToken, ("site_id", siteId), ("name", renamed.Name))
                    .ConfigureAwait(false);
                break;
            case SiteDeleted:
                await ExecuteAsync(transaction, DeleteSiteSql, cancellationToken, ("site_id", siteId), ("lifecycle", nameof(SiteLifecycle.Deleted)))
                    .ConfigureAwait(false);
                break;
            case MembershipRevoked revoked:
                await ExecuteAsync(transaction, RevokeMembershipSql, cancellationToken, ("site_id", siteId), ("user_id", revoked.UserId))
                    .ConfigureAwait(false);
                break;
        }
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
