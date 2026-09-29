using Coldframe.Server.Journal;
using Coldframe.Server.Tests.Samples;
using Npgsql;

namespace Coldframe.Server.IntegrationTests.Journal;

/// <summary>
/// Appends are serialized by one advisory lock, so global positions become visible in commit order and a
/// projector reading <c>position &gt; checkpoint</c> never skips an event (AD-21).
/// </summary>
public sealed class CommitOrderTests(SampleClusterWithoutStreams sample) : IClassFixture<SampleClusterWithoutStreams>
{
    private static readonly TimeSpan AppendTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task AnAppendWaitsForTheJournalLock()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var streamId = SampleGrain.StreamIdOf($"lock-{Guid.NewGuid():N}");

        await using var holder = await sample.Database.DataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await holder.BeginTransactionAsync(cancellationToken);
        await using (var takeLock = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@key)", holder, transaction))
        {
            takeLock.Parameters.AddWithValue("key", JournalStore.AppendLockKey);
            await takeLock.ExecuteNonQueryAsync(cancellationToken);
        }

        var append = sample.Store.AppendAsync(streamId, 0, [new SampleCreated("blocked")], cancellationToken);

        // The append is queued behind the lock this test holds.
        await JournalWait.UntilAsync(
            async () => await sample.Database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM pg_locks WHERE locktype = 'advisory' AND NOT granted") > 0,
            "the append waits for the advisory lock");
        Assert.False(append.IsCompleted, "The append completed while another transaction held the journal lock.");

        await transaction.RollbackAsync(cancellationToken);

        Assert.True(await append.WaitAsync(AppendTimeout, cancellationToken));
        Assert.Equal(1L, await sample.Database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM journal_events WHERE stream_id = @stream_id", ("stream_id", streamId)));
    }

    [Fact]
    public async Task ParallelAppendsToManyStreamsAreAllProjected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var run = Guid.NewGuid().ToString("N");

        var appends = Enumerable.Range(0, 32).Select(async index =>
        {
            var streamId = SampleGrain.StreamIdOf($"parallel-{index}-{run}");
            Assert.True(await sample.Store.AppendAsync(streamId, 0, [new SampleCreated($"P{index}")], cancellationToken));
            Assert.True(await sample.Store.AppendAsync(
                streamId, 1, [new SampleNoted("n1", 1), new SampleNoted("n2", 2)], cancellationToken));
        });
        await Task.WhenAll(appends);

        var last = await sample.LastPositionAsync();
        sample.Wakeup.Wake();
        await JournalWait.UntilCheckpointAsync(sample.Database, SampleProjector.ProjectorName, last);

        Assert.Equal(await sample.JournalPositionsAsync(), await sample.AppliedPositionsAsync());
    }
}
