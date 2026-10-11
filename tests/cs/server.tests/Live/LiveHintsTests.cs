using Coldframe.Server.Journal;
using Coldframe.Server.Live;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coldframe.Server.Tests.Live;

/// <summary>
/// The hint publisher and the connection registry (Story 6.6) without a host: which connections a committed
/// change reaches, that a version never goes down, and that a failing read of the Memberships errs on the side
/// of telling less.
/// </summary>
public sealed class LiveHintsTests
{
    private const string Ada = "ada";
    private const string Ben = "ben";
    private const string Home = "site-home";
    private const string Allotment = "site-allotment";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AChangeReachesTheConnectionsOfTheSitesMembersOnly()
    {
        using var kit = new Kit();
        kit.Memberships.Set(Ada, Home);
        kit.Memberships.Set(Ben, Allotment);
        await kit.ConnectAsync("ada-1", Ada);
        await kit.ConnectAsync("ada-2", Ada);
        await kit.ConnectAsync("ben-1", Ben);

        await kit.Hints.SendAsync(new ReadModelChange(Home, ReadModelResources.Lots, "lot-1", 12), Ct);

        var (connections, target, message) = Assert.Single(kit.Sender.Sent);
        Assert.Equal(["ada-1", "ada-2"], connections.Order(StringComparer.Ordinal));
        Assert.Equal(LiveMessages.ReadModelChanged, target);
        Assert.Equal(new ReadModelChangedMessage("lots", "lot-1", 12), message);
    }

    [Fact]
    public async Task AChangeOfASiteWithoutAnOpenConnectionSendsNothing()
    {
        using var kit = new Kit();
        kit.Memberships.Set(Ben, Allotment);
        await kit.ConnectAsync("ben-1", Ben);

        await kit.Hints.SendAsync(new ReadModelChange(Home, ReadModelResources.Alerts, "alert-1", 3), Ct);

        Assert.Empty(kit.Sender.Sent);
    }

    [Fact]
    public async Task AGrantedMembershipTellsTheUserAndGivesTheConnectionTheSite()
    {
        using var kit = new Kit();
        await kit.ConnectAsync("ada-1", Ada);
        await kit.Hints.SendAsync(new ReadModelChange(Home, ReadModelResources.Lots, "lot-1", 1), Ct);
        Assert.Empty(kit.Sender.Sent);

        kit.Memberships.Set(Ada, Home);
        await kit.Hints.SendAsync(new ReadModelChange(Home, ReadModelResources.Sites, Home, 2, Ada), Ct);
        await kit.Hints.SendAsync(new ReadModelChange(Home, ReadModelResources.Lots, "lot-1", 3), Ct);

        Assert.Equal(
            [new ReadModelChangedMessage("sites", Home, 2), new ReadModelChangedMessage("lots", "lot-1", 3)],
            kit.Sender.Sent.Select(sent => sent.Message));
        Assert.All(kit.Sender.Sent, sent => Assert.Equal(["ada-1"], sent.Connections));
    }

    [Fact]
    public async Task ARevokedMembershipTellsTheUserOnceAndThenNothingOfTheSite()
    {
        using var kit = new Kit();
        kit.Memberships.Set(Ada, Home);
        kit.Memberships.Set(Ben, Home);
        await kit.ConnectAsync("ada-1", Ada);
        await kit.ConnectAsync("ben-1", Ben);

        kit.Memberships.Set(Ada);
        await kit.Hints.SendAsync(new ReadModelChange(Home, ReadModelResources.Sites, Home, 5, Ada), Ct);
        await kit.Hints.SendAsync(new ReadModelChange(Home, ReadModelResources.Readings, "lot-1", 6), Ct);

        Assert.Equal(["ada-1", "ben-1"], kit.Sender.Sent[0].Connections.Order(StringComparer.Ordinal));
        Assert.Equal(["ben-1"], kit.Sender.Sent[1].Connections);
    }

    [Fact]
    public async Task ADeletedSiteTellsItsMembersOnceAndThenNothing()
    {
        using var kit = new Kit();
        kit.Memberships.Set(Ada, Home);
        await kit.ConnectAsync("ada-1", Ada);

        // The projection lists only Active Sites: after the delete the User holds none.
        kit.Memberships.Set(Ada);
        await kit.Hints.SendAsync(new ReadModelChange(Home, ReadModelResources.Sites, Home, 8), Ct);
        await kit.Hints.SendAsync(new ReadModelChange(Home, ReadModelResources.Lots, "lot-1", 9), Ct);

        Assert.Equal(new ReadModelChangedMessage("sites", Home, 8), Assert.Single(kit.Sender.Sent).Message);
    }

    [Fact]
    public async Task WhenTheMembershipsCannotBeReadAfterAChangeTheUserGetsNoMoreHintsOfThatSite()
    {
        using var kit = new Kit();
        kit.Memberships.Set(Ada, Home, Allotment);
        await kit.ConnectAsync("ada-1", Ada);

        kit.Memberships.Fail = true;
        await kit.Hints.SendAsync(new ReadModelChange(Home, ReadModelResources.Sites, Home, 2, Ada), Ct);
        await kit.Hints.SendAsync(new ReadModelChange(Home, ReadModelResources.Lots, "lot-1", 3), Ct);
        await kit.Hints.SendAsync(new ReadModelChange(Allotment, ReadModelResources.Lots, "lot-2", 4), Ct);

        Assert.Equal(
            [new ReadModelChangedMessage("sites", Home, 2), new ReadModelChangedMessage("lots", "lot-2", 4)],
            kit.Sender.Sent.Select(sent => sent.Message));
    }

