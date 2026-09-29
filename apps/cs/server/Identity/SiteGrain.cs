using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Lots;
using Coldframe.Contracts.Sites;
using Coldframe.Server.Identity.Reconciliation;
using Coldframe.Server.Journal;
using Microsoft.Extensions.Options;

namespace Coldframe.Server.Identity;

/// <summary>
/// A Site, keyed by its Site ID. The only writer of the Site's Memberships and Roles, both in its journal
/// and in Phase Two (AD-1, AD-3). It checks its own persisted state, calls Keycloak, then persists.
/// Reconciliation only reads Keycloak and never writes back to it.
/// </summary>
[GrainType("site")]
public sealed partial class SiteGrain(
    IPhaseTwoOrganizations organizations,
    IOptions<KeycloakOptions> options,
    ILogger<SiteGrain> logger) : JournaledStreamGrain<SiteState>, ISiteGrain
{
    /// <summary>
    /// The stable event ID of the operator-visible error for an ownerless edit in Keycloak.
    /// </summary>
    public const int OwnerlessEditRefusedEventId = 3;

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

    /// <inheritdoc />
    public async Task<SiteReconciliationResult> Reconcile(
        RosterExpectation? expectation,
        bool acceptUnconfirmed,
        CancellationToken cancellationToken = default)
    {
        // Sites come into existence only through Initialize; deletion is terminal (AD-20).
        if (State.Lifecycle != SiteLifecycle.Active)
        {
            return Result(SiteReconciliationOutcome.Ignored);
        }

        PhaseTwoRoster? roster;

        using (var budget = new CancellationTokenSource(options.Value.OperationBudget, Clock))
        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(budget.Token, cancellationToken))
        {
            try
            {
                roster = await organizations.GetRosterAsync(SiteId, linked.Token);
            }
            catch (Exception exception) when (exception is IdentityProviderUnavailableException or OperationCanceledException)
            {
                LogReconciliationUnavailable(logger, SiteId, exception);
                return Result(SiteReconciliationOutcome.IdentityProviderUnavailable);
            }
        }

        if (!acceptUnconfirmed && expectation is not null && !Shows(roster, expectation))
        {
            // The listener fires before Keycloak commits: this pull may have run too early.
            return Result(SiteReconciliationOutcome.NotYetVisible);
        }

        var changed = roster is null ? RaiseDeleted() : RaiseRosterDifferences(roster);

        if (changed)
        {
            await ConfirmEvents();
        }

        // Not cancelled by the caller: a finished reconciliation means the projection is current.
        await CatchUpIdentityAsync(CancellationToken.None);

        return Result(changed ? SiteReconciliationOutcome.Changed : SiteReconciliationOutcome.Unchanged);
    }

    /// <inheritdoc />
    public async Task<SiteRenameResult> Rename(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (State.Lifecycle != SiteLifecycle.Active)
        {
            return new SiteRenameResult(SiteRenameOutcome.NotFound);
        }

        if (string.Equals(State.Name, name, StringComparison.Ordinal))
        {
            return new SiteRenameResult(SiteRenameOutcome.Unchanged, State.Name);
        }

        // Keycloak first (AD-3): a rename journaled without it would be reverted by the next reconciliation.
        using (var budget = new CancellationTokenSource(options.Value.OperationBudget, Clock))
        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(budget.Token, cancellationToken))
        {
            try
            {
                await organizations.UpdateDisplayNameAsync(SiteId, name, linked.Token);
            }
            catch (Exception exception) when (exception is IdentityProviderUnavailableException or OperationCanceledException)
            {
                LogRenameUnavailable(logger, SiteId, exception);
                return new SiteRenameResult(SiteRenameOutcome.IdentityProviderUnavailable);
            }
        }

        RaiseEvent(new SiteRenamed(name));
        await ConfirmEvents();

        // Not cancelled by the caller: the rename is journaled, so the projection follows.
        await CatchUpIdentityAsync(CancellationToken.None);

        return new SiteRenameResult(SiteRenameOutcome.Renamed, State.Name);
    }

    /// <inheritdoc />
    public async Task<LotCreationResult> CreateLot(string callerId, string idempotencyKey, string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callerId);
        ArgumentException.ThrowIfNullOrEmpty(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (State.Lifecycle != SiteLifecycle.Active)
        {
            return new LotCreationResult(LotCreationOutcome.NotFound);
        }

        var key = $"{callerId}:{idempotencyKey}";
        var now = Clock.GetUtcNow();
        var creation = State.FindLiveLotCreation(key, now);

        if (creation is not null && !string.Equals(creation.Name, name, StringComparison.Ordinal))
        {
            return new LotCreationResult(LotCreationOutcome.IdempotencyKeyReused);
        }

        if (creation is null)
        {
            // Persisted before the Lot grain is called, so a retry resumes with the same Lot ID.
            RaiseEvent(new LotCreationRequested(key, Guid.CreateVersion7(now).ToString(), name, now));
            await ConfirmEvents();
            creation = State.LotCreations[key];
        }

        // Idempotent for this Site: a completed request answers with the Lot as it is now.
        var created = await GrainFactory.GetGrain<ILotGrain>(creation.LotId).Create(SiteId, creation.Name, cancellationToken);

        if (created is not { Outcome: LotOutcome.Created, Lot: { } lot })
        {
            throw new InvalidOperationException($"Lot {creation.LotId} refused to be created on Site {SiteId}: {created.Outcome}.");
        }

        if (!creation.Completed)
        {
            RaiseEvent(new LotCreationCompleted(key));
            await ConfirmEvents();
        }

        return new LotCreationResult(LotCreationOutcome.Created, lot);
    }

    /// <inheritdoc />
    public async Task<DeviceRegistrationResult> RegisterDevice(
        string deviceId,
        DeviceKind kind,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentException.ThrowIfNullOrEmpty(idempotencyKey);

        if (State.Lifecycle != SiteLifecycle.Active)
        {
            return new DeviceRegistrationResult(DeviceRegistrationOutcome.NotFound);
        }

        var now = Clock.GetUtcNow();

        if (State.FindLiveDeviceRegistration(idempotencyKey, now) is { } registration
            && !string.Equals(registration.DeviceId, deviceId, StringComparison.Ordinal))
        {
            return new DeviceRegistrationResult(DeviceRegistrationOutcome.IdempotencyKeyReused);
        }

        // A Device already on the roster, by this key or another, journals nothing.
        if (!State.Devices.ContainsKey(deviceId))
        {
            RaiseEvent(new DeviceRegistered(deviceId, kind, idempotencyKey, now));
            await ConfirmEvents();
        }

        // Site Pause arrives in Epic 8 (AD-8); until then a Site is never paused.
        return new DeviceRegistrationResult(DeviceRegistrationOutcome.Registered, SitePause.NotPaused);
    }

    // Does the pulled roster show what the event says? A missing Organization shows no member and no role.
    private static bool Shows(PhaseTwoRoster? roster, RosterExpectation expectation)
    {
        var userId = expectation.UserId ?? string.Empty;
        var role = expectation.Role is { } siteRole ? OrganizationRoles[siteRole] : null;

        return expectation.Kind switch
        {
            RosterExpectationKind.OrganizationAbsent => roster is null,
            RosterExpectationKind.MemberPresent => roster is not null && roster.MemberIds.Contains(userId),
            RosterExpectationKind.MemberAbsent => roster is null || !roster.MemberIds.Contains(userId),
            RosterExpectationKind.RoleHeld => roster is not null && role is not null && roster.Holds(role, userId),
            RosterExpectationKind.RoleNotHeld => roster is null || role is null || !roster.Holds(role, userId),
            _ => true,
        };
    }

    private bool RaiseDeleted()
    {
        RaiseEvent(new SiteDeleted());
        return true;
    }

    // Raises the rename, the ownerless-episode events and the plan's grants and revocations.
    private bool RaiseRosterDifferences(PhaseTwoRoster roster)
    {
        var raised = false;

        if (!string.IsNullOrWhiteSpace(roster.DisplayName) && !string.Equals(roster.DisplayName, State.Name, StringComparison.Ordinal))
        {
            RaiseEvent(new SiteRenamed(roster.DisplayName));
            raised = true;
        }

        var plan = SiteRosterPlan.Plan(State.Members, State.Owners, roster);

        if (plan.Ownerless)
        {
            // AD-3 break-glass: never repaired silently, never written back. Logged on every such reconcile,
            // journaled once per episode.
            LogOwnerlessEditRefused(logger, SiteId, string.Join(", ", plan.KeptOwners));

            if (!State.OwnerlessEditRefused)
            {
                RaiseEvent(new SiteOwnerlessEditRefused(plan.KeptOwners));
                raised = true;
            }
        }
        else if (State.OwnerlessEditRefused)
        {
            RaiseEvent(new SiteOwnerlessEditResolved());
            raised = true;
        }

        foreach (var grant in plan.Grants)
        {
            RaiseEvent(grant);
        }

        foreach (var revocation in plan.Revocations)
        {
            RaiseEvent(revocation);
        }

        return raised || plan.ChangesMemberships;
    }

    // The outcome and the Site's state afterwards, including tentative events not yet confirmed.
    private SiteReconciliationResult Result(SiteReconciliationOutcome outcome) =>
        new(
            outcome,
            TentativeState.Lifecycle,
            new Dictionary<string, SiteRole>(TentativeState.Members, StringComparer.Ordinal),
            [.. TentativeState.FormerMembers.Order(StringComparer.Ordinal)]);

    // Read-your-writes: the identity projector stays the only writer of its read model; the grain only
    // drives it, so the next request sees the Membership without waiting for a hint or a poll.
    private Task CatchUpIdentityAsync(CancellationToken cancellationToken) =>
        ServiceProvider.GetProjectionRunner<IdentityProjector>().CatchUpAsync(cancellationToken);

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Keycloak is unavailable while initializing Site {SiteId}; nothing was journaled.")]
    private static partial void LogIdentityProviderUnavailable(ILogger logger, string siteId, Exception exception);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Keycloak is unavailable while reconciling Site {SiteId}; nothing was journaled.")]
    private static partial void LogReconciliationUnavailable(ILogger logger, string siteId, Exception exception);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Keycloak is unavailable while renaming Site {SiteId}; nothing was journaled.")]
    private static partial void LogRenameUnavailable(ILogger logger, string siteId, Exception exception);

    [LoggerMessage(
        EventId = OwnerlessEditRefusedEventId,
        EventName = "OwnerlessEditRefused",
        Level = LogLevel.Error,
        Message = "Keycloak shows Site {SiteId} without an Owner. Coldframe keeps the Owners {KeptOwners} and does not change Keycloak; give the Organization an 'owner' again in Keycloak.")]
    private static partial void LogOwnerlessEditRefused(ILogger logger, string siteId, string keptOwners);
}
