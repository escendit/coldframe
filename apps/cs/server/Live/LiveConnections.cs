using Coldframe.Server.Identity;

namespace Coldframe.Server.Live;

/// <summary>
/// The Sites a User holds a Membership on now, as the hub needs them (Story 6.6).
/// </summary>
public interface ILiveMemberships
{
    /// <summary>
    /// The IDs of the Active Sites <paramref name="userId"/> holds a Membership on.
    /// </summary>
    Task<IReadOnlyCollection<string>> SitesOfAsync(string userId, CancellationToken cancellationToken);
}

/// <summary>
/// Reads the Memberships from the identity projection, the same source the Edge API authorizes from (AD-4).
/// </summary>
public sealed class IdentityLiveMemberships(IdentityReadModel identity) : ILiveMemberships
{
    /// <inheritdoc />
    public async Task<IReadOnlyCollection<string>> SitesOfAsync(string userId, CancellationToken cancellationToken) =>
        [.. (await identity.ListSitesAsync(userId, cancellationToken).ConfigureAwait(false)).Select(site => site.SiteId)];
}

/// <summary>
/// The open hub connections of this Server (Story 6.6): whose each one is, and which Sites it is told about.
/// It lives in memory and in this process only: one Server replica, no backplane, and nothing is kept for a
/// User without a connection.
/// </summary>
/// <remarks>
/// A connection's Sites are the User's Memberships as the identity projection holds them. They are read when
/// the connection opens and again whenever the identity projector commits a change for that User or one of
/// those Sites, so a Membership that is added or removed takes effect without a reconnect. Reads are serialised:
/// the one that starts last reads last, so a connection never ends up with an older answer than a later one.
/// </remarks>
public sealed partial class LiveConnections(ILiveMemberships memberships, ILogger<LiveConnections> logger) : IDisposable
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Connection> _connections = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _reading = new(1, 1);

    /// <summary>
    /// How many connections are open.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _connections.Count;
            }
        }
    }

    /// <summary>
    /// Adds a connection of <paramref name="userId"/>. It is told about no Site until
    /// <see cref="RefreshUserAsync"/> has read the User's Memberships.
    /// </summary>
    /// <param name="connectionId">The SignalR connection ID.</param>
    /// <param name="userId">The User (the token's <c>sub</c>).</param>
    /// <param name="lifetime">What ends with the connection, such as the timer of its token's expiry.</param>
    public void Add(string connectionId, string userId, IDisposable? lifetime = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionId);
        ArgumentException.ThrowIfNullOrEmpty(userId);

        Connection? replaced;

        lock (_gate)
        {
            _connections.Remove(connectionId, out replaced);
            _connections[connectionId] = new Connection(userId, lifetime);
        }

        replaced?.Lifetime?.Dispose();
    }

    /// <summary>
    /// Removes a connection. Unknown IDs are ignored.
    /// </summary>
    public void Remove(string connectionId)
    {
        ArgumentNullException.ThrowIfNull(connectionId);

        Connection? removed;

        lock (_gate)
        {
            _connections.Remove(connectionId, out removed);
        }

        removed?.Lifetime?.Dispose();
    }

    /// <summary>
    /// The open connections of <paramref name="userId"/>.
    /// </summary>
    public IReadOnlyList<string> OfUser(string userId)
    {
        ArgumentNullException.ThrowIfNull(userId);

        lock (_gate)
        {
            return [.. _connections.Where(pair => string.Equals(pair.Value.UserId, userId, StringComparison.Ordinal)).Select(pair => pair.Key)];
        }
    }

    /// <summary>
    /// The open connections that are told about <paramref name="siteId"/>: those of its members.
    /// </summary>
    public IReadOnlyList<string> OfSite(string siteId)
    {
        ArgumentNullException.ThrowIfNull(siteId);

        lock (_gate)
        {
            return [.. _connections.Where(pair => pair.Value.Sites.Contains(siteId)).Select(pair => pair.Key)];
        }
    }

    /// <summary>
    /// Reads the Memberships of <paramref name="userId"/> and gives them to every open connection of that User.
    /// </summary>
    /// <exception cref="Exception">The Memberships could not be read; the connections keep what they had.</exception>
    public async Task RefreshUserAsync(string userId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);

        await _reading.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (OfUser(userId).Count == 0)
            {
                return;
            }

            var sites = (await memberships.SitesOfAsync(userId, cancellationToken).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);

            lock (_gate)
            {
                foreach (var connection in _connections.Values.Where(connection => string.Equals(connection.UserId, userId, StringComparison.Ordinal)))
                {
                    connection.Sites = sites;
                }
            }
        }
        finally
        {
            _reading.Release();
        }
    }

    /// <summary>
    /// Follows a change of the identity read model, after it is committed: reads the Memberships again of the
    /// User it names, or of every User with a connection that is told about the Site. When they cannot be read,
    /// the connections concerned stop being told about that Site, which is the safe side of a removed Membership.
    /// Never throws.
    /// </summary>
    /// <param name="siteId">The Site that changed.</param>
    /// <param name="userId">The User whose Membership changed, or <see langword="null"/> for a change of the Site itself.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task FollowIdentityChangeAsync(string siteId, string? userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(siteId);

        IReadOnlyList<string> users;

        lock (_gate)
        {
            users = userId is not null
                ? [userId]
                : [.. _connections.Values.Where(connection => connection.Sites.Contains(siteId)).Select(connection => connection.UserId).Distinct(StringComparer.Ordinal)];
        }

        foreach (var user in users)
        {
            try
            {
                await RefreshUserAsync(user, cancellationToken).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Whatever failed the read, the connection must not keep a Site it may have lost.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                LogMembershipsNotRead(logger, user, siteId, exception);

                lock (_gate)
                {
                    foreach (var connection in _connections.Values.Where(connection => string.Equals(connection.UserId, user, StringComparison.Ordinal)))
                    {
                        connection.Sites = [.. connection.Sites.Where(site => !string.Equals(site, siteId, StringComparison.Ordinal))];
                    }
                }
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        List<Connection> connections;

        lock (_gate)
        {
            connections = [.. _connections.Values];
            _connections.Clear();
        }

        foreach (var connection in connections)
        {
            connection.Lifetime?.Dispose();
        }

        _reading.Dispose();
    }

    [LoggerMessage(EventId = 1, EventName = "LiveMembershipsNotRead", Level = LogLevel.Warning, Message = "The Memberships of User {UserId} could not be read after Site {SiteId} changed; the User's open connections get no more hints of that Site until they are read.")]
    private static partial void LogMembershipsNotRead(ILogger logger, string userId, string siteId, Exception exception);

    private sealed class Connection(string userId, IDisposable? lifetime)
    {
        public string UserId { get; } = userId;

        public IDisposable? Lifetime { get; } = lifetime;

        // Replaced as a whole, never changed in place, so a reader that holds a set sees it unchanged.
        public HashSet<string> Sites { get; set; } = [];
    }
}
