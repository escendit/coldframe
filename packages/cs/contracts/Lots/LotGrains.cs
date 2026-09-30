namespace Coldframe.Contracts.Lots;

/// <summary>
/// A Lot, keyed by its Lot ID (a UUIDv7). The only writer of the Lot's state, including its occupancy
/// (AD-2, AD-18). Every call names the Site the caller reached it through; a Lot of another Site answers
/// <see cref="LotOutcome.NotFound"/>.
/// </summary>
[Alias("coldframe.lot")]
public interface ILotGrain : IGrainWithStringKey
{
    /// <summary>
    /// Creates the Lot on <paramref name="siteId"/>. Idempotent for the same Site: a Lot that exists
    /// already answers <see cref="LotOutcome.Created"/> with its current state. Only the Site grain calls it.
    /// </summary>
    /// <param name="siteId">The Site ID.</param>
    /// <param name="name">The Lot name, already trimmed and validated.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("create")]
    Task<LotResult> Create(string siteId, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames the Lot. The current name answers <see cref="LotOutcome.Unchanged"/> and journals nothing.
    /// </summary>
    /// <param name="siteId">The Site ID the caller reached the Lot through.</param>
    /// <param name="name">The Lot name, already trimmed and validated.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("rename")]
    Task<LotResult> Rename(string siteId, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the Lot. A Lot holding a Node answers <see cref="LotOutcome.Claimed"/> and journals nothing;
    /// a removed Lot answers <see cref="LotOutcome.AlreadyRemoved"/>.
    /// </summary>
    /// <param name="siteId">The Site ID the caller reached the Lot through.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("remove")]
    Task<LotResult> Remove(string siteId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the Lot, removed or not.
    /// </summary>
    /// <param name="siteId">The Site ID the caller reached the Lot through.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("describe")]
    Task<LotResult> Describe(string siteId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Claims the Lot for a Node (AD-18): the only occupancy check. A free Lot journals
    /// <see cref="LotClaimed"/> and answers <see cref="LotOutcome.Held"/>; a Lot this Node holds already
    /// answers <see cref="LotOutcome.Held"/> and journals nothing. A Lot another Node holds answers
    /// <see cref="LotOutcome.Claimed"/>, a removed Lot <see cref="LotOutcome.AlreadyRemoved"/>, and an
    /// uncreated Lot or one of another Site <see cref="LotOutcome.NotFound"/>; none of them journals anything.
    /// Only the Device grain calls it.
    /// </summary>
    /// <param name="siteId">The Site ID the Node is enrolled on.</param>
    /// <param name="nodeId">The Node's Device ID.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("claim")]
    Task<LotResult> Claim(string siteId, string nodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases the Lot from a Node (AD-18). When <paramref name="nodeId"/> holds the Lot it journals
    /// <see cref="LotReleased"/> and answers <see cref="LotOutcome.Released"/>; otherwise (another Node holds
    /// it, or nobody) it journals nothing and answers <see cref="LotOutcome.Unchanged"/>. An uncreated Lot or
    /// one of another Site answers <see cref="LotOutcome.NotFound"/>. Idempotent. Only the Device grain calls
    /// it, to move or unassign a Node (Story 4.9).
    /// </summary>
    /// <param name="siteId">The Site ID the Node is enrolled on.</param>
    /// <param name="nodeId">The Node's Device ID.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("release")]
    Task<LotResult> Release(string siteId, string nodeId, CancellationToken cancellationToken = default);
}

/// <summary>
/// How a call on a Lot ended.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.lot-outcome")]
public enum LotOutcome
{
    /// <summary>
    /// The Lot exists, now or already.
    /// </summary>
    Created = 0,

    /// <summary>
    /// The Lot was renamed.
    /// </summary>
    Renamed = 1,

    /// <summary>
    /// Nothing needed to change. Nothing was journaled.
    /// </summary>
    Unchanged = 2,

    /// <summary>
    /// The Lot was removed now.
    /// </summary>
    Removed = 3,

    /// <summary>
    /// The Lot was removed before. Nothing was journaled.
    /// </summary>
    AlreadyRemoved = 4,

    /// <summary>
    /// A Node is assigned to the Lot, so it cannot be removed, or (for a claim) another Node holds it.
    /// Nothing was journaled.
    /// </summary>
    Claimed = 5,

    /// <summary>
    /// No such Lot on this Site, or (for a rename) it was removed. Nothing was journaled.
    /// </summary>
    NotFound = 6,

    /// <summary>
    /// The Lot exists; <see cref="LotResult.Lot"/> describes it.
    /// </summary>
    Found = 7,

    /// <summary>
    /// The claiming Node holds the Lot, now or already.
    /// </summary>
    Held = 8,

    /// <summary>
    /// The releasing Node held the Lot, and the Lot is free now.
    /// </summary>
    Released = 9,
}

/// <summary>
/// A Lot as the grain holds it.
/// </summary>
/// <param name="Id">The Lot ID.</param>
/// <param name="SiteId">The Site ID.</param>
/// <param name="Name">The Lot name.</param>
/// <param name="ClaimedBy">The Node that occupies the Lot, if any.</param>
/// <param name="Removed">Whether the Lot was removed.</param>
[GenerateSerializer]
[Alias("coldframe.lot-summary")]
public sealed record LotSummary(
    [property: Id(0)] string Id,
    [property: Id(1)] string SiteId,
    [property: Id(2)] string Name,
    [property: Id(3)] string? ClaimedBy,
    [property: Id(4)] bool Removed);

/// <summary>
/// The result of a call on <see cref="ILotGrain"/>.
/// </summary>
/// <param name="Outcome">How the call ended.</param>
/// <param name="Lot">The Lot afterwards, unless <paramref name="Outcome"/> is <see cref="LotOutcome.NotFound"/>.</param>
[GenerateSerializer]
[Alias("coldframe.lot-result")]
public sealed record LotResult([property: Id(0)] LotOutcome Outcome, [property: Id(1)] LotSummary? Lot = null);
