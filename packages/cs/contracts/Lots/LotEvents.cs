using Coldframe.Contracts.Events;

namespace Coldframe.Contracts.Lots;

/// <summary>
/// The Lot was created on a Site. The first event of every <c>lot/{id}</c> stream.
/// </summary>
/// <param name="SiteId">The Site the Lot belongs to, for its whole life.</param>
/// <param name="Name">The Lot name.</param>
[EventType("lot.created")]
[GenerateSerializer]
[Alias("coldframe.lot-created")]
public sealed record LotCreated([property: Id(0)] string SiteId, [property: Id(1)] string Name);

/// <summary>
/// The Lot was renamed.
/// </summary>
/// <param name="Name">The Lot name from now on.</param>
[EventType("lot.renamed")]
[GenerateSerializer]
[Alias("coldframe.lot-renamed")]
public sealed record LotRenamed([property: Id(0)] string Name);

/// <summary>
/// A Node was assigned to the Lot; the Lot holds its claim (AD-18). Journaled by Node assignment, which
/// arrives in Epic 4; until then only fixtures write it.
/// </summary>
/// <param name="NodeId">The Node that occupies the Lot.</param>
[EventType("lot.claimed")]
[GenerateSerializer]
[Alias("coldframe.lot-claimed")]
public sealed record LotClaimed([property: Id(0)] string NodeId);

/// <summary>
/// The Node left the Lot; the Lot is free again.
/// </summary>
/// <param name="NodeId">The Node that left.</param>
[EventType("lot.released")]
[GenerateSerializer]
[Alias("coldframe.lot-released")]
public sealed record LotReleased([property: Id(0)] string NodeId);

/// <summary>
/// The Lot was removed. Terminal: the stream and a tombstoned read-model row stay, so the Lot ID remains
/// resolvable for history (AD-20).
/// </summary>
[EventType("lot.removed")]
[GenerateSerializer]
[Alias("coldframe.lot-removed")]
public sealed record LotRemoved;
