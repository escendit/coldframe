using System.Text.Json;
using System.Text.Json.Nodes;
using Coldframe.Server.Journal;
using Coldframe.Server.Live;
using Coldframe.Server.Notifications;
using Coldframe.Server.Notifications.Push;
using Coldframe.Server.Tests.Notifications;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Coldframe.Server.Tests.Notifications.PushTestKit;

namespace Coldframe.Server.Tests.Live;

/// <summary>
/// Records what the hub's senders hand to SignalR.
/// </summary>
internal sealed class RecordingSender : ILiveSender
{
    public List<(IReadOnlyList<string> Connections, string Target, object Message)> Sent { get; } = [];

    public Exception? Failure { get; set; }

    public bool Hang { get; set; }

    public async Task SendAsync(IReadOnlyList<string> connectionIds, string target, object message, CancellationToken cancellationToken)
    {
        if (Hang)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        if (Failure is not null)
        {
            throw Failure;
        }

        Sent.Add((connectionIds, target, message));
    }
}

/// <summary>
/// The Notifier's channel to the open web app (Story 6.6): the messages are the contract of
/// <c>packages/asyncapi</c> with the push text, they go to the User's connections only, and a User without an
/// open tab gets nothing now and nothing later.
/// </summary>
public sealed class LiveNotificationChannelTests
{
    private const string Other = "7d1e8f20-3b5c-4a2d-9f4b-0e8a7d6c5b42";

    private static readonly string[] Kinds = ["alert", "reminder", "summary"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("alert")]
    [InlineData("reminder")]
    [InlineData("summary")]
    public async Task TheSerialisedNotificationEqualsTheContractFixtureAndThePushText(string kind)
    {
        var fixture = Fixture($"live.notification.{kind}.json");
        var push = Fixture($"push.{kind}.json");
        var kit = new Kit();
        kit.Connections.Add("tab-1", User);
        var notification = kind switch
        {
            "alert" => Alert(IPhone, Android),
            "reminder" => Reminder(IPhone, Android),
            _ => Summary(kit.Lookups, IPhone, Android),
        };

        var delivery = await kit.Channel.SendAsync(notification, SentAt(fixture), Ct);

        Assert.Same(ChannelDelivery.Nothing, delivery);
        var (connections, target, message) = Assert.Single(kit.Sender.Sent);
        Assert.Equal(["tab-1"], connections);
        Assert.Equal(fixture["target"]!.GetValue<string>(), target);
        var json = JsonSerializer.SerializeToNode(message, message.GetType(), HubPayloadOptions());
        AssertJsonEqual(fixture["message"], json);

        // The same words and the same collapse identity as the phone gets.
        var data = push["fcm"]!["message"]!["data"]!;
        Assert.Equal(
            (data["title"]!.GetValue<string>(), data["body"]!.GetValue<string>(), data["collapseId"]!.GetValue<string>()),
            (json!["title"]!.GetValue<string>(), json["body"]!.GetValue<string>(), json["collapseId"]!.GetValue<string>()));
    }

    [Fact]
    public void TheSerialisedHintEqualsTheContractFixtureAndHoldsOnlyResourceIdAndVersion()
    {
        var fixture = Fixture("live.readmodel.changed.json");

        var json = JsonSerializer.SerializeToNode(new ReadModelChangedMessage(ReadModelResources.Lots, TomatoesLot, 48213), HubPayloadOptions());

        AssertJsonEqual(fixture["message"], json);
        Assert.Equal(["resource", "id", "version"], json!.AsObject().Select(property => property.Key));
        Assert.Equal(["Resource", "Id", "Version"], typeof(ReadModelChangedMessage).GetProperties().Select(property => property.Name));
    }

    [Fact]
    public void TheHubHasExactlyTheTwoMessagesOfTheContract()
    {
        var targets = Kinds
            .Select(kind => Fixture($"live.notification.{kind}.json")["target"]!.GetValue<string>())
            .Append(Fixture("live.readmodel.changed.json")["target"]!.GetValue<string>())
            .Distinct(StringComparer.Ordinal);

        Assert.Equal([LiveMessages.NotificationDelivered, LiveMessages.ReadModelChanged], targets);
        Assert.Equal("/live", LiveMessages.HubPath);
    }

