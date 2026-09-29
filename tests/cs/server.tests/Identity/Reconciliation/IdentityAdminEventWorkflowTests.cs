using Coldframe.Server.Identity.Reconciliation;

namespace Coldframe.Server.Tests.Identity.Reconciliation;

/// <summary>
/// How the <c>IdentityAdminEvent</c> workflow bounds and retries its activity.
/// </summary>
public sealed class IdentityAdminEventWorkflowTests
{
    [Fact]
    public void TheActivityIsBoundedAt45SecondsAndRetriedWithoutLimit()
    {
        var options = IdentityAdminEventWorkflow.ReconcileOptions;
        var retry = options.RetryPolicy;

        Assert.Equal(TimeSpan.FromSeconds(45), options.StartToCloseTimeout);
        Assert.NotNull(retry);
        Assert.Equal(TimeSpan.FromSeconds(1), retry.InitialInterval);
        Assert.Equal(2f, retry.BackoffCoefficient);
        Assert.Equal(TimeSpan.FromMinutes(1), retry.MaximumInterval);

        // Unlimited, so attempt AcceptUnconfirmedFromAttempt is always reached.
        Assert.Equal(0, retry.MaximumAttempts);
    }
}
