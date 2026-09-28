using Coldframe.Contracts.Sites;
using Npgsql;

namespace Coldframe.Server.Identity;

/// <summary>
/// A Site as the identity projection holds it, with the caller's Role.
/// </summary>
/// <param name="SiteId">The Site ID.</param>
/// <param name="Name">The Site name.</param>
/// <param name="Lifecycle">The Site's lifecycle.</param>
/// <param name="CallerRole">The caller's Role on the Site, or <see langword="null"/> without a Membership.</param>
public sealed record SiteAccessView(string SiteId, string Name, SiteLifecycle Lifecycle, SiteRole? CallerRole);

/// <summary>
/// Reads the identity projection. The Edge API's handlers and its authorization policy read Sites and
/// Roles only from here, never from token claims (AD-4). Grains never read it.
/// </summary>
public sealed class IdentityReadModel(NpgsqlDataSource dataSource)
{
    private const string SelectSql =
        """
        SELECT s.name, s.lifecycle, m.role
        FROM identity_sites s
        LEFT JOIN identity_memberships m ON m.site_id = s.site_id AND m.user_id = @user_id
        WHERE s.site_id = @site_id
        """;

    /// <summary>
    /// Returns the Site with the caller's Role, or <see langword="null"/> when the projection holds no such Site.
    /// </summary>
    /// <param name="siteId">The Site ID, in its canonical form.</param>
    /// <param name="userId">The caller's User ID (the OIDC <c>sub</c>).</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<SiteAccessView?> FindSiteAsync(string siteId, string userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(siteId);
        ArgumentNullException.ThrowIfNull(userId);

        await using var command = dataSource.CreateCommand(SelectSql);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("user_id", userId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var role = await reader.IsDBNullAsync(2, cancellationToken).ConfigureAwait(false)
            ? (SiteRole?)null
            : Enum.Parse<SiteRole>(reader.GetString(2));

        return new SiteAccessView(siteId, reader.GetString(0), Enum.Parse<SiteLifecycle>(reader.GetString(1)), role);
    }
}
