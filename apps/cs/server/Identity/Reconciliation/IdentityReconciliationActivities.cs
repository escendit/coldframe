using Coldframe.Contracts.Sites;
using Microsoft.Extensions.Options;
using Temporalio.Activities;
using Temporalio.Exceptions;

namespace Coldframe.Server.Identity.Reconciliation;

/// <summary>
/// The one activity of the Keycloak event pipeline: routes an admin event to its Site grain, then brings
/// the User grains of the Site's current and former members in line with it (AD-3). The Site grain never
/// calls User grains itself, so there is no grain-to-grain cycle with Create Site.
/// </summary>
public sealed partial class IdentityReconciliationActivities(
    IGrainFactory grains,
    IOptions<KeycloakEventOptions> options,
    ILogger<IdentityReconciliationActivities> logger)
{
    /// <summary>
    /// From this attempt on, a pulled roster that contradicts the event is taken as the truth: the event
    /// was lost to a rolled-back transaction or overtaken by a later change.
    /// </summary>
    public const int AcceptUnconfirmedFromAttempt = 5;

    /// <summary>
    /// The failure type when Keycloak does not show yet what the event says.
    /// </summary>
    public const string NotYetVisibleFailure = "RosterNotYetVisible";

    /// <summary>
    /// The failure type when Keycloak could not be reached.
    /// </summary>
    public const string IdentityProviderUnavailableFailure = "IdentityProviderUnavailable";

    /// <summary>
    /// Reconciles the Site the event touches and syncs its members' User grains. Retryable failures are
    /// thrown as <see cref="ApplicationFailureException"/>; an ignored event completes.
    /// </summary>
    /// <param name="adminEvent">The admin event.</param>
    [Activity("Reconcile")]
    public async Task ReconcileAsync(KeycloakAdminEvent adminEvent)
    {
        ArgumentNullException.ThrowIfNull(adminEvent);

        var realmId = options.Value.RealmId
            ?? throw new InvalidOperationException("KeycloakEvents:RealmId is not configured.");

        if (AdminEventRoute.From(adminEvent, realmId) is not { } route)
        {
            LogIgnored(logger, adminEvent.Id, adminEvent.ResourceType, adminEvent.OperationType);
            return;
        }

        var context = ActivityExecutionContext.Current;
        var cancellationToken = context.CancellationToken;
        var acceptUnconfirmed = context.Info.Attempt >= AcceptUnconfirmedFromAttempt;

        var result = await grains
            .GetGrain<ISiteGrain>(route.SiteId)
            .Reconcile(route.Expectation, acceptUnconfirmed, cancellationToken)
            .ConfigureAwait(false);

        switch (result.Outcome)
        {
            case SiteReconciliationOutcome.NotYetVisible:
                throw new ApplicationFailureException(
                    $"Keycloak does not show yet what admin event {adminEvent.Id} says about Site {route.SiteId}.",
                    NotYetVisibleFailure);
            case SiteReconciliationOutcome.IdentityProviderUnavailable:
                throw new ApplicationFailureException(
                    $"Keycloak is unavailable while reconciling Site {route.SiteId}.",
                    IdentityProviderUnavailableFailure);
        }

        if (result.Lifecycle is not (SiteLifecycle.Active or SiteLifecycle.Deleted))
        {
            return;
        }

        // Every run fans out to everyone, so a retry completes a fan-out that was cut short.
        var deleted = result.Lifecycle == SiteLifecycle.Deleted;

        foreach (var (userId, role) in result.Members)
        {
            var user = grains.GetGrain<IUserGrain>(userId);
            await user.SyncSiteMembership(route.SiteId, deleted ? null : role, cancellationToken).ConfigureAwait(false);

            // The Site's Reminder cadence goes with the Membership (Story 6.3): a member who joined after the
            // cadence was set, or whom the request's own fan-out missed, gets it here.
            if (!deleted)
            {
                await user.SyncSiteReminderCadence(route.SiteId, result.ReminderCadence, cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (var userId in result.FormerMembers)
        {
            await grains.GetGrain<IUserGrain>(userId)
                .SyncSiteMembership(route.SiteId, null, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Admin event {EventId} ({ResourceType} {OperationType}) does not touch a Coldframe Site; ignored.")]
    private static partial void LogIgnored(ILogger logger, string? eventId, string? resourceType, string? operationType);
}
