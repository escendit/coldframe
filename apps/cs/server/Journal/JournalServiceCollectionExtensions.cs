using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Coldframe.Server.Journal;

/// <summary>
/// Registers the event journal and projectors.
/// </summary>
public static class JournalServiceCollectionExtensions
{
    /// <summary>
    /// Adds the journal store, the event registry and serializer, and <see cref="TimeProvider.System"/>
    /// unless another <see cref="TimeProvider"/> is registered already. Nothing here runs DDL.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The connection string of the Coldframe database.</param>
    /// <param name="configure">Changes the journal options.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddJournal(
        this IServiceCollection services,
        string connectionString,
        Action<JournalOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.TryAddSingleton(TimeProvider.System);

        var optionsBuilder = services.AddOptions<JournalOptions>();
        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        services.TryAddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        services.TryAddSingleton(provider =>
            new EventTypeRegistry(provider.GetRequiredService<IOptions<JournalOptions>>().Value.EventAssemblies));
        services.TryAddSingleton<JournalSerializer>();
        services.TryAddSingleton<ProjectionWakeup>();
        services.TryAddSingleton<OutboxWakeup>();
        services.TryAddSingleton<JournalStore>();

        return services;
    }

    /// <summary>
    /// Adds a projector and the hosted <see cref="ProjectionRunner"/> that runs it. The runner is also
    /// resolvable by projector type through <see cref="GetProjectionRunner{TProjector}"/>, so a grain can
    /// catch the projector up before it returns (read-your-writes).
    /// </summary>
    /// <typeparam name="TProjector">The projector.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddProjector<TProjector>(this IServiceCollection services)
        where TProjector : class, IProjector
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<TProjector>();
        services.TryAddKeyedSingleton(typeof(TProjector), (provider, _) => ActivatorUtilities.CreateInstance<ProjectionRunner>(
            provider,
            provider.GetRequiredService<TProjector>()));
        services.AddSingleton<IHostedService>(provider => provider.GetProjectionRunner<TProjector>());

        return services;
    }

    /// <summary>
    /// Returns the one <see cref="ProjectionRunner"/> of <typeparamref name="TProjector"/>, the same instance
    /// that runs as a hosted service. <see cref="ProjectionRunner.CatchUpAsync"/> is safe to call while its
    /// background loop runs.
    /// </summary>
    /// <typeparam name="TProjector">The projector.</typeparam>
    /// <param name="services">The service provider.</param>
    /// <returns>The runner.</returns>
    public static ProjectionRunner GetProjectionRunner<TProjector>(this IServiceProvider services)
        where TProjector : class, IProjector
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.GetRequiredKeyedService<ProjectionRunner>(typeof(TProjector));
    }
}
