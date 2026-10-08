using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Sites;

namespace Coldframe.Server.Identity;

/// <summary>
/// One Lot creation a caller asked the Site for, by caller-scoped idempotency key.
/// </summary>
/// <param name="LotId">The Lot ID chosen for the request.</param>
/// <param name="Name">The requested Lot name.</param>
/// <param name="RequestedAt">When the request was received.</param>
/// <param name="Completed">Whether the Lot exists.</param>
[GenerateSerializer]
[Alias("coldframe.site-lot-creation")]
public sealed record LotCreation(
    [property: Id(0)] string LotId,
    [property: Id(1)] string Name,
    [property: Id(2)] DateTimeOffset RequestedAt,
    [property: Id(3)] bool Completed);

/// <summary>
/// One Device registration by caller-scoped idempotency key.
/// </summary>
/// <param name="DeviceId">The Device the key registered.</param>
/// <param name="RegisteredAt">When the Device was registered.</param>
[GenerateSerializer]
[Alias("coldframe.site-device-registration")]
public sealed record DeviceRegistration(
    [property: Id(0)] string DeviceId,
    [property: Id(1)] DateTimeOffset RegisteredAt);

/// <summary>
/// The state of the Site grain: lifecycle, name, Memberships, former members, whether an ownerless
/// edit is being refused, and the Site's open Alerts (Story 6.1).
/// </summary>
[GenerateSerializer]
[Alias("coldframe.site-state")]
public sealed class SiteState
{
    [Id(0)]
    private readonly Dictionary<string, SiteRole> _members = new(StringComparer.Ordinal);

    [Id(3)]
    private readonly HashSet<string> _formerMembers = new(StringComparer.Ordinal);

    [Id(5)]
    private readonly Dictionary<string, LotCreation> _lotCreations = new(StringComparer.Ordinal);

    [Id(6)]
    private readonly Dictionary<string, DeviceKind> _devices = new(StringComparer.Ordinal);

    [Id(7)]
    private readonly Dictionary<string, DeviceRegistration> _deviceRegistrations = new(StringComparer.Ordinal);

    [Id(8)]
    private readonly Dictionary<Guid, SiteAlert> _openAlerts = [];

    /// <summary>
    /// Where the Site is in its lifecycle.
    /// </summary>
    [Id(1)]
    public SiteLifecycle Lifecycle { get; private set; }

    /// <summary>
    /// The Site name, once created.
    /// </summary>
    [Id(2)]
    public string? Name { get; private set; }

    /// <summary>
    /// Each member's Role, by User ID.
    /// </summary>
    public IReadOnlyDictionary<string, SiteRole> Members => _members;

    /// <summary>
    /// The User IDs of the Owners.
    /// </summary>
    public IReadOnlySet<string> Owners =>
        _members.Where(pair => pair.Value == SiteRole.Owner).Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The User IDs that held a Role on the Site and hold none now.
    /// </summary>
    public IReadOnlySet<string> FormerMembers => _formerMembers;

    /// <summary>
    /// Whether Keycloak showed the Site without an Owner and no Owner has reappeared since (AD-3 break-glass).
    /// </summary>
    [Id(4)]
    public bool OwnerlessEditRefused { get; private set; }

    /// <summary>
    /// The Lot creations by caller-scoped idempotency key (<c>{sub}:{key}</c>).
    /// </summary>
    public IReadOnlyDictionary<string, LotCreation> LotCreations => _lotCreations;

    /// <summary>
    /// Returns the Lot creation that still holds <paramref name="key"/> at <paramref name="now"/>. A completed
    /// request expires 24 h after it was received; a pending one never does, because its Lot may exist and a
    /// retry must resume it (as <see cref="UserState.FindLive"/> does for Sites).
    /// </summary>
    public LotCreation? FindLiveLotCreation(string key, DateTimeOffset now) =>
        _lotCreations.TryGetValue(key, out var creation)
        && (!creation.Completed || now - creation.RequestedAt < UserState.IdempotencyKeyLifetime)
            ? creation
            : null;

    /// <summary>
    /// The Device roster (AD-18): each Device's kind, by Device ID.
    /// </summary>
    public IReadOnlyDictionary<string, DeviceKind> Devices => _devices;

    /// <summary>
    /// The Device registrations by caller-scoped idempotency key (<c>{sub}:{key}</c>).
    /// </summary>
    public IReadOnlyDictionary<string, DeviceRegistration> DeviceRegistrations => _deviceRegistrations;

    /// <summary>
    /// Returns the Device registration that still holds <paramref name="key"/> at <paramref name="now"/>: a
    /// registration expires 24 h after it was made.
    /// </summary>
    public DeviceRegistration? FindLiveDeviceRegistration(string key, DateTimeOffset now) =>
        _deviceRegistrations.TryGetValue(key, out var registration)
        && now - registration.RegisteredAt < UserState.IdempotencyKeyLifetime
            ? registration
            : null;

    /// <summary>
    /// The Site's open Alerts by Alert ID (Story 6.1), as the Alert grains reported them: rebuilt from
    /// <see cref="SiteAlertOpened"/> and <see cref="SiteAlertClosed"/> on the Site's own stream.
    /// </summary>
    public IReadOnlyDictionary<Guid, SiteAlert> OpenAlerts => _openAlerts;

    public void Apply(SiteCreated @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        Lifecycle = SiteLifecycle.Active;
        Name = @event.Name;
    }

    public void Apply(MembershipGranted @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        _members[@event.UserId] = @event.Role;
        _formerMembers.Remove(@event.UserId);
    }

    public void Apply(MembershipRevoked @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        if (_members.Remove(@event.UserId))
        {
            _formerMembers.Add(@event.UserId);
        }
    }

    public void Apply(SiteRenamed @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        Name = @event.Name;
    }

    public void Apply(SiteDeleted @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        // Terminal. The Members stay, so the ID and its history remain resolvable (AD-20).
        Lifecycle = SiteLifecycle.Deleted;
    }

    public void Apply(SiteOwnerlessEditRefused @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        OwnerlessEditRefused = true;
    }

    public void Apply(SiteOwnerlessEditResolved @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        OwnerlessEditRefused = false;
    }

    public void Apply(LotCreationRequested @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        _lotCreations[@event.Key] = new LotCreation(@event.LotId, @event.Name, @event.RequestedAt, Completed: false);
    }

    public void Apply(LotCreationCompleted @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        if (_lotCreations.TryGetValue(@event.Key, out var creation))
        {
            _lotCreations[@event.Key] = creation with { Completed = true };
        }
    }

    public void Apply(DeviceRegistered @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        _devices[@event.DeviceId] = @event.Kind;
        _deviceRegistrations[@event.IdempotencyKey] = new DeviceRegistration(@event.DeviceId, @event.RegisteredAt);
    }

    public void Apply(SiteAlertOpened @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        _openAlerts[@event.Alert.AlertId] = @event.Alert;
    }

    public void Apply(SiteAlertClosed @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        _openAlerts.Remove(@event.AlertId);
    }
}
