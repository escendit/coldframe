using Coldframe.Contracts.Sites;

namespace Coldframe.Server.Identity;

/// <summary>
/// The state of the Site grain: lifecycle, name and Memberships.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.site-state")]
public sealed class SiteState
{
    [Id(0)]
    private readonly Dictionary<string, SiteRole> _members = new(StringComparer.Ordinal);

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
    }
}
