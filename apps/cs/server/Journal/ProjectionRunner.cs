using Microsoft.Extensions.Options;
using Npgsql;

namespace Coldframe.Server.Journal;

/// <summary>
/// Runs one <see cref="IProjector"/>: reads the journal after the projector's checkpoint and applies
/// each batch together with the new checkpoint in one transaction (AD-21).
/// </summary>
/// <remarks>
/// It catches up at start, then waits for a hint (<see cref="ProjectionWakeup"/>) or the poll interval,
/// measured on the injected <see cref="TimeProvider"/>. Hints only shorten the wait, so the projector
/// converges with streams disabled. Deleting the projector's checkpoint and read model makes it rebuild
/// from position 0.
/// </remarks>
public sealed partial class ProjectionRunner(
    IProjector projector,
    NpgsqlDataSource dataSource,
    JournalStore store,
    ProjectionWakeup wakeup,
    TimeProvider timeProvider,
    IOptions<JournalOptions> options,
    ILogger<ProjectionRunner> logger) : BackgroundService
{
    private const string EnsureCheckpointSql =
        """
        INSERT INTO projection_checkpoints (projector, position, updated_at)
        VALUES (@projector, 0, @now)
        ON CONFLICT (projector) DO NOTHING
        """;

    private int _waiting;

    /// <summary>
    /// The name of the projector this runner runs.
    /// </summary>
    public string ProjectorName => projector.Name;

    /// <summary>
    /// Whether the runner has caught up and waits for a hint or its next poll.
    /// </summary>
    public bool IsWaiting => Volatile.Read(ref _waiting) == 1;

    /// <summary>
    /// Applies events until the projector has reached the end of the journal.
    /// </summary>
    public async Task CatchUpAsync(CancellationToken cancellationToken)
    {
        var batchSize = options.Value.BatchSize;

        while (true)
        {
            var checkpoint = await ReadCheckpointAsync(cancellationToken).ConfigureAwait(false);
            var batch = await store.ReadFromAsync(checkpoint, batchSize, cancellationToken).ConfigureAwait(false);

            if (batch.Count == 0)
            {
                return;
            }

            if (!await ApplyAsync(batch, checkpoint, cancellationToken).ConfigureAwait(false))
            {
                // The checkpoint moved or was deleted (a rebuild) since the batch was read: read again.
                continue;
            }

            if (batch.Count < batchSize)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Applies a batch in one transaction with the checkpoint. Events at or below the checkpoint are
    /// skipped, so a batch that was already applied changes nothing.
    /// </summary>
    internal Task ApplyAsync(IReadOnlyList<JournalEvent> batch, CancellationToken cancellationToken) =>
        ApplyAsync(batch, readAfter: null, cancellationToken);

    /// <summary>
    /// Applies a batch as <see cref="ApplyAsync(IReadOnlyList{JournalEvent}, CancellationToken)"/> does, but
    /// only when the locked checkpoint still equals <paramref name="readAfter"/>, the checkpoint the batch
    /// was read after. Otherwise it applies nothing, so a rebuild that deleted the checkpoint meanwhile
    /// never skips the events below the old checkpoint.
    /// </summary>
    /// <returns>Whether the batch was considered; <see langword="false"/> when the checkpoint was gone or had moved.</returns>
    internal async Task<bool> ApplyAsync(IReadOnlyList<JournalEvent> batch, long? readAfter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var ensure = new NpgsqlCommand(EnsureCheckpointSql, connection, transaction))
        {
            ensure.Parameters.AddWithValue("projector", projector.Name);
            ensure.Parameters.AddWithValue("now", timeProvider.GetUtcNow());
            await ensure.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        long checkpoint;
        await using (var select = new NpgsqlCommand(
            "SELECT position FROM projection_checkpoints WHERE projector = @projector FOR UPDATE", connection, transaction))
        {
            select.Parameters.AddWithValue("projector", projector.Name);
            // A concurrent delete (a rebuild) can remove the row between the insert and this select.
            if (await select.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not long locked)
            {
                return false;
            }

            checkpoint = locked;
        }

        if (readAfter is { } expected && expected != checkpoint)
        {
            return false;
        }

        var applied = checkpoint;

        foreach (var @event in batch.OrderBy(item => item.Position))
        {
            if (@event.Position <= applied)
            {
                continue;
            }

            await projector.ApplyAsync(@event, transaction, cancellationToken).ConfigureAwait(false);
            applied = @event.Position;
        }

        if (applied == checkpoint)
        {
            return true;
        }

        await using (var update = new NpgsqlCommand(
            "UPDATE projection_checkpoints SET position = @position, updated_at = @now WHERE projector = @projector",
            connection,
            transaction))
        {
            update.Parameters.AddWithValue("projector", projector.Name);
            update.Parameters.AddWithValue("position", applied);
            update.Parameters.AddWithValue("now", timeProvider.GetUtcNow());
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            // Taken before catching up, so a hint that arrives meanwhile is not lost.
            var hint = wakeup.WaitAsync();

            try
            {
                await CatchUpAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                LogCatchUpFailed(logger, projector.Name, exception);
            }

            using var poll = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var delay = Task.Delay(options.Value.PollInterval, timeProvider, poll.Token);

            Volatile.Write(ref _waiting, 1);
            await Task.WhenAny(hint, delay).ConfigureAwait(false);
            Volatile.Write(ref _waiting, 0);

            await poll.CancelAsync().ConfigureAwait(false);
        }
    }

    private async Task<long> ReadCheckpointAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT COALESCE((SELECT position FROM projection_checkpoints WHERE projector = @projector), 0)");
        command.Parameters.AddWithValue("projector", projector.Name);

        return (long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Projector {Projector} failed to catch up; it retries on the next hint or poll.")]
    private static partial void LogCatchUpFailed(ILogger logger, string projector, Exception exception);
}
