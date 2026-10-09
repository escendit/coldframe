using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Notifications;
using Coldframe.Contracts.Sites;
using Coldframe.Server.Notifications;

namespace Coldframe.Server.Identity;

/// <summary>
/// One Site creation a User asked for, by idempotency key.
/// </summary>
/// <param name="SiteId">The Site ID chosen for the request.</param>
/// <param name="Name">The requested Site name.</param>
/// <param name="RequestedAt">When the request was received.</param>
/// <param name="Completed">Whether the Site exists with the User as its Owner.</param>
[GenerateSerializer]
[Alias("coldframe.user-site-creation")]
public sealed record SiteCreation(
    [property: Id(0)] string SiteId,
    [property: Id(1)] string Name,
    [property: Id(2)] DateTimeOffset RequestedAt,
    [property: Id(3)] bool Completed);

/// <summary>
/// What a User set for one Site, and the User grain's copy of that Site's Reminder cadence (Story 6.3).
/// </summary>
/// <param name="Muted">Whether the User muted the Site.</param>
/// <param name="ReminderCadence">The User's own cadence; <see langword="null"/> when the User uses the Site setting.</param>
/// <param name="SiteReminderCadence">The Site's cadence as last handed to the User grain; <see langword="null"/> when never.</param>
[GenerateSerializer]
[Alias("coldframe.user-site-notification-state")]
public sealed record UserSiteNotifications(
    [property: Id(0)] bool Muted = false,
    [property: Id(1)] ReminderCadence? ReminderCadence = null,
    [property: Id(2)] ReminderCadence? SiteReminderCadence = null)
{
    /// <summary>
    /// The settings of a Site the User never set anything for.
    /// </summary>
    public static UserSiteNotifications None { get; } = new();

    /// <summary>
    /// The cadence the User is reminded at: the User's own, else the Site's, else daily.
    /// </summary>
    public ReminderCadence ResolvedReminderCadence => ReminderIntervalRule.Resolve(ReminderCadence, SiteReminderCadence);

    /// <summary>
    /// The interval between the User's Reminders of an Alert of <paramref name="kind"/> on the Site (Story 6.4).
    /// </summary>
    public TimeSpan ReminderInterval(AlertKind kind) => ReminderIntervalRule.Interval(kind, ReminderCadence, SiteReminderCadence);
}

/// <summary>
/// An open Alert the User grain tracks, with its one Reminder deadline (Story 6.4).
/// </summary>
/// <param name="SiteId">The Site the Alert is open on.</param>
/// <param name="Alert">The Alert as its Site lists it.</param>
/// <param name="PreviousDueAt">
/// The due-at (UTC) the next Reminder is counted from: it is due one interval later, at the cadence then in
/// force. While <paramref name="OpeningPending"/>, the due-at of the opening notification itself.
/// </param>
/// <param name="OpeningPending">Whether the opening notification is still to be sent, held or discarded.</param>
/// <param name="HeldFrom">
/// When the first delivery fell due that is held for the Site's summary (UTC); <see langword="null"/> when
/// nothing is held for the Alert.
/// </param>
[GenerateSerializer]
[Alias("coldframe.user-tracked-alert")]
public sealed record TrackedAlert(
    [property: Id(0)] string SiteId,
    [property: Id(1)] SiteAlert Alert,
    [property: Id(2)] DateTimeOffset PreviousDueAt,
    [property: Id(3)] bool OpeningPending,
    [property: Id(4)] DateTimeOffset? HeldFrom = null);

/// <summary>
/// The state of the User grain: the Site creations it has seen, by idempotency key, its Role on each
/// Site it belongs to, its notification settings (Story 6.3), and what it has to deliver (Story 6.4): the open
/// Alerts of its Sites with their Reminder deadlines, what is held for a summary, the window-opening due-at and
/// the Sites whose open Alerts it still has to pull. It also owns the devices the User registered for push
/// (Story 6.5).
/// </summary>
[GenerateSerializer]
[Alias("coldframe.user-state")]
public sealed class UserState
{
    /// <summary>
    /// How long an idempotency key is remembered after its request.
    /// </summary>
    public static readonly TimeSpan IdempotencyKeyLifetime = TimeSpan.FromHours(24);

