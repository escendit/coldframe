using Coldframe.Server.Journal;
using Coldframe.Server.Tests.Samples;

namespace Coldframe.Server.IntegrationTests.Journal;

/// <summary>
/// A test-only journaled grain on the CustomStorage journal.
/// </summary>
public interface ISampleGrain : IGrainWithStringKey
{
    Task Create(string name);

    Task Note(string text, int weight);

    Task<SampleState> GetState();

    Task<int> GetVersion();

    Task<string> GetStreamId();

    Task<DateTimeOffset> GetTime();
}

[GrainType(TypeName)]
public sealed class SampleGrain : JournaledStreamGrain<SampleState>, ISampleGrain
{
    public const string TypeName = "sample";

    public static string StreamIdOf(string key) => $"{TypeName}/{key}";

    public async Task Create(string name)
    {
        RaiseEvent(new SampleCreated(name));
        await ConfirmEvents();
    }

    public async Task Note(string text, int weight)
    {
        RaiseEvent(new SampleNoted(text, weight));
        await ConfirmEvents();
    }

    public Task<SampleState> GetState() => Task.FromResult(State);

    public Task<int> GetVersion() => Task.FromResult(Version);

    public Task<string> GetStreamId() => Task.FromResult(StreamId);

    public Task<DateTimeOffset> GetTime() => Task.FromResult(Clock.GetUtcNow());
}
