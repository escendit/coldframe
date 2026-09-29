using Orleans.Streams;
using Orleans.Streams.Core;

namespace Coldframe.Server.Journal;

/// <summary>
/// Receives journal hints.
/// </summary>
public interface IJournalHintGrain : IGrainWithGuidKey;

/// <summary>
/// Subscribes implicitly to the hint stream and wakes the projection runners of its silo.
/// </summary>
[ImplicitStreamSubscription(JournalHints.StreamNamespace)]
internal sealed partial class JournalHintGrain(ProjectionWakeup wakeup, ILogger<JournalHintGrain> logger) : Grain, IJournalHintGrain, IStreamSubscriptionObserver, IAsyncObserver<long>
{
    public Task OnSubscribed(IStreamSubscriptionHandleFactory handleFactory)
    {
        ArgumentNullException.ThrowIfNull(handleFactory);
        return handleFactory.Create<long>().ResumeAsync(this);
    }

    public Task OnNextAsync(long item, StreamSequenceToken? token = null)
    {
        wakeup.Wake();
        return Task.CompletedTask;
    }

    public Task OnCompletedAsync() => Task.CompletedTask;

    public Task OnErrorAsync(Exception ex)
    {
        LogStreamError(logger, ex);
        return Task.CompletedTask;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "The journal hint stream reported an error; projectors fall back to polling.")]
    private static partial void LogStreamError(ILogger logger, Exception exception);
}
