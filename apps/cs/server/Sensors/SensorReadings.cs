using Npgsql;

namespace Coldframe.Server.Sensors;

/// <summary>
/// One stored Reading of a Sensor as a Calibration client picks it (Story 5.2).
/// </summary>
/// <param name="ReadingSeq">The Reading's <c>reading_seq</c>.</param>
/// <param name="RawValue">The stored raw value.</param>
/// <param name="MeasuredAt">When the Reading was taken.</param>
public sealed record RecentReading(ulong ReadingSeq, long RawValue, DateTimeOffset MeasuredAt);

/// <summary>
/// Looks up the stored Readings of a Sensor for the Sensor grain's validation of a Calibration (Story 5.1). It
/// only reads <c>readings</c>, which only the Device grain writes (AD-9).
/// </summary>
public class SensorReadings(NpgsqlDataSource dataSource)
{
    private const string FindSql =
        "SELECT raw_value FROM readings WHERE sensor_id = @sensor_id AND reading_seq = @reading_seq LIMIT 1";

    private const string RecentSql =
        """
        SELECT reading_seq, raw_value, measured_at
        FROM readings
        WHERE sensor_id = @sensor_id AND measured_at >= @since
        ORDER BY measured_at DESC, reading_seq DESC
        LIMIT @take
        """;

    /// <summary>
    /// Returns the newest stored Readings of the Sensor taken at or after <paramref name="since"/>, newest
    /// first. The <c>measured_at</c> bound keeps the read to the partitions of that window.
    /// </summary>
    /// <param name="sensorId">The Sensor ID.</param>
    /// <param name="since">The oldest <c>measured_at</c> that counts.</param>
    /// <param name="limit">How many Readings at most.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public virtual async Task<IReadOnlyList<RecentReading>> RecentAsync(Guid sensorId, DateTimeOffset since, int limit, CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand(RecentSql);
        command.Parameters.AddWithValue("sensor_id", sensorId);
        command.Parameters.AddWithValue("since", since.ToUniversalTime());
        command.Parameters.AddWithValue("take", limit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var readings = new List<RecentReading>();

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            readings.Add(new RecentReading((ulong)reader.GetDecimal(0), reader.GetInt64(1), reader.GetFieldValue<DateTimeOffset>(2)));
        }

        return readings;
    }

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
