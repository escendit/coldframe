using Coldframe.Contracts.Events;

namespace Coldframe.Contracts.Sensors;

/// <summary>
/// The Sensor's Node declared it for the first time (AD-19). The first event of every <c>sensor/{id}</c>
/// stream. Both Thresholds follow the Specification's defaults from here on.
/// </summary>
/// <param name="DeviceId">The Device ID of the Sensor's Node.</param>
/// <param name="Slot">The Sensor's slot: its index in the Node's Specification set.</param>
/// <param name="Specification">What the Node declared.</param>
/// <param name="DeclaredAt">When the Server accepted the declaration.</param>
[EventType("sensor.declared")]
[GenerateSerializer]
[Alias("coldframe.sensor-declared")]
public sealed record SensorDeclared(
    [property: Id(0)] string DeviceId,
    [property: Id(1)] int Slot,
    [property: Id(2)] SensorSpecification Specification,
    [property: Id(3)] DateTimeOffset DeclaredAt);

/// <summary>
/// The Node declared the Sensor with another Specification (AD-19). It replaces the Specification only: a
/// Threshold that follows the default follows the new one, an override keeps its value, and a cleared side
/// stays cleared.
/// </summary>
/// <param name="Specification">The Specification from now on.</param>
/// <param name="ChangedAt">When the Server accepted the declaration.</param>
[EventType("sensor.specification-changed")]
[GenerateSerializer]
[Alias("coldframe.sensor-specification-changed")]
public sealed record SensorSpecificationChanged(
    [property: Id(0)] SensorSpecification Specification,
    [property: Id(1)] DateTimeOffset ChangedAt);

/// <summary>
/// The Sensor's Thresholds were set (AD-19): each side is <see cref="ThresholdKind.Default"/>,
/// <see cref="ThresholdKind.Override"/> with its value, or <see cref="ThresholdKind.Cleared"/>. Setting
/// Thresholds arrives with Story 5.3; until then only fixtures and tests write it.
/// </summary>
/// <param name="Low">The low side from now on.</param>
/// <param name="High">The high side from now on.</param>
/// <param name="ChangedAt">When the Thresholds were set.</param>
[EventType("sensor.thresholds-changed")]
[GenerateSerializer]
[Alias("coldframe.sensor-thresholds-changed")]
public sealed record SensorThresholdsChanged(
    [property: Id(0)] ThresholdSetting Low,
    [property: Id(1)] ThresholdSetting High,
    [property: Id(2)] DateTimeOffset ChangedAt);
