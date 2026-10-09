using Coldframe.Server.Journal;

namespace Coldframe.Server.Alerts;

/// <summary>
/// Registers the Alerts: the alerts projector and its read model (Story 6.2). The Alert grain (Story 6.1) is
/// found with the other grains and needs no service of its own: it reaches the Site grain through grain calls
/// and the journal alone (AD-5). The Notifier seam (Story 6.4) is registered here when it arrives.
/// </summary>
public static class AlertsHostingExtensions
{
    /// <summary>
    /// Adds the alerts projector and <see cref="AlertsReadModel"/>.
    /// </summary>
    public static IServiceCollection AddAlerts(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<AlertsReadModel>();
        services.AddProjector<AlertsProjector>();

        return services;
    }

    /// <summary>
    /// Adds the Alerts to the application.
    /// </summary>
    public static TBuilder AddAlerts<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddAlerts();

        return builder;
    }
}
