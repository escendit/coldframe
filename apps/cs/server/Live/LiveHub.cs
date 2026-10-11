using System.Globalization;
using Coldframe.Server.Edge;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Coldframe.Server.Live;

/// <summary>
/// The clock access tokens are measured on (Story 6.6): always the real one. The Server's
/// <see cref="TimeProvider"/> may be a test clock that runs ahead (see <c>Testing/ManualClock</c>); a token's
/// lifetime never follows it.
/// </summary>
/// <param name="timeProvider">The real clock; a test hands in a fake.</param>
public sealed class AccessTokenClock(TimeProvider timeProvider)
{
    /// <summary>
    /// The clock.
    /// </summary>
    public TimeProvider TimeProvider { get; } = timeProvider;
}

/// <summary>
/// Sends hub messages to connections. The one seam between the hub's senders and SignalR, so that they are
/// tested without a host.
/// </summary>
public interface ILiveSender
{
    /// <summary>
    /// Sends one message with one argument to the given connections. A connection that has gone is skipped.
    /// </summary>
    Task SendAsync(IReadOnlyList<string> connectionIds, string target, object message, CancellationToken cancellationToken);
}

/// <summary>
/// Sends through the hub's context.
/// </summary>
public sealed class HubLiveSender(IHubContext<LiveHub> hub) : ILiveSender
{
    /// <inheritdoc />
    public Task SendAsync(IReadOnlyList<string> connectionIds, string target, object message, CancellationToken cancellationToken) =>
        hub.Clients.Clients(connectionIds).SendAsync(target, message, cancellationToken);
}

/// <summary>
/// The one SignalR hub of the Server (Story 6.6), for the open web app. It carries exactly two messages, both
/// from the Server to the client: <see cref="LiveMessages.NotificationDelivered"/> and
/// <see cref="LiveMessages.ReadModelChanged"/>. It has no method a client can call: the class declares only the
/// connection callbacks.
/// </summary>
/// <remarks>
/// A caller must be authenticated; the token arrives as a bearer header (the browser connects through the web
/// app's backend-for-frontend, AD-14), never in the URL. The connection is closed when that token expires, on
/// the real clock; the client then connects again through the backend-for-frontend with a fresh one.
/// </remarks>
[Authorize]
public sealed partial class LiveHub(LiveConnections connections, AccessTokenClock tokenClock, ILogger<LiveHub> logger) : Hub
{
    private const string ExpiryClaim = "exp";

    // The longest due time a timer takes (about 49.7 days). A token that lives longer is watched until then,
    // and the connection is closed early: the client connects again.
    private static readonly TimeSpan LongestWatch = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    /// <inheritdoc />
    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirst(EdgeAuthentication.UserIdClaim)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            Context.Abort();
            return;
        }

        connections.Add(Context.ConnectionId, userId, WatchExpiry(Context, tokenClock.TimeProvider));

        try
        {
            await connections.RefreshUserAsync(userId, Context.ConnectionAborted).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Whatever failed the read: a connection without its Sites is closed, and the client connects again.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogConnectionRefused(logger, userId, exception);
            connections.Remove(Context.ConnectionId);
            Context.Abort();
            return;
        }

        await base.OnConnectedAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        connections.Remove(Context.ConnectionId);

        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// After how long a connection opened with a token of the given <c>exp</c> is closed: at once for one
    /// that is past it, at most <see cref="LongestWatch"/> later, and <see langword="null"/> (no watch) for an
    /// <c>exp</c> that is missing, no number of seconds, or outside the range of a date.
    /// </summary>
    internal static TimeSpan? ExpiryDueTime(string? exp, DateTimeOffset now)
    {
        if (!long.TryParse(exp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || seconds > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
        {
            return null;
        }

        var remaining = DateTimeOffset.FromUnixTimeSeconds(seconds) - now;

        return remaining <= TimeSpan.Zero ? TimeSpan.Zero : remaining > LongestWatch ? LongestWatch : remaining;
    }

    // Closes the connection when the token it was opened with expires. A token without a usable expiry is
    // closed by nothing here; the bearer handler refuses one that carries none.
    private static ITimer? WatchExpiry(HubCallerContext context, TimeProvider clock) =>
        ExpiryDueTime(context.User?.FindFirst(ExpiryClaim)?.Value, clock.GetUtcNow()) is { } due
            ? clock.CreateTimer(static state => ((HubCallerContext)state!).Abort(), context, due, Timeout.InfiniteTimeSpan)
            : null;

    [LoggerMessage(EventId = 1, EventName = "LiveConnectionRefused", Level = LogLevel.Warning, Message = "The Memberships of User {UserId} could not be read; the hub connection is closed and the client connects again.")]
    private static partial void LogConnectionRefused(ILogger logger, string userId, Exception exception);
}
