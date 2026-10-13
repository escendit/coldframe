using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Devices;
using Coldframe.Server.Alerts;
using Coldframe.Server.Devices;

namespace Coldframe.Server.Tests.Devices;

/// <summary>
/// The battery state of a Node (Story 7.3): the streaks, the last report counted, the episode and the open
/// <c>battery</c> Alert are derived from the journal alone; one Alert at a time, its open delivered before its
/// close, with the level of the report that opened it. Nothing of it moves the silence clock.
/// </summary>
public sealed class DeviceStateBatteryTests
{
    private const string SiteId = "0192f3a4-7c1e-7d2b-9a51-3f7e2c9b1d00";

    private const string NodeId = "9c0d1e2f3a4b5c68";

    private static readonly DateTimeOffset T = new(2026, 10, 13, 8, 0, 0, TimeSpan.Zero);

    private static readonly WrappedDeviceKey Key = new("0123456789abcdef", new byte[12], new byte[48]);

    [Fact]
    public void ANodeThatWasNeverLowHasNoStreakNoEpisodeAndNoAlert()
    {
        var state = Enrolled();

        Assert.Equal(new BatteryStreaks(false, 0, 0), state.BatteryStreaks);
        Assert.Equal((0, (DeviceBatteryAlert?)null, (DateTimeOffset?)null), (state.BatteryEpisode, state.OpenBatteryAlert, state.BatteryCountedAt));
        Assert.Empty(state.PendingAlertDeliveries);
    }

    [Fact]
    public void ACountedReportMovesTheStreakAndOneThatIsNotNewerChangesNothing()
    {
        var state = Enrolled();

        state.Apply(new DeviceBatteryStreakChanged(1, 0, T));
        state.Apply(new DeviceBatteryStreakChanged(2, 0, T.AddMinutes(15)));

        Assert.Equal((2, 0, (DateTimeOffset?)T.AddMinutes(15)), (state.BatteryLowStreak, state.BatteryRecoveredStreak, state.BatteryCountedAt));

        // The same report again, and an older one arriving late.
        state.Apply(new DeviceBatteryStreakChanged(0, 0, T.AddMinutes(15)));
        state.Apply(new DeviceBatteryStreakChanged(0, 0, T.AddMinutes(5)));

        Assert.Equal((2, (DateTimeOffset?)T.AddMinutes(15)), (state.BatteryLowStreak, state.BatteryCountedAt));
    }

    [Fact]
    public void ALowBatteryEpisodeOpensOneAlertWithItsLevelWhoseOpenIsDeliveredBeforeItsClose()
    {
        var state = Enrolled();
        state.Apply(new DeviceAssigned(SiteId, "lot-lettuce", T));
        state.Apply(new DeviceBatteryStreakChanged(1, 0, T));
        state.Apply(new DeviceBatteryStreakChanged(2, 0, T.AddMinutes(15)));
        var first = AlertIds.Battery(NodeId, 1);
        var startedAt = T.AddMinutes(30).AddSeconds(2);

        state.Apply(new DeviceBatteryLowStarted(1, first, SiteId, "lot-lettuce", 14, T.AddMinutes(30), startedAt));

        Assert.Equal(1, state.BatteryEpisode);
        Assert.Equal(new DeviceBatteryAlert(first, 1, startedAt, 14), state.OpenBatteryAlert);
        Assert.Equal(new BatteryStreaks(true, 0, 0), state.BatteryStreaks);
        Assert.Equal(T.AddMinutes(30), state.BatteryCountedAt);
        Assert.Equal(
            [new PendingDeviceAlertDelivery(first, AlertLifecycle.Open, AlertKind.Battery, 1, SiteId, "lot-lettuce", null, startedAt, null, 14)],
            state.PendingAlertDeliveries);

        // Three recovered reports before the open was acknowledged: the close waits behind the open.
        state.Apply(new DeviceBatteryStreakChanged(0, 1, T.AddMinutes(45)));
        state.Apply(new DeviceBatteryStreakChanged(0, 2, T.AddMinutes(60)));
        Assert.Equal(new BatteryStreaks(true, 0, 2), state.BatteryStreaks);

        state.Apply(new DeviceBatteryLowEnded(1, first, AlertCloseReason.Recovered, T.AddMinutes(75), T.AddMinutes(75).AddSeconds(2)));

        Assert.Null(state.OpenBatteryAlert);
        Assert.Equal(new BatteryStreaks(false, 0, 0), state.BatteryStreaks);
        Assert.Equal(T.AddMinutes(75), state.BatteryCountedAt);
        Assert.Equal([AlertLifecycle.Open, AlertLifecycle.Closed], state.PendingAlertDeliveries.Select(delivery => delivery.Change));
        Assert.Equal(
            (AlertKind.Battery, (AlertCloseReason?)AlertCloseReason.Recovered, (int?)null),
            (state.PendingAlertDeliveries[1].Kind, state.PendingAlertDeliveries[1].Reason, state.PendingAlertDeliveries[1].BatteryPercent));

        state.Apply(new DeviceAlertDelivered(first, AlertLifecycle.Open, T.AddMinutes(76)));
        Assert.Equal(AlertLifecycle.Closed, Assert.Single(state.PendingAlertDeliveries).Change);
        state.Apply(new DeviceAlertDelivered(first, AlertLifecycle.Closed, T.AddMinutes(76)));
        Assert.Empty(state.PendingAlertDeliveries);

        // The next low episode is episode 2, with another Alert ID.
        var second = AlertIds.Battery(NodeId, 2);
        state.Apply(new DeviceBatteryLowStarted(2, second, SiteId, "lot-lettuce", 9, T.AddHours(3), T.AddHours(3)));

        Assert.NotEqual(first, second);
        Assert.Equal((2, second, 9), (state.BatteryEpisode, state.OpenBatteryAlert?.AlertId, state.OpenBatteryAlert?.BatteryPercent));
    }

