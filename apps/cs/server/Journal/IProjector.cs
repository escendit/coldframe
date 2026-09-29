using Npgsql;

namespace Coldframe.Server.Journal;

/// <summary>
/// Builds one read model from the journal (AD-21).
/// </summary>
/// <remarks>
/// The <see cref="ProjectionRunner"/> hands every event to <see cref="ApplyAsync"/> in global-position
/// order, inside the transaction that also moves the projector's checkpoint, and never hands it an event
/// at or below that checkpoint. A projector writes only through that transaction and ignores events it
/// does not project.
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
    Task ApplyAsync(JournalEvent journalEvent, NpgsqlTransaction transaction, CancellationToken cancellationToken);
}
