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
/// <see cref="ThresholdKind.Override"/> with its value, or <see cref="ThresholdKind.Cleared"/>. The Sensor
/// grain journals it when an Administrator changes the Thresholds (Story 5.3) and only when a side really
/// changes; Story 6.1 reads it as a new evaluation epoch.
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

/// <summary>
/// An Administrator picked one reference point of a Calibration (Story 5.1): the raw value of a stored Reading,
/// dry (the probe in dry soil) or wet (in water). The point is kept until the other one arrives; the Sensor stays
/// uncalibrated, or keeps the Calibration in force while it is recalibrated. Taking the point again replaces it.
/// </summary>
/// <param name="Point">Which reference point it is.</param>
/// <param name="ReadingSeq">The <c>reading_seq</c> of the stored Reading the point was taken from.</param>
/// <param name="RawValue">The raw value of that Reading.</param>
/// <param name="RecordedAt">When the Server recorded the point.</param>
[EventType("sensor.calibration-point-recorded")]
[GenerateSerializer]
[Alias("coldframe.sensor-calibration-point-recorded")]
public sealed record SensorCalibrationPointRecorded(
    [property: Id(0)] CalibrationPoint Point,
    [property: Id(1)] ulong ReadingSeq,
    [property: Id(2)] long RawValue,
    [property: Id(3)] DateTimeOffset RecordedAt);

/// <summary>
/// The Sensor was calibrated (Story 5.1): both reference points are known, and this Calibration is the one in
/// force from now on. A Calibration is two-point linear, in either orientation of the raw values. Only Readings
/// stored later use it; the ones already stored keep theirs. It clears the pending points.
/// </summary>
/// <param name="CalibrationId">The new Calibration ID, stamped on every Reading stored under it.</param>
/// <param name="DryRaw">The raw value of the dry point, which reads 0 percent.</param>
/// <param name="WetRaw">The raw value of the wet point, which reads 100 percent.</param>
/// <param name="CalibratedAt">When the Server saved the Calibration.</param>
[EventType("sensor.calibrated")]
[GenerateSerializer]
[Alias("coldframe.sensor-calibrated")]
public sealed record SensorCalibrated(
    [property: Id(0)] Guid CalibrationId,
    [property: Id(1)] long DryRaw,
    [property: Id(2)] long WetRaw,
    [property: Id(3)] DateTimeOffset CalibratedAt);

/// <summary>
/// The Device grain acknowledged the Calibration as in force for the Sensor (AD-9): the Sensor stops delivering it.
/// Until this event is journaled, the Sensor delivers its newest Calibration again on activation and on a timer.
/// </summary>
/// <param name="CalibrationId">The Calibration the Device now holds.</param>
/// <param name="DeliveredAt">When the Device acknowledged it.</param>
[EventType("sensor.calibration-delivered")]
[GenerateSerializer]
[Alias("coldframe.sensor-calibration-delivered")]
public sealed record SensorCalibrationDelivered(
    [property: Id(0)] Guid CalibrationId,
    [property: Id(1)] DateTimeOffset DeliveredAt);
