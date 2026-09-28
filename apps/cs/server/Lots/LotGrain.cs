using Coldframe.Contracts.Lots;
using Coldframe.Server.Journal;

namespace Coldframe.Server.Lots;

/// <summary>
/// A Lot, keyed by its Lot ID. The only writer of the Lot (AD-2), including its occupancy (AD-18): a Lot
/// holding a Node refuses removal. Removal is an event plus a tombstone; the ID stays resolvable (AD-20).
/// After each event it brings the lots projection up to date before it returns (read-your-writes).
/// </summary>
[GrainType("lot")]
public sealed class LotGrain : JournaledStreamGrain<LotState>, ILotGrain
{
    private string LotId => this.GetPrimaryKeyString();

    /// <inheritdoc />
    public async Task<LotResult> Create(string siteId, string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (State.Lifecycle != LotLifecycle.Uncreated)
        {
            if (!BelongsTo(siteId))
            {
                return new LotResult(LotOutcome.NotFound);
            }

            // Idempotent: a retry after the event was journaled only brings the projection up to date.
            await CatchUpLotsAsync();
            return Result(LotOutcome.Created);
        }

        RaiseEvent(new LotCreated(siteId, name));
        await ConfirmEvents();
        await CatchUpLotsAsync();

        return Result(LotOutcome.Created);
    }

    /// <inheritdoc />
    public async Task<LotResult> Rename(string siteId, string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (State.Lifecycle != LotLifecycle.Active || !BelongsTo(siteId))
        {
            return new LotResult(LotOutcome.NotFound);
        }

        if (string.Equals(State.Name, name, StringComparison.Ordinal))
        {
            return Result(LotOutcome.Unchanged);
        }

        RaiseEvent(new LotRenamed(name));
        await ConfirmEvents();
        await CatchUpLotsAsync();

        return Result(LotOutcome.Renamed);
    }

    /// <inheritdoc />
    public async Task<LotResult> Remove(string siteId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteId);

        if (State.Lifecycle == LotLifecycle.Uncreated || !BelongsTo(siteId))
        {
            return new LotResult(LotOutcome.NotFound);
        }

        if (State.Lifecycle == LotLifecycle.Removed)
        {
            return Result(LotOutcome.AlreadyRemoved);
        }

        if (State.ClaimedBy is not null)
        {
            // FR-6, AD-18: the Node must be moved or unassigned first. Nothing is journaled.
            return Result(LotOutcome.Claimed);
        }

        RaiseEvent(new LotRemoved());
        await ConfirmEvents();
        await CatchUpLotsAsync();

        return Result(LotOutcome.Removed);
    }

    /// <inheritdoc />
    public Task<LotResult> Describe(string siteId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteId);

        return Task.FromResult(
            State.Lifecycle == LotLifecycle.Uncreated || !BelongsTo(siteId)
                ? new LotResult(LotOutcome.NotFound)
                : Result(LotOutcome.Found));
    }

    private bool BelongsTo(string siteId) => string.Equals(State.SiteId, siteId, StringComparison.Ordinal);

    private LotResult Result(LotOutcome outcome) =>
        new(outcome, new LotSummary(LotId, State.SiteId!, State.Name!, State.ClaimedBy, State.Lifecycle == LotLifecycle.Removed));

    // Not cancelled by the caller: the events are journaled, so the projection follows.
    private Task CatchUpLotsAsync() =>
        ServiceProvider.GetProjectionRunner<LotsProjector>().CatchUpAsync(CancellationToken.None);
}
