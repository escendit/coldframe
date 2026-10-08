using Coldframe.Contracts.Alerts;
using Coldframe.Server.Alerts;

namespace Coldframe.Server.Tests.Alerts;

/// <summary>
/// The Alert's state and its ID (Story 6.1): an Alert is opened once, closed once, and remembers what its Site
/// grain acknowledged.
/// </summary>
public sealed class AlertStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid Sensor = Guid.Parse("dac4e7fe-93b1-56fc-a363-51235a586394");

    private static readonly AlertOpened Opened = new(
        AlertKind.Threshold,
        ThresholdSide.Low,
        "0192f3a4-7c1e-7d2b-9a51-3f7e2c9b1d00",
        "0192f3a4-8a00-7c3d-8e4f-5a6b7c8d9e02",
        Sensor,
        "5a4b3c2d1e0f7c20",
        "soil_moisture",
        1,
        Now);

    [Fact]
    public void TheIdOfAThresholdAlertIsTheUuidV5OfItsSensorAndEpisode()
    {
        Assert.Equal("sensor:dac4e7fe-93b1-56fc-a363-51235a586394:threshold:3", AlertIds.ThresholdName(Sensor, 3));
        Assert.Equal(
            Coldframe.Crypto.SensorIds.UuidV5(AlertIds.Namespace, "sensor:dac4e7fe-93b1-56fc-a363-51235a586394:threshold:3"),
            AlertIds.Threshold(Sensor, 3));
        Assert.Equal(5, AlertIds.Threshold(Sensor, 3).Version);
        Assert.NotEqual(AlertIds.Threshold(Sensor, 3), AlertIds.Threshold(Sensor, 4));
        Assert.NotEqual(AlertIds.Threshold(Sensor, 3), AlertIds.Threshold(Guid.Empty, 3));

        // A Server-only namespace: never the one Sensor IDs are derived in.
        Assert.NotEqual(Coldframe.Crypto.SensorIds.Namespace, AlertIds.Namespace);
    }

    [Fact]
    public void ANewAlertWasNeverOpened()
    {
        var state = new AlertState();

        Assert.Equal(AlertLifecycle.None, state.Lifecycle);
        Assert.False(state.ReportPending);
    }

    [Fact]
    public void AnOpenedAlertRecordsItsSideSiteLotSensorDeviceAndQuantity()
    {
        var state = new AlertState();

        state.Apply(Opened);

        Assert.Equal(AlertLifecycle.Open, state.Lifecycle);
        Assert.Equal(
            (AlertKind.Threshold, (ThresholdSide?)ThresholdSide.Low, Opened.SiteId, Opened.LotId, Sensor, "5a4b3c2d1e0f7c20", "soil_moisture", Now),
            (state.Kind, state.Side, state.SiteId, state.LotId, state.SensorId, state.DeviceId, state.Quantity, state.OpenedAt));
        Assert.True(state.ReportPending);

        state.Apply(new AlertSiteNotified(AlertLifecycle.Open, Now));

        Assert.False(state.ReportPending);
    }

    [Fact]
    public void AClosedAlertKeepsItsReasonAndOwesTheSiteItsClose()
    {
        var state = new AlertState();
        state.Apply(Opened);
        state.Apply(new AlertSiteNotified(AlertLifecycle.Open, Now));

        state.Apply(new AlertClosed(AlertCloseReason.Recovered, Now.AddHours(1)));

        Assert.Equal(AlertLifecycle.Closed, state.Lifecycle);
        Assert.Equal((AlertCloseReason.Recovered, Now.AddHours(1)), (state.Reason, state.ClosedAt));
        Assert.Equal(Sensor, state.SensorId);
        Assert.True(state.ReportPending);

        state.Apply(new AlertSiteNotified(AlertLifecycle.Closed, Now.AddHours(1)));

        Assert.False(state.ReportPending);
    }
}