    [Id(0)]
    private readonly Dictionary<string, SiteCreation> _siteCreations = new(StringComparer.Ordinal);

    [Id(1)]
    private readonly Dictionary<string, SiteRole> _sites = new(StringComparer.Ordinal);

    [Id(2)]
    private readonly Dictionary<string, UserSiteNotifications> _siteNotifications = new(StringComparer.Ordinal);

    [Id(3)]
    private NotificationWindow? _notificationWindow;

    [Id(6)]
    private readonly Dictionary<Guid, TrackedAlert> _trackedAlerts = [];

    [Id(7)]
    private readonly HashSet<string> _pendingPulls = new(StringComparer.Ordinal);

    [Id(9)]
    private readonly Dictionary<string, PushRegistration> _pushRegistrations = new(StringComparer.Ordinal);

    /// <summary>
    /// The Notification Window: 07:00 to 22:00 until the User changes it.
    /// </summary>
    public NotificationWindow NotificationWindow => _notificationWindow ?? NotificationWindow.Default;

    /// <summary>
    /// The IANA time zone the User chose; <see langword="null"/> until then.
    /// </summary>
    [Id(4)]
    public string? ChosenTimeZone { get; private set; }

    /// <summary>
    /// The IANA time zone a device or browser last reported while the User had chosen none.
    /// </summary>
    [Id(5)]
    public string? DetectedTimeZone { get; private set; }

    /// <summary>
    /// The User's time zone: the chosen one, else the detected one, else <see langword="null"/>.
    /// </summary>
    public string? TimeZone => ChosenTimeZone ?? DetectedTimeZone;

    /// <summary>
    /// What the User set per Site, by Site ID. A Site without an entry has <see cref="UserSiteNotifications.None"/>.
    /// </summary>
    public IReadOnlyDictionary<string, UserSiteNotifications> SiteNotifications => _siteNotifications;

    /// <summary>
    /// The settings for <paramref name="siteId"/>: not muted, the Site's cadence and daily when nothing was set.
    /// </summary>
    public UserSiteNotifications SiteNotificationsOf(string siteId) =>
        _siteNotifications.TryGetValue(siteId, out var settings) ? settings : UserSiteNotifications.None;

    /// <summary>
    /// The Site creations by idempotency key. A key used again after it expired holds the newer request.
    /// </summary>
    public IReadOnlyDictionary<string, SiteCreation> SiteCreations => _siteCreations;

    /// <summary>
    /// The User's Role on each Site it belongs to, by Site ID.
    /// </summary>
    public IReadOnlyDictionary<string, SiteRole> Sites => _sites;

    /// <summary>
    /// The open Alerts the User grain tracks, by Alert ID (Story 6.4): the ones it was told of and the ones it
    /// pulled from its Sites.
    /// </summary>
    public IReadOnlyDictionary<Guid, TrackedAlert> TrackedAlerts => _trackedAlerts;

    /// <summary>
    /// The Sites the User joined whose open Alerts were not pulled yet (Story 6.4).
    /// </summary>
    public IReadOnlySet<string> PendingPulls => _pendingPulls;

    /// <summary>
    /// The devices the User registered for push, by installation ID (Story 6.5).
    /// </summary>
    public IReadOnlyDictionary<string, PushRegistration> PushRegistrations => _pushRegistrations;

    /// <summary>
    /// The push registrations in a stable order: the one registered first comes first, then by installation ID.
    /// </summary>
    public IReadOnlyList<PushRegistration> OrderedPushRegistrations() =>
        [.. _pushRegistrations.Values
            .OrderBy(registration => registration.RegisteredAt)
            .ThenBy(registration => registration.InstallationId, StringComparer.Ordinal)];

    /// <summary>
    /// The window-opening due-at (UTC) while a delivery is held (Story 6.4): when the summaries are due.
    /// </summary>
    [Id(8)]
    public DateTimeOffset? WindowOpensAt { get; private set; }

    /// <summary>
    /// Whether the grain has to be woken: it tracks an open Alert or still has a Site to pull.
    /// </summary>
    public bool NeedsWaking => _trackedAlerts.Count > 0 || _pendingPulls.Count > 0;

    /// <summary>
    /// When the next delivery of <paramref name="alert"/> is due (UTC): its opening notification, else its next
    /// Reminder, one interval at the resolved cadence after the previous due-at. A cadence change therefore
    /// re-schedules from the previous due-at.
    /// </summary>
    public DateTimeOffset DueAt(TrackedAlert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);

