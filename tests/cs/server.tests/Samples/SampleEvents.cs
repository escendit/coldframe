using Coldframe.Contracts.Events;
using Orleans;

namespace Coldframe.Server.Tests.Samples;

// Test-only events for the sample grain (Story 1.2). Domain events arrive with the domain grains.
// The integration tests compile this same file, so both test projects share one definition.

/// <summary>
/// The sample was created.
/// </summary>
[EventType("sample.created")]
[GenerateSerializer]
[Alias("coldframe.tests.sample-created")]
public sealed record SampleCreated([property: Id(0)] string Name);

/// <summary>
/// A note was added to the sample. Schema version 1, kept readable through <see cref="SampleNotedUpcaster"/>.
/// </summary>
[EventType("sample.noted", 1)]
[GenerateSerializer]
[Alias("coldframe.tests.sample-noted-v1")]
public sealed record SampleNotedV1([property: Id(0)] string Text);

/// <summary>
/// A note was added to the sample. Schema version 2 adds its weight.
/// </summary>
[EventType("sample.noted", 2)]
[GenerateSerializer]
[Alias("coldframe.tests.sample-noted")]
public sealed record SampleNoted([property: Id(0)] string Text, [property: Id(1)] int Weight);

/// <summary>
/// Reads a version 1 note as version 2 with a weight of 1.
/// </summary>
public sealed class SampleNotedUpcaster : IEventUpcaster<SampleNotedV1, SampleNoted>
{
    /// <summary>
    /// The weight a version 1 note had implicitly.
    /// </summary>
    public const int ImplicitWeight = 1;

    /// <inheritdoc />
    public SampleNoted Upcast(SampleNotedV1 source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new SampleNoted(source.Text, ImplicitWeight);
    }
}
