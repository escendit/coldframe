namespace Coldframe.Server.Identity.Reconciliation;

/// <summary>
/// Where the Server's Temporal workers pick up the workflows <c>keycloak-temporal-extensions</c> starts
/// for Keycloak's events (AD-3, AD-5).
/// </summary>
public sealed class KeycloakEventOptions
{
    /// <summary>
    /// The configuration section.
    /// </summary>
    public const string SectionName = "KeycloakEvents";

    /// <summary>
    /// The Temporal frontend, as <c>host:port</c>.
    /// </summary>
    public string? TargetHost { get; set; }

    /// <summary>
    /// The Temporal namespace the listener starts its workflows in.
    /// </summary>
    public string? Namespace { get; set; }

    /// <summary>
    /// The ID (not the name) of the Coldframe realm. Events of any other realm are ignored.
    /// </summary>
    public string? RealmId { get; set; }

    /// <summary>
    /// The task queue of the <c>IdentityAdminEvent</c> workflow.
    /// </summary>
    public string AdminTaskQueue { get; set; } = "keycloak-admin-queue";

    /// <summary>
    /// The task queue of the <c>IdentityUserEvent</c> workflow.
    /// </summary>
    public string UserTaskQueue { get; set; } = "keycloak-user-queue";
}
