using Coldframe.Contracts.Sensors;

namespace Coldframe.Server.Sensors;

/// <summary>
/// The two reference points of a Calibration (Story 5.1), as stored: the raw values that read 0 and 100 percent.
/// </summary>
/// <param name="DryRaw">The raw value of the dry point: 0 percent.</param>
/// <param name="WetRaw">The raw value of the wet point: 100 percent.</param>
public sealed record CalibrationPoints(long DryRaw, long WetRaw);

/// <summary>
/// The arithmetic of a Calibration (Story 5.1): two-point linear, in either orientation of the raw values, the
/// result clamped to 0 to 100 and rounded to the nearest 5 percent (a half rounds up). Normalized values are
/// derived from the stored Calibration, never stored, so compensation can be added later without a migration.
/// </summary>
public static class CalibrationMath
{
    /// <summary>
    /// The step the displayed percentage is rounded to.
    /// </summary>
    public const int PercentStep = 5;

    /// <summary>
    /// Whether the two raw values are distinct enough for a Calibration: at least
    /// <see cref="SensorCalibrationLimits.MinimumSpan"/> apart. Equal values never are.
    /// </summary>
    /// <param name="dryRaw">The raw value of the dry point.</param>
    /// <param name="wetRaw">The raw value of the wet point.</param>
    public static bool AreDistinct(long dryRaw, long wetRaw) =>
        dryRaw != wetRaw && Math.Abs((decimal)dryRaw - wetRaw) >= SensorCalibrationLimits.MinimumSpan;

    /// <summary>
    /// The percentage of a raw value under a Calibration: linear between the points, clamped to 0 to 100 and
    /// rounded to the nearest 5.
    /// </summary>
    /// <param name="rawValue">The Reading's raw value.</param>
    /// <param name="dryRaw">The raw value of the dry point.</param>
    /// <param name="wetRaw">The raw value of the wet point.</param>
    /// <exception cref="ArgumentException">The points are equal, so no Calibration has them.</exception>
    public static int Percent(long rawValue, long dryRaw, long wetRaw)
    {
        if (dryRaw == wetRaw)
        {
            throw new ArgumentException("A Calibration needs distinct points.", nameof(wetRaw));
        }

        var percent = 100m * ((decimal)rawValue - dryRaw) / ((decimal)wetRaw - dryRaw);
        var clamped = Math.Clamp(percent, 0m, 100m);

        return (int)(Math.Round(clamped / PercentStep, MidpointRounding.AwayFromZero) * PercentStep);
    }

    /// <summary>
    /// The percentage of a raw value under <paramref name="points"/>.
    /// </summary>
    public static int Percent(long rawValue, CalibrationPoints points)
    {
        ArgumentNullException.ThrowIfNull(points);

        return Percent(rawValue, points.DryRaw, points.WetRaw);
    }
}
