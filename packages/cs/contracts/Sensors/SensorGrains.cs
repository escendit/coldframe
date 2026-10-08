namespace Coldframe.Contracts.Sensors;

/// <summary>
/// A Sensor, keyed by its Sensor ID (AD-19): the UUIDv5 of its Device ID, slot and quantity, in the
/// lowercase hyphenated form. The only writer of the Sensor's state: its Specification, its Thresholds and its
/// Calibration.
/// </summary>
[Alias("coldframe.sensor")]
public interface ISensorGrain : IGrainWithStringKey
{
    /// <summary>
    /// Declares the Sensor with the Specification its Node sent (AD-19). The first declaration journals
    /// <see cref="SensorDeclared"/> with both Thresholds following the Specification's defaults. The same
    /// Specification again journals nothing. Another one journals <see cref="SensorSpecificationChanged"/>:
    /// it replaces the Specification only, never a Threshold's kind or an override's value. Only the Device
    /// grain calls it.
    /// </summary>
    /// <param name="request">The Sensor's Device, slot and Specification.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="ArgumentException">
    /// The request does not derive this grain's Sensor ID. Nothing is journaled.
    /// </exception>
    [Alias("declare")]
#pragma warning disable CA1716 // "Declare" is the domain's word (AD-19); only Visual Basic reserves it, and no grain is written in it.
    Task<SensorDeclarationResult> Declare(DeclareSensor request, CancellationToken cancellationToken = default);
#pragma warning restore CA1716

