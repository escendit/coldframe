using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Devices;
using Coldframe.Server.Alerts;
using Coldframe.Server.Devices;

namespace Coldframe.Server.Tests.Devices;

/// <summary>
/// The silence state of a Device (Story 7.1, AD-6): the expiry is derived from the journal alone, as the latest of
/// enrolment, the last accepted report, the last Server start, the last resume and a relay Hub's recovery, plus
/// the window in force; one silent Alert at a time, its open delivered before its close.
/// </summary>
public sealed class DeviceStateSilenceTests
{
    private const string SiteId = "0192f3a4-7c1e-7d2b-9a51-3f7e2c9b1d00";

    private const string NodeId = "5a4b3c2d1e0f7c20";

    private static readonly DateTimeOffset T = new(2026, 10, 11, 1, 5, 0, TimeSpan.Zero);

    private static readonly WrappedDeviceKey Key = new("0123456789abcdef", new byte[12], new byte[48]);

    [Fact]
    public void ADeviceThatIsNotEnrolledHasNoExpiry()
    {
        var state = new DeviceState();

        Assert.Null(state.SilenceFrom);
        Assert.Null(state.SilenceDueAt);
        Assert.Empty(state.PendingAlertDeliveries);
    }

    [Theory]
    [InlineData(DeviceKind.Node, 360)]
    [InlineData(DeviceKind.Hub, 5)]
    public void AnEnrolledDeviceThatNeverReportsIsDueOneWindowAfterItsEnrolment(DeviceKind kind, int minutes)
    {
        var state = Enrolled(kind);

        Assert.Equal(T, state.SilenceFrom);
        Assert.Equal(T.AddMinutes(minutes), state.SilenceDueAt);
        Assert.Null(state.SilenceWindowOverride);
    }

    // Story 7.2: "silent since" is when the Device was last heard, which the clock start is not once a Server
    // start or a relay Hub's recovery moved it.
    [Fact]
    public void ADeviceWasLastHeardAtItsLastReportOrElseAtItsEnrolmentWhateverRestartedItsClock()
    {
        var hub = Enrolled(DeviceKind.Hub);
        var node = Enrolled(DeviceKind.Node);

        Assert.Equal((T, T), (hub.LastHeardAt, node.LastHeardAt));
        Assert.Null(new DeviceState().LastHeardAt);

        hub.Apply(new DeviceSeen(T.AddMinutes(2), 2, null));
        node.Apply(new DeviceReported(T.AddMinutes(15), T.AddMinutes(16)));
        node.Apply(new DeviceSilenceClockRestarted(T.AddHours(5), SilenceClockRestartCause.ServerStarted, null, T.AddHours(5)));

        Assert.Equal((T.AddMinutes(2), T.AddMinutes(15)), (hub.LastHeardAt, node.LastHeardAt));
        Assert.Equal(T.AddHours(5), node.SilenceFrom);
    }

    [Fact]
    public void TheOpenOfASilenceCarriesWhenTheDeviceWasLastHeardAndAnOlderEpisodeNone()
    {
        var stated = Enrolled(DeviceKind.Node);
        var older = Enrolled(DeviceKind.Node);

        stated.Apply(new DeviceSilenceStarted(1, AlertIds.Silent(NodeId, 1), SiteId, null, T.AddHours(6), T.AddHours(6), T));
        older.Apply(new DeviceSilenceStarted(1, AlertIds.Silent(NodeId, 1), SiteId, null, T.AddHours(6), T.AddHours(6)));

        Assert.Equal((DateTimeOffset?)T, Assert.Single(stated.PendingAlertDeliveries).SilentSince);
        Assert.Null(Assert.Single(older.PendingAlertDeliveries).SilentSince);
    }

    [Fact]
    public void AHubsClockRunsFromItsLastAcceptedHeartbeat()
    {
        var state = Enrolled(DeviceKind.Hub);

        state.Apply(new DeviceSeen(T.AddMinutes(1), 1, null));
        state.Apply(new DeviceSeen(T.AddMinutes(2), 2, null));

        Assert.Equal(T.AddMinutes(2), state.SilenceFrom);
        Assert.Equal(T.AddMinutes(7), state.SilenceDueAt);
    }

