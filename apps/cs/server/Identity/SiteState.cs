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
/// The state of the Site grain: lifecycle, name, Memberships, former members and whether an ownerless
/// edit is being refused.
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
}