    /// <summary>
    /// Returns the Sensor with its Specification and each Threshold's kind and effective value, or
    /// <see langword="null"/> when it was never declared.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("describe")]
    Task<SensorSnapshot?> Describe(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves one or both reference points of the Sensor's Calibration (Story 5.1, AD-9). The Sensor grain is the
    /// only validator and writer of a Calibration. Every point names a stored Reading of this Sensor by its
    /// <c>reading_seq</c>. One point is kept (<see cref="SensorCalibrationPointRecorded"/>) until the other one
    /// arrives, and the Sensor stays as calibrated as it was. Two points, new or pending, that are distinct
    /// (see <see cref="SensorCalibrationLimits.MinimumSpan"/>) journal <see cref="SensorCalibrated"/> with a new
    /// Calibration ID. Then the Calibration is set in force on the Device grain, and only when the Device
    /// acknowledged it the answer is <see cref="SensorCalibrationOutcome.Calibrated"/>. When that call fails the
    /// event stays journaled and is delivered again, on a timer and on activation, until the Device holds it; the
    /// answer is <see cref="SensorCalibrationOutcome.NotDelivered"/> then. A refusal journals nothing.
    /// </summary>
    /// <param name="request">The points to save.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("calibrate")]
    Task<SensorCalibrationResult> Calibrate(CalibrateSensor request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the Sensor's Thresholds with the low the Server proposes, or <see langword="null"/> when the Sensor
    /// was never declared (Story 5.3).
    /// </summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("get-thresholds")]
    Task<SensorThresholds?> GetThresholds(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets one or both sides of the Sensor's Thresholds (Story 5.3, AD-19). The Sensor grain is the only
    /// validator and writer. A side that is <see langword="null"/> stays as it is. On the effective values after
    /// the change: a high needs a low (a Sensor alerts exactly when its effective low exists), the low must be
    /// strictly below the high, and an empty high never alerts. A calibrating Sensor takes whole percent 0 to 100,
    /// any other Sensor values within its Specification's range, in the Specification's unit. A change journals
    /// <see cref="SensorThresholdsChanged"/>, a new evaluation epoch for Story 6.1; a request that leaves both
    /// sides as they are, and a refusal, journal nothing.
    /// </summary>
    /// <param name="request">The sides to set.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("set-thresholds")]
    Task<SetSensorThresholdsResult> SetThresholds(SetSensorThresholds request, CancellationToken cancellationToken = default);
}

/// <summary>
/// The unit of a Sensor's raw Reading value; the contract's <c>Unit</c>.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.sensor-unit")]
public enum SensorUnit
{
    /// <summary>
    /// A raw count, such as the mean ADC count of the soil probe.
    /// </summary>
    RawCount = 0,

    /// <summary>
    /// Milli-degrees Celsius.
    /// </summary>
    MilliDegreeCelsius = 1,

    /// <summary>
    /// Milli-percent.
    /// </summary>
    MilliPercent = 2,

    /// <summary>
    /// Ohms.
    /// </summary>
    Ohm = 3,
}

/// <summary>
/// What a Node declares about one Sensor (AD-19, FR-3).
/// </summary>
/// <param name="Quantity">
/// What the Sensor measures: its Sensor ID token, such as <c>soil_moisture</c> (<c>packages/crypto-spec</c>).
/// </param>
/// <param name="Unit">The unit of the raw Reading value.</param>
/// <param name="RangeMin">The lowest value the Sensor reports, in <paramref name="Unit"/>.</param>
/// <param name="RangeMax">The highest value the Sensor reports, in <paramref name="Unit"/>.</param>
/// <param name="Calibration">Whether two-point Calibration applies to the Sensor.</param>
/// <param name="DefaultLow">
/// The default low Threshold, or <see langword="null"/>: in percent when <paramref name="Calibration"/> is
/// set, otherwise in <paramref name="Unit"/>.
/// </param>
/// <param name="DefaultHigh">The default high Threshold, or <see langword="null"/>; in the unit of <paramref name="DefaultLow"/>.</param>
[GenerateSerializer]
[Alias("coldframe.sensor-specification")]
public sealed record SensorSpecification(
    [property: Id(0)] string Quantity,
    [property: Id(1)] SensorUnit Unit,
    [property: Id(2)] long RangeMin,
    [property: Id(3)] long RangeMax,
    [property: Id(4)] bool Calibration,
    [property: Id(5)] long? DefaultLow = null,
    [property: Id(6)] long? DefaultHigh = null);

/// <summary>
/// How one side of a Sensor's Thresholds is set (AD-19).
/// </summary>
[GenerateSerializer]
[Alias("coldframe.threshold-kind")]
public enum ThresholdKind
{
    /// <summary>
    /// The side follows the Specification's default, which may be absent.
    /// </summary>
    Default = 0,

    /// <summary>
    /// The side has its own value; a declaration never replaces it.
    /// </summary>
    Override = 1,

    /// <summary>
    /// The side has no Threshold, whatever the Specification's default.
    /// </summary>
    Cleared = 2,
}

/// <summary>
/// One side of a Sensor's Thresholds.
/// </summary>
/// <param name="Kind">How the side is set.</param>
/// <param name="Value">
/// In <see cref="SensorThresholdsChanged"/>: the value of an <see cref="ThresholdKind.Override"/>, otherwise
/// <see langword="null"/>. In a <see cref="SensorSnapshot"/>: the effective Threshold, which for
/// <see cref="ThresholdKind.Default"/> is the Specification's default; <see langword="null"/> when the side
/// has none.
/// </param>
[GenerateSerializer]
[Alias("coldframe.threshold-setting")]
public sealed record ThresholdSetting(
    [property: Id(0)] ThresholdKind Kind,
    [property: Id(1)] long? Value = null)
{
    /// <summary>
    /// A side that follows the Specification's default.
    /// </summary>
    public static ThresholdSetting Default { get; } = new(ThresholdKind.Default);
}

/// <summary>
/// A declaration of one Sensor as the Device grain hands it to the Sensor grain.
/// </summary>
/// <param name="DeviceId">The Device ID of the Sensor's Node, 16 lowercase hex digits.</param>
/// <param name="Slot">The Sensor's slot: its index in the Node's Specification set.</param>
/// <param name="Specification">What the Node declared.</param>
[GenerateSerializer]
[Alias("coldframe.declare-sensor")]
public sealed record DeclareSensor(
    [property: Id(0)] string DeviceId,
    [property: Id(1)] int Slot,
    [property: Id(2)] SensorSpecification Specification);

/// <summary>
/// How a declaration ended.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.sensor-declaration-outcome")]
public enum SensorDeclarationOutcome
{
    /// <summary>
    /// The Sensor was declared for the first time.
    /// </summary>
    Declared = 0,

    /// <summary>
    /// The Sensor has this Specification already. Nothing was journaled.
    /// </summary>
    Unchanged = 1,

    /// <summary>
    /// The Sensor's Specification was replaced; its Thresholds kept their kinds and overrides.
    /// </summary>
    SpecificationChanged = 2,
}

/// <summary>
/// The result of <see cref="ISensorGrain.Declare"/>.
/// </summary>
/// <param name="Outcome">How the declaration ended.</param>
[GenerateSerializer]
[Alias("coldframe.sensor-declaration-result")]
public sealed record SensorDeclarationResult([property: Id(0)] SensorDeclarationOutcome Outcome);

/// <summary>
/// A declared Sensor as the grain holds it.
/// </summary>
/// <param name="Id">The Sensor ID.</param>
/// <param name="DeviceId">The Device ID of the Sensor's Node.</param>
/// <param name="Slot">The Sensor's slot.</param>
/// <param name="Specification">The Specification in force.</param>
/// <param name="Low">The low Threshold: its kind and effective value.</param>
/// <param name="High">The high Threshold: its kind and effective value.</param>
/// <param name="Calibration">The Calibration in force, or <see langword="null"/> while the Sensor is uncalibrated.</param>
/// <param name="PendingDryRaw">The dry point kept while waiting for the wet one.</param>
/// <param name="PendingWetRaw">The wet point kept while waiting for the dry one.</param>
[GenerateSerializer]
[Alias("coldframe.sensor-snapshot")]
public sealed record SensorSnapshot(
    [property: Id(0)] string Id,
    [property: Id(1)] string DeviceId,
    [property: Id(2)] int Slot,
    [property: Id(3)] SensorSpecification Specification,
    [property: Id(4)] ThresholdSetting Low,
    [property: Id(5)] ThresholdSetting High,
    [property: Id(6)] SensorCalibration? Calibration = null,
    [property: Id(7)] long? PendingDryRaw = null,
    [property: Id(8)] long? PendingWetRaw = null);

/// <summary>
/// Which reference point of a Calibration.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.calibration-point")]
public enum CalibrationPoint
{
    /// <summary>
    /// The probe in dry soil: it reads 0 percent.
    /// </summary>
    Dry = 0,

    /// <summary>
    /// The probe in water: it reads 100 percent.
    /// </summary>
    Wet = 1,
}

/// <summary>
/// The bounds of a Calibration (Story 5.1).
/// </summary>
public static class SensorCalibrationLimits
{
    /// <summary>
    /// The smallest distance between the dry and the wet raw value: two points closer than this (or equal) are
    /// indistinct and refused. A span this narrow could not tell dry from wet through the probe's noise.
    /// </summary>
    public const long MinimumSpan = 16;
}

/// <summary>
/// One reference point of a Calibration, named by the stored Reading it comes from.
/// </summary>
/// <param name="ReadingSeq">The <c>reading_seq</c> of a stored Reading of the Sensor.</param>
[GenerateSerializer]
[Alias("coldframe.calibration-point-ref")]
public sealed record CalibrationPointRef([property: Id(0)] ulong ReadingSeq);

/// <summary>
/// The points of a Calibration an Administrator saves; at least one is set.
/// </summary>
/// <param name="Dry">The dry point, or <see langword="null"/> to keep the one already held.</param>
/// <param name="Wet">The wet point, or <see langword="null"/> to keep the one already held.</param>
[GenerateSerializer]
[Alias("coldframe.calibrate-sensor")]
public sealed record CalibrateSensor(
    [property: Id(0)] CalibrationPointRef? Dry = null,
    [property: Id(1)] CalibrationPointRef? Wet = null);

/// <summary>
/// The Calibration in force: both raw values and the ID every Reading stored under it carries.
/// </summary>
/// <param name="Id">The Calibration ID.</param>
/// <param name="Revision">1 for the first Calibration of the Sensor, then one more each time; only the highest is in force.</param>
/// <param name="DryRaw">The raw value that reads 0 percent.</param>
/// <param name="WetRaw">The raw value that reads 100 percent.</param>
/// <param name="CalibratedAt">When it was saved.</param>
[GenerateSerializer]
[Alias("coldframe.sensor-calibration")]
public sealed record SensorCalibration(
    [property: Id(0)] Guid Id,
    [property: Id(1)] int Revision,
    [property: Id(2)] long DryRaw,
    [property: Id(3)] long WetRaw,
    [property: Id(4)] DateTimeOffset CalibratedAt);

/// <summary>
/// How <see cref="ISensorGrain.Calibrate"/> ended.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.sensor-calibration-outcome")]
public enum SensorCalibrationOutcome
{
    /// <summary>
    /// The Sensor is calibrated and the Device holds the Calibration.
    /// </summary>
    Calibrated = 0,

    /// <summary>
    /// One point was kept; the other one is still missing. The Sensor stays as calibrated as it was.
    /// </summary>
    PointRecorded = 1,

    /// <summary>
    /// The Sensor was never declared, or its Specification has no Calibration. Nothing was journaled.
    /// </summary>
    NotCalibratable = 2,

    /// <summary>
    /// The request names no point. Nothing was journaled.
    /// </summary>
    NoPoint = 3,

    /// <summary>
    /// A point names a <c>reading_seq</c> the Sensor has no stored Reading for. Nothing was journaled.
    /// </summary>
    UnknownReading = 4,

    /// <summary>
    /// The dry and the wet raw value are equal or closer than <see cref="SensorCalibrationLimits.MinimumSpan"/>.
    /// Nothing was journaled.
    /// </summary>
    IndistinctPoints = 5,

    /// <summary>
    /// The Calibration is saved, but the Device did not acknowledge it yet; it is delivered again until it does.
    /// </summary>
    NotDelivered = 6,
}

/// <summary>
/// The result of <see cref="ISensorGrain.Calibrate"/>.
/// </summary>
/// <param name="Outcome">How the call ended.</param>
/// <param name="Calibration">
/// The Calibration saved or, for an unchanged request, the one in force; <see langword="null"/> when the Sensor
/// has none or the call was refused.
/// </param>
/// <param name="PendingDryRaw">The dry point kept while waiting for the wet one.</param>
/// <param name="PendingWetRaw">The wet point kept while waiting for the dry one.</param>
[GenerateSerializer]
[Alias("coldframe.sensor-calibration-result")]
public sealed record SensorCalibrationResult(
    [property: Id(0)] SensorCalibrationOutcome Outcome,
    [property: Id(1)] SensorCalibration? Calibration = null,
    [property: Id(2)] long? PendingDryRaw = null,
    [property: Id(3)] long? PendingWetRaw = null);

/// <summary>
/// The Thresholds an Administrator sets; a side that is <see langword="null"/> stays as it is (Story 5.3).
/// </summary>
/// <param name="Low">The low side from now on: <see cref="ThresholdKind.Override"/> with a value, or a kind without one.</param>
/// <param name="High">The high side from now on.</param>
[GenerateSerializer]
[Alias("coldframe.set-sensor-thresholds")]
public sealed record SetSensorThresholds(
    [property: Id(0)] ThresholdSetting? Low = null,
    [property: Id(1)] ThresholdSetting? High = null);

/// <summary>
/// A Sensor's Thresholds as the grain holds them (Story 5.3).
/// </summary>
/// <param name="Specification">The Specification in force.</param>
/// <param name="Low">The low side: its kind and effective value, in the Specification's unit (percent for a calibrating Sensor).</param>
/// <param name="High">The high side: its kind and effective value.</param>
/// <param name="ProposedLow">
/// The low the Server proposes, or <see langword="null"/> when the Specification has a default low. It is
/// <c>Min + 20 % x (Max - Min)</c> of the range (20 percent for a calibrating Sensor), a read-time convenience
/// that is never journaled until an Administrator saves it. There is never a proposed high.
/// </param>
[GenerateSerializer]
[Alias("coldframe.sensor-thresholds")]
public sealed record SensorThresholds(
    [property: Id(0)] SensorSpecification Specification,
    [property: Id(1)] ThresholdSetting Low,
    [property: Id(2)] ThresholdSetting High,
    [property: Id(3)] long? ProposedLow = null);

/// <summary>
/// How <see cref="ISensorGrain.SetThresholds"/> ended.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.sensor-thresholds-outcome")]
public enum SensorThresholdsOutcome
{
    /// <summary>
    /// The Thresholds changed and <see cref="SensorThresholdsChanged"/> was journaled.
    /// </summary>
    Changed = 0,

    /// <summary>
    /// The request leaves both sides as they are. Nothing was journaled.
    /// </summary>
    Unchanged = 1,

    /// <summary>
    /// The Sensor was never declared. Nothing was journaled.
    /// </summary>
    NotDeclared = 2,

    /// <summary>
    /// A side is not <c>Default</c>, <c>Override</c> with a value or <c>Cleared</c>. Nothing was journaled.
    /// </summary>
    MalformedSide = 3,

    /// <summary>
    /// An override is outside 0 to 100 (a calibrating Sensor) or outside the Specification's range. Nothing was journaled.
    /// </summary>
    OutOfRange = 4,

    /// <summary>
    /// The Thresholds would have a high and no low. Nothing was journaled.
    /// </summary>
    LowRequired = 5,

    /// <summary>
    /// The low would not be strictly below the high. Nothing was journaled.
    /// </summary>
    LowNotBelowHigh = 6,
}

/// <summary>
/// The result of <see cref="ISensorGrain.SetThresholds"/>.
/// </summary>
/// <param name="Outcome">How the call ended.</param>
/// <param name="Thresholds">The Thresholds in force after the call; <see langword="null"/> for a Sensor never declared.</param>
[GenerateSerializer]
[Alias("coldframe.set-sensor-thresholds-result")]
public sealed record SetSensorThresholdsResult(
    [property: Id(0)] SensorThresholdsOutcome Outcome,
    [property: Id(1)] SensorThresholds? Thresholds = null);
