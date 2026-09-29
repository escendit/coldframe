using System.Text.Json;
using Coldframe.Contracts.Events;
using Coldframe.Server.Journal;
using Coldframe.Server.Tests.Samples;

namespace Coldframe.Server.Tests.Journal;

public sealed class EventTypeRegistryTests
{
    private static EventTypeRegistry CreateRegistry() => new([typeof(SampleCreated).Assembly]);

    [Fact]
    public void RegistersEveryAliasAndSchemaVersionOfTheScannedAssemblies()
    {
        var registry = CreateRegistry();

        Assert.Equal(typeof(SampleCreated), registry.Resolve("sample.created", 1));
        Assert.Equal(typeof(SampleNotedV1), registry.Resolve("sample.noted", 1));
        Assert.Equal(typeof(SampleNoted), registry.Resolve("sample.noted", 2));
        Assert.Equal(2, registry.GetNewestSchemaVersion("sample.noted"));
    }

    [Fact]
    public void SerializesTheNewestVersionWithItsAliasAndSchemaVersion()
    {
        var serializer = new JournalSerializer(CreateRegistry());

        var serialized = serializer.Serialize(new SampleNoted("water", 3));

        Assert.Equal("sample.noted", serialized.Alias);
        Assert.Equal(2, serialized.SchemaVersion);

        using var payload = JsonDocument.Parse(serialized.Payload);
        Assert.Equal("water", payload.RootElement.GetProperty("text").GetString());
        Assert.Equal(3, payload.RootElement.GetProperty("weight").GetInt32());
    }

    [Fact]
    public void RefusesToWriteAnOldSchemaVersion()
    {
        var serializer = new JournalSerializer(CreateRegistry());

        Assert.Throws<InvalidOperationException>(() => serializer.Serialize(new SampleNotedV1("water")));
    }

    [Fact]
    public void RefusesToWriteAnUnregisteredType()
    {
        var serializer = new JournalSerializer(CreateRegistry());

        Assert.Throws<InvalidOperationException>(() => serializer.Serialize(new Unregistered()));
    }

    [Fact]
    public void ReadsAnOldSchemaVersionThroughItsUpcaster()
    {
        var serializer = new JournalSerializer(CreateRegistry());

        var @event = serializer.Deserialize("sample.noted", 1, """{"text":"water"}""");

        var noted = Assert.IsType<SampleNoted>(@event);
        Assert.Equal("water", noted.Text);
        Assert.Equal(SampleNotedUpcaster.ImplicitWeight, noted.Weight);
    }

    [Fact]
    public void ReadsTheNewestSchemaVersionDirectly()
    {
        var serializer = new JournalSerializer(CreateRegistry());

        var @event = serializer.Deserialize("sample.noted", 2, """{"text":"water","weight":5}""");

        Assert.Equal(new SampleNoted("water", 5), @event);
    }

    [Theory]
    [InlineData("sample.unknown", 1)]
    [InlineData("sample.noted", 3)]
    public void FailsOnAnUnknownAliasOrSchemaVersionNamingBoth(string alias, int schemaVersion)
    {
        var serializer = new JournalSerializer(CreateRegistry());

        var exception = Assert.Throws<UnknownEventTypeException>(() => serializer.Deserialize(alias, schemaVersion, "{}"));

        Assert.Equal(alias, exception.Alias);
        Assert.Equal(schemaVersion, exception.SchemaVersion);
        Assert.Contains(alias, exception.Message, StringComparison.Ordinal);
        Assert.Contains($"schema version {schemaVersion}", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAnOldVersionWithoutAnUpcaster()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => new EventTypeRegistry([typeof(OrphanV1), typeof(OrphanV2)], []));

        Assert.Contains("broken.orphan", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAGapInSchemaVersions()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => new EventTypeRegistry([typeof(OrphanV2)], []));

        Assert.Contains("broken.orphan", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsTwoTypesWithTheSameAliasAndSchemaVersion()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => new EventTypeRegistry([typeof(OrphanV1), typeof(DuplicateOfOrphanV1)], []));

        Assert.Contains("broken.orphan", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAnUpcasterThatSkipsAVersion()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new EventTypeRegistry(
            [typeof(ChainV1), typeof(ChainV2), typeof(ChainV3)],
            [typeof(ChainV2ToV3), typeof(ChainV1ToV3)]));

        Assert.Contains(nameof(ChainV1ToV3), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAnUpcasterThatCrossesAliases()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new EventTypeRegistry(
            [typeof(ChainV1), typeof(ChainV2), typeof(OtherV2)],
            [typeof(ChainV1ToV2), typeof(ChainV1ToOtherV2)]));

        Assert.Contains(nameof(ChainV1ToOtherV2), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsTwoUpcastersFromTheSameVersion()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new EventTypeRegistry(
            [typeof(ChainV1), typeof(ChainV2)],
            [typeof(ChainV1ToV2), typeof(AnotherChainV1ToV2)]));

        Assert.Contains("broken.chain", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsAVersionOnePayloadThroughAChainOfUpcasters()
    {
        var registry = new EventTypeRegistry(
            [typeof(ChainV1), typeof(ChainV2), typeof(ChainV3)],
            [typeof(ChainV1ToV2), typeof(ChainV2ToV3)]);
        var serializer = new JournalSerializer(registry);

        var @event = serializer.Deserialize("broken.chain", 1, """{"value":"seed"}""");

        Assert.Equal(new ChainV3("seed", 2, "v3"), @event);
    }

    // Deliberately broken contracts. They are nested and private, so an assembly scan never finds them.
    [EventType("broken.orphan", 1)]
    private sealed record OrphanV1(string Value);

    [EventType("broken.orphan", 2)]
    private sealed record OrphanV2(string Value);

    [EventType("broken.orphan", 1)]
    private sealed record DuplicateOfOrphanV1(string Value);

    private sealed record Unregistered;

    // A contract with three schema versions, plus well-formed and malformed upcasters for it.
    // Nested and private, so an assembly scan never finds them.
    [EventType("broken.chain", 1)]
    private sealed record ChainV1(string Value);

    [EventType("broken.chain", 2)]
    private sealed record ChainV2(string Value, int Count);

    [EventType("broken.chain", 3)]
    private sealed record ChainV3(string Value, int Count, string Label);

    [EventType("broken.other", 2)]
    private sealed record OtherV2(string Value, int Count);

    private sealed class ChainV1ToV2 : IEventUpcaster<ChainV1, ChainV2>
    {
        public ChainV2 Upcast(ChainV1 source) => new(source.Value, 2);
    }

    private sealed class AnotherChainV1ToV2 : IEventUpcaster<ChainV1, ChainV2>
    {
        public ChainV2 Upcast(ChainV1 source) => new(source.Value, 0);
    }

    private sealed class ChainV2ToV3 : IEventUpcaster<ChainV2, ChainV3>
    {
        public ChainV3 Upcast(ChainV2 source) => new(source.Value, source.Count, "v3");
    }

    private sealed class ChainV1ToV3 : IEventUpcaster<ChainV1, ChainV3>
    {
        public ChainV3 Upcast(ChainV1 source) => new(source.Value, 0, "skipped");
    }

    private sealed class ChainV1ToOtherV2 : IEventUpcaster<ChainV1, OtherV2>
    {
        public OtherV2 Upcast(ChainV1 source) => new(source.Value, 0);
    }
}
