using Coldframe.Server.Journal;

namespace Coldframe.Server.Lots;

/// <summary>
/// Registers the Lots: the lots projector and its read model. The Lot grain is found with the other grains.
/// </summary>
public static class LotsHostingExtensions
{
    /// <summary>
    /// Adds the lots projector and <see cref="LotsReadModel"/>.
    /// </summary>
    public static IServiceCollection AddLots(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<LotsReadModel>();
        services.AddProjector<LotsProjector>();

        return services;
    }

    /// <summary>
    /// Adds the lots projector and <see cref="LotsReadModel"/> to the application.
    /// </summary>
    public static TBuilder AddLots<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddLots();

        return builder;
    }
}
