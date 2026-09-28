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
/// The workflow <c>keycloak-temporal-extensions</c> starts for every Keycloak user event. The Server does
/// not act on user events; it completes them so the queue never piles up.
/// </summary>
[Workflow("IdentityUserEvent")]
public sealed class IdentityUserEventWorkflow
{
    /// <summary>
    /// Completes at once.
    /// </summary>
    /// <param name="userEvent">The user event, unused.</param>
    [WorkflowRun]
    public Task RunAsync(KeycloakUserEvent userEvent) => Task.CompletedTask;
}
