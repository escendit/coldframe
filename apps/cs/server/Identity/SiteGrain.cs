using Coldframe.Contracts.Alerts;
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
/// Reconciliation only reads Keycloak and never writes back to it. It also keeps the set of the Site's open
/// Alerts (Story 6.1), as the Alert grains report them, and the Site's Reminder cadence (Story 6.3), and
/// never calls User grains: it answers with its members, and its caller tells them (Stories 6.3 and 6.4).
/// It owns the Site's Pause and its end date (Story 8.1, AD-8) and delivers it to every Device on its roster.
/// </summary>
/// <remarks>
/// <para>
/// Site Pause (Story 8.1; AD-6, AD-8): <see cref="SetPause"/> and <see cref="LiftPause"/> journal the Pause with
/// its next revision, and with it every Device on the roster as pending. The Site grain then tells each pending
/// Device its current Pause (<see cref="IDeviceGrain.SetSitePause"/>, level-triggered and idempotent) and journals
/// each answer as <see cref="SitePauseDelivered"/>; a Device that did not answer stays pending. Whether a Device is
/// paused is the Device grain's state alone: nothing here stores or derives it. The end date is a UTC due-at in
/// the Site's state: the first wake or activation at or after it journals <see cref="SiteResumed"/>, which is
/// delivered the same way. A Device registered while the Site is paused reads the Pause in the reply of
/// <see cref="RegisterDevice"/>.
/// </para>
/// <para>
/// Delivery never runs in a turn that other calls wait behind. A Device that is told the Pause closes its Alerts
/// before it answers, and each close is reported to this Site; a Device that enrols awaits
/// <see cref="RegisterDevice"/>. A Site grain that awaited a Device in an ordinary turn would stop both until a
/// call timed out. So the commands only journal and start the delivery timer, whose ticks interleave, as
/// <see cref="DeliverPause"/> does; the <c>site-pause</c> reminder (period 1 min, kept only while a Pause has an
/// end date or a delivery is pending) restarts that timer after a deactivation or a silo restart, and so does
/// every activation. A registration that failed is tried again by every tick, and the timer does not stop before
/// the reminder is registered.
/// </para>
/// </remarks>
[GrainType("site")]
public sealed partial class SiteGrain(
    IPhaseTwoOrganizations organizations,
    IOptions<KeycloakOptions> options,
    ILogger<SiteGrain> logger) : JournaledStreamGrain<SiteState>, ISiteGrain, IRemindable
{
    /// <summary>
    /// The name of the reminder that wakes the grain for its Pause (Story 8.1): to end one whose end date has
    /// passed, and to deliver it to the Devices that have not acknowledged it. It only wakes; the due-at and the
    /// pending Devices are journaled state. Registered only while there is either, and it removes itself.
    /// </summary>
    public const string PauseReminderName = "site-pause";

    private static readonly TimeSpan PauseReminderPeriod = TimeSpan.FromMinutes(1);

    // While the grain is active, a pending delivery is tried again this often.
    private static readonly TimeSpan PauseDeliveryPeriod = TimeSpan.FromSeconds(5);

    // How long a command waits for the reminder to be registered before it goes on without it.
    private static readonly TimeSpan PauseReminderPatience = TimeSpan.FromSeconds(5);

    private IGrainTimer? _pauseDeliveryTimer;

    // The delivery under way, if any: deliveries interleave with other calls, never with each other.
    private Task? _pauseDelivery;

    private bool _pauseReminderRegistered;
    private Task<IGrainReminder>? _pauseReminderRegistration;

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

        // The join reply carries the Site's Pause (AD-8, Story 8.1): a Device enrolled into a paused Site starts
        // paused. A Device registered now is not pending for the Pause in force; the reply is its delivery.
        return new DeviceRegistrationResult(DeviceRegistrationOutcome.Registered, TentativeState.Pause);
    }

    /// <inheritdoc />
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);

        // Every activation processes what is overdue (AD-6) and goes on delivering what is pending. Not inline:
        // the first tick does both, in a turn of its own that nothing waits behind.
        if (PauseNeedsWaking(State))
        {
            StartPauseDelivery();
        }
    }

    /// <inheritdoc />
    public async Task<SitePauseResult> SetPause(DateTimeOffset? endsAt, CancellationToken cancellationToken = default)
    {
        if (State.Lifecycle != SiteLifecycle.Active)
        {
            return new SitePauseResult(SitePauseOutcome.NotFound, SitePause.NotPaused);
        }

        var current = TentativeState.Pause;

        // A Pause never ends in the past, nor at the instant it is set (AD-6: the grain's own clock decides).
        if (endsAt is { } end && end <= Clock.GetUtcNow())
        {
            return new SitePauseResult(SitePauseOutcome.InvalidEnd, current);
        }

        // The same Pause again journals nothing; a delivery that is still pending goes on.
        if (current.Paused && current.EndsAt == endsAt)
        {
            StartPauseDeliveryWhenNeeded();
            return new SitePauseResult(SitePauseOutcome.Unchanged, current);
        }

        // The reminder first: a Pause that is journaled is never without its wake, also when the silo stops
        // right after the write. A reminder without a Pause finds nothing to do and removes itself.
        await EnsurePauseReminderAsync();

        // The clock is read after that wait, which can take seconds: the end is checked again, and the Pause is
        // stamped with the time it is journaled at.
        var now = Clock.GetUtcNow();

        if (endsAt is { } late && late <= now)
        {
            return new SitePauseResult(SitePauseOutcome.InvalidEnd, TentativeState.Pause);
        }

        var pause = new SitePause(true, endsAt, TentativeState.Pause.Revision + 1);
        RaiseEvent(new SitePaused(pause.EndsAt, pause.Revision, now));
        await ConfirmEvents();
        StartPauseDelivery();

        return new SitePauseResult(SitePauseOutcome.Changed, pause);
    }

    /// <inheritdoc />
    public async Task<SitePauseResult> LiftPause(CancellationToken cancellationToken = default)
    {
        if (State.Lifecycle != SiteLifecycle.Active)
        {
            return new SitePauseResult(SitePauseOutcome.NotFound, SitePause.NotPaused);
        }

        if (!TentativeState.Pause.Paused)
        {
            StartPauseDeliveryWhenNeeded();
            return new SitePauseResult(SitePauseOutcome.Unchanged, TentativeState.Pause);
        }

        await EnsurePauseReminderAsync();

        // Read again after the wait: a delivery that interleaved may have ended the Pause at its due-at.
        if (!TentativeState.Pause.Paused)
        {
            return new SitePauseResult(SitePauseOutcome.Unchanged, TentativeState.Pause);
        }

        var pause = new SitePause(false, null, TentativeState.Pause.Revision + 1);
        RaiseEvent(new SiteResumed(pause.Revision, Clock.GetUtcNow()));
        await ConfirmEvents();
        StartPauseDelivery();

        return new SitePauseResult(SitePauseOutcome.Changed, pause);
    }

    /// <inheritdoc />
    public Task<SitePauseStatus> GetPause(CancellationToken cancellationToken = default) => Task.FromResult(PauseStatus());

    /// <inheritdoc />
    public async Task<SitePauseStatus> DeliverPause(CancellationToken cancellationToken = default)
    {
        await DeliverPauseOnceAsync();
        return PauseStatus();
    }

    /// <inheritdoc />
    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (!string.Equals(reminderName, PauseReminderName, StringComparison.Ordinal))
        {
            return;
        }

        // The wake of the Site's own deadline (AD-6): a Pause whose end date has passed ends here, and its resume
        // is pending for every Device from this write on.
        await EndPauseWhenDueAsync();

        if (PauseNeedsWaking(TentativeState))
        {
            // Delivery itself is the timer's: this turn has calls waiting behind it.
            StartPauseDelivery();
        }
        else
        {
            // Nothing has an end date and nothing is pending: the reminder removes itself.
            await RemovePauseReminderAsync();
        }
    }

    private static bool PauseNeedsWaking(SiteState state) =>
        state.PendingPauseDeliveries.Count > 0 || state.Pause is { Paused: true, EndsAt: not null };

    private SitePauseStatus PauseStatus() =>
        new(TentativeState.Pause, [.. TentativeState.PendingPauseDeliveries.Order(StringComparer.Ordinal)]);

    private void StartPauseDeliveryWhenNeeded()
    {
        if (PauseNeedsWaking(TentativeState))
        {
            StartPauseDelivery();
        }
    }

    // Starts the delivery timer, or makes a running one tick now. Its ticks interleave with the grain's other
    // calls, so a Device that reports an Alert to this Site, or enrols, while it is being told the Pause is
    // answered at once. The timer stops itself once nothing is pending.
    private void StartPauseDelivery()
    {
        if (_pauseDeliveryTimer is null)
        {
            _pauseDeliveryTimer = this.RegisterGrainTimer(
                PauseDeliveryTickAsync,
                new GrainTimerCreationOptions(TimeSpan.Zero, PauseDeliveryPeriod) { Interleave = true });
        }
        else
        {
            _pauseDeliveryTimer.Change(TimeSpan.Zero, PauseDeliveryPeriod);
        }
    }

    private async Task PauseDeliveryTickAsync()
    {
        await DeliverPauseOnceAsync();

        // A Pause with an end date, or a pending delivery, must never be without its wake: while the reminder is
        // not registered (the registration failed, or this activation has not made it yet), the tick tries
        // again, and the timer keeps ticking until it holds.
        var needsWaking = PauseNeedsWaking(TentativeState);

        if (needsWaking && !_pauseReminderRegistered)
        {
            await EnsurePauseReminderAsync();
        }

        if (TentativeState.PendingPauseDeliveries.Count == 0 && (!needsWaking || _pauseReminderRegistered))
        {
            _pauseDeliveryTimer?.Dispose();
            _pauseDeliveryTimer = null;
        }
    }

    // One attempt at a time: a tick and a call that arrive together run one after the other, never side by side.
    private async Task DeliverPauseOnceAsync()
    {
        while (_pauseDelivery is { IsCompleted: false } running)
        {
            await running;
        }

        var delivery = DeliverPendingPauseAsync();
        _pauseDelivery = delivery;
        await delivery;
    }

    // Ends a Pause that is past its due-at, then tells every pending Device the Pause in force and journals each
    // answer. Any answer acknowledges: a Device that is not enrolled on this Site reads the Pause in the join
    // reply of its next enrolment, and one that applied a newer revision needs this one no more. A Device that
    // throws or does not answer stays pending. Never throws.
    private async Task DeliverPendingPauseAsync()
    {
        try
        {
            await EndPauseWhenDueAsync();

            var pause = TentativeState.Pause;
            var pending = TentativeState.PendingPauseDeliveries.ToList();

            if (pending.Count == 0)
            {
                return;
            }

            await EnsurePauseReminderAsync();

            var acknowledged = new List<string>(pending.Count);

            await Task.WhenAll(pending.Select(async deviceId =>
            {
                try
                {
                    // Not cancelled by a caller: the Pause is journaled and is delivered until the Device answers.
                    await GrainFactory.GetGrain<IDeviceGrain>(deviceId).SetSitePause(SiteId, pause, CancellationToken.None);
                    acknowledged.Add(deviceId);
                }
#pragma warning disable CA1031 // Whatever kept the Device from answering, it stays pending and is told again.
                catch (Exception exception)
#pragma warning restore CA1031
                {
                    LogPauseNotDelivered(logger, SiteId, deviceId, exception);
                }
            }));

            // The Pause may have changed while the Devices were asked: an answer to an older one settles nothing.
            if (acknowledged.Count == 0 || TentativeState.Pause.Revision != pause.Revision)
            {
                return;
            }

            var now = Clock.GetUtcNow();

            foreach (var deviceId in acknowledged.Where(TentativeState.PendingPauseDeliveries.Contains).ToList())
            {
                RaiseEvent(new SitePauseDelivered(deviceId, pause.Revision, now));
            }

            await ConfirmEvents();
        }
#pragma warning disable CA1031 // Whatever failed, the pending state is journaled and the next tick or wake goes on from it.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogPauseDeliveryFailed(logger, SiteId, exception);
        }
    }

    // The owner of an end date ends it (AD-6, AD-8): a Pause whose due-at has passed is resumed with the next
    // revision, which makes every Device on the roster pending again. Checked against the tentative state, so a
    // tick and a wake that find the same due-at journal one resume.
    private async Task EndPauseWhenDueAsync()
    {
        var now = Clock.GetUtcNow();

        if (!TentativeState.PauseIsOverdue(now))
        {
            return;
        }

        RaiseEvent(new SiteResumed(TentativeState.Pause.Revision + 1, now));
        await ConfirmEvents();
    }

    // Registered once per activation; RegisterOrUpdateReminder keeps one reminder however often it is called.
    private async Task EnsurePauseReminderAsync()
    {
        if (_pauseReminderRegistered)
        {
            return;
        }

        try
        {
            // A command never waits long for the reminder service, which can take a while to answer after a silo
            // start: the registration goes on, and the next call looks at it again.
            _pauseReminderRegistration ??= this.RegisterOrUpdateReminder(PauseReminderName, PauseReminderPeriod, PauseReminderPeriod);
            await _pauseReminderRegistration.WaitAsync(PauseReminderPatience);
            _pauseReminderRegistered = true;
        }
#pragma warning disable CA1031 // Without a reminder the timer still delivers while the grain is active, and the next call registers it again.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            if (_pauseReminderRegistration is { IsCompleted: true })
            {
                _pauseReminderRegistration = null;
            }

            LogPauseReminderFailed(logger, SiteId, exception);
        }
    }

    private async Task RemovePauseReminderAsync()
    {
        try
        {
            if (await this.GetReminder(PauseReminderName) is { } reminder)
            {
                await this.UnregisterReminder(reminder);
            }

            _pauseReminderRegistered = false;
            _pauseReminderRegistration = null;
        }
#pragma warning disable CA1031 // A stray reminder only finds nothing to do and removes itself next time.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogPauseReminderFailed(logger, SiteId, exception);
        }
    }

    /// <inheritdoc />
    public async Task<SiteAlertReportResult> AlertOpened(SiteAlert alert, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alert);

        // A Site that is not active keeps no Alerts and has nobody to tell; the report is acknowledged so that
        // nothing waits on a Site that is gone.
        if (State.Lifecycle != SiteLifecycle.Active)
        {
            return SiteAlertReportResult.Nobody;
        }

        // Idempotent: the Alert grain reports until this returns and every member answered.
        if (!State.OpenAlerts.ContainsKey(alert.AlertId))
        {
            RaiseEvent(new SiteAlertOpened(alert));
            await ConfirmEvents();
        }

        return AlertReport();
    }

    /// <inheritdoc />
    public async Task<SiteAlertReportResult> AlertClosed(Guid alertId, AlertCloseReason reason, DateTimeOffset closedAt, CancellationToken cancellationToken = default)
    {
        // Idempotent: an Alert the Site does not list (closed already, or never listed) journals nothing.
        if (State.OpenAlerts.ContainsKey(alertId))
        {
            RaiseEvent(new SiteAlertClosed(alertId, reason, closedAt));
            await ConfirmEvents();
        }

        // The members are named also when nothing changed: the Alert grain repeats a report one of them did
        // not answer.
        return State.Lifecycle == SiteLifecycle.Active ? AlertReport() : SiteAlertReportResult.Nobody;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SiteAlert>> OpenAlerts(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SiteAlert>>(
            State.Lifecycle == SiteLifecycle.Active
                ? [.. State.OpenAlerts.Values.OrderBy(alert => alert.OpenedAt).ThenBy(alert => alert.AlertId)]
                : []);

    // The Site grain itself never calls User grains (a User grain calls its Site grains): the Alert grain does.
    private SiteAlertReportResult AlertReport() => new([.. State.Members.Keys.Order(StringComparer.Ordinal)]);

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

    /// <inheritdoc />
    public Task<ReminderCadence?> GetReminderCadence(CancellationToken cancellationToken = default) =>
        Task.FromResult<ReminderCadence?>(State.Lifecycle == SiteLifecycle.Active ? State.ReminderCadence : null);

    /// <inheritdoc />
    public async Task<SiteReminderCadenceResult> SetReminderCadence(ReminderCadence cadence, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(cadence))
        {
            throw new ArgumentOutOfRangeException(nameof(cadence), cadence, "Unknown Reminder cadence.");
        }

        if (State.Lifecycle != SiteLifecycle.Active)
        {
            return new SiteReminderCadenceResult(SiteReminderCadenceOutcome.NotFound, State.ReminderCadence, []);
        }

        var outcome = SiteReminderCadenceOutcome.Unchanged;

        if (State.ReminderCadence != cadence)
        {
            RaiseEvent(new SiteReminderCadenceChanged(cadence, Clock.GetUtcNow()));
            await ConfirmEvents();
            outcome = SiteReminderCadenceOutcome.Changed;
        }

        // The members are named also when nothing changed: the caller hands them the cadence again, which
        // finishes a fan-out that was cut short. The Site grain itself never calls User grains.
        return new SiteReminderCadenceResult(outcome, State.ReminderCadence, [.. State.Members.Keys.Order(StringComparer.Ordinal)]);
    }

    // The outcome and the Site's state afterwards, including tentative events not yet confirmed.
    private SiteReconciliationResult Result(SiteReconciliationOutcome outcome) =>
        new(
            outcome,
            TentativeState.Lifecycle,
            new Dictionary<string, SiteRole>(TentativeState.Members, StringComparer.Ordinal),
            [.. TentativeState.FormerMembers.Order(StringComparer.Ordinal)],
            TentativeState.ReminderCadence);

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

    [LoggerMessage(EventId = 5, EventName = "PauseNotDelivered", Level = LogLevel.Warning, Message = "The Pause of Site {SiteId} did not reach Device {DeviceId}; it stays pending and is delivered again.")]
    private static partial void LogPauseNotDelivered(ILogger logger, string siteId, string deviceId, Exception exception);

    [LoggerMessage(EventId = 6, EventName = "PauseDeliveryFailed", Level = LogLevel.Warning, Message = "The Pause of Site {SiteId} could not be ended or its delivery not journaled; the next tick or wake tries again.")]
    private static partial void LogPauseDeliveryFailed(ILogger logger, string siteId, Exception exception);

    [LoggerMessage(EventId = 7, EventName = "PauseReminderFailed", Level = LogLevel.Warning, Message = "The Pause reminder of Site {SiteId} could not be registered or removed; the next call tries again.")]
    private static partial void LogPauseReminderFailed(ILogger logger, string siteId, Exception exception);

    [LoggerMessage(
        EventId = OwnerlessEditRefusedEventId,
        EventName = "OwnerlessEditRefused",
        Level = LogLevel.Error,
        Message = "Keycloak shows Site {SiteId} without an Owner. Coldframe keeps the Owners {KeptOwners} and does not change Keycloak; give the Organization an 'owner' again in Keycloak.")]
    private static partial void LogOwnerlessEditRefused(ILogger logger, string siteId, string keptOwners);
}
