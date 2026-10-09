using Coldframe.Contracts.Sensors;

namespace Coldframe.Server.Sensors;

/// <summary>
/// The unit a Sensor's Thresholds are shown in (AD-14): the grain stores the Specification's unit, the API and
/// the notifications speak display units.
/// </summary>
public static class SensorDisplay
{
    /// <summary>
    /// How many stored units make one display unit: a calibrating Sensor is percent already.
    /// </summary>
    public static decimal Factor(SensorSpecification specification)
    {
        ArgumentNullException.ThrowIfNull(specification);

        return specification.Calibration ? 1m : specification.Unit switch
        {
            SensorUnit.MilliDegreeCelsius or SensorUnit.MilliPercent or SensorUnit.Ohm => 1000m,
            _ => 1m,
        };
    }

    /// <summary>
    /// The display unit: <c>%</c> for a calibrating Sensor, otherwise <c>°C</c>, <c>%</c>, <c>kΩ</c> or <c>raw</c>.
    /// </summary>
    public static string Unit(SensorSpecification specification)
    {
        ArgumentNullException.ThrowIfNull(specification);

        return specification.Calibration ? "%" : specification.Unit switch
        {
            SensorUnit.MilliDegreeCelsius => "°C",
            SensorUnit.MilliPercent => "%",
            SensorUnit.Ohm => "kΩ",
            _ => "raw",
        };
    }
}