    [Fact]
    public void ANodesClockRunsFromTheNewestMeasuredAtAndNeverFromTheArrivalTime()
    {
        var state = Enrolled(DeviceKind.Node);

        // A frame measured at T + 1 h that arrives two hours later counts from when it was measured.
        state.Apply(new DeviceReported(T.AddHours(1), T.AddHours(3)));

        Assert.Equal(T.AddHours(1), state.LastReportedAt);
        Assert.Equal(T.AddHours(1), state.SilenceFrom);
        Assert.Equal(T.AddHours(7), state.SilenceDueAt);

        // An older backlog frame moves nothing.
        state.Apply(new DeviceReported(T.AddMinutes(30), T.AddHours(4)));

        Assert.Equal(T.AddHours(1), state.LastReportedAt);
        Assert.Equal(T.AddHours(7), state.SilenceDueAt);
    }

    [Fact]
    public void AnOverrideMovesTheExpiryWithoutAnotherEventAndClearingItReturnsToTheDefault()
    {
        var state = Enrolled(DeviceKind.Node);

        state.Apply(new DeviceSilenceWindowSet(8 * 3600, T.AddMinutes(1)));

        Assert.Equal(TimeSpan.FromHours(8), state.SilenceWindowOverride);
        Assert.Equal(T.AddHours(8), state.SilenceDueAt);
        Assert.Equal(T, state.SilenceFrom);

        state.Apply(new DeviceSilenceWindowSet(null, T.AddMinutes(2)));

        Assert.Null(state.SilenceWindowOverride);
        Assert.Equal(T.AddHours(6), state.SilenceDueAt);
    }

    [Fact]
    public void AServerStartAndARelayHubsRecoveryRestartTheClockButNeverBackwards()
    {
        var state = Enrolled(DeviceKind.Node);
        state.Apply(new DeviceReported(T.AddHours(2), T.AddHours(2)));

        state.Apply(new DeviceSilenceClockRestarted(T.AddHours(7), SilenceClockRestartCause.ServerStarted, null, T.AddHours(7)));
        Assert.Equal(T.AddHours(13), state.SilenceDueAt);

        state.Apply(new DeviceSilenceClockRestarted(T.AddHours(9), SilenceClockRestartCause.RelayHubRecovered, "92064422c012f481", T.AddHours(13)));
        Assert.Equal(T.AddHours(15), state.SilenceDueAt);

        // A restart at an earlier time changes nothing, and it is no report of the Node.
        state.Apply(new DeviceSilenceClockRestarted(T.AddHours(1), SilenceClockRestartCause.ServerStarted, null, T.AddHours(14)));
        Assert.Equal(T.AddHours(9), state.SilenceFrom);
        Assert.Equal(T.AddHours(2), state.LastReportedAt);
    }

    [Fact]
    public void TheWindowRestartsWhenTheLastPauseSourceClears()
    {
        var state = Enrolled(DeviceKind.Node);
        state.Apply(new DevicePaused(DevicePauseSource.Device, null, T.AddHours(1)));
        state.Apply(new DevicePaused(DevicePauseSource.Site, null, T.AddHours(2)));

        // Pausing moves no clock, and one source clearing is no resume.
        state.Apply(new DeviceResumed(DevicePauseSource.Site, T.AddHours(8)));
        Assert.True(state.IsPaused);
        Assert.Equal(T, state.SilenceFrom);

        state.Apply(new DeviceResumed(DevicePauseSource.Device, T.AddHours(9)));
        Assert.False(state.IsPaused);
        Assert.Equal(T.AddHours(9), state.SilenceFrom);
        Assert.Equal(T.AddHours(15), state.SilenceDueAt);
    }

