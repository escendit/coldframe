using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Sensors;
using Coldframe.Server.Alerts;
using Coldframe.Server.Sensors;

namespace Coldframe.Server.Tests.Sensors;

/// <summary>
/// The evaluation context and the uncalibrated Alert of a Sensor (Story 7.4, AD-8): the context the Device grain
/// sent last, the uncalibrated episode counter and the open Alert of kind <c>uncalibrated</c> are derived from the
/// journal alone, and <see cref="UncalibratedRule"/> decides from them when the Alert opens and closes. A stream
/// journaled before the story holds none of it and opens nothing.
/// </summary>
public sealed class SensorStateContextTests
{
    private const string SiteId = "0192f3a4-7c1e-7d2b-9a51-3f7e2c9b1d00";

    private const string Peppers = "lot-peppers";

    private const string Beans = "lot-beans";

    private static readonly Guid SensorId = Guid.Parse("dac4e7fe-93b1-56fc-a363-51235a586394");

    private static readonly DateTimeOffset T = new(2026, 10, 13, 8, 0, 0, TimeSpan.Zero);

    private static readonly SensorSpecification Soil = new("soil_moisture", SensorUnit.RawCount, 0, 4095, true, 30, 80);

    private static readonly SensorSpecification Air = new("air_temperature", SensorUnit.MilliDegreeCelsius, -40_000, 85_000, false);

    [Fact]
    public void ASensorJournaledBeforeTheStoryHoldsNoContextAndOpensNothing()
    {
        var state = Declared(Soil);

        Assert.Equal(((SensorContext?)null, 0, (SensorUncalibratedAlert?)null), (state.Context, state.UncalibratedEpisode, state.OpenUncalibratedAlert));
        Assert.True(state.NeedsCalibration);
        Assert.Empty(state.PendingAlertDeliveries);

        // It needs calibration, but nobody said where its Node is: no Alert until a context arrives.
        Assert.Equal(UncalibratedStep.None, UncalibratedRule.Decide(state.Context, state.Specification, state.Calibrated, null));
    }

    [Fact]
    public void TheNewestContextIsHeldAndOneThatIsNotNewerChangesNothing()
    {
        var state = Declared(Soil);

        state.Apply(new SensorContextChanged(SiteId, Peppers, false, 3, T));
        Assert.Equal(new SensorContext(SiteId, Peppers, false, 3), state.Context);

        // The same epoch again, and an older one arriving late.
        state.Apply(new SensorContextChanged(SiteId, null, false, 3, T.AddMinutes(1)));
        state.Apply(new SensorContextChanged(SiteId, Beans, true, 2, T.AddMinutes(2)));
        Assert.Equal(new SensorContext(SiteId, Peppers, false, 3), state.Context);

        state.Apply(new SensorContextChanged(SiteId, null, false, 4, T.AddMinutes(3)));
        Assert.Equal(new SensorContext(SiteId, null, false, 4), state.Context);
        Assert.False(state.Context!.Evaluated);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(Peppers, true)]
    public void AContextWithoutALotOrPausedResetsTheStreakAndAnotherLotDoesNot(string? lotId, bool paused)
    {
        var state = Declared(Soil);
        state.Apply(new SensorContextChanged(SiteId, Peppers, false, 1, T));
        state.Apply(new SensorStreakChanged(ThresholdPosition.BelowLow, 2, 1, T));

        // A move keeps the journaled streak: the next Reading of the new epoch starts it over (Story 6.1).
        state.Apply(new SensorContextChanged(SiteId, Beans, false, 2, T.AddMinutes(1)));
        Assert.Equal(new StreakState(null, ThresholdPosition.BelowLow, 2), state.Streak);

        state.Apply(new SensorContextChanged(SiteId, lotId, paused, 3, T.AddMinutes(2)));
        Assert.Equal(new StreakState(null, ThresholdPosition.Within, 0), state.Streak);
    }

