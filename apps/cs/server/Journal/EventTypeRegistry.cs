using System.Reflection;
using Coldframe.Contracts.Events;

namespace Coldframe.Server.Journal;

/// <summary>
/// One registered event contract: its stable alias, its schema version and its CLR type.
/// </summary>
public sealed record EventTypeInfo(string Alias, int SchemaVersion, Type ClrType);

/// <summary>
/// Maps event contracts to their stable aliases and schema versions, and chains the upcasters that
/// keep old versions readable (AD-21).
/// </summary>
/// <remarks>
/// Only public types are registered. The registry refuses a set in which an alias has a gap in its
/// schema versions, two contracts share an alias and version, or an old version has no upcaster to the
/// next, so an unreadable journal fails when the registry is built, before any row is read.
/// </remarks>
public sealed class EventTypeRegistry
{
    private static readonly Type UpcasterDefinition = typeof(IEventUpcaster<,>);

    private readonly Dictionary<(string Alias, int SchemaVersion), EventTypeInfo> _byAlias = [];
    private readonly Dictionary<Type, EventTypeInfo> _byType = [];
    private readonly Dictionary<string, int> _newest = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, Func<object, object>> _upcasters = [];

    /// <summary>
    /// Scans the exported types of <paramref name="assemblies"/> for contracts and upcasters.
    /// </summary>
    public EventTypeRegistry(IEnumerable<Assembly> assemblies)
        : this(Scan(assemblies))
    {
    }

    /// <summary>
    /// Registers exactly the given contracts and upcasters.
    /// </summary>
    public EventTypeRegistry(IEnumerable<Type> eventTypes, IEnumerable<Type> upcasterTypes)
    {
        ArgumentNullException.ThrowIfNull(eventTypes);
        ArgumentNullException.ThrowIfNull(upcasterTypes);

        foreach (var type in eventTypes.Distinct())
        {
            Register(type);
        }

        foreach (var type in upcasterTypes.Distinct())
        {
            RegisterUpcaster(type);
        }

        Validate();
    }

    private EventTypeRegistry((List<Type> EventTypes, List<Type> UpcasterTypes) scanned)
        : this(scanned.EventTypes, scanned.UpcasterTypes)
    {
    }

    /// <summary>
    /// Every registered contract, ordered by alias and schema version.
    /// </summary>
    public IReadOnlyList<EventTypeInfo> EventTypes =>
        [.. _byAlias.Values.OrderBy(info => info.Alias, StringComparer.Ordinal).ThenBy(info => info.SchemaVersion)];

    /// <summary>
    /// Returns the contract registered for an alias and schema version.
    /// </summary>
    /// <exception cref="UnknownEventTypeException">No contract carries that alias and schema version.</exception>
    public Type Resolve(string alias, int schemaVersion) =>
        _byAlias.TryGetValue((alias, schemaVersion), out var info)
            ? info.ClrType
            : throw new UnknownEventTypeException(alias, schemaVersion);

    /// <summary>
    /// Returns the newest schema version registered for an alias.
    /// </summary>
    public int GetNewestSchemaVersion(string alias) =>
        _newest.TryGetValue(alias, out var version) ? version : throw new UnknownEventTypeException(alias, 0);

    /// <summary>
    /// Returns the registration of a contract type.
    /// </summary>
    /// <exception cref="InvalidOperationException">The type is not a registered contract.</exception>
    public EventTypeInfo GetInfo(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return _byType.TryGetValue(type, out var info)
            ? info
            : throw new InvalidOperationException(
                $"'{type.FullName}' is not a registered event contract. Mark it with [EventType] in a scanned assembly.");
    }

    /// <summary>
    /// Runs the upcasters from the event's schema version up to the newest one.
    /// </summary>
    public object UpcastToNewest(object @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var info = GetInfo(@event.GetType());
        var newest = _newest[info.Alias];

        for (var version = info.SchemaVersion; version < newest; version++)
        {
            @event = _upcasters[@event.GetType()](@event);
        }

        return @event;
    }

    private static (List<Type> EventTypes, List<Type> UpcasterTypes) Scan(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        var exported = assemblies.Distinct().SelectMany(assembly => assembly.GetExportedTypes()).ToList();

        return (
            exported.Where(type => type.GetCustomAttribute<EventTypeAttribute>() is not null).ToList(),
            exported.Where(type => type is { IsClass: true, IsAbstract: false } && GetUpcasterInterfaces(type).Any()).ToList());
    }

    private static IEnumerable<Type> GetUpcasterInterfaces(Type type) =>
        type.GetInterfaces().Where(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == UpcasterDefinition);

    private void Register(Type type)
    {
        var attribute = type.GetCustomAttribute<EventTypeAttribute>()
            ?? throw new InvalidOperationException($"'{type.FullName}' has no [EventType] attribute.");

        if (string.IsNullOrWhiteSpace(attribute.Alias) || attribute.SchemaVersion < 1)
        {
            throw new InvalidOperationException(
                $"'{type.FullName}' needs a non-empty alias and a schema version of at least 1.");
        }

        var info = new EventTypeInfo(attribute.Alias, attribute.SchemaVersion, type);

        if (!_byAlias.TryAdd((info.Alias, info.SchemaVersion), info))
        {
            throw new InvalidOperationException(
                $"Both '{_byAlias[(info.Alias, info.SchemaVersion)].ClrType.FullName}' and '{type.FullName}' " +
                $"claim alias '{info.Alias}' with schema version {info.SchemaVersion}.");
        }

        _byType.Add(type, info);
        _newest[info.Alias] = Math.Max(_newest.GetValueOrDefault(info.Alias), info.SchemaVersion);
    }

    private void RegisterUpcaster(Type type)
    {
        foreach (var contract in GetUpcasterInterfaces(type))
        {
            var from = GetInfo(contract.GenericTypeArguments[0]);
            var to = GetInfo(contract.GenericTypeArguments[1]);

            if (!string.Equals(from.Alias, to.Alias, StringComparison.Ordinal) || to.SchemaVersion != from.SchemaVersion + 1)
            {
                throw new InvalidOperationException(
                    $"'{type.FullName}' must upcast one schema version of one alias to the next, " +
                    $"not '{from.Alias}' v{from.SchemaVersion} to '{to.Alias}' v{to.SchemaVersion}.");
            }

            var instance = Activator.CreateInstance(type)
                ?? throw new InvalidOperationException($"'{type.FullName}' could not be created.");
            var method = contract.GetMethod(nameof(IEventUpcaster<object, object>.Upcast))!;

            if (!_upcasters.TryAdd(from.ClrType, source => method.Invoke(instance, [source])!))
            {
                throw new InvalidOperationException(
                    $"More than one upcaster reads '{from.Alias}' v{from.SchemaVersion}.");
            }
        }
    }

    private void Validate()
    {
        foreach (var (alias, newest) in _newest)
        {
            for (var version = 1; version <= newest; version++)
            {
                if (!_byAlias.TryGetValue((alias, version), out var info))
                {
                    throw new InvalidOperationException(
                        $"Alias '{alias}' has schema version {newest} but no contract for schema version {version}.");
                }

                if (version < newest && !_upcasters.ContainsKey(info.ClrType))
                {
                    throw new InvalidOperationException(
                        $"Alias '{alias}' v{version} ('{info.ClrType.FullName}') has no upcaster to v{version + 1}, " +
                        "so rows written with it could no longer be read.");
                }
            }
        }
    }
}
