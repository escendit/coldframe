namespace Coldframe.Server.Journal;

/// <summary>
/// One event read from the journal, already upcast to the newest schema version of its contract.
/// </summary>
/// <param name="Position">The global position, in commit order.</param>
/// <param name="StreamId">The stream, one per journaled grain.</param>
/// <param name="Version">The version within the stream, starting at 1.</param>
/// <param name="RecordedAt">When the Server recorded it, in UTC, from its <see cref="TimeProvider"/>.</param>
/// <param name="Data">The event contract.</param>
public sealed record JournalEvent(long Position, string StreamId, int Version, DateTimeOffset RecordedAt, object Data);
