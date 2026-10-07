using Coldframe.Contracts.Sensors;
using Coldframe.Server.Sensors;

namespace Coldframe.Server.Tests.Sensors;

/// <summary>
/// The Sensor's Calibration state (Story 5.1): a point is kept until the other one arrives, a Calibration
/// replaces the one in force and clears the pending points, and delivery is tracked on the stream.
/// </summary>
public sealed class SensorStateCalibrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private static readonly SensorSpecification Soil = new("soil_moisture", SensorUnit.RawCount, 0, 4095, true, 30, 80);

    private static readonly Guid First = Guid.Parse("0192f3a4-9000-7000-8000-000000000001");
    private static readonly Guid Second = Guid.Parse("0192f3a4-9000-7000-8000-000000000002");

    private static SensorState Declared()
    {
        var state = new SensorState();
        state.Apply(new SensorDeclared("5a4b3c2d1e0f7c20", 0, Soil, Now));
        return state;
    }

    [Fact]
    public void ADeclaredSensorIsUncalibratedWithoutPendingPoints()
    {
        var state = Declared();

        Assert.Null(state.Calibration);
        Assert.False(state.Calibrated);
        Assert.Equal((null, null), (state.PendingDryRaw, state.PendingWetRaw));
        Assert.False(state.DeliveryPending);
    }

    [Fact]
    public void OnePointIsKeptAndTheSensorStaysUncalibrated()
    {
        var state = Declared();

        state.Apply(new SensorCalibrationPointRecorded(CalibrationPoint.Dry, 7, 3000, Now));

        Assert.Equal((3000L, (long?)null), (state.PendingDryRaw, state.PendingWetRaw));
        Assert.Null(state.Calibration);
        Assert.False(state.Calibrated);
        Assert.False(state.DeliveryPending);
    }

    [Fact]
    public void ThePointsMayComeInEitherOrderAndTakingAPointAgainReplacesIt()
    {
        var state = Declared();

        state.Apply(new SensorCalibrationPointRecorded(CalibrationPoint.Wet, 9, 1200, Now));
        state.Apply(new SensorCalibrationPointRecorded(CalibrationPoint.Wet, 10, 1250, Now));

        Assert.Equal(((long?)null, (long?)1250), (state.PendingDryRaw, state.PendingWetRaw));
    }

    [Fact]
    public void ACalibrationIsInForceWithItsIdAndRevisionAndClearsThePendingPoints()
    {
        var state = Declared();
        state.Apply(new SensorCalibrationPointRecorded(CalibrationPoint.Dry, 7, 3000, Now));

        state.Apply(new SensorCalibrated(First, 3000, 1200, Now));

        Assert.Equal(new SensorCalibration(First, 1, 3000, 1200, Now), state.Calibration);
        Assert.True(state.Calibrated);
        Assert.Equal(((long?)null, (long?)null), (state.PendingDryRaw, state.PendingWetRaw));
        Assert.True(state.DeliveryPending);
    }

    [Fact]
    public void ARecalibrationReplacesTheCalibrationWithTheNextRevisionAndASingleNewPointKeepsItInForce()
    {
        var state = Declared();
        state.Apply(new SensorCalibrated(First, 3000, 1200, Now));

        // A point of a recalibration: the Calibration in force is not touched.
        state.Apply(new SensorCalibrationPointRecorded(CalibrationPoint.Dry, 20, 3100, Now));
        Assert.Equal(First, state.Calibration!.Id);
        Assert.Equal(3100L, state.PendingDryRaw);

        state.Apply(new SensorCalibrated(Second, 3100, 1100, Now.AddHours(1)));

        Assert.Equal(new SensorCalibration(Second, 2, 3100, 1100, Now.AddHours(1)), state.Calibration);
        Assert.Null(state.PendingDryRaw);
    }

    [Fact]
    public void DeliveryIsPendingUntilTheCurrentCalibrationIsDelivered()
    {
        var state = Declared();
        state.Apply(new SensorCalibrated(First, 3000, 1200, Now));
        Assert.True(state.DeliveryPending);

        // Delivering an older Calibration does not deliver the current one.
        state.Apply(new SensorCalibrated(Second, 3100, 1100, Now));
        state.Apply(new SensorCalibrationDelivered(First, Now));
        Assert.True(state.DeliveryPending);

        state.Apply(new SensorCalibrationDelivered(Second, Now));
        Assert.False(state.DeliveryPending);
        Assert.Equal(Second, state.DeliveredCalibrationId);
    }

    [Fact]
    public void ACalibratedSensorWithDeliveryDoneStaysCalibratedThroughASpecificationChange()
    {
        var state = Declared();
        state.Apply(new SensorCalibrated(First, 3000, 1200, Now));
        state.Apply(new SensorCalibrationDelivered(First, Now));

        state.Apply(new SensorSpecificationChanged(Soil with { RangeMax = 8191 }, Now));

        Assert.Equal(First, state.Calibration!.Id);
        Assert.False(state.DeliveryPending);
    }
}
