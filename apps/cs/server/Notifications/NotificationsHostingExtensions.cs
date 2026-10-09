using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Coldframe.Server.Notifications;

/// <summary>
/// Registers the Notifier seam (Story 6.4). The User grain hands every due notification to
/// <see cref="INotifier"/>; a channel plugs in with
/// <c>services.AddSingleton&lt;INotificationChannel, TChannel&gt;()</c>: the push channels (Story 6.5) through
/// <see cref="Push.PushHostingExtensions.AddPushChannels(IServiceCollection, IConfiguration)"/>, the open web
/// app in Story 6.6. Without a channel the Notifier still records every notification in the log and on its meter.
/// </summary>
public static class NotificationsHostingExtensions
{
    /// <summary>
    /// Adds <see cref="Notifier"/> as the <see cref="INotifier"/>, with the metrics and the clock it needs.
    /// </summary>
    public static IServiceCollection AddNotifications(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddMetrics();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<INotifier, Notifier>();

        return services;
    }

    /// <summary>
    /// Adds the Notifier seam to the application.
    /// </summary>
    public static TBuilder AddNotifications<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddNotifications();

        return builder;
    }
}
