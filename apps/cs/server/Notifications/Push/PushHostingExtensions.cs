using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Coldframe.Server.Notifications.Push;

/// <summary>
/// Whether a push provider has a channel.
/// </summary>
public enum PushProviderState
{
    /// <summary>
    /// The credentials are present and usable: the channel is registered.
    /// </summary>
    Configured = 0,

    /// <summary>
    /// No credentials: no channel.
    /// </summary>
    NoCredentials = 1,

    /// <summary>
    /// Credentials are present but cannot be used (a key that does not parse): no channel.
    /// </summary>
    UnusableCredentials = 2,
}

/// <summary>
/// Which push channels the Server registered, decided once from the configuration at start.
/// </summary>
/// <param name="Apns">The APNs channel.</param>
/// <param name="Fcm">The FCM channel.</param>
/// <param name="ApnsProblem">Why the APNs credentials cannot be used.</param>
/// <param name="FcmProblem">Why the FCM credentials cannot be used.</param>
public sealed record PushProviders(PushProviderState Apns, PushProviderState Fcm, string? ApnsProblem = null, string? FcmProblem = null);

/// <summary>
/// Registers the push channels behind the Notifier seam (Story 6.5): APNs and FCM, each only when its
/// credentials are configured (section <see cref="PushOptions.SectionName"/>, from the optional Secret
/// <c>coldframe-push</c>). A provider without credentials gets no channel and one log entry at start; the Server
/// starts and runs without any.
/// </summary>
public static class PushHostingExtensions
{
    /// <summary>
    /// Adds what both channels share: the options, the lookups and the content builder. No channel.
    /// </summary>
    public static IServiceCollection AddPushContent(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<PushOptions>()
            .Validate(options => options.SendBudget > TimeSpan.Zero && options.SendBudget <= TimeSpan.FromSeconds(10), "Push:SendBudget must be positive and at most 10 s: the User grain waits for a channel.")
            .Validate(options => options.RequestTimeout > TimeSpan.Zero && options.RequestTimeout <= options.SendBudget, "Push:RequestTimeout must be positive and within Push:SendBudget.")
            .Validate(options => options.LookupBudget > TimeSpan.Zero && options.LookupBudget <= TimeSpan.FromSeconds(10), "Push:LookupBudget must be positive and at most 10 s.")
            .Validate(options => options.MaxAttempts is >= 1 and <= 5, "Push:MaxAttempts must be 1 to 5.")
            .Validate(options => options.RetryDelay >= TimeSpan.Zero, "Push:RetryDelay must not be negative.")
            .Validate(options => options.Apns.ProductionBaseUrl.IsAbsoluteUri && options.Apns.SandboxBaseUrl.IsAbsoluteUri && options.Fcm.BaseUrl.IsAbsoluteUri, "The push provider base URLs must be absolute.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Apns.Topic), "Push:Apns:Topic is required.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IPushLookups, PushLookups>();
        services.TryAddSingleton<PushContentBuilder>();

        return services;
    }

    /// <summary>
    /// Adds the APNs channel. The credentials of <see cref="PushOptions.Apns"/> must be usable.
    /// </summary>
    public static IServiceCollection AddApnsChannel(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddPushContent();
        // The service defaults give every client a resilience handler; here the channel's own bounded retry is
        // the only one, inside the send budget the User grain waits for.
#pragma warning disable EXTEXP0001 // The one supported way to take the default resilience handler off a named client.
        services.AddHttpClient(ApnsChannel.HttpClientName).ConfigureHttpClient(ConfigureTimeout).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
        services.TryAddSingleton(provider => new ApnsProviderToken(
            provider.GetRequiredService<IOptions<PushOptions>>().Value.Apns,
            provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<INotificationChannel, ApnsChannel>();

        return services;
    }

    /// <summary>
    /// Adds the FCM channel. The credentials of <see cref="PushOptions.Fcm"/> must be usable.
    /// </summary>
    public static IServiceCollection AddFcmChannel(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddPushContent();
        // The service defaults give every client a resilience handler; here the channel's own bounded retry is
        // the only one, inside the send budget the User grain waits for.
#pragma warning disable EXTEXP0001 // The one supported way to take the default resilience handler off a named client.
        services.AddHttpClient(FcmServiceAccount.HttpClientName).ConfigureHttpClient(ConfigureTimeout).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
        services.TryAddSingleton(provider => new FcmServiceAccount(
            provider.GetRequiredService<IOptions<PushOptions>>().Value.Fcm,
            provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<INotificationChannel, FcmChannel>();

        return services;
    }

    /// <summary>
    /// Adds the push channels the configuration has usable credentials for, and the one log entry at start that
    /// names each provider without a channel.
    /// </summary>
    public static IServiceCollection AddPushChannels(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(PushOptions.SectionName);
        services.AddPushContent();
        services.AddOptions<PushOptions>().Bind(section);

        // Decided here, from the same configuration the options bind: a provider without usable credentials
        // registers no channel at all, so nothing can fail later inside a User grain's turn.
        var providers = Inspect(section.Get<PushOptions>() ?? new PushOptions());

        if (providers.Apns == PushProviderState.Configured)
        {
            services.AddApnsChannel();
        }

        if (providers.Fcm == PushProviderState.Configured)
        {
            services.AddFcmChannel();
        }

        services.AddSingleton(providers);
        services.AddHostedService<PushProvidersLog>();

        return services;
    }

    /// <summary>
    /// Adds the push channels to the application.
    /// </summary>
    public static TBuilder AddPushChannels<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddPushChannels(builder.Configuration);

        return builder;
    }

    /// <summary>
    /// Says for each provider whether <paramref name="options"/> holds credentials a channel can use.
    /// </summary>
    public static PushProviders Inspect(PushOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var (apns, apnsProblem) = Try(options.Apns.HasCredentials, () =>
        {
            using var token = new ApnsProviderToken(options.Apns, TimeProvider.System);
        });
        var (fcm, fcmProblem) = Try(options.Fcm.HasCredentials, () =>
        {
            using var account = new FcmServiceAccount(options.Fcm, new NoHttpClientFactory(), TimeProvider.System);
        });

        return new PushProviders(apns, fcm, apnsProblem, fcmProblem);
    }

    private static (PushProviderState State, string? Problem) Try(bool hasCredentials, Action create)
    {
        if (!hasCredentials)
        {
            return (PushProviderState.NoCredentials, null);
        }

        try
        {
            create();
            return (PushProviderState.Configured, null);
        }
        catch (ArgumentException exception)
        {
            // The message names what is wrong, never the credential.
            return (PushProviderState.UnusableCredentials, exception.Message);
        }
    }

    private static void ConfigureTimeout(IServiceProvider provider, HttpClient client) =>
        client.Timeout = provider.GetRequiredService<IOptions<PushOptions>>().Value.RequestTimeout;

    // Parsing the service account makes no request.
    private sealed class NoHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new NotSupportedException();
    }
}

/// <summary>
/// Logs once at start which push providers have no channel (Story 6.5).
/// </summary>
public sealed partial class PushProvidersLog(PushProviders providers, ILogger<PushProvidersLog> logger) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        Log("APNs", providers.Apns, providers.ApnsProblem);
        Log("FCM", providers.Fcm, providers.FcmProblem);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void Log(string provider, PushProviderState state, string? problem)
    {
        switch (state)
        {
            case PushProviderState.NoCredentials:
                LogNoCredentials(logger, provider);
                break;
            case PushProviderState.UnusableCredentials:
                LogUnusableCredentials(logger, provider, problem ?? string.Empty);
                break;
            default:
                LogConfigured(logger, provider);
                break;
        }
    }

    [LoggerMessage(EventId = 1, EventName = "PushChannelNotConfigured", Level = LogLevel.Information, Message = "No {Provider} credentials are configured (Secret coldframe-push): no {Provider} push is sent.")]
    private static partial void LogNoCredentials(ILogger logger, string provider);

    [LoggerMessage(EventId = 2, EventName = "PushChannelCredentialsUnusable", Level = LogLevel.Error, Message = "The {Provider} credentials cannot be used, so no {Provider} push is sent: {Problem}")]
    private static partial void LogUnusableCredentials(ILogger logger, string provider, string problem);

    [LoggerMessage(EventId = 3, EventName = "PushChannelConfigured", Level = LogLevel.Information, Message = "The {Provider} push channel is configured.")]
    private static partial void LogConfigured(ILogger logger, string provider);
}
