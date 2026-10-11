using Coldframe.Server.Journal;
using Coldframe.Server.Notifications;
using Coldframe.Server.Notifications.Push;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Coldframe.Server.Live;

/// <summary>
/// Registers and maps the SignalR hub of the open web app (Story 6.6): the connection registry, the hint
/// publisher behind <see cref="IReadModelHints"/> and the Notifier's channel.
/// </summary>
public static class LiveHostingExtensions
{
    /// <summary>
    /// Adds the hub's services. There is no backplane: the connections live in this process, and the chart
    /// runs one Server replica.
    /// </summary>
    public static IServiceCollection AddLive(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services
            .AddSignalR()
            .AddJsonProtocol(protocol => LiveMessages.Configure(protocol.PayloadSerializerOptions));

        // Tokens are measured on the real clock, whatever TimeProvider the Server runs on. The bearer handler
        // validates a lifetime against the system clock by itself; its options are pinned as well, so that it
        // never picks the Server's TimeProvider up from the container.
        services.TryAddSingleton(new AccessTokenClock(TimeProvider.System));
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<AccessTokenClock>((options, clock) => options.TimeProvider = clock.TimeProvider);

        services.AddPushContent();
        services.TryAddSingleton<ILiveMemberships, IdentityLiveMemberships>();
        services.TryAddSingleton<LiveConnections>();
        services.TryAddSingleton<ILiveSender, HubLiveSender>();
        services.TryAddSingleton<LiveHints>();
        services.TryAddSingleton<IReadModelHints>(provider => provider.GetRequiredService<LiveHints>());
        services.AddHostedService(provider => provider.GetRequiredService<LiveHints>());
        services.AddSingleton<INotificationChannel, LiveNotificationChannel>();

        return services;
    }

    /// <summary>
    /// Adds the hub's services to the application.
    /// </summary>
    public static TBuilder AddLive<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddLive();

        return builder;
    }

    /// <summary>
    /// Maps the hub at <see cref="LiveMessages.HubPath"/>, over Server-Sent Events and long polling only: the
    /// web app's backend-for-frontend cannot upgrade a connection, so WebSockets are not offered. The hub is
    /// not part of <c>MapEdgeApi()</c>: it is no REST endpoint and has no place in the OpenAPI contract.
    /// </summary>
    public static IEndpointRouteBuilder MapLiveHub(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapHub<LiveHub>(
            LiveMessages.HubPath,
            options => options.Transports = HttpTransportType.ServerSentEvents | HttpTransportType.LongPolling);

        return endpoints;
    }
}
