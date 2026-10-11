namespace Coldframe.Server.Testing;

/// <summary>
/// A clock a test can move forward (Story 6.6): the real time plus an offset that only grows. Timers and
/// delays keep their real length; only what the Server reads as "now" moves, so a window opening or a
/// Reminder that is hours away is reached at once.
/// </summary>
/// <remarks>
/// It exists for the end-to-end test that runs the Server as its own process, where no test can hand in a
/// fake clock. The Server uses it only when the host environment is Development and
/// <c>Testing:ManualClock</c> is <c>true</c> (<see cref="TestingHostingExtensions"/>); a deployment sets
/// neither. Access tokens are never measured on it.
/// </remarks>
/// <param name="inner">The real clock.</param>
public sealed class ManualClock(TimeProvider inner) : TimeProvider
{
    private long _offsetTicks;

    /// <summary>
    /// How far the clock runs ahead of the real time.
    /// </summary>
    public TimeSpan Offset => TimeSpan.FromTicks(Interlocked.Read(ref _offsetTicks));

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => inner.GetUtcNow() + Offset;

    /// <summary>
    /// Moves the clock forward to <paramref name="now"/>. A time that is not ahead of the clock changes nothing:
    /// the clock never goes back.
    /// </summary>
    /// <param name="now">The time the clock shows from now on.</param>
    /// <returns>Whether the clock moved.</returns>
    public bool AdvanceTo(DateTimeOffset now)
    {
        while (true)
        {
            var current = Interlocked.Read(ref _offsetTicks);
            var wanted = (now - inner.GetUtcNow()).Ticks;

            if (wanted <= current)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _offsetTicks, wanted, current) == current)
            {
                return true;
            }
        }
    }
}
