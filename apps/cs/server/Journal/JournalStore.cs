using Npgsql;
using NpgsqlTypes;

namespace Coldframe.Server.Journal;

/// <summary>
/// The PostgreSQL event journal (AD-21): one event table with a global position, and an outbox row per
/// event written in the same transaction.
/// </summary>
/// <remarks>
/// Every append takes one transaction-scoped advisory lock before it inserts. Appends therefore commit
/// one after another, and global positions become visible in commit order: a projector that reads
/// <c>position &gt; checkpoint</c> never skips an event that commits later with a smaller position.
/// </remarks>
public sealed class JournalStore(
    NpgsqlDataSource dataSource,
    JournalSerializer serializer,
    TimeProvider timeProvider,
    OutboxWakeup outboxWakeup)
{
    /// <summary>
    /// The advisory lock key that serializes appends ("cf-jrnl" in ASCII).
    /// </summary>
    public const long AppendLockKey = 0x63_66_2D_6A_72_6E_6C;

    private const string UniqueViolation = "23505";
    private const string StreamVersionConstraint = "ux_journal_events_stream_id_version";

    private const string AppendSql =
        """
        WITH appended AS (
            INSERT INTO journal_events (stream_id, version, type_alias, schema_version, payload, recorded_at)
            SELECT @stream_id, event.version, event.type_alias, event.schema_version, event.payload::jsonb, @recorded_at
            FROM unnest(@versions, @aliases, @schema_versions, @payloads)
                WITH ORDINALITY AS event(version, type_alias, schema_version, payload, ordinal)
            ORDER BY event.ordinal
            RETURNING position
        )
        INSERT INTO journal_outbox (position)
        SELECT position FROM appended
        """;

    private const string SelectColumns =
        "SELECT position, stream_id, version, type_alias, schema_version, payload::text, recorded_at FROM journal_events";

    /// <summary>
    /// Appends events to a stream if its current version is <paramref name="expectedVersion"/>.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the events and their outbox rows were committed; <see langword="false"/>
    /// when the stream has moved on, in which case nothing was written.
    /// </returns>
    public async Task<bool> AppendAsync(
        string streamId,
        int expectedVersion,
        IReadOnlyList<object> events,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedVersion);
        ArgumentNullException.ThrowIfNull(events);

        if (events.Count == 0)
        {
            return true;
        }

        var serialized = events.Select(serializer.Serialize).ToList();
        var recordedAt = timeProvider.GetUtcNow();

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var takeLock = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@key)", connection, transaction))
        {
            takeLock.Parameters.AddWithValue("key", AppendLockKey);
            await takeLock.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var current = new NpgsqlCommand(
            "SELECT COALESCE(MAX(version), 0) FROM journal_events WHERE stream_id = @stream_id", connection, transaction))
        {
            current.Parameters.AddWithValue("stream_id", streamId);
            var version = (int)(await current.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;

            if (version != expectedVersion)
            {
                return false;
            }
        }

        await using (var append = new NpgsqlCommand(AppendSql, connection, transaction))
        {
            append.Parameters.AddWithValue("stream_id", streamId);
            append.Parameters.AddWithValue("recorded_at", recordedAt);
            append.Parameters.AddWithValue("versions", Enumerable.Range(expectedVersion + 1, serialized.Count).ToArray());
            append.Parameters.AddWithValue("aliases", serialized.Select(item => item.Alias).ToArray());
            append.Parameters.AddWithValue("schema_versions", serialized.Select(item => item.SchemaVersion).ToArray());
            append.Parameters.Add(new NpgsqlParameter("payloads", NpgsqlDbType.Array | NpgsqlDbType.Text)
            {
                Value = serialized.Select(item => item.Payload).ToArray(),
            });

            try
            {
                await append.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (PostgresException exception) when (
                exception.SqlState == UniqueViolation && exception.ConstraintName == StreamVersionConstraint)
            {
                // Another writer got there first. The lock makes this unlikely; the constraint makes it safe.
                return false;
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        outboxWakeup.Wake();

        return true;
    }

    /// <summary>
    /// Reads every event of a stream in version order.
    /// </summary>
    public async Task<IReadOnlyList<JournalEvent>> ReadStreamAsync(string streamId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);

        await using var command = dataSource.CreateCommand($"{SelectColumns} WHERE stream_id = @stream_id ORDER BY version");
        command.Parameters.AddWithValue("stream_id", streamId);

        return await ReadAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads up to <paramref name="maxCount"/> events after a global position, in position order.
    /// </summary>
    public async Task<IReadOnlyList<JournalEvent>> ReadFromAsync(
        long afterPosition,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(afterPosition);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);

        await using var command = dataSource.CreateCommand(
            $"{SelectColumns} WHERE position > @after ORDER BY position LIMIT @max_count");
        command.Parameters.AddWithValue("after", afterPosition);
        command.Parameters.AddWithValue("max_count", maxCount);

        return await ReadAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<JournalEvent>> ReadAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var events = new List<JournalEvent>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var data = serializer.Deserialize(reader.GetString(3), reader.GetInt32(4), reader.GetString(5));

            events.Add(new JournalEvent(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetFieldValue<DateTimeOffset>(6),
                data));
        }

        return events;
    }
}
