using System.Text.Json;
using System.Text.Json.Serialization;

namespace Coldframe.Server.Journal;

/// <summary>
/// An event as the journal stores it.
/// </summary>
public sealed record SerializedEvent(string Alias, int SchemaVersion, string Payload);

/// <summary>
/// Writes and reads journal payloads with System.Text.Json (AD-21): camelCase, enums as strings,
/// absent optional fields omitted.
/// </summary>
public sealed class JournalSerializer(EventTypeRegistry registry)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// The registry this serializer resolves aliases with.
    /// </summary>
    public EventTypeRegistry Registry { get; } = registry ?? throw new ArgumentNullException(nameof(registry));

    /// <summary>
    /// Serializes an event. Only the newest schema version of a contract may be written.
    /// </summary>
    public SerializedEvent Serialize(object @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var info = Registry.GetInfo(@event.GetType());
        var newest = Registry.GetNewestSchemaVersion(info.Alias);

        if (info.SchemaVersion != newest)
        {
            throw new InvalidOperationException(
                $"'{info.ClrType.FullName}' is schema version {info.SchemaVersion} of '{info.Alias}'; " +
                $"new events are written as schema version {newest}.");
        }

        return new SerializedEvent(info.Alias, info.SchemaVersion, JsonSerializer.Serialize(@event, info.ClrType, Options));
    }

    /// <summary>
    /// Reads a stored event as the newest schema version of its contract.
    /// </summary>
    /// <exception cref="UnknownEventTypeException">No contract carries the alias and schema version.</exception>
    public object Deserialize(string alias, int schemaVersion, string payload)
    {
        ArgumentNullException.ThrowIfNull(alias);
        ArgumentNullException.ThrowIfNull(payload);

        var type = Registry.Resolve(alias, schemaVersion);
        var @event = JsonSerializer.Deserialize(payload, type, Options)
            ?? throw new JsonException($"The payload of '{alias}' v{schemaVersion} is null.");

        return Registry.UpcastToNewest(@event);
    }
}
