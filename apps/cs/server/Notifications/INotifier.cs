using Coldframe.Contracts.Notifications;

namespace Coldframe.Server.Notifications;

/// <summary>
/// The Notifier seam (Story 6.4): where the User grain hands over a notification that is due. The seam holds no
/// timing and no filtering logic: the User grain alone decided that the notification is due, inside the
/// Notification Window and not muted.
/// </summary>
public interface INotifier
{
    /// <summary>
    /// Sends <paramref name="notification"/> through every registered <see cref="INotificationChannel"/> and
    /// records its <c>dueAt</c> and <c>sentAt</c>. Delivery is at-least-once: when this throws (no channel
    /// delivered), the User grain keeps the delivery due and hands it over again on its next wake.
    /// </summary>
    /// <param name="notification">The notification.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>When the notification was sent (UTC), from the <see cref="TimeProvider"/>.</returns>
    Task<DateTimeOffset> SendAsync(Notification notification, CancellationToken cancellationToken = default);
}
