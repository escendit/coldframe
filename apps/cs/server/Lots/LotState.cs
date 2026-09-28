using Coldframe.Contracts.Lots;

namespace Coldframe.Server.Lots;

/// <summary>
/// The state of the Lot grain: lifecycle, Site, name and the Node that occupies it.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.lot-state")]
public sealed class LotState
{
    /// <summary>
    /// Where the Lot is in its lifecycle.
    /// </summary>
    [Id(0)]
    public LotLifecycle Lifecycle { get; private set; }

    /// <summary>
    /// The Site the Lot belongs to, once created.
    /// </summary>
    [Id(1)]
    public string? SiteId { get; private set; }

    /// <summary>
    /// The Lot name, once created.
    /// </summary>
    [Id(2)]
    public string? Name { get; private set; }

    /// <summary>
    /// The Node that occupies the Lot (AD-18), or <see langword="null"/> when it is free.
    /// </summary>
    [Id(3)]
    public string? ClaimedBy { get; private set; }

    public void Apply(LotCreated @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        Lifecycle = LotLifecycle.Active;
        SiteId = @event.SiteId;
        Name = @event.Name;
    }

    public void Apply(LotRenamed @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        Name = @event.Name;
    }

    public void Apply(LotClaimed @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ClaimedBy = @event.NodeId;
    }

    public void Apply(LotReleased @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        if (string.Equals(ClaimedBy, @event.NodeId, StringComparison.Ordinal))
        {
            ClaimedBy = null;
        }
    }

    public void Apply(LotRemoved @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        // Terminal. The name and Site stay, so the ID remains resolvable (AD-20).
        Lifecycle = LotLifecycle.Removed;
    }
}
