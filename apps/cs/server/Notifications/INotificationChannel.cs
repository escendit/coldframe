using Coldframe.Contracts.Notifications;

namespace Coldframe.Server.Notifications;

/// <summary>
/// What a channel did with one notification (Story 6.5).
/// </summary>
/// <param name="Delivered">
/// To how many devices or connections the channel handed the notification. 0 when the User has nothing the
/// channel could send to, which is no failure.
/// </param>
/// <param name="InvalidInstallations">
/// The installation IDs of the push registrations whose token the provider reported as invalid or unregistered.
/// The channel never calls the User grain (it runs inside that grain's turn): the grain journals their removal.
/// </param>
public sealed record ChannelDelivery(int Delivered, IReadOnlyList<string> InvalidInstallations)
{
    /// <summary>
    /// Nothing was sent and nothing is wrong: the User has no device on this channel.
    /// </summary>
    public static ChannelDelivery Nothing { get; } = new(0, []);

    /// <summary>
    /// The notification reached <paramref name="delivered"/> devices or connections.
    /// </summary>
    public static ChannelDelivery To(int delivered) => new(delivered, []);
}

/// <summary>
/// An exception a channel throws that still names the push registrations its provider no longer knows: the
/// Notifier hands them on although the channel failed.
/// </summary>
public interface IInvalidInstallationsSource
{
    /// <summary>
    /// The installation IDs of the registrations the provider reported as invalid before the channel failed.
    /// </summary>
    IReadOnlyList<string> InvalidInstallations { get; }
}

/// <summary>
/// One way a notification reaches a User (Story 6.4): push (Story 6.5) or the open web app (Story 6.6). A
/// channel only delivers: it never filters, delays or schedules, and it writes the text.
/// </summary>
public interface INotificationChannel
{
    /// <summary>
    /// Delivers <paramref name="notification"/>, within a time the channel bounds itself: the User grain waits
    /// for it. A User the channel has nothing to send to is a success with nothing delivered. A channel that
    /// throws is logged and the other channels are still tried. Only when a channel threw and no channel
    /// delivered does the send fail and the User grain repeat it, so a channel must tolerate the same
    /// notification twice, and one that failed while another delivered is not handed it again.
    /// </summary>
    /// <param name="notification">The notification.</param>
    /// <param name="sentAt">When the Notifier sent it (UTC).</param>
    /// <param name="cancellationToken">Cancels the delivery.</param>
    /// <returns>What was delivered, and the registrations the provider no longer knows.</returns>
    Task<ChannelDelivery> SendAsync(Notification notification, DateTimeOffset sentAt, CancellationToken cancellationToken = default);
}
