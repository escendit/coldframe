namespace Coldframe.Contracts.Events;

/// <summary>
/// Gives a journaled event contract its stable alias and schema version (AD-21).
/// </summary>
/// <remarks>
/// The alias is written to every journal row instead of the CLR type name, so a contract can be
/// renamed or moved without breaking the journal. It never changes once an event has been written.
/// A breaking change to the payload keeps the alias, adds a new contract with the next schema
/// version, and keeps the old contract readable through an <see cref="IEventUpcaster{TFrom, TTo}"/>.
/// </remarks>
/// <param name="alias">The stable alias, lowercase and dot-separated, such as <c>site.renamed</c>.</param>
/// <param name="schemaVersion">The schema version of this contract, starting at 1.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class EventTypeAttribute(string alias, int schemaVersion = 1) : Attribute
{
    /// <summary>
    /// The stable alias stored in the journal.
    /// </summary>
    public string Alias { get; } = alias;

    /// <summary>
    /// The schema version of this contract.
    /// </summary>
    public int SchemaVersion { get; } = schemaVersion;
}
