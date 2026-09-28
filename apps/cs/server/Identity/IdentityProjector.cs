using Coldframe.Contracts.Sites;
using Coldframe.Server.Journal;
using Npgsql;

namespace Coldframe.Server.Identity;

/// <summary>
/// Projects Sites and Memberships into <c>identity_sites</c> and <c>identity_memberships</c>, the read
/// model the authorization policy reads (AD-4). It is the only writer of those tables.
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
        }
    }
}
