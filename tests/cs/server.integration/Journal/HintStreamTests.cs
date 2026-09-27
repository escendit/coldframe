namespace Coldframe.Server.IntegrationTests.Journal;

/// <summary>
/// With the hint stream enabled, the outbox dispatcher wakes the projector without waiting for a poll.
/// Hints carry only a position (AD-5, AD-21).
/// </summary>
public sealed class HintStreamTests(SampleClusterWithHints sample) : IClassFixture<SampleClusterWithHints>
{
    [Fact]
    public async Task TheProjectorConvergesFromTheHintAlone()
    {
        var grain = sample.Grain($"hint-{Guid.NewGuid():N}");

        await grain.Create("H");
        await grain.Note("h1", 2);
        var last = await sample.LastPositionAsync();

        // The fake clock never moves in this test, so no poll interval can elapse.
        await JournalWait.UntilCheckpointAsync(sample.Database, SampleProjector.ProjectorName, last);

        Assert.Contains((await grain.GetStreamId(), "H", 1, 2), await sample.ReadModelAsync());
        await JournalWait.UntilAsync(
            async () => await sample.Database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM journal_outbox WHERE dispatched_at IS NULL AND position <= @last", ("last", last)) == 0,
            "the outbox dispatcher marks every row up to the last position as dispatched");
    }
}
