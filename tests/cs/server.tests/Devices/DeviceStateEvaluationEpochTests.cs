using Coldframe.Contracts.Devices;
using Coldframe.Server.Devices;

namespace Coldframe.Server.Tests.Devices;

/// <summary>
/// The evaluation epoch of a Node (Story 6.1): derived from its assignment and Pause events, so a Sensor's
/// streak never runs across a move, an unassignment or a Pause.
/// </summary>
public sealed class DeviceStateEvaluationEpochTests
{
    private const string SiteId = "0192f3a4-7c1e-7d2b-9a51-3f7e2c9b1d00";

    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EveryAssignmentAndPauseEventStartsANewEpoch()
    {
        var state = new DeviceState();
        Assert.Equal(0, state.EvaluationEpoch);

        object[] events =
        [
            new DeviceAssigned(SiteId, "lot-a", Now),
            new DeviceMoved(SiteId, "lot-a", "lot-b", Now),
            new DevicePaused(DevicePauseSource.Device, null, Now),
            new DeviceResumed(DevicePauseSource.Device, Now),
            new DeviceUnassigned(SiteId, "lot-b", Now),
        ];

        var epochs = new List<long>();
        foreach (var @event in events)
        {
            ((dynamic)state).Apply((dynamic)@event);
            epochs.Add(state.EvaluationEpoch);
        }

        Assert.Equal([1, 2, 3, 4, 5], epochs);
    }

    [Fact]
    public void OtherEventsKeepTheEpoch()
    {
        var state = new DeviceState();
        state.Apply(new DeviceAssigned(SiteId, "lot-a", Now));

        state.Apply(new DeviceSeen(Now, 1, null));
        state.Apply(new DeviceRelayChanged("92064422c012f481", Now));
        state.Apply(new DeviceLotReleased("lot-z", Now));
        state.Apply(new DeviceSpecificationsDeclared([1, 2, 3], [], Now));
        state.Apply(new DeviceCalibrationSet(Guid.NewGuid(), Guid.NewGuid(), 1, Now));

        Assert.Equal(1, state.EvaluationEpoch);
    }
}
