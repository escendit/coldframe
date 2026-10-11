using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coldframe.Server.Journal;
using Coldframe.Server.Live;
using Coldframe.Server.Notifications;
using Coldframe.Server.Tests.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using static Coldframe.Server.Tests.Notifications.PushTestKit;

namespace Coldframe.Server.Tests.Live;

/// <summary>
/// The SignalR hub (Story 6.6) on a real listener, called the way the web app's backend-for-frontend calls it:
/// a bearer header, Server-Sent Events or long polling. Who receives which message, that a client can call
/// nothing, and that the connection ends with its token.
/// </summary>
public sealed class LiveHubTests
{
    private const string Ada = "5b0c7c1e-2a4d-4f1b-8e3a-9d7f6c5b4a31";
    private const string Ben = "7d1e8f20-3b5c-4a2d-9f4b-0e8a7d6c5b42";
    private const string OtherSite = "0199c1f0-5a00-7000-8000-000000000002";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ARequestToTheHubWithoutABearerTokenIs401ProblemDetails()
    {
        await using var host = await LiveTestHost.StartAsync(cancellationToken: Ct);
        using var client = host.CreateClient();

        using var negotiate = await client.PostAsync(new Uri("/live/negotiate?negotiateVersion=1", UriKind.Relative), content: null, Ct);
        using var stream = await client.GetAsync(new Uri("/live", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, negotiate.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, stream.StatusCode);
        Assert.Equal("application/problem+json", negotiate.Content.Headers.ContentType?.MediaType);
        var problem = JsonNode.Parse(await negotiate.Content.ReadAsStringAsync(Ct))!;
        Assert.Equal("urn:coldframe:problem:unauthorized", problem["type"]!.GetValue<string>());
        Assert.Equal(0, host.Services.GetRequiredService<LiveConnections>().Count);
    }

    [Fact]
    public async Task ATokenInTheUrlIsNotAccepted()
    {
        await using var host = await LiveTestHost.StartAsync(cancellationToken: Ct);
        using var client = host.CreateClient();

        using var negotiate = await client.PostAsync(
            new Uri($"/live/negotiate?negotiateVersion=1&access_token={host.Token(Ada)}", UriKind.Relative),
            content: null,
            Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, negotiate.StatusCode);
    }

    [Fact]
    public async Task TheHubOffersServerSentEventsAndLongPollingAndNoWebSockets()
    {
        await using var host = await LiveTestHost.StartAsync(cancellationToken: Ct);
        using var client = host.CreateClient(host.Token(Ada));

        using var negotiate = await client.PostAsync(new Uri("/live/negotiate?negotiateVersion=1", UriKind.Relative), content: null, Ct);

        Assert.Equal(HttpStatusCode.OK, negotiate.StatusCode);
        var transports = JsonNode.Parse(await negotiate.Content.ReadAsStringAsync(Ct))!["availableTransports"]!.AsArray()
            .Select(transport => transport!["transport"]!.GetValue<string>())
            .Order(StringComparer.Ordinal);
        Assert.Equal(["LongPolling", "ServerSentEvents"], transports);
    }

    [Fact]
    public void TheHubDeclaresNoMethodAClientCanCallAndRequiresAnAuthenticatedCaller()
    {
        var declared = typeof(LiveHub)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name);

        // SignalR exposes every public method a hub declares that is no override of Hub's own.
        Assert.Equal([nameof(Hub.OnConnectedAsync), nameof(Hub.OnDisconnectedAsync)], declared.Order(StringComparer.Ordinal));
        Assert.All(
            typeof(LiveHub).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            method => Assert.NotEqual(method, method.GetBaseDefinition()));
        Assert.NotNull(typeof(LiveHub).GetCustomAttribute<AuthorizeAttribute>());
    }

    [Theory]
    [InlineData("OnConnectedAsync")]
    [InlineData("Send")]
    [InlineData("notification.delivered")]
    [InlineData("readmodel.changed")]
    public async Task AClientThatCallsAMethodIsRefusedAndNothingReachesAnotherConnection(string method)
    {
        await using var host = await LiveTestHost.StartAsync(cancellationToken: Ct);
        host.Memberships.Set(Ada, Site);
        host.Memberships.Set(Ben, Site);
        await using var ada = await host.ConnectAsync(Ada, Ct);
        await using var ben = await host.ConnectAsync(Ben, Ct);

        await Assert.ThrowsAsync<HubException>(() => ada.Connection.InvokeAsync(method, new { resource = "lots", id = "x", version = 1 }, Ct));

        await SendHintAsync(host, new ReadModelChange(Site, ReadModelResources.Alerts, "sentinel", 1));
        Assert.Equal(["sentinel"], (await ben.WaitForAsync(1, Ct)).Select(IdOf));
    }

