using Coldframe.Contracts.Notifications;

namespace Coldframe.Server.Notifications;

/// <summary>
/// One way a notification reaches a User (Story 6.4): push (Story 6.5) or the open web app (Story 6.6). None
/// ships with the seam. A channel only delivers: it never filters, delays or schedules, and it writes the text.
/// </summary>
public interface INotificationChannel
{
    /// <summary>
    /// Delivers <paramref name="notification"/>, within a time the channel bounds itself: the User grain waits
    /// for it. A channel that throws is logged and the other channels are still tried. Only when every channel
    /// threw does the send fail and the User grain repeat it, so a channel must tolerate the same notification
    /// twice, and one that failed alone is not handed it again.
    /// </summary>
    /// <param name="notification">The notification.</param>
    /// <param name="sentAt">When the Notifier sent it (UTC).</param>
    /// <param name="cancellationToken">Cancels the delivery.</param>
    Task SendAsync(Notification notification, DateTimeOffset sentAt, CancellationToken cancellationToken = default);
}
