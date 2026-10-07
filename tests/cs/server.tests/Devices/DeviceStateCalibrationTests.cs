using Coldframe.Contracts.Devices;
using Coldframe.Server.Devices;

namespace Coldframe.Server.Tests.Devices;

/// <summary>
/// The Device grain's cache of the Calibration in force per Sensor (Story 5.1, AD-9): it only remembers which
/// Calibration ID to stamp, and a revision that is not above the one held changes nothing.
/// </summary>
public sealed class DeviceStateCalibrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid Soil = Guid.Parse("dac4e7fe-93b1-56fc-a363-51235a586394");
    private static readonly Guid Other = Guid.Parse("e62a2dbe-b439-5906-a842-35abfb36458a");

    private static readonly Guid One = Guid.Parse("0192f3a4-9000-7000-8000-000000000001");
    private static readonly Guid Two = Guid.Parse("0192f3a4-9000-7000-8000-000000000002");

    [Fact]
    public void ASensorWithoutCalibrationHasNoneInForce()
    {
        var state = new DeviceState();

        Assert.Null(state.CalibrationOf(Soil));
        Assert.False(state.HoldsCalibration(Soil, 1));
    }

    [Fact]
    public void ACalibrationInForceIsKeptPerSensor()
    {
        var state = new DeviceState();

        state.Apply(new DeviceCalibrationSet(Soil, One, 1, Now));

        Assert.Equal(One, state.CalibrationOf(Soil));
        Assert.Null(state.CalibrationOf(Other));
        Assert.True(state.HoldsCalibration(Soil, 1));
        Assert.False(state.HoldsCalibration(Soil, 2));
    }

    [Fact]
    public void ANewerRevisionReplacesTheCalibrationAndAnOlderOneNeverDoes()
    {
        var state = new DeviceState();
        state.Apply(new DeviceCalibrationSet(Soil, One, 1, Now));

        state.Apply(new DeviceCalibrationSet(Soil, Two, 2, Now));
        Assert.Equal(Two, state.CalibrationOf(Soil));

        // A redelivery of the first one, arriving late.
        state.Apply(new DeviceCalibrationSet(Soil, One, 1, Now));
        Assert.Equal(Two, state.CalibrationOf(Soil));
        Assert.True(state.HoldsCalibration(Soil, 1));
        Assert.True(state.HoldsCalibration(Soil, 2));
    }
}
