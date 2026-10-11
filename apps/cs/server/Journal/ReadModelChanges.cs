namespace Coldframe.Server.Journal;

/// <summary>
/// The names of the read models a hint can name (Story 6.6): the <c>ReadModelResource</c> values of
/// <c>packages/asyncapi</c>.
/// </summary>
public static class ReadModelResources
{
    /// <summary>
    /// The caller's Sites, a Site's name or a Membership; the ID is the Site.
    /// </summary>
    public const string Sites = "sites";

    /// <summary>
    /// A Lot or its status; the ID is the Lot.
    /// </summary>
    public const string Lots = "lots";

    /// <summary>
    /// New Readings of the Node on a Lot; the ID is the Lot.
    /// </summary>
    public const string Readings = "readings";

    /// <summary>
    /// A Device of a Site; the ID is the Device.
    /// </summary>
    public const string Devices = "devices";

    /// <summary>
    /// An Alert that opened or closed; the ID is the Alert.
    /// </summary>
    public const string Alerts = "alerts";
}

/// <summary>
/// One row of a read model that changed (Story 6.6): what a <c>readmodel.changed</c> hint is made from. It
/// names the row and never carries anything of it.
/// </summary>
/// <param name="SiteId">The Site whose members may be told.</param>
/// <param name="Resource">One of <see cref="ReadModelResources"/>.</param>
/// <param name="Id">The ID of what changed.</param>
/// <param name="Version">A positive number that never decreases for the same resource and ID.</param>
/// <param name="UserId">
/// The User a Membership was granted to or revoked from, who is told as well: that User is not, or no longer,
/// among the Site's members. <see langword="null"/> for every other change.
/// </param>
public sealed record ReadModelChange(string SiteId, string Resource, string Id, long Version, string? UserId = null);

/// <summary>
/// What a projector reports while it applies a batch: the rows it touched. The
/// <see cref="ProjectionRunner"/> sets <see cref="Position"/> to the event being applied, which becomes the
/// version of everything that event touches, and publishes the list only after the batch is committed.
/// </summary>
public sealed class ReadModelChanges
{
    private readonly List<ReadModelChange> _items = [];

    /// <summary>
    /// The global position of the event being applied.
    /// </summary>
    public long Position { get; set; }

    /// <summary>
    /// What was touched so far, in the order it was reported.
    /// </summary>
    public IReadOnlyList<ReadModelChange> Items => _items;

    /// <summary>
    /// Reports that the event being applied changed a row.
    /// </summary>
    /// <param name="siteId">The Site of the row.</param>
    /// <param name="resource">One of <see cref="ReadModelResources"/>.</param>
    /// <param name="id">The ID of what changed.</param>
    /// <param name="userId">The User of a granted or revoked Membership, otherwise <see langword="null"/>.</param>
    public void Touch(string siteId, string resource, string id, string? userId = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(siteId);
        ArgumentException.ThrowIfNullOrEmpty(resource);
        ArgumentException.ThrowIfNullOrEmpty(id);

        _items.Add(new ReadModelChange(siteId, resource, id, Position, userId));
    }
}

/// <summary>
/// Where committed read-model changes go (Story 6.6): the SignalR hub turns them into
/// <c>readmodel.changed</c> hints. A host without the hub registers none, and nothing is published.
/// </summary>
public interface IReadModelHints
{
    /// <summary>
    /// Takes changes that are already committed. It returns at once and never throws: a hint that cannot be
    /// sent is dropped, and it never fails or repeats the commit it follows.
    /// </summary>
    /// <param name="changes">The committed changes, in commit order.</param>
    void Publish(IReadOnlyList<ReadModelChange> changes);
}