    [Fact]
    public void AnUncalibratedEpisodeOpensOneAlertWhoseOpenIsDeliveredBeforeItsClose()
    {
        var state = Declared(Soil);
        state.Apply(new SensorContextChanged(SiteId, Peppers, false, 1, T));
        var first = AlertIds.Uncalibrated(SensorId, 1);

        state.Apply(new SensorUncalibratedEpisodeOpened(1, first, SiteId, Peppers, 1, T));

        Assert.Equal((1, new SensorUncalibratedAlert(first, 1, Peppers)), (state.UncalibratedEpisode, state.OpenUncalibratedAlert));
        Assert.Equal(
            [new PendingAlertDelivery(first, AlertLifecycle.Open, 1, null, SiteId, Peppers, null, T, AlertKind.Uncalibrated)],
            state.PendingAlertDeliveries);

        // It counts apart from the Threshold episodes and touches no Threshold state.
        Assert.Equal((0, (SensorOpenAlert?)null), (state.Episode, state.OpenAlert));

        // The same episode again, and a second one while the first is open, are not new Alerts.
        state.Apply(new SensorUncalibratedEpisodeOpened(1, first, SiteId, Peppers, 1, T));
        state.Apply(new SensorUncalibratedEpisodeOpened(2, AlertIds.Uncalibrated(SensorId, 2), SiteId, Beans, 2, T));
        Assert.Equal(1, state.UncalibratedEpisode);
        Assert.Single(state.PendingAlertDeliveries);

        // A close of another Alert changes nothing; the close of this one is queued behind its open.
        state.Apply(new SensorUncalibratedEpisodeClosed(2, AlertIds.Uncalibrated(SensorId, 2), AlertCloseReason.Calibrated, T.AddHours(1)));
        Assert.NotNull(state.OpenUncalibratedAlert);

        state.Apply(new SensorUncalibratedEpisodeClosed(1, first, AlertCloseReason.Calibrated, T.AddHours(1)));
        Assert.Null(state.OpenUncalibratedAlert);
        Assert.Equal(
            [
                new PendingAlertDelivery(first, AlertLifecycle.Open, 1, null, SiteId, Peppers, null, T, AlertKind.Uncalibrated),
                new PendingAlertDelivery(first, AlertLifecycle.Closed, 1, null, null, null, AlertCloseReason.Calibrated, T.AddHours(1), AlertKind.Uncalibrated),
            ],
            state.PendingAlertDeliveries);

        state.Apply(new SensorAlertDelivered(first, AlertLifecycle.Open, T.AddHours(1)));
        state.Apply(new SensorAlertDelivered(first, AlertLifecycle.Closed, T.AddHours(1)));
        Assert.Empty(state.PendingAlertDeliveries);

        // The next episode is another Alert.
        var second = AlertIds.Uncalibrated(SensorId, 2);
        state.Apply(new SensorUncalibratedEpisodeOpened(2, second, SiteId, Beans, 2, T.AddHours(2)));
        Assert.Equal((2, new SensorUncalibratedAlert(second, 2, Beans)), (state.UncalibratedEpisode, state.OpenUncalibratedAlert));
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void AThresholdDeliveryKeepsItsSideAndItsKind()
    {
        var state = Declared(Soil);
        var alertId = AlertIds.Threshold(SensorId, 1);

        state.Apply(new SensorThresholdEpisodeOpened(1, alertId, ThresholdSide.Low, SiteId, Peppers, 20, 30, 1, T, T));

        var delivery = Assert.Single(state.PendingAlertDeliveries);
        Assert.Equal((AlertKind.Threshold, (ThresholdSide?)ThresholdSide.Low), (delivery.Kind, delivery.Side));
    }

    [Fact]
    public void TheAlertIdIsTheUuidV5OfTheSensorTheKindAndTheEpisode()
    {
        Assert.Equal("sensor:dac4e7fe-93b1-56fc-a363-51235a586394:uncalibrated:3", AlertIds.UncalibratedName(SensorId, 3));
        Assert.Equal(Coldframe.Crypto.SensorIds.UuidV5(AlertIds.Namespace, AlertIds.UncalibratedName(SensorId, 3)), AlertIds.Uncalibrated(SensorId, 3));
        Assert.NotEqual(AlertIds.Threshold(SensorId, 3), AlertIds.Uncalibrated(SensorId, 3));
    }

    // The rule, row by row: it opens exactly while the Sensor needs calibration on a Lot and is not paused.
    [Fact]
    public void TheAlertOpensOnlyForASensorThatNeedsCalibrationOnALotAndIsNotPaused()
    {
        var onPeppers = new SensorContext(SiteId, Peppers, false, 1);

        Assert.Equal(new UncalibratedStep(null, Opens: true), UncalibratedRule.Decide(onPeppers, Soil, calibrated: false, null));

        // Already calibrated, no calibration required, never declared.
        Assert.Equal(UncalibratedStep.None, UncalibratedRule.Decide(onPeppers, Soil, calibrated: true, null));
        Assert.Equal(UncalibratedStep.None, UncalibratedRule.Decide(onPeppers, Air, calibrated: false, null));
        Assert.Equal(UncalibratedStep.None, UncalibratedRule.Decide(onPeppers, null, calibrated: false, null));

        // On no Lot, paused, or nobody said.
        Assert.Equal(UncalibratedStep.None, UncalibratedRule.Decide(new SensorContext(SiteId, null, false, 1), Soil, false, null));
        Assert.Equal(UncalibratedStep.None, UncalibratedRule.Decide(new SensorContext(SiteId, Peppers, true, 1), Soil, false, null));
        Assert.Equal(UncalibratedStep.None, UncalibratedRule.Decide(null, Soil, false, null));
    }

    [Fact]
    public void AnOpenAlertStaysWhileNothingChanges()
    {
        Assert.Equal(UncalibratedStep.None, UncalibratedRule.Decide(new SensorContext(SiteId, Peppers, false, 1), Soil, false, Peppers));

        // The same Lot under a newer epoch (unassigned and assigned again, the first context lost): still true.
        Assert.Equal(UncalibratedStep.None, UncalibratedRule.Decide(new SensorContext(SiteId, Peppers, false, 5), Soil, false, Peppers));
    }

    [Fact]
    public void AnOpenAlertClosesWithTheReasonOfWhatEndedIt()
    {
        var onPeppers = new SensorContext(SiteId, Peppers, false, 1);

        // A Calibration was saved.
        Assert.Equal(new UncalibratedStep(AlertCloseReason.Calibrated, Opens: false), UncalibratedRule.Decide(onPeppers, Soil, calibrated: true, Peppers));

        // The Specification stopped requiring calibration.
        Assert.Equal(new UncalibratedStep(AlertCloseReason.Removed, Opens: false), UncalibratedRule.Decide(onPeppers, Air, calibrated: false, Peppers));

        // The Node is on no Lot.
        Assert.Equal(
            new UncalibratedStep(AlertCloseReason.Unassigned, Opens: false),
            UncalibratedRule.Decide(new SensorContext(SiteId, null, false, 2), Soil, false, Peppers));

        // The Node is paused.
        Assert.Equal(
            new UncalibratedStep(AlertCloseReason.Paused, Opens: false),
            UncalibratedRule.Decide(new SensorContext(SiteId, Peppers, true, 2), Soil, false, Peppers));
    }

    [Fact]
    public void AMoveClosesTheAlertOfTheOldLotAndOpensOneForTheNew()
    {
        Assert.Equal(
            new UncalibratedStep(AlertCloseReason.Unassigned, Opens: true),
            UncalibratedRule.Decide(new SensorContext(SiteId, Beans, false, 2), Soil, false, Peppers));

        // Moved while paused: the old Alert closes, and none opens until the Node resumes.
        Assert.Equal(
            new UncalibratedStep(AlertCloseReason.Unassigned, Opens: false),
            UncalibratedRule.Decide(new SensorContext(SiteId, Beans, true, 2), Soil, false, Peppers));
    }

    private static SensorState Declared(SensorSpecification specification)
    {
        var state = new SensorState();
        state.Apply(new SensorDeclared("5a4b3c2d1e0f7c20", 0, specification, T));
        return state;
    }
}
