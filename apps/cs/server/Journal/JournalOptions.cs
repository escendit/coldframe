using System.Reflection;
using Coldframe.Contracts.Events;

namespace Coldframe.Server.Journal;

/// <summary>
/// Options of the event journal and its projection pipeline.
/// </summary>
public sealed class JournalOptions
{
    /// <summary>
    /// The assemblies scanned for event contracts (<see cref="EventTypeAttribute"/>) and upcasters.
    /// <c>Coldframe.Contracts</c> is always included.
    /// </summary>
    public IList<Assembly> EventAssemblies { get; } = [typeof(EventTypeAttribute).Assembly];

    /// <summary>
    /// How long a projection runner or the outbox dispatcher waits before it polls again when
    /// nothing wakes it. Hints only shorten the wait; polling alone keeps projectors correct.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The largest number of events a projector applies in one transaction.
    /// </summary>
    public int BatchSize { get; set; } = 500;
}
