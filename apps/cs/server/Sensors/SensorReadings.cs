using Npgsql;

namespace Coldframe.Server.Sensors;

/// <summary>
/// Looks up the stored Readings of a Sensor for the Sensor grain's validation of a Calibration (Story 5.1). It
/// only reads <c>readings</c>, which only the Device grain writes (AD-9).
/// </summary>
public class SensorReadings(NpgsqlDataSource dataSource)
{
    private const string FindSql =
        "SELECT raw_value FROM readings WHERE sensor_id = @sensor_id AND reading_seq = @reading_seq LIMIT 1";

    /// <summary>
    /// Returns the raw value of the stored Reading of the Sensor with this <c>reading_seq</c>, or
    /// <see langword="null"/> when the Sensor has no such Reading stored (a Reading discarded by a Pause is none).
    /// </summary>
    /// <param name="sensorId">The Sensor ID.</param>
    /// <param name="readingSeq">The Reading's <c>reading_seq</c>.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public virtual async Task<long?> FindRawValueAsync(Guid sensorId, ulong readingSeq, CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand(FindSql);
        command.Parameters.AddWithValue("sensor_id", sensorId);
        command.Parameters.AddWithValue("reading_seq", (decimal)readingSeq);

        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is long raw ? raw : null;
    }
}
