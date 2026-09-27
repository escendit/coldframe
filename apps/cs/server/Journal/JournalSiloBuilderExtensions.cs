namespace Coldframe.Server.Journal;

/// <summary>
/// Configures the silo side of the journal.
/// </summary>
public static class JournalSiloBuilderExtensions
{
    /// <summary>
    /// Makes the CustomStorage log-consistency provider the default for journaled grains and, when
    /// <paramref name="hintStream"/> is given, adds the hint stream and the outbox dispatcher.
    /// </summary>
    /// <param name="silo">The silo builder.</param>
    /// <param name="hintStream">
    /// The one seam for the hint stream: adds a stream provider named <see cref="JournalHints.StreamProvider"/>
    /// with implicit subscriptions. <see langword="null"/> disables hints; projectors then converge by polling.
    /// </param>
    /// <returns>The silo builder.</returns>
    public static ISiloBuilder AddJournalGrains(this ISiloBuilder silo, Action<ISiloBuilder>? hintStream)
    {
        ArgumentNullException.ThrowIfNull(silo);

        silo.AddCustomStorageBasedLogConsistencyProviderAsDefault();

        if (hintStream is not null)
        {
            hintStream(silo);
            silo.Services.AddHostedService<OutboxDispatcher>();
        }

        return silo;
    }
}
