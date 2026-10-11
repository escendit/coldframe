using System.Threading.Channels;
using Coldframe.Server.Journal;

namespace Coldframe.Server.Live;

/// <summary>
/// Turns committed read-model changes into <c>readmodel.changed</c> hints (Story 6.6) and sends each to the
/// open connections of the Site's members, and of the User whose Membership changed.
/// </summary>
/// <remarks>
/// <see cref="Publish"/> only queues: a projector's commit and a Device's ingest never wait for a browser. One
/// loop sends in the order of the commits. For a change of the identity read model it first reads the
/// Memberships of the connections concerned again, and sends the hint to those that were told about the Site
/// before or are after it, so the User who lost a Site learns it once and nothing of the Site afterwards. A
/// hint carries the resource, the ID and a version, never data. With no open connection nothing is queued and
/// nothing is kept. A full queue drops the oldest hint: a client reads again on the next one, or when it reconnects.
/// </remarks>
public sealed partial class LiveHints(LiveConnections connections, ILiveSender sender, ILogger<LiveHints> logger)
    : BackgroundService, IReadModelHints
{
    private const int QueueCapacity = 4096;

    /// <summary>
    /// How long one read of the Memberships, and one send, may take: the loop sends one hint at a time, and
    /// a read holds the lock every new connection waits for.
    /// </summary>
    internal TimeSpan Budget { get; set; } = TimeSpan.FromSeconds(5);

    private readonly Channel<ReadModelChange> _queue = Channel.CreateBounded<ReadModelChange>(
        new BoundedChannelOptions(QueueCapacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    // The highest version sent per resource and ID: a projector that is rebuilt from position 0, or a Node's
    // backlog frame, never makes a version go down while this Server runs. A restart closes every connection.
    private readonly Dictionary<(string Resource, string Id), long> _versions = [];

    /// <inheritdoc />
    public void Publish(IReadOnlyList<ReadModelChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        if (connections.Count == 0)
        {
            return;
        }

        foreach (var change in changes)
        {
            _queue.Writer.TryWrite(change);
        }
    }

    /// <summary>
    /// Sends one committed change as a hint. The loop calls it; a test calls it directly.
    /// </summary>
    internal async Task SendAsync(ReadModelChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        var targets = new HashSet<string>(connections.OfSite(change.SiteId), StringComparer.Ordinal);

        if (string.Equals(change.Resource, ReadModelResources.Sites, StringComparison.Ordinal))
        {
            // A read that hangs ends with its budget; the connections concerned then lose the Site.
            using var reading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            reading.CancelAfter(Budget);
            await connections.FollowIdentityChangeAsync(change.SiteId, change.UserId, reading.Token).ConfigureAwait(false);
            targets.UnionWith(connections.OfSite(change.SiteId));

            if (change.UserId is { } userId)
            {
                targets.UnionWith(connections.OfUser(userId));
            }
        }

        if (targets.Count == 0)
        {
            return;
        }

        var key = (change.Resource, change.Id);
        var version = Math.Max(Math.Max(1, change.Version), _versions.GetValueOrDefault(key));
        _versions[key] = version;

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(Budget);

        await sender
            .SendAsync([.. targets], LiveMessages.ReadModelChanged, new ReadModelChangedMessage(change.Resource, change.Id, version), budget.Token)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var change in _queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await SendAsync(change, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // A hint is best effort: one that cannot be sent is dropped and the next is tried.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                LogHintNotSent(logger, change.Resource, exception);
            }
        }
    }

    [LoggerMessage(EventId = 1, EventName = "LiveHintNotSent", Level = LogLevel.Warning, Message = "A {Resource} hint could not be sent; it is dropped.")]
    private static partial void LogHintNotSent(ILogger logger, string resource, Exception exception);
}
