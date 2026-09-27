namespace Coldframe.Server.Journal;

/// <summary>
/// The Orleans stream that carries journal hints (AD-5, AD-21). A hint holds only a global position;
/// it tells projection runners to read the journal now instead of at their next poll.
/// </summary>
public static class JournalHints
{
    /// <summary>
    /// The name of the stream provider. NATS JetStream in the Server, memory streams in tests.
    /// </summary>
    public const string StreamProvider = "hints";

    /// <summary>
    /// The stream namespace the journal hint grain subscribes to implicitly.
    /// </summary>
    public const string StreamNamespace = "journal-hints";

    /// <summary>
    /// The one stream key. With a single silo, one stream and one subscriber grain are enough.
    /// </summary>
    public static readonly Guid StreamKey = Guid.Empty;
}
