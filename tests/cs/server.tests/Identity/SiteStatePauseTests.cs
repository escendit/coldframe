using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Sites;
using Coldframe.Server.Identity;

namespace Coldframe.Server.Tests.Identity;

/// <summary>
/// The Site's Pause in the Site grain's state (Story 8.1; AD-6, AD-8): the Pause with its end date and revision,
/// and the Devices that have not acknowledged it, all of it replayed from the Site's own stream.
/// </summary>
public sealed class SiteStatePauseTests
{
    private const string Hub = "92064422c012f481";

    private const string Node = "5a4b3c2d1e0f7c20";

    private static readonly DateTimeOffset T = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ASiteIsNotPausedUntilItIsAndOwesNoDeviceAnything()
    {
        var state = Roster();

        Assert.Equal(SitePause.NotPaused, state.Pause);
        Assert.Empty(state.PendingPauseDeliveries);
        Assert.False(state.PauseIsOverdue(T.AddYears(10)));
    }

    [Fact]
    public void APauseIsOwedToEveryDeviceOnTheRosterUntilEachAcknowledgesIt()
    {
        var state = Roster();
        state.Apply(new SitePaused(T.AddDays(30), 1, T));

        Assert.Equal(new SitePause(true, T.AddDays(30), 1), state.Pause);
        Assert.Equal([Node, Hub], state.PendingPauseDeliveries.Order(StringComparer.Ordinal));

        state.Apply(new SitePauseDelivered(Hub, 1, T));
        Assert.Equal([Node], state.PendingPauseDeliveries);

        // The same acknowledgement again, and one of a Device that is not pending, change nothing.
        state.Apply(new SitePauseDelivered(Hub, 1, T));
        state.Apply(new SitePauseDelivered("0000000000000000", 1, T));
        Assert.Equal([Node], state.PendingPauseDeliveries);

        state.Apply(new SitePauseDelivered(Node, 1, T));
        Assert.Empty(state.PendingPauseDeliveries);
    }

    [Fact]
    public void AChangedPauseAndAResumeAreOwedToEveryDeviceAgainAndAnOlderAcknowledgementSettlesNothing()
    {
        var state = Roster();
        state.Apply(new SitePaused(null, 1, T));
        state.Apply(new SitePauseDelivered(Hub, 1, T));

        state.Apply(new SiteResumed(2, T.AddHours(1)));
        Assert.Equal(new SitePause(false, null, 2), state.Pause);
        Assert.Equal([Node, Hub], state.PendingPauseDeliveries.Order(StringComparer.Ordinal));

        // The Node answers the Pause of revision 1 late: it has not heard the resume.
        state.Apply(new SitePauseDelivered(Node, 1, T.AddHours(1)));
        Assert.Equal([Node, Hub], state.PendingPauseDeliveries.Order(StringComparer.Ordinal));

        state.Apply(new SitePaused(T.AddDays(7), 3, T.AddHours(2)));
        state.Apply(new SitePauseDelivered(Node, 3, T.AddHours(2)));
        Assert.Equal([Hub], state.PendingPauseDeliveries);
    }

    [Fact]
    public void ADeviceRegisteredWhileTheSiteIsPausedIsNotPendingBecauseItReadsTheJoinReply()
    {
        var state = Roster();
        state.Apply(new SitePaused(null, 1, T));
        state.Apply(new DeviceRegistered("9c0d1e2f3a4b5c68", DeviceKind.Node, "u:k3", T.AddMinutes(1)));

        Assert.DoesNotContain("9c0d1e2f3a4b5c68", state.PendingPauseDeliveries);

        // The next change of the Pause is owed to it like to every other Device.
        state.Apply(new SiteResumed(2, T.AddHours(1)));
        Assert.Contains("9c0d1e2f3a4b5c68", state.PendingPauseDeliveries);
    }

    [Fact]
    public void APauseIsOverdueFromItsEndOnAndOneWithoutAnEndNever()
    {
        var state = Roster();
        state.Apply(new SitePaused(T.AddDays(30), 1, T));

        Assert.False(state.PauseIsOverdue(T.AddDays(30).AddSeconds(-1)));
        Assert.True(state.PauseIsOverdue(T.AddDays(30)));
        Assert.True(state.PauseIsOverdue(T.AddDays(31)));

        state.Apply(new SitePaused(null, 2, T.AddDays(1)));
        Assert.False(state.PauseIsOverdue(T.AddYears(10)));

        state.Apply(new SiteResumed(3, T.AddDays(2)));
        Assert.False(state.PauseIsOverdue(T.AddYears(10)));
    }

    private static SiteState Roster()
    {
        var state = new SiteState();
        state.Apply(new SiteCreated("Home", "owner"));
        state.Apply(new DeviceRegistered(Hub, DeviceKind.Hub, "u:k1", T.AddDays(-2)));
        state.Apply(new DeviceRegistered(Node, DeviceKind.Node, "u:k2", T.AddDays(-1)));
        return state;
    }
}