    [Theory]
    [InlineData(HttpTransportType.ServerSentEvents)]
    [InlineData(HttpTransportType.LongPolling)]
    public async Task ANotificationReachesEveryConnectionOfItsUserAndNoOtherUser(HttpTransportType transport)
    {
        await using var host = await LiveTestHost.StartAsync(cancellationToken: Ct);
        host.Memberships.Set(Ada, Site);
        host.Memberships.Set(Ben, Site);
        await using var first = await host.ConnectAsync(Ada, Ct, transport);
        await using var second = await host.ConnectAsync(Ada, Ct, transport);
        await using var other = await host.ConnectAsync(Ben, Ct, transport);
        var channel = host.Services.GetServices<INotificationChannel>().OfType<LiveNotificationChannel>().Single();
        var fixture = Fixture("live.notification.alert.json");

        var delivery = await channel.SendAsync(Alert() with { UserId = Ada }, SentAt(fixture), Ct);

        // Sent to both tabs, and counted as delivered to none: a tab is no proof the User was told.
        Assert.Same(ChannelDelivery.Nothing, delivery);

        foreach (var tab in new[] { first, second })
        {
            var (target, json) = Assert.Single(await tab.WaitForAsync(1, Ct));
            Assert.Equal(fixture["target"]!.GetValue<string>(), target);
            AssertJsonEqual(fixture["message"], JsonNode.Parse(json));
        }

        // Ben is a member of the same Site and gets nothing of Ada's notification: only the hint that follows.
        await SendHintAsync(host, new ReadModelChange(Site, ReadModelResources.Alerts, "sentinel", 1));
        var (onlyTarget, _) = Assert.Single(await other.WaitForAsync(1, Ct));
        Assert.Equal(LiveMessages.ReadModelChanged, onlyTarget);
    }

    [Fact]
    public async Task AHintReachesOnlyTheConnectionsOfTheSitesMembersAndHoldsThreeFields()
    {
        await using var host = await LiveTestHost.StartAsync(cancellationToken: Ct);
        host.Memberships.Set(Ada, Site);
        host.Memberships.Set(Ben, OtherSite);
        await using var ada = await host.ConnectAsync(Ada, Ct);
        await using var ben = await host.ConnectAsync(Ben, Ct);
        var fixture = Fixture("live.readmodel.changed.json");

        await SendHintAsync(host, new ReadModelChange(Site, ReadModelResources.Lots, TomatoesLot, 48213));
        await SendHintAsync(host, new ReadModelChange(OtherSite, ReadModelResources.Devices, "5a4b3c2d1e0f7c20", 7));

        var (target, json) = Assert.Single(await ada.WaitForAsync(1, Ct));
        Assert.Equal(fixture["target"]!.GetValue<string>(), target);
        AssertJsonEqual(fixture["message"], JsonNode.Parse(json));
        Assert.Equal(["resource", "id", "version"], JsonNode.Parse(json)!.AsObject().Select(property => property.Key));
        Assert.Equal(["5a4b3c2d1e0f7c20"], (await ben.WaitForAsync(1, Ct)).Select(IdOf));
    }

    [Fact]
    public async Task AMembershipAddedOrRemovedWhileConnectedTakesEffectWithoutAReconnect()
    {
        await using var host = await LiveTestHost.StartAsync(cancellationToken: Ct);
        await using var ada = await host.ConnectAsync(Ada, Ct);

        // No Membership yet: a hint of the Site does not reach her.
        await SendHintNowAsync(host, new ReadModelChange(Site, ReadModelResources.Lots, "before", 1));

        // The identity projector commits her Membership: she is told, and from now on gets the Site's hints.
        host.Memberships.Set(Ada, Site);
        await SendHintNowAsync(host, new ReadModelChange(Site, ReadModelResources.Sites, Site, 2, Ada));
        await SendHintNowAsync(host, new ReadModelChange(Site, ReadModelResources.Lots, "while-member", 3));

        // It is revoked: she is told once more, and nothing of the Site reaches her afterwards.
        host.Memberships.Set(Ada);
        await SendHintNowAsync(host, new ReadModelChange(Site, ReadModelResources.Sites, Site, 4, Ada));
        await SendHintNowAsync(host, new ReadModelChange(Site, ReadModelResources.Lots, "after", 5));
        await SendHintNowAsync(host, new ReadModelChange(Site, ReadModelResources.Sites, Site, 6));

        // A hint of her own, so that the test knows everything before it has arrived.
        host.Memberships.Set(Ada, OtherSite);
        await SendHintNowAsync(host, new ReadModelChange(OtherSite, ReadModelResources.Sites, OtherSite, 7, Ada));

        var received = await ada.WaitForAsync(4, Ct);
        Assert.Equal(
            [("sites", Site, 2L), ("lots", "while-member", 3L), ("sites", Site, 4L), ("sites", OtherSite, 7L)],
            received.Select(message => (JsonNode.Parse(message.Json)!["resource"]!.GetValue<string>(), IdOf(message), JsonNode.Parse(message.Json)!["version"]!.GetValue<long>())));
        Assert.Equal(HubConnectionState.Connected, ada.Connection.State);
    }