    [Fact]
    public async Task AMembershipReadThatHangsEndsWithItsBudgetAndStallsNeitherTheHintsNorANewConnection()
    {
        using var kit = new Kit();
        kit.Hints.Budget = TimeSpan.FromMilliseconds(100);
        kit.Memberships.Set(Ada, Home, Allotment);
        kit.Memberships.Set(Ben, Home);
        await kit.ConnectAsync("ada-1", Ada);

        kit.Memberships.Hang = true;
        await kit.Hints.SendAsync(new ReadModelChange(Home, ReadModelResources.Sites, Home, 2, Ada), Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct);

        // The lock is free again: the next hint is sent, and a new connection reads its Memberships.
        kit.Memberships.Hang = false;
        await kit.ConnectAsync("ben-1", Ben).WaitAsync(TimeSpan.FromSeconds(10), Ct);
        await kit.Hints.SendAsync(new ReadModelChange(Allotment, ReadModelResources.Lots, "lot-2", 3), Ct);
        await kit.Hints.SendAsync(new ReadModelChange(Home, ReadModelResources.Lots, "lot-1", 4), Ct);

        // Ada was told of the change, and lost the Site whose Memberships could not be read.
        Assert.Equal(
            [(new ReadModelChangedMessage("sites", Home, 2), "ada-1"), (new ReadModelChangedMessage("lots", "lot-2", 3), "ada-1"), (new ReadModelChangedMessage("lots", "lot-1", 4), "ben-1")],
            kit.Sender.Sent.Select(sent => ((ReadModelChangedMessage)sent.Message, Assert.Single(sent.Connections))));
    }

    [Fact]
    public async Task AVersionIsPositiveAndNeverDecreasesForTheSameResourceAndId()
    {
        using var kit = new Kit();
        kit.Memberships.Set(Ada, Home);
        await kit.ConnectAsync("ada-1", Ada);

        // A rebuilt projector replays from position 0, and a Node's backlog frame is older than its last one.
        foreach (var version in new long[] { 40, 41, 7, 0, -3, 42 })
        {
            await kit.Hints.SendAsync(new ReadModelChange(Home, ReadModelResources.Readings, "lot-1", version), Ct);
        }

        await kit.Hints.SendAsync(new ReadModelChange(Home, ReadModelResources.Lots, "lot-1", 5), Ct);
        await kit.Hints.SendAsync(new ReadModelChange(Home, ReadModelResources.Readings, "lot-2", 0), Ct);

        Assert.Equal(
            [40L, 41, 41, 41, 41, 42, 5, 1],
            kit.Sender.Sent.Select(sent => ((ReadModelChangedMessage)sent.Message).Version));
    }

    [Fact]
    public async Task PublishKeepsNothingWhileNoConnectionIsOpenAndNeverThrows()
    {
        using var kit = new Kit();
        kit.Memberships.Set(Ada, Home);

        kit.Hints.Publish([new ReadModelChange(Home, ReadModelResources.Lots, "lot-1", 1)]);
        await kit.ConnectAsync("ada-1", Ada);
        await kit.Hints.StartAsync(Ct);
        kit.Hints.Publish([new ReadModelChange(Home, ReadModelResources.Lots, "lot-2", 2)]);

        await WaitUntilAsync(() => kit.Sender.Sent.Count == 1);
        Assert.Equal(new ReadModelChangedMessage("lots", "lot-2", 2), kit.Sender.Sent[0].Message);

        // A send that fails is dropped; the next hint is still sent.
        kit.Sender.Failure = new InvalidOperationException("The connection is gone.");
        kit.Hints.Publish([new ReadModelChange(Home, ReadModelResources.Lots, "lot-3", 3)]);
        await Task.Delay(TimeSpan.FromMilliseconds(100), Ct);
        kit.Sender.Failure = null;
        kit.Hints.Publish([new ReadModelChange(Home, ReadModelResources.Lots, "lot-4", 4)]);

        await WaitUntilAsync(() => kit.Sender.Sent.Count == 2);
        Assert.Equal(new ReadModelChangedMessage("lots", "lot-4", 4), kit.Sender.Sent[1].Message);
        await kit.Hints.StopAsync(Ct);
    }

    [Fact]
    public async Task AClosedConnectionIsForgottenAndItsLifetimeEnds()
    {
        using var kit = new Kit();
        kit.Memberships.Set(Ada, Home);
        using var lifetime = new Flag();
        kit.Connections.Add("ada-1", Ada, lifetime);
        await kit.Connections.RefreshUserAsync(Ada, Ct);
        Assert.Equal(["ada-1"], kit.Connections.OfSite(Home));

        kit.Connections.Remove("ada-1");
        kit.Connections.Remove("ada-1");

        Assert.True(lifetime.Disposed);
        Assert.Equal((0, 0, 0), (kit.Connections.Count, kit.Connections.OfUser(Ada).Count, kit.Connections.OfSite(Home).Count));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));

        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }

    private sealed class Flag : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private sealed class Kit : IDisposable
    {
        public Kit()
        {
            Connections = new LiveConnections(Memberships, NullLogger<LiveConnections>.Instance);
            Hints = new LiveHints(Connections, Sender, NullLogger<LiveHints>.Instance);
        }

        public FakeMemberships Memberships { get; } = new();

        public RecordingSender Sender { get; } = new();

        public LiveConnections Connections { get; }

        public LiveHints Hints { get; }

        public async Task ConnectAsync(string connectionId, string userId)
        {
            Connections.Add(connectionId, userId);
            await Connections.RefreshUserAsync(userId, Ct);
        }

        public void Dispose()
        {
            Hints.Dispose();
            Connections.Dispose();
        }
    }
}
