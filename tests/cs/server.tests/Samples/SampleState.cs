using Orleans;

namespace Coldframe.Server.Tests.Samples;

/// <summary>
/// The state of the test-only sample grain. It applies only the newest version of each event;
/// older versions reach it through their upcaster.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.tests.sample-state")]
public sealed class SampleState
{
    [Id(0)]
    public string? Name { get; set; }

    [Id(1)]
    public int NoteCount { get; set; }

    [Id(2)]
    public int TotalWeight { get; set; }

    public void Apply(SampleCreated @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        Name = @event.Name;
    }

    public void Apply(SampleNoted @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        NoteCount++;
        TotalWeight += @event.Weight;
    }
}
