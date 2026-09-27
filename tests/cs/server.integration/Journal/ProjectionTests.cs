using Coldframe.Server.Tests.Samples;
using Microsoft.Extensions.DependencyInjection;

namespace Coldframe.Server.IntegrationTests.Journal;

/// <summary>
/// Projectors read the journal by global position, checkpoint in the same transaction as their
/// read model, rebuild from zero, and converge with streams disabled (AD-21).
/// </summary>
public sealed class ProjectionTests(SampleClusterWithoutStreams sample) : IClassFixture<SampleClusterWithoutStreams>
{
    [Fact]
    public async Task AppliesEventsAcrossStreamsInGlobalPositionOrderAndRebuildsFromZero()
    {
        var run = Guid.NewGuid().ToString("N");
        var first = sample.Grain($"order-a-{run}");
        var second = sample.Grain($"order-b-{run}");
        var third = sample.Grain($"order-c-{run}");

        await first.Create("A");
        await second.Create("B");
        await first.Note("a1", 2);
        await third.Create("C");
        await second.Note("b1", 5);
        await first.Note("a2", 1);

        var last = await sample.LastPositionAsync();
        sample.Wakeup.Wake();
        await JournalWait.UntilCheckpointAsync(sample.Database, SampleProjector.ProjectorName, last);

        Assert.Equal(last, await sample.CheckpointAsync());
        Assert.Equal(await sample.JournalPositionsAsync(), await sample.AppliedPositionsAsync());

        var readModel = await sample.ReadModelAsync();
        Assert.Contains((SampleGrain.StreamIdOf($"order-a-{run}"), "A", 2, 3), readModel);
        Assert.Contains((SampleGrain.StreamIdOf($"order-b-{run}"), "B", 1, 5), readModel);
        Assert.Contains((SampleGrain.StreamIdOf($"order-c-{run}"), "C", 0, 0), readModel);

        // Rebuild: without its read model and checkpoint the projector starts again from position 0.
        await sample.Database.ExecuteAsync(
            "DELETE FROM sample_read_model; DELETE FROM sample_applied; DELETE FROM projection_checkpoints WHERE projector = @projector",
            ("projector", SampleProjector.ProjectorName));

        sample.Wakeup.Wake();
        await JournalWait.UntilCheckpointAsync(sample.Database, SampleProjector.ProjectorName, last);

        Assert.Equal(readModel, await sample.ReadModelAsync());
        Assert.Equal(await sample.JournalPositionsAsync(), await sample.AppliedPositionsAsync());
    }

    [Fact]
    public async Task ApplyingAnEventAtOrBelowTheCheckpointChangesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var grain = sample.Grain($"duplicate-{Guid.NewGuid():N}");
        await grain.Create("D");
        await grain.Note("d1", 4);

        var last = await sample.LastPositionAsync();
        sample.Wakeup.Wake();
        await JournalWait.UntilCheckpointAsync(sample.Database, SampleProjector.ProjectorName, last);

        var readModel = await sample.ReadModelAsync();
        var applied = await sample.AppliedPositionsAsync();
        var checkpoint = await sample.CheckpointAsync();

        var alreadyApplied = await sample.Store.ReadFromAsync(0, 1000, cancellationToken);
        Assert.NotEmpty(alreadyApplied);

        await sample.Runner.ApplyAsync(alreadyApplied, cancellationToken);

