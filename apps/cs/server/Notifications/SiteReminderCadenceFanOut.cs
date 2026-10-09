using Coldframe.Contracts.Sites;

namespace Coldframe.Server.Notifications;

/// <summary>
/// How setting a Site's Reminder cadence and handing it to the members ended.
/// </summary>
public enum SiteReminderCadenceDelivery
{
    /// <summary>
    /// The Site holds the cadence and every member's User grain was handed it.
    /// </summary>
    Delivered,

    /// <summary>
    /// The Site is not active. Nothing changed.
    /// </summary>
    SiteNotFound,

    /// <summary>
    /// The Site holds the cadence, but a member's User grain could not be handed it. The same request again
    /// finishes it, and so does the next reconciliation of the Site.
    /// </summary>
    NotDelivered,
}

/// <summary>
/// Sets a Site's Reminder cadence and hands it to every member's User grain (Story 6.3). The Site grain
/// never calls User grains (AD-3), so the caller does, as the reconciliation activity does for Memberships.
/// </summary>
public static partial class SiteReminderCadenceFanOut
{
    /// <summary>
    /// Writes the cadence to the Site grain, then calls <see cref="IUserGrain.SyncSiteReminderCadence"/> for
    /// every member the grain names. The members are handed it also when the Site already had the cadence,
    /// so repeating a request repairs a fan-out that failed halfway.
    /// </summary>
    /// <param name="grains">The grain factory.</param>
    /// <param name="siteId">The canonical Site ID.</param>
    /// <param name="cadence">The cadence.</param>
    /// <param name="logger">Where a failed hand-over is logged.</param>
    /// <param name="cancellationToken">Cancels the calls.</param>
    public static async Task<(SiteReminderCadenceDelivery Delivery, ReminderCadence Cadence)> SetAsync(
        IGrainFactory grains,
        string siteId,
        ReminderCadence cadence,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grains);
        ArgumentException.ThrowIfNullOrWhiteSpace(siteId);
        ArgumentNullException.ThrowIfNull(logger);

        var result = await grains.GetGrain<ISiteGrain>(siteId).SetReminderCadence(cadence, cancellationToken).ConfigureAwait(false);

        if (result.Outcome == SiteReminderCadenceOutcome.NotFound)
        {
            return (SiteReminderCadenceDelivery.SiteNotFound, result.Cadence);
        }

        try
        {
            await SyncMembersAsync(grains, siteId, result.Members, result.Cadence, cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Whatever kept a User grain from answering, the Site holds the cadence: the caller is told to send it again.
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
        {
            LogNotDelivered(logger, siteId, exception);
            return (SiteReminderCadenceDelivery.NotDelivered, result.Cadence);
        }

        return (SiteReminderCadenceDelivery.Delivered, result.Cadence);
    }

    /// <summary>
    /// Hands <paramref name="cadence"/> to the User grain of every one of <paramref name="members"/>. A User
    /// grain that already holds it journals nothing.
    /// </summary>
    /// <param name="grains">The grain factory.</param>
    /// <param name="siteId">The Site ID.</param>
    /// <param name="members">The User IDs of the Site's members.</param>
    /// <param name="cadence">The Site's Reminder cadence.</param>
    /// <param name="cancellationToken">Cancels the calls.</param>
    public static async Task SyncMembersAsync(
        IGrainFactory grains,
        string siteId,
        IEnumerable<string> members,
        ReminderCadence cadence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grains);
        ArgumentNullException.ThrowIfNull(members);

        foreach (var userId in members)
        {
            await grains.GetGrain<IUserGrain>(userId)
                .SyncSiteReminderCadence(siteId, cadence, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Site {SiteId} holds its Reminder cadence, but not every member was handed it; the request is to be sent again.")]
    private static partial void LogNotDelivered(ILogger logger, string siteId, Exception exception);
}