        return alert.OpeningPending ? alert.PreviousDueAt : alert.PreviousDueAt + ReminderInterval(alert);
    }

    /// <summary>
    /// The interval between the User's Reminders of <paramref name="alert"/>.
    /// </summary>
    public TimeSpan ReminderInterval(TrackedAlert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);

        return SiteNotificationsOf(alert.SiteId).ReminderInterval(alert.Alert.Kind);
    }

    /// <summary>
    /// The tracked Alerts with a delivery due at <paramref name="now"/>, the oldest due-at first. A muted Site
    /// has none: what falls due for it is discarded.
    /// </summary>
    public IReadOnlyList<TrackedAlert> DueAlerts(DateTimeOffset now) =>
        [.. _trackedAlerts.Values
            .Where(alert => !SiteNotificationsOf(alert.SiteId).Muted && DueAt(alert) <= now)
            .OrderBy(DueAt)
            .ThenBy(alert => alert.Alert.AlertId)];

    /// <summary>
    /// The tracked Alerts with a delivery held for a summary, by Site, each Site's oldest Alert first.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<TrackedAlert>> HeldAlertsBySite() =>
        [.. _trackedAlerts.Values
            .Where(alert => alert.HeldFrom is not null)
            .GroupBy(alert => alert.SiteId, StringComparer.Ordinal)
            .OrderBy(site => site.Key, StringComparer.Ordinal)
            .Select(site => (IReadOnlyList<TrackedAlert>)[.. site.OrderBy(alert => alert.Alert.OpenedAt).ThenBy(alert => alert.Alert.AlertId)])];

    /// <summary>
    /// Returns the request that still holds <paramref name="idempotencyKey"/> at <paramref name="now"/>.
    /// A completed request expires 24 h after it was received; a pending one never does, because its
    /// Organization may exist and a retry must resume it.
    /// </summary>
    public SiteCreation? FindLive(string idempotencyKey, DateTimeOffset now) =>
        _siteCreations.TryGetValue(idempotencyKey, out var creation)
        && (!creation.Completed || now - creation.RequestedAt < IdempotencyKeyLifetime)
            ? creation
            : null;

    public void Apply(SiteCreationRequested @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        _siteCreations[@event.IdempotencyKey] = new SiteCreation(@event.SiteId, @event.Name, @event.RequestedAt, Completed: false);
    }

    public void Apply(SiteCreationCompleted @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        if (_siteCreations.TryGetValue(@event.IdempotencyKey, out var creation)
            && string.Equals(creation.SiteId, @event.SiteId, StringComparison.Ordinal))
        {
            _siteCreations[@event.IdempotencyKey] = creation with { Completed = true };
        }
    }

    public void Apply(SiteMembershipChanged @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        if (@event.Role is { } role)
        {
            // A Membership for a Site the User did not hold: its open Alerts are to be pulled (Story 6.4). A
            // changed Role is no join.
            if (!_sites.ContainsKey(@event.SiteId))
            {
                _pendingPulls.Add(@event.SiteId);
            }

            _sites[@event.SiteId] = role;
        }
        else
        {
            _sites.Remove(@event.SiteId);

            // The Membership ended: the mute, the cadence and the Site's cadence go with it (Story 6.3).
            _siteNotifications.Remove(@event.SiteId);

            // And so do the Site's Alerts with their held deliveries and deadlines (Story 6.4).
            _pendingPulls.Remove(@event.SiteId);

            foreach (var alertId in AlertsOf(@event.SiteId))
            {
                _trackedAlerts.Remove(alertId);
            }

            ForgetWindowOpeningUnlessHeld();
        }
    }

    public void Apply(NotificationWindowChanged @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        _notificationWindow = new NotificationWindow(@event.FromMinutes, @event.ToMinutes);
    }

    public void Apply(TimeZoneDetected @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        DetectedTimeZone = @event.TimeZone;
    }

    public void Apply(TimeZoneChosen @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ChosenTimeZone = @event.TimeZone;
    }

    public void Apply(SiteMuteChanged @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        Set(@event.SiteId, SiteNotificationsOf(@event.SiteId) with { Muted = @event.Muted });

        foreach (var alertId in AlertsOf(@event.SiteId))
        {
            var alert = _trackedAlerts[alertId];

            if (@event.Muted)
            {
                // A delivery of a muted Site is discarded, a held one included: it is not sent later.
                _trackedAlerts[alertId] = alert with { HeldFrom = null };
            }
            else if (DueAt(alert) <= @event.ChangedAt)
            {
                // Unmuting starts no catch-up: what fell due while the Site was muted is gone, and the Reminder
                // advanced all the while, so the next one is due on the same schedule, after the unmute.
                _trackedAlerts[alertId] = alert with
                {
                    PreviousDueAt = ReminderIntervalRule.LastAtOrBefore(DueAt(alert), ReminderInterval(alert), @event.ChangedAt),
                    OpeningPending = false,
                };
            }
        }

        ForgetWindowOpeningUnlessHeld();
    }

    public void Apply(PersonalReminderCadenceChanged @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        Set(@event.SiteId, SiteNotificationsOf(@event.SiteId) with { ReminderCadence = @event.Cadence });
    }

    public void Apply(SiteReminderCadenceSynced @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        Set(@event.SiteId, SiteNotificationsOf(@event.SiteId) with { SiteReminderCadence = @event.Cadence });
    }

    public void Apply(AlertTracked @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        // Tracked once: a second report of the same Alert changes no deadline.
        _trackedAlerts.TryAdd(
            @event.Alert.AlertId,
            new TrackedAlert(@event.SiteId, @event.Alert, @event.RemindFrom, OpeningPending: @event.Told));
    }

    public void Apply(AlertDropped @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        _trackedAlerts.Remove(@event.AlertId);
        ForgetWindowOpeningUnlessHeld();
    }

    public void Apply(DeliveryHeld @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        if (!_trackedAlerts.TryGetValue(@event.AlertId, out var alert))
        {
            return;
        }

        // One entry per Alert however many of its deliveries are held: only the first sets the held-from instant.
        _trackedAlerts[@event.AlertId] = alert with
        {
            PreviousDueAt = @event.DueAt,
            OpeningPending = false,
            HeldFrom = alert.HeldFrom ?? @event.DueAt,
        };
        WindowOpensAt = @event.WindowOpensAt;
    }

    public void Apply(NotificationSent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        foreach (var alertId in @event.AlertIds)
        {
            if (!_trackedAlerts.TryGetValue(alertId, out var alert))
            {
                continue;
            }

            // A summary releases what was held and moves no deadline: the Reminders stay anchored to their own
            // due-at. An Alert or Reminder notification is that due-at.
            _trackedAlerts[alertId] = @event.Kind == NotificationKind.Summary
                ? alert with { HeldFrom = null }
                : alert with { PreviousDueAt = @event.DueAt, OpeningPending = false };
        }

        ForgetWindowOpeningUnlessHeld();
    }

    public void Apply(PushDeviceRegistered @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        _pushRegistrations[@event.InstallationId] = new PushRegistration(
            @event.InstallationId,
            @event.Platform,
            @event.Token,
            @event.Environment,
            @event.RegisteredAt);
    }

    public void Apply(PushDeviceRemoved @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        _pushRegistrations.Remove(@event.InstallationId);
    }

    public void Apply(SiteAlertsPulled @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        _pendingPulls.Remove(@event.SiteId);
    }

    private List<Guid> AlertsOf(string siteId) =>
        [.. _trackedAlerts.Values
            .Where(alert => string.Equals(alert.SiteId, siteId, StringComparison.Ordinal))
            .Select(alert => alert.Alert.AlertId)];

    // The window-opening due-at exists only while something is held.
    private void ForgetWindowOpeningUnlessHeld()
    {
        if (!_trackedAlerts.Values.Any(alert => alert.HeldFrom is not null))
        {
            WindowOpensAt = null;
        }
    }

    // A Site with no mute, no cadence of the User's own and no synced Site cadence keeps no entry. A synced
    // Site cadence, Daily included, keeps one.
    private void Set(string siteId, UserSiteNotifications settings)
    {
        if (settings == UserSiteNotifications.None)
        {
            _siteNotifications.Remove(siteId);
        }
        else
        {
            _siteNotifications[siteId] = settings;
        }
    }
}
