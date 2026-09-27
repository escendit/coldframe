using Microsoft.Extensions.Options;
using Npgsql;
using Orleans.Runtime;

namespace Coldframe.Server.Journal;

/// <summary>
/// Publishes a hint for undispatched outbox rows to the hint stream and marks them dispatched.
/// It runs only when a hint stream is configured.
/// </summary>
/// <remarks>
/// A hint carries only the highest position of the rows it covers. Publishing happens before the rows
/// are marked, inside their transaction, so a failure leaves them for the next attempt; a repeated hint
/// is harmless. The dispatcher is woken after every append in this silo and polls on the injected
/// <see cref="TimeProvider"/> for rows other silos left behind.
/// </remarks>
internal sealed partial class OutboxDispatcher(
    IClusterClient client,
    NpgsqlDataSource dataSource,
    OutboxWakeup wakeup,
    TimeProvider timeProvider,
    IHostApplicationLifetime lifetime,
    IOptions<JournalOptions> options,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private const string ClaimSql =
        """
        SELECT position FROM journal_outbox
        WHERE dispatched_at IS NULL
        ORDER BY position
        LIMIT @limit
        FOR UPDATE SKIP LOCKED
        """;

    private const string MarkSql =
        "UPDATE journal_outbox SET dispatched_at = @now WHERE position = ANY(@positions)";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The silo is active once the host has started; streams are usable from then on.
        await WhenStartedAsync(stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            var appended = wakeup.WaitAsync();

            try
            {
                while (await DispatchAsync(stoppingToken).ConfigureAwait(false))
                {
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                LogDispatchFailed(logger, exception);
            }

            using var poll = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            await Task.WhenAny(appended, Task.Delay(options.Value.PollInterval, timeProvider, poll.Token)).ConfigureAwait(false);
            await poll.CancelAsync().ConfigureAwait(false);
        }
    }

    private async Task<bool> DispatchAsync(CancellationToken cancellationToken)
    {
        var limit = options.Value.BatchSize;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var positions = new List<long>();
        await using (var claim = new NpgsqlCommand(ClaimSql, connection, transaction))
        {
            claim.Parameters.AddWithValue("limit", limit);
            await using var reader = await claim.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                positions.Add(reader.GetInt64(0));
            }
        }

        if (positions.Count == 0)
        {
            return false;
        }

        var stream = client
            .GetStreamProvider(JournalHints.StreamProvider)
            .GetStream<long>(StreamId.Create(JournalHints.StreamNamespace, JournalHints.StreamKey));
        await stream.OnNextAsync(positions[^1]).ConfigureAwait(false);

        await using (var mark = new NpgsqlCommand(MarkSql, connection, transaction))
        {
            mark.Parameters.AddWithValue("now", timeProvider.GetUtcNow());
            mark.Parameters.AddWithValue("positions", positions.ToArray());
            await mark.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return positions.Count == limit;
    }

    private Task WhenStartedAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        return started.Task.WaitAsync(stoppingToken);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Dispatching journal hints failed; projectors still converge by polling.")]
    private static partial void LogDispatchFailed(ILogger logger, Exception exception);
}
