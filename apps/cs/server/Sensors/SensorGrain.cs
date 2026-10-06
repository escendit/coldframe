using Coldframe.Contracts.Sensors;
using Coldframe.Crypto;
using Coldframe.Server.Journal;

namespace Coldframe.Server.Sensors;

/// <summary>
/// A Sensor, keyed by its Sensor ID. The only writer of the Sensor's state (AD-19): the Specification its
/// Node declared and its Thresholds, each side <c>Default | Override(value) | Cleared</c>. A declaration
/// replaces the Specification only. It evaluates nothing yet: no Reading reaches it before Epic 6.
/// </summary>
[GrainType("sensor")]
public sealed class SensorGrain : JournaledStreamGrain<SensorState>, ISensorGrain
{
    private string SensorId => this.GetPrimaryKeyString();

    /// <inheritdoc />
    public async Task<SensorDeclarationResult> Declare(DeclareSensor request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Specification);
        ArgumentException.ThrowIfNullOrEmpty(request.DeviceId);
        ArgumentException.ThrowIfNullOrEmpty(request.Specification.Quantity);

        // The Sensor ID is its Device, slot and quantity (AD-19): a request for another Sensor is refused.
        if (request.Slot < 0 || !Derives(request))
        {
            throw new ArgumentException("The request does not derive this Sensor ID.", nameof(request));
        }

        if (State.Specification is not { } current)
        {
            RaiseEvent(new SensorDeclared(request.DeviceId, request.Slot, request.Specification, Clock.GetUtcNow()));
            await ConfirmEvents();
            return new SensorDeclarationResult(SensorDeclarationOutcome.Declared);
        }

        if (current == request.Specification)
        {
            return new SensorDeclarationResult(SensorDeclarationOutcome.Unchanged);
        }

        RaiseEvent(new SensorSpecificationChanged(request.Specification, Clock.GetUtcNow()));
        await ConfirmEvents();
        return new SensorDeclarationResult(SensorDeclarationOutcome.SpecificationChanged);
    }

    /// <inheritdoc />
    public Task<SensorSnapshot?> Describe(CancellationToken cancellationToken = default) =>
        Task.FromResult(
            State.Specification is { } specification
                ? new SensorSnapshot(
                    SensorId,
                    State.DeviceId!,
                    State.Slot,
                    specification,
                    new ThresholdSetting(State.Low.Kind, State.EffectiveLow),
                    new ThresholdSetting(State.High.Kind, State.EffectiveHigh))
                : null);

    private bool Derives(DeclareSensor request)
    {
        DeviceId deviceId;
        try
        {
            deviceId = DeviceId.Parse(request.DeviceId);
        }
        catch (CryptoFailureException)
        {
            return false;
        }

        return string.Equals(
            SensorIds.Derive(deviceId, (uint)request.Slot, request.Specification.Quantity).ToString("D"),
            SensorId,
            StringComparison.Ordinal);
    }
}