    [Fact]
    public void AnEpisodeAppliedAgainOrAnEndOfAnotherAlertChangesNothing()
    {
        var state = Enrolled();
        var alertId = AlertIds.Battery(NodeId, 1);
        var started = new DeviceBatteryLowStarted(1, alertId, SiteId, null, 14, T, T);

        state.Apply(started);
        state.Apply(started);
        state.Apply(new DeviceBatteryLowStarted(2, AlertIds.Battery(NodeId, 2), SiteId, null, 12, T.AddMinutes(15), T.AddMinutes(15)));
        state.Apply(new DeviceBatteryLowEnded(2, AlertIds.Battery(NodeId, 2), AlertCloseReason.Recovered, T.AddMinutes(30), T.AddMinutes(30)));

        Assert.Equal((1, alertId, 14), (state.BatteryEpisode, state.OpenBatteryAlert?.AlertId, state.OpenBatteryAlert?.BatteryPercent));
        var open = Assert.Single(state.PendingAlertDeliveries);
        Assert.Equal((AlertLifecycle.Open, (string?)null, (int?)14), (open.Change, open.LotId, open.BatteryPercent));
    }

    [Fact]
    public void TheBatteryAndTheSilenceOfANodeAreApart()
    {
        var state = Enrolled();
        var clock = (state.SilenceFrom, state.SilenceDueAt);
        var battery = AlertIds.Battery(NodeId, 1);
        var silent = AlertIds.Silent(NodeId, 1);

        state.Apply(new DeviceBatteryStreakChanged(1, 0, T.AddHours(1)));
        state.Apply(new DeviceBatteryLowStarted(1, battery, SiteId, null, 14, T.AddHours(2), T.AddHours(2)));

        // No battery event moves the silence clock, the silence episode or the Node's liveness.
        Assert.Equal(clock, (state.SilenceFrom, state.SilenceDueAt));
        Assert.Equal((0, (DeviceSilentAlert?)null, (DateTimeOffset?)null), (state.SilenceEpisode, state.OpenSilentAlert, state.LastReportedAt));

        // Both Alerts can be open at once, each with its own episode counter and its own ID.
        state.Apply(new DeviceSilenceStarted(1, silent, SiteId, null, T.AddHours(6), T.AddHours(6), T));
        Assert.NotEqual(battery, silent);
        Assert.Equal((battery, silent), (state.OpenBatteryAlert?.AlertId, state.OpenSilentAlert?.AlertId));
        Assert.Equal([AlertKind.Battery, AlertKind.Silent], state.PendingAlertDeliveries.Select(delivery => delivery.Kind));

        // The end of one leaves the other open.
        state.Apply(new DeviceSilenceEnded(1, silent, AlertCloseReason.Recovered, T.AddHours(7)));
        Assert.Equal((battery, (Guid?)null), (state.OpenBatteryAlert?.AlertId, state.OpenSilentAlert?.AlertId));
    }

    [Fact]
    public void AStreamWrittenBeforeThisStoryReadsWithoutABatteryState()
    {
        var state = Enrolled();
        state.Apply(new DeviceReported(T.AddMinutes(15), T.AddMinutes(15)));
        state.Apply(new DeviceSilenceStarted(1, AlertIds.Silent(NodeId, 1), SiteId, null, T.AddHours(6), T.AddHours(6)));

        Assert.Equal(new BatteryStreaks(false, 0, 0), state.BatteryStreaks);
        Assert.Equal(0, state.BatteryEpisode);
        Assert.Null(Assert.Single(state.PendingAlertDeliveries).BatteryPercent);
    }

    private static DeviceState Enrolled()
    {
        var state = new DeviceState();
        state.Apply(new DeviceEnrolled(SiteId, DeviceKind.Node, Key, T));
        return state;
    }
}
