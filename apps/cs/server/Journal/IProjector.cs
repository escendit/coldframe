using Npgsql;

namespace Coldframe.Server.Journal;

/// <summary>
/// Builds one read model from the journal (AD-21).
/// </summary>
/// <remarks>
/// The <see cref="ProjectionRunner"/> hands every event to <see cref="ApplyAsync"/> in global-position
/// order, inside the transaction that also moves the projector's checkpoint, and never hands it an event
/// at or below that checkpoint. A projector writes only through that transaction and ignores events it
/// does not project. It reports every row it touched to the <see cref="ReadModelChanges"/> it is handed
/// (Story 6.6); the runner publishes them as hints once the transaction is committed.
/// </remarks>
public interface IProjector
{
    /// <summary>
    /// The stable name of the projector; its checkpoint is stored under it.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Applies one event to the read model.
    /// </summary>
    /// <param name="journalEvent">The event.</param>
    /// <param name="transaction">The transaction that also moves the checkpoint.</param>
    /// <param name="changes">Takes the rows this event touched.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task ApplyAsync(JournalEvent journalEvent, NpgsqlTransaction transaction, ReadModelChanges changes, CancellationToken cancellationToken);
}
