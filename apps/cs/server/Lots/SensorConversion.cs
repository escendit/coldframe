using Coldframe.Server.Sensors;

namespace Coldframe.Server.Lots;

/// <summary>
/// Converts a stored Reading value to the unit clients show (AD-14: units are the Server's business, clients
/// only format).
/// </summary>
/// <remarks>
/// Soil moisture is the raw count, unless the Reading was stored with a Calibration (Epic 5): then it is the
/// percentage derived from that Calibration's points, rounded to the nearest 5, and never a percentage otherwise.
/// Temperature (milli-°C) and humidity (milli-%) are divided by 1000, gas resistance (Ω) becomes kΩ.
/// </remarks>
public static class SensorConversion
{
    /// <summary>
    /// The quantity tokens of the contract's <c>SensorQuantity</c>, in the order the Lot detail lists Sensors.
    /// </summary>
    public static readonly IReadOnlyList<string> Quantities = ["soil_moisture", "air_temperature", "relative_humidity", "gas_resistance"];

    /// <summary>
    /// Returns the converted value and its contract unit, or <see langword="null"/> for a quantity the contract does not name.
    /// </summary>
    /// <param name="quantity">The quantity token.</param>
    /// <param name="rawValue">The stored value.</param>
    public static (double Value, string Unit)? Convert(string quantity, long rawValue) => Convert(quantity, rawValue, calibration: null);

    /// <summary>
    /// Returns the converted value and its contract unit, or <see langword="null"/> for a quantity the contract
    /// does not name. A soil-moisture Reading with <paramref name="calibration"/> is a percentage.
    /// </summary>
    /// <param name="quantity">The quantity token.</param>
    /// <param name="rawValue">The stored value.</param>
    /// <param name="calibration">The points of the Calibration the Reading was stored with, if any.</param>
    public static (double Value, string Unit)? Convert(string quantity, long rawValue, CalibrationPoints? calibration) => quantity switch
    {
        "soil_moisture" when calibration is not null => (CalibrationMath.Percent(rawValue, calibration), "%"),
        "soil_moisture" => (rawValue, "raw"),
        "air_temperature" => ((double)(rawValue / 1000m), "°C"),
        "relative_humidity" => ((double)(rawValue / 1000m), "%"),
        "gas_resistance" => ((double)(rawValue / 1000m), "kΩ"),
        _ => null,
    };
}
