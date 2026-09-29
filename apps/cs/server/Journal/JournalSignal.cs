namespace Coldframe.Server.Journal;

/// <summary>
/// A silo-local wake-up: waiters take <see cref="WaitAsync"/> before they check for work, so a
/// <see cref="Wake"/> that arrives while they work is never lost.
/// </summary>
public abstract class JournalSignal
{
    private TaskCompletionSource _next = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// A task that completes on the next <see cref="Wake"/>.
    /// </summary>
    public Task WaitAsync() => Volatile.Read(ref _next).Task;

    /// <summary>
    /// Wakes every current waiter.
    /// </summary>
    public void Wake() =>
        Interlocked.Exchange(ref _next, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
}

/// <summary>
/// Wakes the projection runners of this silo. The journal hint grain raises it when a hint arrives.
/// </summary>
public sealed class ProjectionWakeup : JournalSignal;

/// <summary>
/// Wakes the outbox dispatcher of this silo after an append commits.
/// </summary>
public sealed class OutboxWakeup : JournalSignal;
