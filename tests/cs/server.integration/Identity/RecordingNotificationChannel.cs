using System.Collections.Concurrent;
using Coldframe.Contracts.Notifications;
using Coldframe.Server.Notifications;

namespace Coldframe.Server.IntegrationTests.Identity;

/// <summary>
/// One notification as the Notifier handed it to the channel.
/// </summary>
public sealed record RecordedNotification(Notification Notification, DateTimeOffset SentAt);

/// <summary>
/// The Notifier test double (Story 6.4): a channel that records every notification it is handed, and that a test
/// can make fail the next sends to one User, the way a push provider that is down does. It decides nothing, as
/// no channel does.
/// </summary>
public sealed class RecordingNotificationChannel : INotificationChannel
{
    private readonly Lock _lock = new();
    private readonly List<RecordedNotification> _sent = [];
    private readonly ConcurrentDictionary<string, int> _failures = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _withoutDevice = new(StringComparer.Ordinal);

    /// <summary>
    /// How many sends were failed.
    /// </summary>
    public int Failed { get; private set; }

    /// <summary>
    /// Everything sent to <paramref name="userId"/>, in the order it was sent.
    /// </summary>
    public IReadOnlyList<RecordedNotification> For(string userId)
    {
        lock (_lock)
        {
            return [.. _sent.Where(sent => string.Equals(sent.Notification.UserId, userId, StringComparison.Ordinal))];
        }
    }

    /// <summary>
    /// Makes the channel answer for <paramref name="userId"/> as a channel does on which the User has no device:
    /// it still records, and reports nothing delivered, so the push channels alone decide whether a send counts.
    /// </summary>
    public void WithoutDevice(string userId) => _withoutDevice[userId] = true;

    /// <summary>
    /// Makes the next <paramref name="sends"/> sends to <paramref name="userId"/> fail.
    /// </summary>
    public void FailNext(string userId, int sends) => _failures[userId] = sends;

    public Task<ChannelDelivery> SendAsync(Notification notification, DateTimeOffset sentAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        lock (_lock)
        {
            if (_failures.TryGetValue(notification.UserId, out var left) && left > 0)
            {
                _failures[notification.UserId] = left - 1;
                Failed++;
                throw new InvalidOperationException("The test failed this send.");
            }

            _sent.Add(new RecordedNotification(notification, sentAt));

            // It stands for a device that took the notification, unless a test wants the User to have none here.
            return Task.FromResult(_withoutDevice.ContainsKey(notification.UserId) ? ChannelDelivery.Nothing : ChannelDelivery.To(1));
        }
    }
}
