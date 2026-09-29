namespace Coldframe.Server.IntegrationTests.Journal;

/// <summary>
/// Waits for the journal or a projector to reach a state, with a bounded timeout (AD-6).
/// It polls on the real clock, independent of any <c>FakeTimeProvider</c> the Server under test uses,
/// and never sleeps a fixed time in place of a condition.
/// </summary>
public static class JournalWait
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(25);

    /// <summary>
    /// Waits until <paramref name="condition"/> holds, or fails the test after the timeout.
    /// </summary>
    public static async Task UntilAsync(Func<Task<bool>> condition, string description, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(condition);

        var limit = timeout ?? DefaultTimeout;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(limit);
        using var poll = new PeriodicTimer(PollInterval, TimeProvider.System);

        try
        {
            do
            {
                if (await condition())
                {
                    return;
                }
            }
            while (await poll.WaitForNextTickAsync(deadline.Token));
        }
        catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
        {
        }

        Assert.Fail($"Timed out after {limit.TotalSeconds:0} s waiting until {description}.");
    }

    /// <summary>
    /// Waits until the projector's checkpoint reaches <paramref name="position"/>.
    /// </summary>
    public static Task UntilCheckpointAsync(JournalDatabase database, string projector, long position, TimeSpan? timeout = null) =>
        UntilAsync(
            async () => await database.ScalarAsync<long>(
                "SELECT COALESCE((SELECT position FROM projection_checkpoints WHERE projector = @projector), 0)",
                ("projector", projector)) >= position,
            $"the checkpoint of '{projector}' reaches position {position}",
            timeout);

    /// <summary>
    /// Waits until the journal's last global position reaches <paramref name="position"/>.
    /// </summary>
    public static Task UntilPositionAsync(JournalDatabase database, long position, TimeSpan? timeout = null) =>
        UntilAsync(
            async () => await database.ScalarAsync<long>("SELECT COALESCE(MAX(position), 0) FROM journal_events") >= position,
            $"the journal reaches position {position}",
            timeout);
}
