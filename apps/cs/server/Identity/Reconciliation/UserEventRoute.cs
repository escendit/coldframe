using Coldframe.Contracts.Sites;

namespace Coldframe.Server.Identity.Reconciliation;

/// <summary>
/// Where a Keycloak user event goes (Story 9.1). Only one user event touches a roster: accepting a Phase Two
/// invitation, which grants the Membership and the Organization role directly and emits no admin event.
/// </summary>
public static class UserEventRoute
{
    /// <summary>
    /// The event type Phase Two's invitation required action emits, accepted or rejected.
    /// </summary>
    public const string InvitationActionType = "CUSTOM_REQUIRED_ACTION";

    /// <summary>
    /// The detail that names the Organization of the invitation.
    /// </summary>
    public const string OrganizationDetail = "org_id";

    /// <summary>
    /// Routes a user event. Pure.
    /// </summary>
    /// <param name="userEvent">The user event.</param>
    /// <param name="realmId">The ID of the Coldframe realm.</param>
    /// <returns>
    /// The Site the accepting User joined, expecting them on its roster, or <see langword="null"/> when the
    /// event is ignored: another realm, an error (a rejected invitation carries one), another type, or no User
    /// or Organization.
    /// </returns>
    public static AdminEventRoute? From(KeycloakUserEvent userEvent, string realmId)
    {
        ArgumentNullException.ThrowIfNull(userEvent);
        ArgumentException.ThrowIfNullOrWhiteSpace(realmId);

        if (!string.Equals(userEvent.RealmId, realmId, StringComparison.Ordinal)
            || userEvent.Error is not null
            || !string.Equals(userEvent.Type, InvitationActionType, StringComparison.Ordinal)
            || userEvent.UserId is not { Length: > 0 } userId
            || userEvent.Details?.GetValueOrDefault(OrganizationDetail) is not { Length: > 0 } organizationId)
        {
            return null;
        }

        return new AdminEventRoute(organizationId, new RosterExpectation(RosterExpectationKind.MemberPresent, userId));
    }
}
