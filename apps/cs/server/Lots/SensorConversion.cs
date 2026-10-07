namespace Coldframe.Server.Lots;

/// <summary>
/// Converts a stored Reading value to the unit clients show (AD-14: units are the Server's business, clients
/// only format).
/// </summary>
/// <remarks>
/// Soil moisture stays the raw count until Calibration exists (Epic 5): no percentage is ever produced here.
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
    public static (double Value, string Unit)? Convert(string quantity, long rawValue) => quantity switch
    {
        "soil_moisture" => (rawValue, "raw"),
        "air_temperature" => ((double)(rawValue / 1000m), "°C"),
        "relative_humidity" => ((double)(rawValue / 1000m), "%"),
        "gas_resistance" => ((double)(rawValue / 1000m), "kΩ"),
        _ => null,
    };
}