    [Fact]
    public async Task ItSendsToTheConnectionsOfTheNotifiedUserOnlyAndCountsNoneAsDelivered()
    {
        var kit = new Kit();
        kit.Connections.Add("ada-1", User);
        kit.Connections.Add("ada-2", User);
        kit.Connections.Add("ben-1", Other);

        var delivery = await kit.Channel.SendAsync(Alert(), AlertDueAt, Ct);

        // An open tab is no proof that the User was told: its toggle may be off, which the Server cannot know.
        Assert.Same(ChannelDelivery.Nothing, delivery);
        Assert.Equal(["ada-1", "ada-2"], Assert.Single(kit.Sender.Sent).Connections.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task AnOpenTabDoesNotStandInForAPhoneWhenThePushChannelFailedTheNotifierStillFails()
    {
        var kit = new Kit();
        kit.Connections.Add("ada-1", User);
        using var metrics = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var push = new FailingChannel();
        var notifier = new Notifier(
            [push, kit.Channel],
            metrics.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>(),
            kit.Clock,
            NullLogger<Notifier>.Instance);

        // The provider is down, the tab got the message: the delivery stays due and the User grain repeats it.
        var thrown = await Assert.ThrowsAsync<NotificationNotDeliveredException>(() => notifier.SendAsync(Alert(IPhone), Ct));

        Assert.Same(push.Failure, Assert.Single(thrown.Failures));
        Assert.Single(kit.Sender.Sent);

        // Without a failing channel the same send is no failure, with or without a tab.
        var alone = new Notifier([kit.Channel], metrics.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>(), kit.Clock, NullLogger<Notifier>.Instance);
        await alone.SendAsync(Alert(), Ct);
        kit.Connections.Remove("ada-1");
        await alone.SendAsync(Alert(), Ct);
        Assert.Equal(2, kit.Sender.Sent.Count);
    }

    [Fact]
    public async Task WithNoOpenConnectionNothingIsSentNothingIsLookedUpAndNothingIsKeptForLater()
    {
        var kit = new Kit();
        kit.Connections.Add("ben-1", Other);

        var delivery = await kit.Channel.SendAsync(Alert(), AlertDueAt, Ct);

        // No failure: the Notifier does not throw, so the User grain records the delivery as sent.
        Assert.Same(ChannelDelivery.Nothing, delivery);
        Assert.Empty(kit.Sender.Sent);
        Assert.Equal(0, kit.Lookups.Calls);

        // A tab that opens afterwards gets nothing of it.
        kit.Connections.Add("ada-1", User);
        Assert.Empty(kit.Sender.Sent);
    }

    [Fact]
    public async Task ARepeatedSendCarriesTheSameCollapseIdentitySoTheBrowserShowsItOnce()
    {
        var kit = new Kit();
        kit.Connections.Add("ada-1", User);

        await kit.Channel.SendAsync(Alert(), AlertDueAt, Ct);
        await kit.Channel.SendAsync(Alert(), AlertDueAt.AddSeconds(30), Ct);

        Assert.Equal(2, kit.Sender.Sent.Count);
        Assert.Single(kit.Sender.Sent.Select(sent => ((NotificationDeliveredMessage)sent.Message).CollapseId).Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ItNeverFiltersAMutedSiteOrATimeOutsideTheWindowIsNotItsBusiness()
    {
        var kit = new Kit();
        kit.Connections.Add("ada-1", User);

        // 03:00 in Zurich, far outside the default window: the channel sends what it is handed.
        var delivery = await kit.Channel.SendAsync(Alert() with { DueAt = AlertDueAt.AddHours(-5) }, AlertDueAt.AddHours(-5), Ct);

        Assert.Same(ChannelDelivery.Nothing, delivery);
        Assert.Single(kit.Sender.Sent);
    }

    [Fact]
    public async Task ASummaryWithNoLotLeftSendsNothing()
    {
        var kit = new Kit();
        kit.Connections.Add("ada-1", User);
        var summary = Summary(kit.Lookups);
        kit.Lookups.Lots.Clear();

        var delivery = await kit.Channel.SendAsync(summary, AlertDueAt, Ct);

        Assert.Same(ChannelDelivery.Nothing, delivery);
        Assert.Empty(kit.Sender.Sent);
    }

    [Fact]
    public async Task ASendThatFailsThrowsSoTheNotifierCountsTheChannelAsFailed()
    {
        var kit = new Kit();
        kit.Connections.Add("ada-1", User);
        kit.Sender.Failure = new InvalidOperationException("The connection is gone.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => kit.Channel.SendAsync(Alert(), AlertDueAt, Ct));
    }

    [Fact]
    public async Task ASendThatHangsEndsWithinTheChannelsOwnBudget()
    {
        var kit = new Kit();
        kit.Connections.Add("ada-1", User);
        kit.Sender.Hang = true;

        var send = kit.Channel.SendAsync(Alert(), AlertDueAt, Ct);
        await Task.Delay(TimeSpan.FromMilliseconds(50), Ct);
        Assert.False(send.IsCompleted);
        kit.Clock.Advance(LiveNotificationChannel.SendBudget);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
    }

    private sealed class FailingChannel : INotificationChannel
    {
        public Exception Failure { get; } = new InvalidOperationException("The provider is down.");

        public Task<ChannelDelivery> SendAsync(Coldframe.Contracts.Notifications.Notification notification, DateTimeOffset sentAt, CancellationToken cancellationToken = default) =>
            Task.FromException<ChannelDelivery>(Failure);
    }

    // The serializer options the hub's JSON protocol really uses.
    private static JsonSerializerOptions HubPayloadOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLive();
        using var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOptions<JsonHubProtocolOptions>>().Value.PayloadSerializerOptions;
    }

    private sealed class Kit
    {
        public Kit()
        {
            Lookups = TomatoesLookups();
            Connections = new LiveConnections(new FakeMemberships(), NullLogger<LiveConnections>.Instance);
            Channel = new LiveNotificationChannel(Connections, Sender, Builder(Lookups, clock: Clock), Clock);
        }

        public FakeLookups Lookups { get; }

        public FakeTimeProvider Clock { get; } = new(AlertDueAt);

        public RecordingSender Sender { get; } = new();

        public LiveConnections Connections { get; }

        public LiveNotificationChannel Channel { get; }
    }
}
