using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Sensors;
using Coldframe.Server.Sensors;

namespace Coldframe.Server.Tests.Sensors;

/// <summary>
/// The evaluation state of the Sensor grain (Story 6.1): the streak, the episode, the open Alert and the
/// deliveries the Alert grain has not acknowledged, all replayed from the Sensor's own stream.
/// </summary>
public sealed class SensorStateEvaluationTests
{
    private const string SiteId = "0192f3a4-7c1e-7d2b-9a51-3f7e2c9b1d00";

    private const string LotId = "0192f3a4-8a00-7c3d-8e4f-5a6b7c8d9e02";

    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private static readonly SensorSpecification Soil = new("soil_moisture", SensorUnit.RawCount, 0, 4095, true, 30, 80);

    private static readonly Guid First = Guid.Parse("0192f3a4-a000-7000-8000-000000000001");

    private static readonly Guid Second = Guid.Parse("0192f3a4-a000-7000-8000-000000000002");

    [Fact]
    public void ANewSensorHasNoStreakNoEpisodeAndNoAlert()
    {
        var state = Declared();

        Assert.Equal(new StreakState(null, ThresholdPosition.Within, 0), state.Streak);
        Assert.Equal((0, 0L, (DateTimeOffset?)null), (state.Episode, state.EvaluationEpoch, state.LastEvaluatedAt));
        Assert.Null(state.OpenAlert);
        Assert.Empty(state.PendingAlertDeliveries);
    }

    [Fact]
    public void AStreakChangeKeepsItsPositionCountEpochAndTime()
    {
        var state = Declared();

        state.Apply(new SensorStreakChanged(ThresholdPosition.BelowLow, 2, 7, Now));

        Assert.Equal(new StreakState(null, ThresholdPosition.BelowLow, 2), state.Streak);
        Assert.Equal((7L, Now), (state.EvaluationEpoch, state.LastEvaluatedAt));
    }

    [Fact]
    public void AnOpenedEpisodeIsTheOpenAlertAndWaitsForItsDelivery()
    {
        var state = Declared();
        state.Apply(new SensorStreakChanged(ThresholdPosition.BelowLow, 2, 1, Now));

        state.Apply(Opened(1, First, ThresholdSide.Low, Now.AddMinutes(15)));

        Assert.Equal(1, state.Episode);
        Assert.Equal(new SensorOpenAlert(First, 1, ThresholdSide.Low), state.OpenAlert);
        Assert.Equal(new StreakState(ThresholdSide.Low, ThresholdPosition.Within, 0), state.Streak);
        Assert.Equal(Now.AddMinutes(15), state.LastEvaluatedAt);
        var pending = Assert.Single(state.PendingAlertDeliveries);
        Assert.Equal((First, AlertLifecycle.Open, SiteId, LotId), (pending.AlertId, pending.Change, pending.SiteId, pending.LotId));

        state.Apply(new SensorAlertDelivered(First, AlertLifecycle.Open, Now.AddMinutes(16)));

        Assert.Empty(state.PendingAlertDeliveries);
        Assert.NotNull(state.OpenAlert);
    }

    [Fact]
    public void AClosedEpisodeLeavesNoOpenAlertAndItsDeliveriesStayInOrder()
    {
        var state = Declared();
        state.Apply(Opened(1, First, ThresholdSide.Low, Now));

        // The switch from low to high: the close and the next open are journaled together.
        state.Apply(new SensorThresholdEpisodeClosed(1, First, AlertCloseReason.Recovered, 1, Now.AddHours(1), Now.AddHours(1)));
        state.Apply(Opened(2, Second, ThresholdSide.High, Now.AddHours(1)));

        Assert.Equal(2, state.Episode);
        Assert.Equal(new SensorOpenAlert(Second, 2, ThresholdSide.High), state.OpenAlert);
        Assert.Equal(
            [(First, AlertLifecycle.Open), (First, AlertLifecycle.Closed), (Second, AlertLifecycle.Open)],
            state.PendingAlertDeliveries.Select(delivery => (delivery.AlertId, delivery.Change)));

        state.Apply(new SensorAlertDelivered(First, AlertLifecycle.Open, Now.AddHours(2)));

        // Only the open is acknowledged: the close of the same Alert is still pending.
        Assert.Equal(
            [(First, AlertLifecycle.Closed), (Second, AlertLifecycle.Open)],
            state.PendingAlertDeliveries.Select(delivery => (delivery.AlertId, delivery.Change)));

        state.Apply(new SensorAlertDelivered(First, AlertLifecycle.Closed, Now.AddHours(2)));

        Assert.Equal([(Second, AlertLifecycle.Open)], state.PendingAlertDeliveries.Select(delivery => (delivery.AlertId, delivery.Change)));
    }

