using Coldframe.Server.Notifications;
using Coldframe.Server.Notifications.Push;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using static Coldframe.Server.Tests.Notifications.PushTestKit;

namespace Coldframe.Server.Tests.Notifications;

/// <summary>
/// The push channels are optional (Story 6.5): a provider without credentials registers no channel and says so
/// once at start, and the Server runs all the same.
/// </summary>
public sealed class PushHostingTests
{
    [Fact]
    public async Task WithoutCredentialsNoChannelIsRegisteredAndEachProviderIsLoggedOnceAtStart()
    {
        var log = new Entries();
        await using var provider = Build(new Dictionary<string, string?>(StringComparer.Ordinal), log);

        Assert.Empty(provider.GetServices<INotificationChannel>());
        Assert.IsType<Notifier>(provider.GetRequiredService<INotifier>());

        foreach (var service in provider.GetServices<IHostedService>())
        {
            await service.StartAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(
            [
                (LogLevel.Information, "No APNs credentials are configured (Secret coldframe-push): no APNs push is sent."),
                (LogLevel.Information, "No FCM credentials are configured (Secret coldframe-push): no FCM push is sent."),
            ],
            log.Written);
    }

    [Fact]
    public async Task WithCredentialsBothChannelsAreRegisteredInOrder()
    {
        var (serviceAccount, publicKey) = NewServiceAccount();
        publicKey.Dispose();
        await using var provider = Build(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Push:Apns:KeyId"] = "KEY1234567",
                ["Push:Apns:TeamId"] = "TEAM123456",
                ["Push:Apns:PrivateKeyPem"] = NewP256Pem(),
                ["Push:Fcm:ServiceAccountJson"] = serviceAccount,
                ["Push:Apns:ProductionBaseUrl"] = "http://apns.test:9000",
            },
            new Entries());

        Assert.Equal([typeof(ApnsChannel), typeof(FcmChannel)], provider.GetServices<INotificationChannel>().Select(channel => channel.GetType()));
        Assert.Equal(new Uri("http://apns.test:9000"), provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PushOptions>>().Value.Apns.ProductionBaseUrl);
    }

    [Fact]
    public async Task OnlyTheProviderWithCredentialsGetsAChannel()
    {
        var (serviceAccount, publicKey) = NewServiceAccount();
        publicKey.Dispose();
        await using var provider = Build(
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["Push:Fcm:ServiceAccountJson"] = serviceAccount },
            new Entries());

        Assert.IsType<FcmChannel>(Assert.Single(provider.GetServices<INotificationChannel>()));
    }

    [Fact]
    public async Task CredentialsThatCannotBeUsedRegisterNoChannelAndAreLoggedAsAnErrorWithoutTheSecret()
    {
        var log = new Entries();
        await using var provider = Build(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Push:Apns:KeyId"] = "KEY1234567",
                ["Push:Apns:TeamId"] = "TEAM123456",
                ["Push:Apns:PrivateKeyPem"] = "-----BEGIN PRIVATE KEY-----\nc2VjcmV0\n-----END PRIVATE KEY-----",
                ["Push:Fcm:ServiceAccountJson"] = """{"project_id":"p","private_key":"c2VjcmV0"}""",
            },
            log);

        Assert.Empty(provider.GetServices<INotificationChannel>());

        foreach (var service in provider.GetServices<IHostedService>())
        {
            await service.StartAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal([LogLevel.Error, LogLevel.Error], log.Written.Select(entry => entry.Level));
        Assert.All(log.Written, entry => Assert.DoesNotContain("c2VjcmV0", entry.Message, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Push:SendBudget", "00:01:00")]
    [InlineData("Push:SendBudget", "00:00:00")]
    [InlineData("Push:RequestTimeout", "00:00:06")]
    [InlineData("Push:LookupBudget", "00:00:11")]
    [InlineData("Push:MaxAttempts", "6")]
    public async Task ABoundTheUserGrainCouldNotWaitForFailsTheOptions(string setting, string value)
    {
        await using var provider = Build(new Dictionary<string, string?>(StringComparer.Ordinal) { [setting] = value }, new Entries());

        Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(
            () => provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PushOptions>>().Value);
    }

    [Fact]
    public async Task ThePushClientsCarryNoResilienceHandlerSoTheChannelsRetryIsTheOnlyOne()
    {
        var (serviceAccount, publicKey) = NewServiceAccount();
        publicKey.Dispose();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Push:Apns:KeyId"] = "KEY1234567",
                ["Push:Apns:TeamId"] = "TEAM123456",
                ["Push:Apns:PrivateKeyPem"] = NewP256Pem(),
                ["Push:Fcm:ServiceAccountJson"] = serviceAccount,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();

        // As the service defaults do for every client of the Server.
        services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());
        services.AddNotifications();
        services.AddPushChannels(configuration);
        await using var provider = services.BuildServiceProvider();
        var handlers = provider.GetRequiredService<IHttpMessageHandlerFactory>();

        Assert.Contains(Chain(handlers.CreateHandler("another-client")), handler => handler is Microsoft.Extensions.Http.Resilience.ResilienceHandler);
        Assert.DoesNotContain(Chain(handlers.CreateHandler(ApnsChannel.HttpClientName)), handler => handler is Microsoft.Extensions.Http.Resilience.ResilienceHandler);
        Assert.DoesNotContain(Chain(handlers.CreateHandler(FcmServiceAccount.HttpClientName)), handler => handler is Microsoft.Extensions.Http.Resilience.ResilienceHandler);
    }

    private static IEnumerable<HttpMessageHandler> Chain(HttpMessageHandler handler)
    {
        for (HttpMessageHandler? current = handler; current is not null; current = (current as DelegatingHandler)?.InnerHandler)
        {
            yield return current;
        }
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings, Entries log)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(log).SetMinimumLevel(LogLevel.Information));
        services.AddNotifications();
        services.AddPushChannels(configuration);

        // The lookups read the read models, which this host does not have.
        services.AddSingleton<IPushLookups>(new FakeLookups());

        return services.BuildServiceProvider();
    }

    private sealed class Entries : ILoggerProvider, ILogger
    {
        public List<(LogLevel Level, string Message)> Written { get; } = [];

        public ILogger CreateLogger(string categoryName) =>
            categoryName == typeof(PushProvidersLog).FullName ? this : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            Written.Add((logLevel, formatter(state, exception)));
        }

        public void Dispose()
        {
        }
    }
}
