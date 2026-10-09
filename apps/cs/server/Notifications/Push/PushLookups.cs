using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Sensors;
using Coldframe.Server.Identity;
using Coldframe.Server.Lots;
using Coldframe.Server.Sensors;
using Npgsql;

namespace Coldframe.Server.Notifications.Push;

/// <summary>
/// The names and values a push text needs and the grains do not hand over (Story 6.5). Every lookup answers
/// <see langword="null"/> for what it does not find.
/// </summary>
public interface IPushLookups
{
    /// <summary>
    /// The Site's name.
    /// </summary>
    Task<string?> SiteNameAsync(string siteId, string userId, CancellationToken cancellationToken);

    /// <summary>
    /// The name of a Lot of the Site that was not removed.
    /// </summary>
    Task<string?> LotNameAsync(string siteId, string lotId, CancellationToken cancellationToken);

    /// <summary>
    /// The newest stored Reading of the Sensor taken at or after <paramref name="since"/>, in display units.
    /// </summary>
    Task<PushReading?> NewestReadingAsync(Guid sensorId, string quantity, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>
    /// The Sensor's effective Threshold of <paramref name="side"/>, in display units.
    /// </summary>
    Task<PushValue?> ThresholdAsync(Guid sensorId, ThresholdSide side, CancellationToken cancellationToken);
}

/// <summary>
/// Looks the names and values up where the Server keeps them: the Site in the identity projection, the Lot in
/// the lots projection, the Reading in <c>readings</c> with the Calibration it was stored with, and the
/// Threshold from the Sensor grain.
/// </summary>
/// <remarks>
/// A channel runs inside the notified User grain's turn. <see cref="ISensorGrain.GetThresholds"/> interleaves,
/// because the Sensor grain may be awaiting that very User grain through the Alert fan-out; no other grain is
/// called from here, and never the User grain.
/// </remarks>
public sealed class PushLookups(
    IdentityReadModel identity,
    LotsReadModel lots,
    NpgsqlDataSource dataSource,
    IGrainFactory grains) : IPushLookups
{
    private const string NewestReadingSql =
        """
        SELECT r.raw_value, r.measured_at, c.dry_raw, c.wet_raw
        FROM readings r
        LEFT JOIN calibrations c ON c.calibration_id = r.calibration_id
        WHERE r.sensor_id = @sensor_id AND r.measured_at >= @since
        ORDER BY r.measured_at DESC, r.reading_seq DESC
        LIMIT 1
        """;

    /// <inheritdoc />
    public async Task<string?> SiteNameAsync(string siteId, string userId, CancellationToken cancellationToken) =>
        (await identity.FindSiteAsync(siteId, userId, cancellationToken).ConfigureAwait(false))?.Name;

    /// <inheritdoc />
    public async Task<string?> LotNameAsync(string siteId, string lotId, CancellationToken cancellationToken) =>
        await lots.FindLotAsync(siteId, lotId, cancellationToken).ConfigureAwait(false) is { Removed: false } lot ? lot.Name : null;

    /// <inheritdoc />
    public async Task<PushReading?> NewestReadingAsync(Guid sensorId, string quantity, DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(NewestReadingSql);
        command.Parameters.AddWithValue("sensor_id", sensorId);
        command.Parameters.AddWithValue("since", since.ToUniversalTime());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var calibration = await reader.IsDBNullAsync(2, cancellationToken).ConfigureAwait(false)
            || await reader.IsDBNullAsync(3, cancellationToken).ConfigureAwait(false)
            ? null
            : new CalibrationPoints(reader.GetInt64(2), reader.GetInt64(3));

        return SensorConversion.Convert(quantity, reader.GetInt64(0), calibration) is var (value, unit)
            ? new PushReading(new PushValue(value, unit), reader.GetFieldValue<DateTimeOffset>(1))
            : null;
    }

    /// <inheritdoc />
    public async Task<PushValue?> ThresholdAsync(Guid sensorId, ThresholdSide side, CancellationToken cancellationToken)
    {
        var thresholds = await grains.GetGrain<ISensorGrain>(sensorId.ToString("D")).GetThresholds(cancellationToken).ConfigureAwait(false);

        return thresholds is not null && (side == ThresholdSide.Low ? thresholds.Low.Value : thresholds.High.Value) is { } stored
            ? new PushValue((double)(stored / SensorDisplay.Factor(thresholds.Specification)), SensorDisplay.Unit(thresholds.Specification))
            : null;
    }
}