    [Fact]
    public void ARecoveredEpisodeLeavesNoAlertAndNoStreak()
    {
        var state = Declared();
        state.Apply(Opened(1, First, ThresholdSide.Low, Now));
        state.Apply(new SensorStreakChanged(ThresholdPosition.Within, 2, 1, Now.AddMinutes(30)));

        state.Apply(new SensorThresholdEpisodeClosed(1, First, AlertCloseReason.Recovered, 1, Now.AddMinutes(45), Now.AddMinutes(45)));

        Assert.Null(state.OpenAlert);
        Assert.Equal(new StreakState(null, ThresholdPosition.Within, 0), state.Streak);
        Assert.Equal(1, state.Episode);
        Assert.Equal(Now.AddMinutes(45), state.LastEvaluatedAt);
    }

    [Fact]
    public void ChangedThresholdsResetTheStreakAndKeepTheOpenAlert()
    {
        var state = Declared();
        state.Apply(Opened(1, First, ThresholdSide.Low, Now));
        state.Apply(new SensorStreakChanged(ThresholdPosition.Within, 2, 1, Now.AddMinutes(30)));

        state.Apply(new SensorThresholdsChanged(new ThresholdSetting(ThresholdKind.Override, 25), ThresholdSetting.Default, Now.AddMinutes(31)));

        Assert.Equal(new StreakState(ThresholdSide.Low, ThresholdPosition.Within, 0), state.Streak);
        Assert.Equal(new SensorOpenAlert(First, 1, ThresholdSide.Low), state.OpenAlert);
        Assert.Equal(Now.AddMinutes(30), state.LastEvaluatedAt);
    }

    [Fact]
    public void AChangedSpecificationResetsTheStreakAndKeepsTheOpenAlert()
    {
        var state = Declared();
        state.Apply(Opened(1, First, ThresholdSide.Low, Now));
        state.Apply(new SensorStreakChanged(ThresholdPosition.Within, 2, 1, Now.AddMinutes(30)));

        state.Apply(new SensorSpecificationChanged(Soil with { DefaultLow = 25 }, Now.AddMinutes(31)));

        Assert.Equal(new StreakState(ThresholdSide.Low, ThresholdPosition.Within, 0), state.Streak);
        Assert.Equal(new SensorOpenAlert(First, 1, ThresholdSide.Low), state.OpenAlert);
        Assert.Equal(Now.AddMinutes(30), state.LastEvaluatedAt);
    }

    [Fact]
    public void ANewCalibrationResetsTheStreakAndKeepsTheOpenAlert()
    {
        var state = Declared();
        state.Apply(Opened(1, First, ThresholdSide.Low, Now));
        state.Apply(new SensorStreakChanged(ThresholdPosition.Within, 2, 1, Now.AddMinutes(30)));

        state.Apply(new SensorCalibrated(Guid.Parse("0192f3a4-9000-7000-8000-000000000001"), 3000, 1000, Now.AddMinutes(31)));

        Assert.Equal(new StreakState(ThresholdSide.Low, ThresholdPosition.Within, 0), state.Streak);
        Assert.Equal(new SensorOpenAlert(First, 1, ThresholdSide.Low), state.OpenAlert);
        Assert.Equal(Now.AddMinutes(30), state.LastEvaluatedAt);
    }

    private static SensorState Declared()
    {
        var state = new SensorState();
        state.Apply(new SensorDeclared("5a4b3c2d1e0f7c20", 0, Soil, Now.AddDays(-1)));
        return state;
    }

    private static SensorThresholdEpisodeOpened Opened(int episode, Guid alertId, ThresholdSide side, DateTimeOffset at) =>
        new(episode, alertId, side, SiteId, LotId, 20, 30, 1, at, at);
}
