namespace Coldframe.Server.Alerts;

/// <summary>
/// Registers the Alerts (Story 6.1). The Alert grain is found with the other grains, and it needs no service of
/// its own yet: it reaches the Site grain through grain calls and the journal alone (AD-5). The Alerts read
/// model (Story 6.2) and the Notifier seam (Story 6.4) are registered here when they arrive.
/// </summary>
public static class AlertsHostingExtensions
{
    /// <summary>
    /// Adds the Alerts.
    /// </summary>
    public static IServiceCollection AddAlerts(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

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
