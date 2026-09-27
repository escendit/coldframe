using System.Text.Json;
using Coldframe.Server.Journal;
using Coldframe.Server.Tests.Samples;

namespace Coldframe.Server.IntegrationTests.Journal;

/// <summary>
/// The CustomStorage journal behind a <c>JournaledGrain</c> (AD-2, AD-21).
/// </summary>
public sealed class JournalAppendTests(SampleClusterWithoutStreams sample) : IClassFixture<SampleClusterWithoutStreams>
{
    [Fact]
    public async Task RaisingAnEventWritesOneJournalRowAndOneOutboxRow()
    {
        var grain = sample.Grain($"append-{Guid.NewGuid():N}");
        var streamId = await grain.GetStreamId();
        var now = sample.Time.GetUtcNow();

        await grain.Create("Bed by the fence");

        await using var command = sample.Database.DataSource.CreateCommand(
            """
            SELECT e.position, e.version, e.type_alias, e.schema_version, e.payload::text, e.recorded_at,
                   (SELECT COUNT(*) FROM journal_outbox o WHERE o.position = e.position)
            FROM journal_events e
            WHERE e.stream_id = @stream_id
            """);
        command.Parameters.AddWithValue("stream_id", streamId);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken), "The journal has no row for the stream.");
        Assert.True(reader.GetInt64(0) > 0);
        Assert.Equal(1, reader.GetInt32(1));
        Assert.Equal("sample.created", reader.GetString(2));
        Assert.Equal(1, reader.GetInt32(3));

        using (var payload = JsonDocument.Parse(reader.GetString(4)))
        {
            Assert.Equal("Bed by the fence", payload.RootElement.GetProperty("name").GetString());
        }

        Assert.Equal(now, reader.GetFieldValue<DateTimeOffset>(5));
        Assert.Equal(1L, reader.GetInt64(6));
        Assert.False(await reader.ReadAsync(TestContext.Current.CancellationToken), "The journal has more than one row for the stream.");
    }

    [Fact]
    public async Task RecordedAtFollowsTheFakeTimeProvider()
    {
        var grain = sample.Grain($"time-{Guid.NewGuid():N}");
        var streamId = await grain.GetStreamId();

        sample.Time.Advance(TimeSpan.FromMinutes(7));
        var now = sample.Time.GetUtcNow();

        Assert.Equal(now, await grain.GetTime());

        await grain.Create("Herbs");

        var recordedAt = await sample.Database.ScalarAsync<DateTime>(
            "SELECT recorded_at FROM journal_events WHERE stream_id = @stream_id", ("stream_id", streamId));
        Assert.Equal(now.UtcDateTime, recordedAt);
    }

    [Fact]
    public async Task AStaleExpectedVersionWritesNothing()
    {
        var streamId = SampleGrain.StreamIdOf($"stale-{Guid.NewGuid():N}");
        var cancellationToken = TestContext.Current.CancellationToken;

        Assert.True(await sample.Store.AppendAsync(streamId, 0, [new SampleCreated("first")], cancellationToken));
        Assert.False(await sample.Store.AppendAsync(streamId, 0, [new SampleCreated("second")], cancellationToken));

        var rows = await sample.Database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM journal_events WHERE stream_id = @stream_id", ("stream_id", streamId));
        Assert.Equal(1L, rows);

        var events = await sample.Store.ReadStreamAsync(streamId, cancellationToken);
        Assert.Equal(new SampleCreated("first"), Assert.Single(events).Data);
    }

    [Fact]
    public async Task AGrainRebuildsItsStateFromTheJournalIncludingAnOldEventVersion()
    {
        var key = $"old-{Guid.NewGuid():N}";
        var streamId = SampleGrain.StreamIdOf(key);

        // Written by an earlier release, before notes had a weight.
        await InsertRawAsync(streamId, 1, "sample.created", 1, """{"name":"Old bed"}""");
        await InsertRawAsync(streamId, 2, "sample.noted", 1, """{"text":"sown"}""");

        var grain = sample.Grain(key);
        await grain.Note("watered", 3);

        var state = await grain.GetState();
        Assert.Equal("Old bed", state.Name);
        Assert.Equal(2, state.NoteCount);
        Assert.Equal(SampleNotedUpcaster.ImplicitWeight + 3, state.TotalWeight);
        Assert.Equal(3, await grain.GetVersion());
    }

    [Fact]
    public async Task ReadingAnUnknownAliasFailsNamingAliasAndSchemaVersion()
    {
        var streamId = SampleGrain.StreamIdOf($"unknown-{Guid.NewGuid():N}");
        await InsertRawAsync(streamId, 1, "sample.vanished", 4, "{}");

        var exception = await Assert.ThrowsAsync<UnknownEventTypeException>(
            () => sample.Store.ReadStreamAsync(streamId, TestContext.Current.CancellationToken));

        Assert.Equal("sample.vanished", exception.Alias);
        Assert.Equal(4, exception.SchemaVersion);
    }

    private Task InsertRawAsync(string streamId, int version, string alias, int schemaVersion, string payload) =>
        sample.Database.ExecuteAsync(
            """
            INSERT INTO journal_events (stream_id, version, type_alias, schema_version, payload, recorded_at)
            VALUES (@stream_id, @version, @alias, @schema_version, @payload::jsonb, @recorded_at)
            """,
            ("stream_id", streamId),
            ("version", version),
            ("alias", alias),
            ("schema_version", schemaVersion),
            ("payload", payload),
            ("recorded_at", SampleCluster.Start));
}
