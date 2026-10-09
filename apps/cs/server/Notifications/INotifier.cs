using Coldframe.Contracts.Notifications;

namespace Coldframe.Server.Notifications;

/// <summary>
/// What the Notifier did with one notification.
/// </summary>
/// <param name="SentAt">When the notification was sent (UTC), from the <see cref="TimeProvider"/>.</param>
/// <param name="InvalidInstallations">
/// The installation IDs of the User's push registrations a provider reported as invalid (Story 6.5): the User
/// grain journals their removal.
/// </param>
public sealed record NotifierResult(DateTimeOffset SentAt, IReadOnlyList<string> InvalidInstallations);

/// <summary>
/// No channel delivered a notification and at least one failed: the delivery stays due.
/// </summary>
public sealed class NotificationNotDeliveredException : Exception
{
    /// <summary>
    /// Creates the exception.
    /// </summary>
    public NotificationNotDeliveredException()
        : this("No channel delivered the notification.")
    {
    }

    /// <summary>
    /// Creates the exception with <paramref name="message"/>.
    /// </summary>
    public NotificationNotDeliveredException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates the exception with <paramref name="message"/> and its cause.
    /// </summary>
    public NotificationNotDeliveredException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates the exception over what the channels threw and the registrations the channels that did answer
    /// reported as invalid.
    /// </summary>
    public NotificationNotDeliveredException(string message, IReadOnlyList<Exception> failures, IReadOnlyList<string> invalidInstallations)
        : base(message, new AggregateException(failures))
    {
        Failures = failures;
        InvalidInstallations = invalidInstallations;
    }

    /// <summary>
    /// What each failed channel threw, in channel order.
    /// </summary>
    public IReadOnlyList<Exception> Failures { get; } = [];

    /// <summary>
    /// The installation IDs a provider reported as invalid although the send as a whole failed.
    /// </summary>
    public IReadOnlyList<string> InvalidInstallations { get; } = [];
}

/// <summary>
/// The Notifier seam (Story 6.4): where the User grain hands over a notification that is due. The seam holds no
/// timing and no filtering logic: the User grain alone decided that the notification is due, inside the
/// Notification Window and not muted.
/// </summary>
public interface INotifier
{
    /// <summary>
    /// Sends <paramref name="notification"/> through every registered <see cref="INotificationChannel"/> and
    /// records its <c>dueAt</c> and <c>sentAt</c>. Delivery is at-least-once: when this throws
    /// <see cref="NotificationNotDeliveredException"/> (a channel failed and none delivered), the User grain
    /// keeps the delivery due and hands it over again on its next wake.
    /// </summary>
    /// <param name="notification">The notification.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>When the notification was sent, and the push registrations that are no longer valid.</returns>
    Task<NotifierResult> SendAsync(Notification notification, CancellationToken cancellationToken = default);
}
