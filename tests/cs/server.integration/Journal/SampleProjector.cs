using Coldframe.Server.Journal;
using Coldframe.Server.Tests.Samples;
using Npgsql;

namespace Coldframe.Server.IntegrationTests.Journal;

/// <summary>
/// A test-only projector. It keeps one row per sample stream and logs every position it applies,
/// so a test can check the order.
/// </summary>
public sealed class SampleProjector : IProjector
{
    public const string ProjectorName = "sample";

    /// <summary>
    /// The read-model tables. Test code creates them; application code never runs DDL.
    /// </summary>
    public const string CreateTablesSql =
        """
        CREATE TABLE sample_read_model (
            stream_id text PRIMARY KEY,
            name text,
            note_count integer NOT NULL,
            total_weight integer NOT NULL
        );
        CREATE TABLE sample_applied (
            sequence bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            position bigint NOT NULL
        );
        """;

    private string? _failOnStreamId;
    private int _faults;

    public string Name => ProjectorName;

    /// <summary>
    /// How many times the fault switch has thrown.
    /// </summary>
    public int Faults => Volatile.Read(ref _faults);

    /// <summary>
    /// Makes the next event of <paramref name="streamId"/> throw once, after the events before it in the
    /// same batch have been written, so a test can check that the transaction rolls back.
    /// </summary>
    public void FailOnceOn(string streamId) => Volatile.Write(ref _failOnStreamId, streamId);

    public async Task ApplyAsync(JournalEvent journalEvent, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(journalEvent);
        ArgumentNullException.ThrowIfNull(transaction);

        var connection = transaction.Connection!;

        if (string.Equals(journalEvent.StreamId, Volatile.Read(ref _failOnStreamId), StringComparison.Ordinal)
            && Interlocked.Exchange(ref _failOnStreamId, null) is not null)
        {
            Interlocked.Increment(ref _faults);
            throw new InvalidOperationException($"The sample projector fails on {journalEvent.StreamId} as the test asked.");
        }

        await using (var log = new NpgsqlCommand("INSERT INTO sample_applied (position) VALUES (@position)", connection, transaction))
        {
            log.Parameters.AddWithValue("position", journalEvent.Position);
            await log.ExecuteNonQueryAsync(cancellationToken);
        }

        var sql = journalEvent.Data switch
        {
            SampleCreated =>
                """
                INSERT INTO sample_read_model (stream_id, name, note_count, total_weight)
                VALUES (@stream_id, @name, 0, 0)
                ON CONFLICT (stream_id) DO UPDATE SET name = EXCLUDED.name
                """,
            SampleNoted =>
                """
                INSERT INTO sample_read_model (stream_id, name, note_count, total_weight)
                VALUES (@stream_id, NULL, 1, @weight)
                ON CONFLICT (stream_id) DO UPDATE
                SET note_count = sample_read_model.note_count + 1,
                    total_weight = sample_read_model.total_weight + EXCLUDED.total_weight
                """,
            _ => null,
        };

        if (sql is null)
        {
            return;
        }

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("stream_id", journalEvent.StreamId);

        switch (journalEvent.Data)
        {
            case SampleCreated created:
                command.Parameters.AddWithValue("name", created.Name);
                break;
            case SampleNoted noted:
                command.Parameters.AddWithValue("weight", noted.Weight);
                break;
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
