using Coldframe.Contracts.Sites;
using Coldframe.Server.Journal;

namespace Coldframe.Server.Identity;

/// <summary>
/// A Site, keyed by its Site ID. The only writer of the Site's Memberships and Roles, both in its journal
/// and in Phase Two (AD-1, AD-3). It checks its own persisted state, calls Keycloak, then persists.
/// </summary>
[GrainType("site")]
public sealed partial class SiteGrain(
    IPhaseTwoOrganizations organizations,
    ILogger<SiteGrain> logger) : JournaledStreamGrain<SiteState>, ISiteGrain
{
    /// <summary>
    /// The Organization roles every Site has, one per <see cref="SiteRole"/>.
    /// </summary>
    public static readonly IReadOnlyDictionary<SiteRole, string> OrganizationRoles = new Dictionary<SiteRole, string>
    {
        [SiteRole.Owner] = "owner",
        [SiteRole.Administrator] = "administrator",
        [SiteRole.Member] = "member",
    };

    private string SiteId => this.GetPrimaryKeyString();

    /// <inheritdoc />
    public async Task<SiteInitializationResult> Initialize(string name, string ownerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        switch (State.Lifecycle)
        {
            case SiteLifecycle.Active when State.Owners.Contains(ownerId):
                // Idempotent: a retry after the events were journaled only brings the projection up to date,
                // not cancelled by the caller's budget, as on the first initialization.
                await CatchUpIdentityAsync(CancellationToken.None);
                return new SiteInitializationResult(SiteInitializationOutcome.Initialized, State.Name);
            case SiteLifecycle.Active or SiteLifecycle.Deleted:
                return new SiteInitializationResult(SiteInitializationOutcome.Conflict);
        }

        try
        {
            foreach (var role in OrganizationRoles.Values)
            {
                await organizations.EnsureRoleAsync(SiteId, role, cancellationToken);
            }

            await organizations.AddMemberAsync(SiteId, ownerId, cancellationToken);
            await organizations.GrantRoleAsync(SiteId, OrganizationRoles[SiteRole.Owner], ownerId, cancellationToken);
        }
        catch (Exception exception) when (exception is IdentityProviderUnavailableException or OperationCanceledException)
        {
            // Unreachable, failing, or too slow for the caller's budget. Nothing was journaled; the
            // Keycloak writes so far are idempotent, so a retry resumes.
            LogIdentityProviderUnavailable(logger, SiteId, exception);
            return new SiteInitializationResult(SiteInitializationOutcome.IdentityProviderUnavailable);
        }

        RaiseEvent(new SiteCreated(name, ownerId));
        RaiseEvent(new MembershipGranted(ownerId, SiteRole.Owner));
        await ConfirmEvents();

        // Not cancelled by the caller's budget: the events are journaled, so the projection follows.
        await CatchUpIdentityAsync(CancellationToken.None);

        return new SiteInitializationResult(SiteInitializationOutcome.Initialized, State.Name);
    }

    // Read-your-writes: the identity projector stays the only writer of its read model; the grain only
    // drives it, so the next request sees the Membership without waiting for a hint or a poll.
    private Task CatchUpIdentityAsync(CancellationToken cancellationToken) =>
        ServiceProvider.GetProjectionRunner<IdentityProjector>().CatchUpAsync(cancellationToken);

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Keycloak is unavailable while initializing Site {SiteId}; nothing was journaled.")]
    private static partial void LogIdentityProviderUnavailable(ILogger logger, string siteId, Exception exception);
}
