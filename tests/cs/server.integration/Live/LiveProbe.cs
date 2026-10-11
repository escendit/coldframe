using System.Collections.Concurrent;
using System.Text.Json;
using Aspire.Hosting.Testing;
using Coldframe.Server.IntegrationTests.Edge;
using Coldframe.Server.Live;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace Coldframe.Server.IntegrationTests.Live;

/// <summary>
/// One hint as the Server sent it.
/// </summary>
/// <param name="Resource">The read model.</param>
/// <param name="Id">What changed.</param>
/// <param name="Version">Its version.</param>
/// <param name="Json">The whole message.</param>
internal sealed record ReceivedHint(string Resource, string Id, long Version, string Json);

/// <summary>
/// A hub connection to the AppHost's Server, opened the way the web app's backend-for-frontend opens it: over
/// Server-Sent Events with the User's access token as a bearer header.
/// </summary>
internal sealed class LiveProbe : IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly ConcurrentQueue<ReceivedHint> _hints = new();
    private readonly ConcurrentQueue<string> _notifications = new();

    private LiveProbe(HubConnection connection)
    {
        _connection = connection;
        connection.On<JsonElement>(LiveMessages.ReadModelChanged, message => _hints.Enqueue(new ReceivedHint(
            message.GetProperty("resource").GetString()!,
            message.GetProperty("id").GetString()!,
            message.GetProperty("version").GetInt64(),
            message.GetRawText())));
        connection.On<JsonElement>(LiveMessages.NotificationDelivered, message => _notifications.Enqueue(message.GetRawText()));
    }

    public IReadOnlyList<ReceivedHint> Hints => [.. _hints];

    public IReadOnlyList<string> Notifications => [.. _notifications];

    public HubConnectionState State => _connection.State;

    public static async Task<LiveProbe> ConnectAsync(EdgeApiFixture edge, TestUser user, CancellationToken cancellationToken)
    {
        var server = edge.AppHost.App.GetEndpoint("server", "http");
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server, LiveMessages.HubPath), options =>
            {
                options.Transports = HttpTransportType.ServerSentEvents;
                options.AccessTokenProvider = () => Task.FromResult<string?>(user.AccessToken);
            })
            .Build();
        var probe = new LiveProbe(connection);
        await connection.StartAsync(cancellationToken);

        return probe;
    }

    /// <summary>
    /// Waits for a hint that matches, and returns it.
    /// </summary>
    public async Task<ReceivedHint> WaitForAsync(string resource, string id, CancellationToken cancellationToken, long atLeast = 1)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));

        while (true)
        {
            if (_hints.FirstOrDefault(hint => hint.Resource == resource && hint.Id == id && hint.Version >= atLeast) is { } found)
            {
                return found;
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Assert.Fail($"No {resource} hint for {id} arrived. Received: {string.Join(", ", _hints.Select(hint => hint.Json))}");
            }
        }
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
