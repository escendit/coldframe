using Coldframe.Contracts.Notifications;
using Coldframe.Server.Notifications;
using Coldframe.Server.Notifications.Push;

namespace Coldframe.Server.Live;

/// <summary>
/// The Notifier's channel to the open web app (Story 6.6): sends <c>notification.delivered</c> to every open
/// hub connection of the notified User. It holds no timing, window, mute or cadence logic and never filters;
/// the text is the push text (<see cref="PushContentBuilder"/>), so the web and a phone show the same words.
/// </summary>
/// <remarks>
/// With no open connection of the User it sends nothing, looks nothing up and keeps nothing for later. Its
/// answer is always <see cref="ChannelDelivery.Nothing"/>, also after it sent: an open tab is no proof that
/// the User was told, because each browser decides whether it shows the notification (the toggle is per
/// browser, and the Server does not know it). So a tab never stands in for a phone: when a push channel
/// failed, the Notifier still fails the send and the User grain repeats it. <c>Nothing</c> is no failure
/// either, so with no phone and no tab the User grain records the delivery as sent. The notification's tag is
/// the collapse identity, so a repeated send and a second tab do not stack. The channel runs in the User
/// grain's turn and never calls that grain; its wait is bounded by <see cref="SendBudget"/>, and a send that
/// fails or runs out of it throws.
/// </remarks>
public sealed class LiveNotificationChannel(
    LiveConnections connections,
    ILiveSender sender,
    PushContentBuilder content,
    TimeProvider clock) : INotificationChannel
{
    /// <summary>
    /// How long one send may take: the User grain waits for it.
    /// </summary>
    public static readonly TimeSpan SendBudget = TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    public async Task<ChannelDelivery> SendAsync(Notification notification, DateTimeOffset sentAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (connections.OfUser(notification.UserId).Count == 0)
        {
            return ChannelDelivery.Nothing;
        }

        // Bounded by the builder's own lookup budget; null when there is nothing to say.
        if (await content.BuildAsync(notification, sentAt).ConfigureAwait(false) is not { } built)
        {
            return ChannelDelivery.Nothing;
        }

        // Read again: a tab may have closed, or opened, while the text was looked up.
        var open = connections.OfUser(notification.UserId);

        if (open.Count == 0)
        {
            return ChannelDelivery.Nothing;
        }

        // The source owns its timer and disposes it with itself: nothing can cancel it after it is gone.
        using var budget = new CancellationTokenSource(SendBudget, clock);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(budget.Token, cancellationToken);

        await sender
            .SendAsync(open, LiveMessages.NotificationDelivered, NotificationDeliveredMessage.From(built), bounded.Token)
            .ConfigureAwait(false);

        // Sent, not delivered: whether a browser shows it is the browser's own choice.
        return ChannelDelivery.Nothing;
    }
}
