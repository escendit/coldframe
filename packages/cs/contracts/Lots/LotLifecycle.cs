namespace Coldframe.Contracts.Lots;

/// <summary>
/// The lifecycle of a Lot.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.lot-lifecycle")]
public enum LotLifecycle
{
    /// <summary>
    /// The Lot grain has not been created.
    /// </summary>
    Uncreated = 0,

    /// <summary>
    /// The Lot exists.
    /// </summary>
    Active = 1,

    /// <summary>
    /// The Lot was removed. Its ID stays resolvable; nothing is hard-deleted.
    /// </summary>
    Removed = 2,
}
