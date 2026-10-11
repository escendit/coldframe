using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Coldframe.Server.Edge;
using Coldframe.Server.Live;
using Coldframe.Server.Notifications.Push;
using Coldframe.Server.Testing;
using Coldframe.Server.Tests.Notifications;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Coldframe.Server.Tests.Live;

/// <summary>
/// The Memberships a test says the identity projection holds.
/// </summary>
internal sealed class FakeMemberships : ILiveMemberships
{
    private readonly ConcurrentDictionary<string, string[]> _sites = new(StringComparer.Ordinal);

    public bool Fail { get; set; }

    /// <summary>
    /// The read never answers; it ends only when it is cancelled.
    /// </summary>
    public bool Hang { get; set; }

    public void Set(string userId, params string[] sites) => _sites[userId] = sites;

    public async Task<IReadOnlyCollection<string>> SitesOfAsync(string userId, CancellationToken cancellationToken)
    {
        if (Hang)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        return Fail
            ? throw new InvalidOperationException("The identity read model is down.")
            : _sites.GetValueOrDefault(userId) ?? [];
    }
}

/// <summary>
/// What a hub connection of a test received, as the JSON the Server sent.
/// </summary>
internal sealed class LiveClient(HubConnection connection) : IAsyncDisposable
{
    private readonly ConcurrentQueue<(string Target, string Json)> _received = new();
    private readonly SemaphoreSlim _arrived = new(0);
    private readonly TaskCompletionSource<Exception?> _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public HubConnection Connection => connection;

    public IReadOnlyList<(string Target, string Json)> Received => [.. _received];

    public Task<Exception?> Closed => _closed.Task;

    public void Listen()
    {
        foreach (var target in new[] { LiveMessages.NotificationDelivered, LiveMessages.ReadModelChanged })
        {
            connection.On<JsonElement>(target, message =>
            {
                _received.Enqueue((target, message.GetRawText()));
                _arrived.Release();
            });
        }

        connection.Closed += error =>
        {
            _closed.TrySetResult(error);
            return Task.CompletedTask;
        };
    }

    /// <summary>
    /// Waits until <paramref name="count"/> messages have arrived in all, and returns them.
    /// </summary>
    public async Task<IReadOnlyList<(string Target, string Json)>> WaitForAsync(int count, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));

        while (_received.Count < count)
        {
            await _arrived.WaitAsync(timeout.Token);
        }

        return Received;
    }

    public async ValueTask DisposeAsync()
    {
        await connection.DisposeAsync();
        _arrived.Dispose();
    }
}

/// <summary>
/// The Server's hub on a loopback port, with the Edge API's own bearer validation against a key of the test and
/// with the Server's hub registrations. No silo, no database: the Memberships and the push lookups are fakes.
/// </summary>
internal sealed class LiveTestHost : IAsyncDisposable
{
    public const string Issuer = "https://id.test/realms/coldframe";

    private static readonly SymmetricSecurityKey SigningKey = new(Encoding.UTF8.GetBytes("coldframe-live-test-signing-key-0123456789"));

    private readonly WebApplication _app;

    private LiveTestHost(WebApplication app, FakeMemberships memberships, FakeLookups lookups, FakeTimeProvider tokenClock)
    {
        _app = app;
        Memberships = memberships;
        Lookups = lookups;
        TokenClock = tokenClock;
    }

    public FakeMemberships Memberships { get; }

    public FakeLookups Lookups { get; }

    /// <summary>
    /// The clock access tokens are measured on. It starts at the real time.
    /// </summary>
    public FakeTimeProvider TokenClock { get; }

    public IServiceProvider Services => _app.Services;

    public Uri BaseAddress { get; private set; } = null!;

    public static async Task<LiveTestHost> StartAsync(
        string environment = "Production",
        bool? manualClock = null,
        CancellationToken cancellationToken = default)
    {
        var memberships = new FakeMemberships();
        var lookups = PushTestKit.TomatoesLookups();
        var tokenClock = new FakeTimeProvider(TimeProvider.System.GetUtcNow());

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Identity:Authority"] = Issuer,
            ["Identity:Audience"] = "coldframe-server",
            [TestingHostingExtensions.ManualClockKey] = manualClock?.ToString(),
        });

        // The Site-role policy's handler wants the identity read model; nothing here asks it, and the data
        // source opens connections lazily.
        builder.Services.AddSingleton(Npgsql.NpgsqlDataSource.Create("Host=localhost;Database=coldframe"));
        builder.Services.AddSingleton<Coldframe.Server.Identity.IdentityReadModel>();
        builder.Services.AddSingleton<ILiveMemberships>(memberships);
        builder.Services.AddSingleton<IPushLookups>(lookups);
        builder.Services.AddSingleton(new AccessTokenClock(tokenClock));
        builder.AddTestingClock();
        builder.AddEdgeApi();
        builder.AddLive();

        // The realm's keys without a discovery request: the handler is given its configuration.
        builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
            configuration.SigningKeys.Add(SigningKey);
            options.Configuration = configuration;
            options.ConfigurationManager = new Microsoft.IdentityModel.Protocols.StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
        });

        var app = builder.Build();
        app.UseEdgeApi();
        app.MapLiveHub();
        app.MapTestingClock();
        await app.StartAsync(cancellationToken);

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();

        return new LiveTestHost(app, memberships, lookups, tokenClock) { BaseAddress = new Uri(address) };
    }

    /// <summary>
    /// An access token as Keycloak issues it for <paramref name="userId"/>, valid for <paramref name="lifetime"/>
    /// from now on the token clock.
    /// </summary>
    public string Token(string userId, TimeSpan? lifetime = null)
    {
        var now = TokenClock.GetUtcNow().UtcDateTime;

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = "coldframe-server",
            Subject = new ClaimsIdentity([new Claim(EdgeAuthentication.UserIdClaim, userId)]),
            IssuedAt = now,
            NotBefore = now.AddMinutes(-1),
            Expires = now + (lifetime ?? TimeSpan.FromMinutes(5)),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
        });
    }

    public HttpClient CreateClient(string? accessToken = null)
    {
        var client = new HttpClient { BaseAddress = BaseAddress };

        if (accessToken is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return client;
    }

    /// <summary>
    /// Connects as the backend-for-frontend does: the token as a bearer header, over the given transport.
    /// </summary>
    public async Task<LiveClient> ConnectAsync(
        string userId,
        CancellationToken cancellationToken,
        HttpTransportType transport = HttpTransportType.ServerSentEvents,
        string? accessToken = null)
    {
        var token = accessToken ?? Token(userId);
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(BaseAddress, LiveMessages.HubPath), options =>
            {
                options.Transports = transport;
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();
        var client = new LiveClient(connection);
        client.Listen();
        await connection.StartAsync(cancellationToken);

        // The hub registers the connection in OnConnectedAsync, which the handshake does not wait for.
        var connections = Services.GetRequiredService<LiveConnections>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));

        while (!connections.OfUser(userId).Contains(connection.ConnectionId, StringComparer.Ordinal))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }

        return client;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
