namespace Coldframe.Server.Journal;

/// <summary>
/// A journal row names an alias and schema version that no registered contract carries.
/// </summary>
public sealed class UnknownEventTypeException : Exception
{
    public UnknownEventTypeException()
        : this(string.Empty, 0)
    {
    }

    public UnknownEventTypeException(string message)
        : base(message)
    {
        Alias = string.Empty;
    }

    public UnknownEventTypeException(string message, Exception innerException)
        : base(message, innerException)
    {
        Alias = string.Empty;
    }

    public UnknownEventTypeException(string alias, int schemaVersion)
        : base($"No event contract is registered for alias '{alias}' with schema version {schemaVersion}.")
    {
        Alias = alias;
        SchemaVersion = schemaVersion;
    }

    /// <summary>
    /// The alias of the row.
    /// </summary>
    public string Alias { get; }

    /// <summary>
    /// The schema version of the row.
    /// </summary>
    public int SchemaVersion { get; }
}
