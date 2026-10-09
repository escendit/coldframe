using Coldframe.Contracts.Sites;

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
    public ReminderCadence ResolvedReminderCadence =>
        ReminderCadence ?? SiteReminderCadence ?? Contracts.Sites.ReminderCadence.Daily;
}

/// <summary>
/// The state of the User grain: the Site creations it has seen, by idempotency key, its Role on each
/// Site it belongs to, and its notification settings (Story 6.3).
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
            _sites[@event.SiteId] = role;
        }
        else
        {
            _sites.Remove(@event.SiteId);

            // The Membership ended: the mute, the cadence and the Site's cadence go with it (Story 6.3).
            _siteNotifications.Remove(@event.SiteId);
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