    [Fact]
    public void ASilenceEpisodeOpensOneAlertWhoseOpenIsDeliveredBeforeItsClose()
    {
        var state = Enrolled(DeviceKind.Node);
        state.Apply(new DeviceAssigned(SiteId, "lot-a", T));
        var first = AlertIds.Silent(NodeId, 1);

        state.Apply(new DeviceSilenceStarted(1, first, SiteId, "lot-a", T.AddHours(6), T.AddHours(6).AddSeconds(20)));

        Assert.Equal(1, state.SilenceEpisode);
        Assert.Equal(new DeviceSilentAlert(first, 1, T.AddHours(6).AddSeconds(20)), state.OpenSilentAlert);
        Assert.Equal(
            [new PendingDeviceAlertDelivery(first, AlertLifecycle.Open, AlertKind.Silent, 1, SiteId, "lot-a", null, T.AddHours(6).AddSeconds(20))],
            state.PendingAlertDeliveries);

        // The Device reports again before the open was acknowledged: the close waits behind the open.
        state.Apply(new DeviceReported(T.AddHours(7), T.AddHours(7)));
        state.Apply(new DeviceSilenceEnded(1, first, AlertCloseReason.Recovered, T.AddHours(7)));

        Assert.Null(state.OpenSilentAlert);
        Assert.Equal(T.AddHours(7), state.LastRecoveredAt);
        Assert.Equal([AlertLifecycle.Open, AlertLifecycle.Closed], state.PendingAlertDeliveries.Select(delivery => delivery.Change));
        Assert.Equal(AlertCloseReason.Recovered, state.PendingAlertDeliveries[1].Reason);

        // Acknowledging the open leaves the close pending, and the other way round.
        state.Apply(new DeviceAlertDelivered(first, AlertLifecycle.Open, T.AddHours(7)));
        Assert.Equal(AlertLifecycle.Closed, Assert.Single(state.PendingAlertDeliveries).Change);

        state.Apply(new DeviceAlertDelivered(first, AlertLifecycle.Closed, T.AddHours(7)));
        Assert.Empty(state.PendingAlertDeliveries);

        // The next silence is another episode with another Alert ID.
        var second = AlertIds.Silent(NodeId, 2);
        state.Apply(new DeviceSilenceStarted(2, second, SiteId, "lot-a", T.AddHours(13), T.AddHours(13)));

        Assert.NotEqual(first, second);
        Assert.Equal((2, second), (state.SilenceEpisode, state.OpenSilentAlert?.AlertId));
    }

    [Fact]
    public void AnEpisodeAppliedAgainOrAnEndOfAnotherAlertChangesNothing()
    {
        var state = Enrolled(DeviceKind.Hub);
        var alertId = AlertIds.Silent(NodeId, 1);
        var started = new DeviceSilenceStarted(1, alertId, SiteId, null, T.AddMinutes(5), T.AddMinutes(5));

        state.Apply(started);
        state.Apply(started);
        state.Apply(new DeviceSilenceStarted(2, AlertIds.Silent(NodeId, 2), SiteId, null, T.AddMinutes(5), T.AddMinutes(6)));
        state.Apply(new DeviceSilenceEnded(2, AlertIds.Silent(NodeId, 2), AlertCloseReason.Recovered, T.AddMinutes(7)));

        Assert.Equal((1, alertId), (state.SilenceEpisode, state.OpenSilentAlert?.AlertId));
        Assert.Single(state.PendingAlertDeliveries);
        Assert.Null(state.LastRecoveredAt);
        Assert.Null(state.PendingAlertDeliveries[0].LotId);
    }

    [Fact]
    public void AStreamWrittenBeforeSilenceExistedHasItsClockFromEnrolmentAndHeartbeats()
    {
        // No new event is needed to read an old stream: the clock is derived from the events it always had.
        var state = Enrolled(DeviceKind.Hub);
        state.Apply(new DeviceSeen(T.AddDays(1), 5, 61_000));
        state.Apply(new DeviceRelayChanged("92064422c012f481", T.AddDays(1)));

        Assert.Equal(T.AddDays(1), state.SilenceFrom);
        Assert.Equal(0, state.SilenceEpisode);
        Assert.Null(state.LastReportedAt);
    }

    private static DeviceState Enrolled(DeviceKind kind)
    {
        var state = new DeviceState();
        state.Apply(new DeviceEnrolled(SiteId, kind, Key, T));
        return state;
    }
}
