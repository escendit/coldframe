using Temporalio.Common;
using Temporalio.Workflows;

namespace Coldframe.Server.Identity.Reconciliation;

/// <summary>
/// The workflow <c>keycloak-temporal-extensions</c> starts for every Keycloak admin event. Deterministic:
/// it only runs <see cref="IdentityReconciliationActivities.ReconcileAsync"/>, which does all I/O.
/// </summary>
[Workflow("IdentityAdminEvent")]
public sealed class IdentityAdminEventWorkflow
{
    /// <summary>
    /// How one reconciliation is bounded and retried: 1 s, doubling, at most 1 min apart, without limit.
    /// </summary>
    public static readonly ActivityOptions ReconcileOptions = new()
    {
        StartToCloseTimeout = TimeSpan.FromSeconds(45),
        RetryPolicy = new RetryPolicy
        {
            InitialInterval = TimeSpan.FromSeconds(1),
            BackoffCoefficient = 2,
            MaximumInterval = TimeSpan.FromMinutes(1),
            MaximumAttempts = 0,
        },
    };

    /// <summary>
    /// Reconciles the Site the event touches.
    /// </summary>
    /// <param name="adminEvent">The admin event.</param>
    [WorkflowRun]
    public Task RunAsync(KeycloakAdminEvent adminEvent) =>
        Workflow.ExecuteActivityAsync(
            (IdentityReconciliationActivities activities) => activities.ReconcileAsync(adminEvent),
            ReconcileOptions);
}

/// <summary>
/// The workflow <c>keycloak-temporal-extensions</c> starts for every Keycloak user event. Deterministic: it only
/// runs <see cref="IdentityReconciliationActivities.ReconcileUserEventAsync"/>, which reconciles the Site of an
/// accepted Phase Two invitation (Story 9.1) and completes every other user event at once.
/// </summary>
[Workflow("IdentityUserEvent")]
public sealed class IdentityUserEventWorkflow
{
    /// <summary>
    /// Reconciles the Site the event touches, if any, bounded and retried like an admin event.
    /// </summary>
    /// <param name="userEvent">The user event.</param>
    [WorkflowRun]
    public Task RunAsync(KeycloakUserEvent userEvent)
    {
        ArgumentNullException.ThrowIfNull(userEvent);

        // Sign-ins and the like complete without an activity; the activity checks the rest of the route.
        if (userEvent.Error is not null
            || !string.Equals(userEvent.Type, UserEventRoute.InvitationActionType, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        return Workflow.ExecuteActivityAsync(
            (IdentityReconciliationActivities activities) => activities.ReconcileUserEventAsync(userEvent),
            IdentityAdminEventWorkflow.ReconcileOptions);
    }
}
