using Microsoft.Extensions.DependencyInjection;
using Orleans.EventSourcing;
using Orleans.EventSourcing.CustomStorage;

namespace Coldframe.Server.Journal;

/// <summary>
/// The base of every event-sourced grain (AD-2): a <see cref="JournaledGrain{TGrainState, TEventBase}"/>
/// whose log lives in the journal through the CustomStorage log-consistency provider.
/// </summary>
/// <remarks>
/// On activation the grain replays its stream; old event versions arrive already upcast. Events are
/// applied through public <c>Apply</c> overloads on <typeparamref name="TState"/>. When another writer
/// has moved the stream on, the append returns <see langword="false"/> and Orleans re-reads the stream.
/// </remarks>
/// <typeparam name="TState">The grain state.</typeparam>
public abstract class JournaledStreamGrain<TState> : JournaledGrain<TState, object>, ICustomStorageInterface<TState, object>
    where TState : class, new()
{
    private JournalStore? _store;

    /// <summary>
    /// The journal stream of this grain: its grain ID, such as <c>site/0192…</c>.
    /// </summary>
    protected virtual string StreamId => this.GetGrainId().ToString();

    /// <summary>
    /// The only clock grain code reads (AD-6): the unkeyed <see cref="System.TimeProvider"/> from the
    /// container, which tests replace with a fake.
    /// </summary>
    protected TimeProvider Clock => ServiceProvider.GetRequiredService<TimeProvider>();

    private JournalStore Store => _store ??= ServiceProvider.GetRequiredService<JournalStore>();

    /// <inheritdoc />
    public async Task<KeyValuePair<int, TState>> ReadStateFromStorage()
    {
        var events = await Store.ReadStreamAsync(StreamId).ConfigureAwait(true);
        var state = new TState();

        foreach (var @event in events)
        {
            TransitionState(state, @event.Data);
        }

        return new KeyValuePair<int, TState>(events.Count == 0 ? 0 : events[^1].Version, state);
    }

    /// <inheritdoc />
    public Task<bool> ApplyUpdatesToStorage(IReadOnlyList<object> updates, int expectedVersion) =>
        Store.AppendAsync(StreamId, expectedVersion, updates);
}
