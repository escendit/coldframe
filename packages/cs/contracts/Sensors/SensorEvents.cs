using Coldframe.Contracts.Alerts;
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
/// changes. It resets the Sensor's evaluation streak (Story 6.1); an open Alert stays open.
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

/// <summary>
/// The Sensor's evaluation streak changed (Story 6.1): a Reading continued it, started one on another side, or
/// reset it. Journaled only when the streak changes, never for a steady Reading (the journal replays in full).
/// </summary>
/// <param name="Position">Where the Readings of the streak are; <see cref="ThresholdPosition.Within"/> with a count of 0 is no streak.</param>
/// <param name="Count">How many consecutive Readings the streak has: 0, 1 or 2. The third opens or closes an Alert.</param>
/// <param name="Epoch">The evaluation epoch of the Node when the Reading was evaluated.</param>
/// <param name="MeasuredAt">When the Reading was taken; no Reading at or before it is evaluated again.</param>
[EventType("sensor.streak-changed")]
[GenerateSerializer]
[Alias("coldframe.sensor-streak-changed")]
public sealed record SensorStreakChanged(
    [property: Id(0)] ThresholdPosition Position,
    [property: Id(1)] int Count,
    [property: Id(2)] long Epoch,
    [property: Id(3)] DateTimeOffset MeasuredAt);

/// <summary>
/// Three consecutive Readings were beyond the same Threshold (Story 6.1): the Sensor's next episode, and with
/// it the Alert whose ID the episode derives. Journaled before the Alert grain is called, so a retry opens the
/// same Alert. From here on the Sensor delivers the open to the Alert grain until
/// <see cref="SensorAlertDelivered"/> is journaled.
/// </summary>
/// <param name="Episode">The episode: 1 for the Sensor's first, then one more each time.</param>
/// <param name="AlertId">The Alert ID: the UUIDv5 of <c>sensor:{sensorId}:threshold:{episode}</c>.</param>
/// <param name="Side">The Threshold that was crossed.</param>
/// <param name="SiteId">The Site of the Node when the episode opened.</param>
/// <param name="LotId">The Lot of the Node when the episode opened.</param>
/// <param name="Value">The third Reading as it was compared: percent for a calibrating Sensor, else the Specification's unit.</param>
/// <param name="Threshold">The Threshold it was compared with, in the unit of <paramref name="Value"/>.</param>
/// <param name="Epoch">The evaluation epoch of the Node when the Reading was evaluated.</param>
/// <param name="MeasuredAt">When the third Reading was taken.</param>
/// <param name="OpenedAt">When the Sensor journaled the episode; the Alert's opening time.</param>
[EventType("sensor.threshold-episode-opened")]
[GenerateSerializer]
[Alias("coldframe.sensor-threshold-episode-opened")]
public sealed record SensorThresholdEpisodeOpened(
    [property: Id(0)] int Episode,
    [property: Id(1)] Guid AlertId,
    [property: Id(2)] ThresholdSide Side,
    [property: Id(3)] string SiteId,
    [property: Id(4)] string LotId,
    [property: Id(5)] long Value,
    [property: Id(6)] long Threshold,
    [property: Id(7)] long Epoch,
    [property: Id(8)] DateTimeOffset MeasuredAt,
    [property: Id(9)] DateTimeOffset OpenedAt);

/// <summary>
/// The Sensor's open episode ended (Story 6.1): three consecutive Readings were back within the Thresholds, or
/// on the other side. Journaled before the Alert grain is called; the Sensor delivers the close until
/// <see cref="SensorAlertDelivered"/> is journaled.
/// </summary>
/// <param name="Episode">The episode that ended.</param>
/// <param name="AlertId">The Alert of that episode.</param>
/// <param name="Reason">Why the Alert closes.</param>
/// <param name="Epoch">The evaluation epoch of the Node when the episode ended.</param>
/// <param name="MeasuredAt">When the Reading that ended it was taken; <see langword="null"/> when no Reading did.</param>
/// <param name="ClosedAt">When the Sensor journaled the end; the Alert's closing time.</param>
[EventType("sensor.threshold-episode-closed")]
[GenerateSerializer]
[Alias("coldframe.sensor-threshold-episode-closed")]
public sealed record SensorThresholdEpisodeClosed(
    [property: Id(0)] int Episode,
    [property: Id(1)] Guid AlertId,
    [property: Id(2)] AlertCloseReason Reason,
    [property: Id(3)] long Epoch,
    [property: Id(4)] DateTimeOffset? MeasuredAt,
    [property: Id(5)] DateTimeOffset ClosedAt);

/// <summary>
/// The Alert grain holds an open or a close of the Sensor's Alert and its Site knows it (Story 6.1): the Sensor
/// stops delivering it.
/// </summary>
/// <param name="AlertId">The Alert.</param>
/// <param name="Change">
/// What was delivered: <see cref="AlertLifecycle.Open"/> that the Alert opened,
/// <see cref="AlertLifecycle.Closed"/> that it closed.
/// </param>
/// <param name="DeliveredAt">When the Alert grain acknowledged it.</param>
[EventType("sensor.alert-delivered")]
[GenerateSerializer]
[Alias("coldframe.sensor-alert-delivered")]
public sealed record SensorAlertDelivered(
    [property: Id(0)] Guid AlertId,
    [property: Id(1)] AlertLifecycle Change,
    [property: Id(2)] DateTimeOffset DeliveredAt);
