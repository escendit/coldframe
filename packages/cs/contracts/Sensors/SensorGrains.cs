namespace Coldframe.Contracts.Sensors;

/// <summary>
/// A Sensor, keyed by its Sensor ID (AD-19): the UUIDv5 of its Device ID, slot and quantity, in the
/// lowercase hyphenated form. The only writer of the Sensor's state: its Specification and its Thresholds.
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
[GenerateSerializer]
[Alias("coldframe.sensor-snapshot")]
public sealed record SensorSnapshot(
    [property: Id(0)] string Id,
    [property: Id(1)] string DeviceId,
    [property: Id(2)] int Slot,
    [property: Id(3)] SensorSpecification Specification,
    [property: Id(4)] ThresholdSetting Low,
    [property: Id(5)] ThresholdSetting High);
