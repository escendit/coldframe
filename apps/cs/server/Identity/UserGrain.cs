using Coldframe.Contracts.Notifications;
using Coldframe.Contracts.Sites;
using Coldframe.Server.Journal;
using Coldframe.Server.Notifications;
using Microsoft.Extensions.Options;

namespace Coldframe.Server.Identity;

/// <summary>
/// A User, keyed by the OIDC <c>sub</c>. Creates Sites idempotently per key (AD-3): it persists the
/// request before calling Keycloak, creates the tagged Phase Two Organization, and hands the Site to
/// its Site grain. It creates the Organization and writes nothing else to Keycloak (AD-1). It also holds
/// the User's Role on each Site, as the Site grains report it, and the User's notification settings
/// (Story 6.3): the Notification Window, the time zone, and per Site the mute, the User's own Reminder
/// cadence and a copy of the Site's. It owns the devices the User registered for push (Story 6.5).
/// </summary>
/// <remarks>
/// <para>
/// Delivery timing (Story 6.4): the User grain is the only place that decides when to notify. It learns of its
/// Sites' open Alerts (told by the Alert grain on open and close, pulled from the Site grain when it joins a
/// Site, reconciled against the Site grain on activation) and hands the <see cref="INotifier"/> what is due:
/// at once inside the Notification Window, held for one summary per Site when the window opens, and as
/// Reminders at the resolved cadence. A muted Site's deliveries are discarded.
/// </para>
/// <para>
/// Every deadline is journaled state (AD-6): one Reminder due-at per open Alert and the window-opening due-at.
/// The <c>deliver-notifications</c> reminder and a grain timer only wake the grain, and exist only while it
/// tracks an Alert or has a Site to pull; every wake and every activation processes everything overdue. A
/// delivery is journaled as sent only after the Notifier returned, so it is at-least-once.
/// </para>
/// </remarks>
[GrainType("user")]
public sealed partial class UserGrain(
    IPhaseTwoOrganizations organizations,
    IOptions<KeycloakOptions> options,
    INotifier notifier,
    ILogger<UserGrain> logger) : JournaledStreamGrain<UserState>, IUserGrain, IRemindable
{
    /// <summary>
    /// The name of the reminder that wakes the grain while it has something to deliver or to pull.
    /// </summary>
    public const string WakeReminderName = "deliver-notifications";

    private static readonly TimeSpan WakeTimerDue = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan WakeTimerPeriod = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan WakeReminderPeriod = TimeSpan.FromMinutes(1);

    // The Sites to reconcile against Site.OpenAlerts() since this activation; not journaled, every activation
    // starts over.
    private readonly HashSet<string> _unreconciled = new(StringComparer.Ordinal);

    private IGrainTimer? _wakeTimer;

    // Whether this activation knows the reminder to be registered; a stray one says so when it ticks.
    private bool _wakeReminderRegistered;

    private string UserId => this.GetPrimaryKeyString();

    /// <inheritdoc />
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);

        // Reconciled and delivered by the first wake, not inline: a Site or a Notifier that cannot be reached
        // never fails the activation.
        _unreconciled.UnionWith(State.Sites.Keys);
        await ScheduleWakesAsync();
    }

    /// <inheritdoc />
    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (string.Equals(reminderName, WakeReminderName, StringComparison.Ordinal))
        {
            // It ticked, so it exists: a stray one (its unregister failed earlier) is removed by this wake.
            _wakeReminderRegistered = true;
            await WakeAsync();
        }
    }

    /// <inheritdoc />
    public async Task<SiteCreationResult> CreateSite(string idempotencyKey, string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var now = Clock.GetUtcNow();
        var creation = State.FindLive(idempotencyKey, now);

        if (creation is not null && !string.Equals(creation.Name, name, StringComparison.Ordinal))
        {
            return new SiteCreationResult(SiteCreationOutcome.IdempotencyKeyReused);
        }

        if (creation is { Completed: true })
        {
            return Created(creation);
        }

        if (creation is null)
        {
            // Persisted before any Keycloak call, so a retry resumes with the same Site ID.
            var requested = new SiteCreationRequested(idempotencyKey, Guid.CreateVersion7(now).ToString(), name, now);
            RaiseEvent(requested);
            await ConfirmEvents();
            creation = State.SiteCreations[idempotencyKey];
        }

        using var budget = new CancellationTokenSource(options.Value.OperationBudget, Clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(budget.Token, cancellationToken);

        try
        {
            await EnsureOrganizationAsync(idempotencyKey, creation, linked.Token);

            var initialized = await GrainFactory
                .GetGrain<ISiteGrain>(creation.SiteId)
                .Initialize(creation.Name, UserId, linked.Token);

            switch (initialized.Outcome)
            {
                case SiteInitializationOutcome.Initialized:
                    break;
                case SiteInitializationOutcome.IdentityProviderUnavailable:
                    return new SiteCreationResult(SiteCreationOutcome.IdentityProviderUnavailable);
                default:
                    throw new InvalidOperationException(
                        $"Site {creation.SiteId} refused to be initialized for its creator: {initialized.Outcome}.");
            }
        }
        catch (Exception exception) when (
            exception is IdentityProviderUnavailableException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogIdentityProviderUnavailable(logger, creation.SiteId, exception);
            return new SiteCreationResult(SiteCreationOutcome.IdentityProviderUnavailable);
        }

        RaiseEvent(new SiteCreationCompleted(idempotencyKey, creation.SiteId));

        // A reconciliation may have recorded a Role for this Site already; it is newer than the creation.
        if (!State.Sites.ContainsKey(creation.SiteId))
        {
            RaiseEvent(new SiteMembershipChanged(creation.SiteId, SiteRole.Owner));
        }

        await ConfirmEvents();

        await PullJoinedSiteAsync(creation.SiteId);
        await ScheduleWakesAsync();

        return Created(State.SiteCreations[idempotencyKey]);
    }

    /// <inheritdoc />
    public async Task SyncSiteMembership(string siteId, SiteRole? role, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteId);

        SiteRole? current = State.Sites.TryGetValue(siteId, out var held) ? held : null;

        // A User may hold settings for a Site whose Membership this grain never heard of (the Edge policy
        // reads the projection): the end of the Membership is journaled then too, which drops them.
        if (current == role && (role is not null || !State.SiteNotifications.ContainsKey(siteId)))
        {
            return;
        }

        RaiseEvent(new SiteMembershipChanged(siteId, role));
        await ConfirmEvents();

        // A Site the User did not hold: its open Alerts are pulled now, and on every wake until that succeeds.
        // A Membership that ended took the Site's Alerts and deadlines with it.
        await PullJoinedSiteAsync(siteId);
        await ScheduleWakesAsync();
    }

    /// <inheritdoc />
    public async Task AlertOpened(string siteId, SiteAlert alert, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteId);
        ArgumentNullException.ThrowIfNull(alert);

        // Idempotent: the Alert grain reports until every member answered. A Site that is not in the User's own
        // set is acknowledged and nothing is stored: when the Membership arrives, the pull finds the Alert.
        if (!State.Sites.ContainsKey(siteId) || State.TrackedAlerts.ContainsKey(alert.AlertId))
        {
            return;
        }

        // Due at once. Sent, held or discarded by the next wake, a moment from now: this call itself sends
        // nothing, so the Alert grain's report does not wait for a channel.
        var now = Clock.GetUtcNow();
        RaiseEvent(new AlertTracked(siteId, alert, Told: true, RemindFrom: now, TrackedAt: now));
        await ConfirmEvents();
        await ScheduleWakesAsync();
    }

    /// <inheritdoc />
    public async Task AlertClosed(string siteId, Guid alertId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteId);

        if (!State.TrackedAlerts.TryGetValue(alertId, out var tracked) || !string.Equals(tracked.SiteId, siteId, StringComparison.Ordinal))
        {
            return;
        }

        // Closing never notifies: the Alert, what was held for it and its Reminder are gone.
        RaiseEvent(new AlertDropped(alertId, Clock.GetUtcNow()));
        await ConfirmEvents();
        await ScheduleWakesAsync();
    }

    /// <inheritdoc />
    public Task<UserNotificationSettings> GetNotificationSettings(CancellationToken cancellationToken = default) =>
        Task.FromResult(Settings());

    /// <inheritdoc />
    public async Task<UpdateNotificationSettingsResult> UpdateNotificationSettings(
        UpdateNotificationSettings update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        // Everything is checked before anything is journaled: a refusal changes nothing.
        if (update.Window is { IsValid: false })
        {
            return new UpdateNotificationSettingsResult(NotificationSettingsOutcome.InvalidWindow, Settings());
        }

        if (!IsTimeZone(update.TimeZone) || !IsTimeZone(update.DetectedTimeZone))
        {
            return new UpdateNotificationSettingsResult(NotificationSettingsOutcome.InvalidTimeZone, Settings());
        }

        var now = Clock.GetUtcNow();
        var changed = false;

        if (update.Window is { } window && window != State.NotificationWindow)
        {
            RaiseEvent(new NotificationWindowChanged(window.FromMinutes, window.ToMinutes, now));
            changed = true;
        }

        if (update.TimeZone is { } chosen)
        {
            if (!string.Equals(State.ChosenTimeZone, chosen, StringComparison.Ordinal))
            {
                RaiseEvent(new TimeZoneChosen(chosen, now));
                changed = true;
            }
        }
        else if (update.DetectedTimeZone is { } detected
            && State.ChosenTimeZone is null
            && !string.Equals(State.DetectedTimeZone, detected, StringComparison.Ordinal))
        {
            // Only a proposal: it never replaces a zone the User chose, and never confirms one.
            RaiseEvent(new TimeZoneDetected(detected, now));
            changed = true;
        }

        if (changed)
        {
            await ConfirmEvents();
        }

        return new UpdateNotificationSettingsResult(
            changed ? NotificationSettingsOutcome.Changed : NotificationSettingsOutcome.Unchanged,
            Settings());
    }

    /// <inheritdoc />
    public Task<UserSiteNotificationSettings> GetSiteNotificationSettings(string siteId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteId);

        return Task.FromResult(SiteSettings(siteId));
    }

    /// <inheritdoc />
    public async Task<UserSiteNotificationSettings> SetSiteNotificationSettings(
        string siteId,
        bool muted,
        ReminderCadence? reminderCadence,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteId);

        if (reminderCadence is { } requested && !Enum.IsDefined(requested))
        {
            throw new ArgumentOutOfRangeException(nameof(reminderCadence), reminderCadence, "Unknown Reminder cadence.");
        }

        var current = State.SiteNotificationsOf(siteId);
        var now = Clock.GetUtcNow();
        var changed = false;

        if (current.Muted != muted)
        {
            RaiseEvent(new SiteMuteChanged(siteId, muted, now));
            changed = true;
        }

        if (current.ReminderCadence != reminderCadence)
        {
            RaiseEvent(new PersonalReminderCadenceChanged(siteId, reminderCadence, now));
            changed = true;
        }

        if (changed)
        {
            await ConfirmEvents();
        }

        return SiteSettings(siteId);
    }

    /// <inheritdoc />
    public async Task SyncSiteReminderCadence(string siteId, ReminderCadence cadence, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteId);

        if (!Enum.IsDefined(cadence))
        {
            throw new ArgumentOutOfRangeException(nameof(cadence), cadence, "Unknown Reminder cadence.");
        }

        // A Site the grain never heard a cadence of reminds daily, so daily is no change for it.
        if ((State.SiteNotificationsOf(siteId).SiteReminderCadence ?? ReminderCadence.Daily) == cadence)
        {
            return;
        }

        RaiseEvent(new SiteReminderCadenceSynced(siteId, cadence));
        await ConfirmEvents();
    }

    /// <inheritdoc />
    public async Task<PushRegistrationOutcome> RegisterPushDevice(RegisterPushDevice request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Checked before anything is journaled: a refusal changes nothing. APNs needs its environment, FCM has none.
        if (!PushRegistrationLimits.IsInstallationId(request.InstallationId)
            || !PushRegistrationLimits.IsToken(request.Token)
            || !Enum.IsDefined(request.Platform)
            || (request.Environment is { } environment && !Enum.IsDefined(environment))
            || (request.Platform == PushPlatform.Apns) != (request.Environment is not null))
        {
            return PushRegistrationOutcome.Invalid;
        }

        if (State.PushRegistrations.TryGetValue(request.InstallationId, out var current)
            && current.Platform == request.Platform
            && current.Environment == request.Environment
            && string.Equals(current.Token, request.Token, StringComparison.Ordinal))
        {
            return PushRegistrationOutcome.Unchanged;
        }

        var now = Clock.GetUtcNow();

        // State is the confirmed state, so what this call removes is worked out before anything is raised.
        var others = State.OrderedPushRegistrations()
            .Where(other => !string.Equals(other.InstallationId, request.InstallationId, StringComparison.Ordinal))
            .ToList();

        // A token belongs to one installation: an app that was installed again got a new installation ID and
        // may have kept its token, and the phone must not be pushed twice.
        var sameToken = others
            .Where(other => other.Platform == request.Platform && string.Equals(other.Token, request.Token, StringComparison.Ordinal))
            .ToList();

        // Beyond the limit the one registered longest ago makes room; the one being registered is the newest.
        var kept = others.Except(sameToken).ToList();
        var overflow = kept.Take(Math.Max(0, kept.Count + 1 - PushRegistrationLimits.MaxRegistrations));

        foreach (var replaced in sameToken.Concat(overflow))
        {
            RaiseEvent(new PushDeviceRemoved(replaced.InstallationId, PushDeviceRemovalReason.Replaced, now));
        }

        RaiseEvent(new PushDeviceRegistered(request.InstallationId, request.Platform, request.Token, request.Environment, now));
        await ConfirmEvents();

        return PushRegistrationOutcome.Registered;
    }

    /// <inheritdoc />
    public async Task<bool> RemovePushDevice(string installationId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installationId);

        if (!State.PushRegistrations.ContainsKey(installationId))
        {
            return false;
        }

        RaiseEvent(new PushDeviceRemoved(installationId, PushDeviceRemovalReason.Requested, Clock.GetUtcNow()));
        await ConfirmEvents();

        return true;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PushRegistration>> GetPushRegistrations(CancellationToken cancellationToken = default) =>
        Task.FromResult(State.OrderedPushRegistrations());

    private static NotificationEntry Entry(SiteAlert alert) =>
        new(alert.AlertId, alert.Kind, alert.Side, alert.LotId, alert.SensorId, alert.DeviceId, alert.Quantity, alert.OpenedAt);

    // One wake: pulls what is to be pulled, delivers everything overdue, then keeps or drops the timer and the
    // reminder. Never throws: whatever failed stays due or pending for the next wake.
    private async Task WakeAsync()
    {
        try
        {
            await PullSitesAsync();
            await DeliverDueAsync();
        }
#pragma warning disable CA1031 // Whatever failed the wake, the journaled deadlines stand and the next wake tries again.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogWakeFailed(logger, UserId, exception);
        }

        await ScheduleWakesAsync();
    }

    // The pull of a Site the User just joined, inline, so that the Membership sync returns with the Site's open
    // Alerts tracked. A failure leaves the pull pending and never fails the caller.
    private async Task PullJoinedSiteAsync(string siteId)
    {
        if (!State.PendingPulls.Contains(siteId))
        {
            return;
        }

        try
        {
            await PullSiteAsync(siteId);
        }
#pragma warning disable CA1031 // Whatever failed the pull, it stays pending and is repeated on the next wake.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogPullFailed(logger, UserId, siteId, exception);
        }
    }

    // Every Site that was joined and not pulled yet, and every Site not reconciled since this activation.
    private async Task PullSitesAsync()
    {
        foreach (var siteId in State.PendingPulls.Union(_unreconciled, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList())
        {
            try
            {
                await PullSiteAsync(siteId);
            }
#pragma warning disable CA1031 // Whatever failed the pull, it is repeated on the next wake; the other Sites are still pulled.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                LogPullFailed(logger, UserId, siteId, exception);
            }
        }
    }

    // Brings the tracked Alerts of one Site in line with Site.OpenAlerts(): adds the missing ones, without an
    // opening notification, and drops the ones the Site no longer lists.
    private async Task PullSiteAsync(string siteId)
    {
        if (!State.Sites.ContainsKey(siteId))
        {
            _unreconciled.Remove(siteId);
            return;
        }

        var open = await GrainFactory.GetGrain<ISiteGrain>(siteId).OpenAlerts(CancellationToken.None);
        var now = Clock.GetUtcNow();
        var settings = State.SiteNotificationsOf(siteId);
        var raised = false;

        foreach (var alert in open.Where(alert => !State.TrackedAlerts.ContainsKey(alert.AlertId)))
        {
            // The first Reminder is due at the first openedAt + n × interval after now.
            var interval = settings.ReminderInterval(alert.Kind);
            var firstReminder = ReminderIntervalRule.FirstAfter(alert.OpenedAt, interval, now);
            RaiseEvent(new AlertTracked(siteId, alert, Told: false, RemindFrom: firstReminder - interval, TrackedAt: now));
            raised = true;
        }

        var listed = open.Select(alert => alert.AlertId).ToHashSet();

        foreach (var gone in State.TrackedAlerts.Values.Where(tracked =>
            string.Equals(tracked.SiteId, siteId, StringComparison.Ordinal) && !listed.Contains(tracked.Alert.AlertId)).ToList())
        {
            RaiseEvent(new AlertDropped(gone.Alert.AlertId, now));
            raised = true;
        }

        if (State.PendingPulls.Contains(siteId))
        {
            RaiseEvent(new SiteAlertsPulled(siteId, now));
            raised = true;
        }

        if (raised)
        {
            await ConfirmEvents();
        }

        _unreconciled.Remove(siteId);
    }

    // Everything overdue: each due delivery is sent inside the Notification Window and held outside it, then
    // every Site with held deliveries gets its one summary once the window is open.
    private async Task DeliverDueAsync()
    {
        var now = Clock.GetUtcNow();
        var window = State.NotificationWindow;
        var zone = NotificationWindowRule.ZoneOf(State.TimeZone);
        var open = NotificationWindowRule.Contains(window, zone, now);
        var held = false;

        foreach (var alert in State.DueAlerts(now))
        {
            // Overdue Reminders collapse into the last one, so that the next is due after now.
            var dueAt = alert.OpeningPending
                ? alert.PreviousDueAt
                : ReminderIntervalRule.LastAtOrBefore(State.DueAt(alert), State.ReminderInterval(alert), now);

            if (alert.HeldFrom is not null)
            {
                // Something is held for the Alert already: this delivery joins it, and the summary has one entry.
                // The window-opening due-at follows the window as it is now.
                var opensAt = open ? State.WindowOpensAt ?? dueAt : NotificationWindowRule.NextOpening(window, zone, now);
                RaiseEvent(new DeliveryHeld(alert.SiteId, alert.Alert.AlertId, dueAt, opensAt));
                held = true;
            }
            else if (!open || !NotificationWindowRule.Contains(window, zone, dueAt))
            {
                // Due outside the window, or found only after the window closed (nothing woke the grain in
                // between): never sent at night. A window that is open now opened after the due-at, and the
                // summary below follows in this wake.
                var windowOpensAt = NotificationWindowRule.NextOpening(window, zone, open ? dueAt : now);
                RaiseEvent(new DeliveryHeld(alert.SiteId, alert.Alert.AlertId, dueAt, windowOpensAt));
                held = true;
            }
            else
            {
                var kind = alert.OpeningPending ? NotificationKind.Alert : NotificationKind.Reminder;
                await SendAsync(new Notification(UserId, alert.SiteId, kind, dueAt, HeldFrom: null, [Entry(alert.Alert)]));
            }
        }

        if (held)
        {
            await ConfirmEvents();
        }

        // Held deliveries wait for the window as it is now, so a changed window or zone is followed at once.
        if (!open)
        {
            return;
        }

        foreach (var site in State.HeldAlertsBySite())
        {
            // Due when the window opened; when the User opened it earlier than the journaled due-at, now.
            var dueAt = State.WindowOpensAt is { } opensAt && opensAt <= now
                ? Later(opensAt, NotificationWindowRule.LastOpening(window, zone, now))
                : now;

            await SendAsync(new Notification(
                UserId,
                site[0].SiteId,
                NotificationKind.Summary,
                dueAt,
                site.Min(alert => alert.HeldFrom),
                [.. site.Select(alert => Entry(alert.Alert))]));
        }
    }

    // Hands one notification to the Notifier and journals it as sent only after the Notifier returned. A failure
    // leaves the delivery due (or held) for the next wake; it never stops the other deliveries. A channel runs
    // inside this turn and cannot ask this grain for anything (Story 6.5): the notification carries the push
    // registrations, the time zone and the window, and the registrations a provider no longer knows come back
    // and are journaled as removed, whether the send succeeded or not.
    private async Task SendAsync(Notification notification)
    {
        notification = notification with
        {
            Registrations = State.OrderedPushRegistrations(),
            TimeZone = State.TimeZone,
            Window = State.NotificationWindow,
        };

        NotifierResult result;

        try
        {
            result = await notifier.SendAsync(notification, CancellationToken.None);
        }
        catch (NotificationNotDeliveredException exception)
        {
            LogNotSent(logger, Notifier.NameOf(notification.Kind), UserId, notification.SiteId, exception);

            if (RaiseRemovals(exception.InvalidInstallations))
            {
                await ConfirmEvents();
            }

            return;
        }
#pragma warning disable CA1031 // Whatever failed the send, the delivery stays due and is handed over again.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogNotSent(logger, Notifier.NameOf(notification.Kind), UserId, notification.SiteId, exception);
            return;
        }

        RaiseRemovals(result.InvalidInstallations);
        RaiseEvent(new NotificationSent(
            notification.SiteId,
            notification.Kind,
            [.. notification.Entries.Select(entry => entry.AlertId)],
            notification.DueAt,
            result.SentAt));
        await ConfirmEvents();
    }

    // A token the provider reported as invalid or unregistered: its registration is removed, so the next
    // notification is not sent to it.
    private bool RaiseRemovals(IReadOnlyList<string> invalidInstallations)
    {
        var raised = false;

        foreach (var installationId in invalidInstallations.Where(State.PushRegistrations.ContainsKey).Distinct(StringComparer.Ordinal))
        {
            RaiseEvent(new PushDeviceRemoved(installationId, PushDeviceRemovalReason.Invalid, Clock.GetUtcNow()));
            LogPushDeviceInvalid(logger, UserId, installationId);
            raised = true;
        }

        return raised;
    }

    // The timer and the reminder exist only while there is something to deliver or to pull. Never throws:
    // without a reminder the timer and the next activation still wake the grain.
    private async Task ScheduleWakesAsync()
    {
        if (State.NeedsWaking || _unreconciled.Count > 0)
        {
            _wakeTimer ??= this.RegisterGrainTimer(WakeAsync, new GrainTimerCreationOptions(WakeTimerDue, WakeTimerPeriod));
        }
        else
        {
            _wakeTimer?.Dispose();
            _wakeTimer = null;
        }

        try
        {
            if (State.NeedsWaking)
            {
                if (!_wakeReminderRegistered)
                {
                    await this.RegisterOrUpdateReminder(WakeReminderName, WakeReminderPeriod, WakeReminderPeriod);
                    _wakeReminderRegistered = true;
                }
            }
            else if (_wakeReminderRegistered)
            {
                if (await this.GetReminder(WakeReminderName) is { } reminder)
                {
                    await this.UnregisterReminder(reminder);
                }

                _wakeReminderRegistered = false;
            }
        }
#pragma warning disable CA1031 // A missing reminder is registered by the next wake; a stray one removes itself when it ticks.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogReminderFailed(logger, UserId, exception);
        }
    }

    private static DateTimeOffset Later(DateTimeOffset first, DateTimeOffset second) => first >= second ? first : second;

    private static bool IsTimeZone(string? timeZone) =>
        timeZone is null || (timeZone.Length is > 0 and <= UserNotificationLimits.MaxTimeZoneLength && !string.IsNullOrWhiteSpace(timeZone));

    private UserNotificationSettings Settings() =>
        new(State.NotificationWindow, State.TimeZone, State.ChosenTimeZone is not null);

    private UserSiteNotificationSettings SiteSettings(string siteId)
    {
        var settings = State.SiteNotificationsOf(siteId);
        return new UserSiteNotificationSettings(settings.Muted, settings.ReminderCadence, settings.ResolvedReminderCadence);
    }

    private static SiteCreationResult Created(SiteCreation creation) =>
        new(SiteCreationOutcome.Created, new SiteSummary(creation.SiteId, creation.Name, SiteRole.Owner));

    // Recovery first looks the Organization up by its tag; creation then uses the persisted ID, so a
    // 409 resolves by reading that ID. Both paths converge on one Organization.
    private async Task EnsureOrganizationAsync(string idempotencyKey, SiteCreation creation, CancellationToken cancellationToken)
    {
        var tag = $"{UserId}:{idempotencyKey}";

        var tagged = await organizations.FindByAttributeAsync(IPhaseTwoOrganizations.IdempotencyKeyAttribute, tag, cancellationToken);

        // A key reused after 24 h tags a second Organization; only the one with this request's ID counts.
        if (tagged.Any(organization => string.Equals(organization.Id, creation.SiteId, StringComparison.Ordinal)))
        {
            return;
        }

        var organization = new PhaseTwoOrganization(
            creation.SiteId,
            creation.SiteId,
            creation.Name,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                [IPhaseTwoOrganizations.IdempotencyKeyAttribute] = [tag],
            });

        if (await organizations.CreateAsync(organization, cancellationToken))
        {
            return;
        }

        _ = await organizations.GetAsync(creation.SiteId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Phase Two reports a conflict for Organization {creation.SiteId} but has no Organization with that ID.");
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Keycloak is unavailable while creating Site {SiteId}; the request stays pending.")]
    private static partial void LogIdentityProviderUnavailable(ILogger logger, string siteId, Exception exception);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "User {UserId} could not pull the open Alerts of Site {SiteId}; the pull is repeated on the next wake.")]
    private static partial void LogPullFailed(ILogger logger, string userId, string siteId, Exception exception);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "The {Kind} notification of User {UserId} for Site {SiteId} was not sent; it stays due and is sent on the next wake.")]
    private static partial void LogNotSent(ILogger logger, string kind, string userId, string siteId, Exception exception);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "The wake-up reminder of User {UserId} could not be registered or removed; the next wake tries again.")]
    private static partial void LogReminderFailed(ILogger logger, string userId, Exception exception);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "A wake of User {UserId} failed; its deadlines stand and the next wake tries again.")]
    private static partial void LogWakeFailed(ILogger logger, string userId, Exception exception);

    [LoggerMessage(EventId = 6, Level = LogLevel.Information, Message = "The push provider no longer knows the token of installation {InstallationId} of User {UserId}; its registration was removed.")]
    private static partial void LogPushDeviceInvalid(ILogger logger, string userId, string installationId);
}