    [Fact]
    public async Task TheConnectionIsClosedWhenItsTokenExpires()
    {
        await using var host = await LiveTestHost.StartAsync(cancellationToken: Ct);
        var token = host.Token(Ada, TimeSpan.FromMinutes(5));
        await using var ada = await host.ConnectAsync(Ada, Ct, accessToken: token);
        var connections = host.Services.GetRequiredService<LiveConnections>();

        host.TokenClock.Advance(TimeSpan.FromMinutes(4));
        await Task.Delay(TimeSpan.FromMilliseconds(200), Ct);
        Assert.Equal(HubConnectionState.Connected, ada.Connection.State);

        host.TokenClock.Advance(TimeSpan.FromMinutes(2));

        await ada.Closed.WaitAsync(TimeSpan.FromSeconds(20), Ct);
        await WaitUntilAsync(() => connections.Count == 0);
    }

    [Fact]
    public async Task ATokenWithAFarExpiryConnectsAndIsWatchedForNoLongerThanATimerTakes()
    {
        await using var host = await LiveTestHost.StartAsync(cancellationToken: Ct);
        host.Memberships.Set(Ada, Site);

        // Longer than the 49.7 days a timer's due time may be.
        await using var ada = await host.ConnectAsync(Ada, Ct, accessToken: host.Token(Ada, TimeSpan.FromDays(400)));

        await SendHintAsync(host, new ReadModelChange(Site, ReadModelResources.Lots, "lot-1", 1));
        Assert.Single(await ada.WaitForAsync(1, Ct));
        Assert.Equal(HubConnectionState.Connected, ada.Connection.State);
    }

    [Fact]
    public void TheExpiryWatchIsClampedToWhatATimerTakesAndAnUnusableExpIsNotWatched()
    {
        var now = new DateTimeOffset(2026, 10, 10, 8, 0, 0, TimeSpan.Zero);
        static string Exp(DateTimeOffset at) => at.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(TimeSpan.FromMinutes(5), LiveHub.ExpiryDueTime(Exp(now.AddMinutes(5)), now));
        Assert.Equal(TimeSpan.Zero, LiveHub.ExpiryDueTime(Exp(now.AddMinutes(-1)), now));

        var far = LiveHub.ExpiryDueTime(Exp(now.AddDays(400)), now);
        var last = LiveHub.ExpiryDueTime(Exp(DateTimeOffset.MaxValue), now);
        Assert.Equal(TimeSpan.FromMilliseconds(uint.MaxValue - 1), far);
        Assert.Equal(far, last);

        // The real clock takes that due time, and no longer one.
        using (TimeProvider.System.CreateTimer(_ => { }, null, far!.Value, Timeout.InfiniteTimeSpan))
        {
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => TimeProvider.System.CreateTimer(_ => { }, null, far.Value + TimeSpan.FromMilliseconds(1), Timeout.InfiniteTimeSpan));

        // Beyond any date, no number, negative, missing: no watch, and no exception.
        Assert.Null(LiveHub.ExpiryDueTime("253402300800", now));
        Assert.Null(LiveHub.ExpiryDueTime("9223372036854775807", now));
        Assert.Null(LiveHub.ExpiryDueTime("99999999999999999999999", now));
        Assert.Null(LiveHub.ExpiryDueTime("-5", now));
        Assert.Null(LiveHub.ExpiryDueTime("soon", now));
        Assert.Null(LiveHub.ExpiryDueTime(null, now));
    }

    [Fact]
    public async Task AConnectionWhoseMembershipsCannotBeReadIsClosedAndRegistersNothing()
    {
        await using var host = await LiveTestHost.StartAsync(cancellationToken: Ct);
        host.Memberships.Fail = true;
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(host.BaseAddress, LiveMessages.HubPath), options =>
            {
                options.Transports = HttpTransportType.ServerSentEvents;
                options.AccessTokenProvider = () => Task.FromResult<string?>(host.Token(Ada));
            })
            .Build();
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Closed += _ =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };

        await using (connection)
        {
            try
            {
                await connection.StartAsync(Ct);
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(20), Ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException and not TimeoutException)
            {
                // The Server may close before the handshake ends: the start fails instead.
            }
        }

        await WaitUntilAsync(() => host.Services.GetRequiredService<LiveConnections>().Count == 0);
    }

    // Through the publisher's own queue and loop, as a projector's commit hands a change on.
    private static async Task SendHintAsync(LiveTestHost host, ReadModelChange change)
    {
        host.Services.GetRequiredService<IReadModelHints>().Publish([change]);
        await Task.Yield();
    }

    // Sent before the call returns, for a test that changes the Memberships between two hints.
    private static Task SendHintNowAsync(LiveTestHost host, ReadModelChange change) =>
        host.Services.GetRequiredService<LiveHints>().SendAsync(change, Ct);

    private static string IdOf((string Target, string Json) message) =>
        JsonDocument.Parse(message.Json).RootElement.GetProperty("id").GetString()!;

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));

        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
    }
}