        Assert.Equal(readModel, await sample.ReadModelAsync());
        Assert.Equal(applied, await sample.AppliedPositionsAsync());
        Assert.Equal(checkpoint, await sample.CheckpointAsync());
    }

    [Fact]
    public async Task ABatchReadBeforeARebuildDeletedTheCheckpointIsNotApplied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // Start from a caught-up projector.
        var before = await sample.LastPositionAsync();
        sample.Wakeup.Wake();
        await JournalWait.UntilCheckpointAsync(sample.Database, SampleProjector.ProjectorName, before);
        await JournalWait.UntilAsync(() => Task.FromResult(sample.Runner.IsWaiting), "the projection runner waits");
        var readAfter = await sample.CheckpointAsync();

        var grain = sample.Grain($"rebuild-race-{Guid.NewGuid():N}");
        await grain.Create("R");
        var last = await sample.LastPositionAsync();

        // A batch read after the old checkpoint, then a rebuild deletes the read model and checkpoint.
        var batch = await sample.Store.ReadFromAsync(readAfter, 1000, cancellationToken);
        Assert.NotEmpty(batch);
        await sample.Database.ExecuteAsync(
            "DELETE FROM sample_read_model; DELETE FROM sample_applied; DELETE FROM projection_checkpoints WHERE projector = @projector",
            ("projector", SampleProjector.ProjectorName));

        Assert.False(await sample.Runner.ApplyAsync(batch, readAfter, cancellationToken));
        Assert.Empty(await sample.AppliedPositionsAsync());
        Assert.Equal(0L, await sample.Database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM projection_checkpoints WHERE projector = @projector",
            ("projector", SampleProjector.ProjectorName)));

        // The rebuild then applies every position from 0.
        sample.Wakeup.Wake();
        await JournalWait.UntilCheckpointAsync(sample.Database, SampleProjector.ProjectorName, last);
        Assert.Equal(await sample.JournalPositionsAsync(), await sample.AppliedPositionsAsync());
    }

    [Fact]
    public async Task AProjectorFailureRollsBackReadModelAndCheckpointTogether()
    {
        var run = Guid.NewGuid().ToString("N");
        var healthy = sample.Grain($"fault-ok-{run}");
        var faulty = sample.Grain($"fault-bad-{run}");
        var projector = sample.SiloServices.GetRequiredService<SampleProjector>();

        // Start from a caught-up projector.
        var before = await sample.LastPositionAsync();
        sample.Wakeup.Wake();
        await JournalWait.UntilCheckpointAsync(sample.Database, SampleProjector.ProjectorName, before);
        var checkpoint = await sample.CheckpointAsync();
        var applied = await sample.AppliedPositionsAsync();
        var faults = projector.Faults;

        // The healthy event comes first in the batch, so it has been written when the faulty one throws.
        projector.FailOnceOn(await faulty.GetStreamId());
        await healthy.Create("healthy");
        await faulty.Create("faulty");
        var last = await sample.LastPositionAsync();

        sample.Wakeup.Wake();
        await JournalWait.UntilAsync(() => Task.FromResult(projector.Faults > faults), "the sample projector has thrown");
        await JournalWait.UntilAsync(() => Task.FromResult(sample.Runner.IsWaiting), "the runner waits again after the failure");

        Assert.Equal(checkpoint, await sample.CheckpointAsync());
        Assert.Equal(applied, await sample.AppliedPositionsAsync());
        Assert.DoesNotContain(await sample.ReadModelAsync(), row => row.StreamId == SampleGrain.StreamIdOf($"fault-ok-{run}"));

        // The fault has cleared; the next wake applies both events, each exactly once.
        sample.Wakeup.Wake();
        await JournalWait.UntilCheckpointAsync(sample.Database, SampleProjector.ProjectorName, last);

        Assert.Equal(await sample.JournalPositionsAsync(), await sample.AppliedPositionsAsync());
        var readModel = await sample.ReadModelAsync();
        Assert.Contains((SampleGrain.StreamIdOf($"fault-ok-{run}"), "healthy", 0, 0), readModel);
        Assert.Contains((SampleGrain.StreamIdOf($"fault-bad-{run}"), "faulty", 0, 0), readModel);
    }

    [Fact]
    public async Task WithoutAStreamProviderTheProjectorConvergesOnTheNextPoll()
    {
        // Let the runner finish whatever it is doing and wait for its next poll.
        await JournalWait.UntilAsync(() => Task.FromResult(sample.Runner.IsWaiting), "the projection runner waits for its next poll");

        var grain = sample.Grain($"poll-{Guid.NewGuid():N}");
        await grain.Create("P");
        await grain.Note("p1", 6);
        var last = await sample.LastPositionAsync();

        // Nothing wakes the runner: no stream provider, and the fake clock has not moved.
        Assert.True(await sample.CheckpointAsync() < last);

        sample.Time.Advance(SampleCluster.PollInterval);

        await JournalWait.UntilCheckpointAsync(sample.Database, SampleProjector.ProjectorName, last);
        Assert.Contains((await grain.GetStreamId(), "P", 1, 6), await sample.ReadModelAsync());
    }
}
