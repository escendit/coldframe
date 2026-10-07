using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Lots;
using Coldframe.Contracts.Sensors;
using Coldframe.Crypto;
using Coldframe.Server.Journal;
using Npgsql;
using NpgsqlTypes;

namespace Coldframe.Server.Lots;

/// <summary>
/// Projects Lots into <c>lots</c>, the read model of Garden and Site settings, with the Server's status of
/// every Lot (AD-14). It is the only writer of that table and of its three support tables,
/// <c>lot_status_devices</c>, <c>lot_status_sensors</c> and <c>calibrations</c> (the points of every
/// Calibration, from which a Reading's percentage is derived).
/// </summary>
/// <remarks>
/// <para>
/// It reads three kinds of streams. Lot events create, rename, claim, release and remove the row. Device
/// events (<c>device.paused</c>, <c>device.resumed</c>, <c>device.specifications-declared</c>) and Sensor
/// events (<c>sensor.declared</c>, <c>sensor.specification-changed</c>, <c>sensor.calibrated</c>) fill the
/// support tables. After every
/// event that can change a status, the Lots it touches are evaluated again through
/// <see cref="LotStatusRule"/>, the only place that decides a status.
/// </para>
/// <para>
/// <c>status_since</c> moves only when the status changes, to the time of the event that changed it: the
/// journal's recorded time for a Lot event, the event's own time otherwise. Applying an event again
/// therefore changes nothing, and deleting the four tables' rows and the checkpoint rebuilds them from position 0.
/// </para>
/// <para>
/// Two inputs of the rule have no producer yet. No Silent Alert exists before Epic 7, so the only silence
/// the projector knows is a Node that has declared no Sensor: it has never reported, and its Lot is
/// <c>unknown</c> with cause <c>node</c>. No Threshold Alert exists before Epic 6, so the open low-side
/// Alert is always absent. A declared <c>calibration: true</c> soil-moisture Sensor is uncalibrated until its
/// <c>sensor.calibrated</c> event is projected (Story 5.1).
/// </para>
/// </remarks>
public sealed class LotsProjector : IProjector
{
    /// <summary>
    /// The projector's stable name; its checkpoint is stored under it.
    /// </summary>
    public const string ProjectorName = "lots";

    private const string LotStreamPrefix = "lot/";

    private const string DeviceStreamPrefix = "device/";

    private const string SensorStreamPrefix = "sensor/";

    private const string CreateSql =
        """
        INSERT INTO lots (lot_id, site_id, name, status, status_since, claimed_by, created_at, removed_at)
        VALUES (@lot_id, @site_id, @name, @status, @at, NULL, @at, NULL)
        ON CONFLICT (lot_id) DO NOTHING
        """;

    private const string RenameSql = "UPDATE lots SET name = @name WHERE lot_id = @lot_id";

    // A claim the Lot already holds keeps its time.
    private const string ClaimSql =
        """
        UPDATE lots SET claimed_by = @node_id, claimed_at = @at
        WHERE lot_id = @lot_id AND claimed_by IS DISTINCT FROM @node_id
        """;

    private const string ReleaseSql =
        "UPDATE lots SET claimed_by = NULL, claimed_at = NULL WHERE lot_id = @lot_id AND claimed_by = @node_id";

    private const string RemoveSql = "UPDATE lots SET removed_at = @at WHERE lot_id = @lot_id AND removed_at IS NULL";

    private const string PauseDeviceSql =
        """
        INSERT INTO lot_status_devices (device_id, device_paused, device_paused_until)
        VALUES (@device_id, @paused, @until)
        ON CONFLICT (device_id) DO UPDATE SET device_paused = EXCLUDED.device_paused, device_paused_until = EXCLUDED.device_paused_until
        """;

    private const string PauseSiteSql =
        """
        INSERT INTO lot_status_devices (device_id, site_paused, site_paused_until)
        VALUES (@device_id, @paused, @until)
        ON CONFLICT (device_id) DO UPDATE SET site_paused = EXCLUDED.site_paused, site_paused_until = EXCLUDED.site_paused_until
        """;

    private const string DeclareDeviceSql =
        """
        INSERT INTO lot_status_devices (device_id, sensor_ids)
        VALUES (@device_id, @sensor_ids)
        ON CONFLICT (device_id) DO UPDATE SET sensor_ids = EXCLUDED.sensor_ids
        """;

    private const string DeclareSensorSql =
        """
        INSERT INTO lot_status_sensors (sensor_id, device_id, quantity, calibration)
        VALUES (@sensor_id, @device_id, @quantity, @calibration)
        ON CONFLICT (sensor_id) DO UPDATE
        SET device_id = EXCLUDED.device_id, quantity = EXCLUDED.quantity, calibration = EXCLUDED.calibration
        """;

    // A Calibration's points by its ID; an event applied again changes nothing.
    private const string CalibrationSql =
        """
        INSERT INTO calibrations (calibration_id, sensor_id, dry_raw, wet_raw, calibrated_at)
        VALUES (@calibration_id, @sensor_id, @dry_raw, @wet_raw, @calibrated_at)
        ON CONFLICT (calibration_id) DO NOTHING
        """;

    private const string CalibratedSensorSql =
        """
        UPDATE lot_status_sensors SET calibrated = true
        WHERE sensor_id = @sensor_id
        RETURNING device_id
        """;

    private const string ChangeSensorSql =
        """
        UPDATE lot_status_sensors SET quantity = @quantity, calibration = @calibration
        WHERE sensor_id = @sensor_id
        RETURNING device_id
        """;

    // The inputs of the rule for the Lots one key selects, read from what this transaction has written.
    private const string InputsSql =
        """
        SELECT l.lot_id,
               l.claimed_by IS NOT NULL,
               d.sensor_ids IS NOT NULL,
               COALESCE(d.device_paused, false),
               d.device_paused_until,
               COALESCE(d.site_paused, false),
               d.site_paused_until,
               EXISTS (
                   SELECT 1 FROM lot_status_sensors s
                   WHERE s.sensor_id = ANY (d.sensor_ids) AND s.quantity = @soil_moisture AND s.calibration AND NOT s.calibrated)
        FROM lots l
        LEFT JOIN lot_status_devices d ON d.device_id = l.claimed_by
        WHERE
        """;

    private const string StoreStatusSql =
        """
        UPDATE lots
        SET status_since = CASE WHEN status = @status THEN status_since ELSE @at END,
            status = @status,
            unknown_cause = @unknown_cause,
            paused_by = @paused_by,
            paused_until = @paused_until
        WHERE lot_id = @lot_id
        """;

    /// <inheritdoc />
    public string Name => ProjectorName;

    /// <summary>
    /// Returns the Lot ID of a Lot stream (<c>lot/{id}</c>), or <see langword="null"/> for any other stream.
    /// </summary>
    public static string? LotIdOf(string streamId) => IdOf(streamId, LotStreamPrefix);

    /// <summary>
    /// Returns the Device ID of a Device stream (<c>device/{id}</c>), or <see langword="null"/> for any other stream.
    /// </summary>
    public static string? DeviceIdOf(string streamId) => IdOf(streamId, DeviceStreamPrefix);

    /// <summary>
    /// Returns the Sensor ID of a Sensor stream (<c>sensor/{id}</c>), or <see langword="null"/> for any other
    /// stream or an ID that is not a UUID.
    /// </summary>
    public static Guid? SensorIdOf(string streamId) =>
        IdOf(streamId, SensorStreamPrefix) is { } id && Guid.TryParse(id, out var sensorId) ? sensorId : null;

    /// <summary>
    /// Returns the inputs of the status rule as the projector knows them today: a Node that has declared no
    /// Sensor is silent, and no low-side Alert is open (see the remarks of the class).
    /// </summary>
    /// <param name="hasNode">Whether a Node occupies the Lot.</param>
    /// <param name="hasDeclared">Whether the Server accepted a Specification set of that Node.</param>
    /// <param name="pausedBy">The Pause sources of that Node.</param>
    /// <param name="uncalibratedSoilSensor">Whether the Node has a calibrating soil-moisture Sensor without Calibration.</param>
    public static LotStatusInputs InputsOf(bool hasNode, bool hasDeclared, LotPauseSources pausedBy, bool uncalibratedSoilSensor) =>
        new(
            hasNode,
            pausedBy,
            hasNode && !hasDeclared ? LotSilence.Node : LotSilence.None,
            uncalibratedSoilSensor,
            OpenLowAlert: false);

    /// <inheritdoc />
    public Task ApplyAsync(JournalEvent journalEvent, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(journalEvent);
        ArgumentNullException.ThrowIfNull(transaction);

        if (LotIdOf(journalEvent.StreamId) is { } lotId)
        {
            return ApplyLotAsync(lotId, journalEvent, transaction, cancellationToken);
        }

        if (DeviceIdOf(journalEvent.StreamId) is { } deviceId)
        {
            return ApplyDeviceAsync(deviceId, journalEvent.Data, transaction, cancellationToken);
        }

        if (SensorIdOf(journalEvent.StreamId) is { } sensorId)
        {
            return ApplySensorAsync(sensorId, journalEvent.Data, transaction, cancellationToken);
        }

        return Task.CompletedTask;
    }

    private static string? IdOf(string streamId, string prefix)
    {
        ArgumentNullException.ThrowIfNull(streamId);

        return streamId.StartsWith(prefix, StringComparison.Ordinal) ? streamId[prefix.Length..] : null;
    }

    private static async Task ApplyLotAsync(string lotId, JournalEvent journalEvent, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        // Lot events carry no time of their own: the journal's is the time of the change.
        var at = journalEvent.RecordedAt.ToUniversalTime();

        switch (journalEvent.Data)
        {
            case LotCreated created:
                await ExecuteAsync(
                    transaction,
                    CreateSql,
                    cancellationToken,
                    ("lot_id", lotId),
                    ("site_id", created.SiteId),
                    ("name", created.Name),
                    ("status", LotStatusRule.NoNode),
                    ("at", at)).ConfigureAwait(false);
                break;
            case LotRenamed renamed:
                await ExecuteAsync(transaction, RenameSql, cancellationToken, ("lot_id", lotId), ("name", renamed.Name)).ConfigureAwait(false);
                break;
            case LotClaimed claimed:
                await ExecuteAsync(transaction, ClaimSql, cancellationToken, ("lot_id", lotId), ("node_id", claimed.NodeId), ("at", at)).ConfigureAwait(false);
                await EvaluateAsync(transaction, "l.lot_id = @key", lotId, at, cancellationToken).ConfigureAwait(false);
                break;
            case LotReleased released:
                await ExecuteAsync(transaction, ReleaseSql, cancellationToken, ("lot_id", lotId), ("node_id", released.NodeId)).ConfigureAwait(false);
                await EvaluateAsync(transaction, "l.lot_id = @key", lotId, at, cancellationToken).ConfigureAwait(false);
                break;
            case LotRemoved:
                await ExecuteAsync(transaction, RemoveSql, cancellationToken, ("lot_id", lotId), ("at", at)).ConfigureAwait(false);
                break;
            default:
                break;
        }
    }

    private static async Task ApplyDeviceAsync(string deviceId, object data, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        DateTimeOffset at;

        switch (data)
        {
            case DevicePaused paused:
                at = paused.PausedAt;
                await PauseAsync(transaction, deviceId, paused.Source, paused: true, paused.EndsAt, cancellationToken).ConfigureAwait(false);
                break;
            case DeviceResumed resumed:
                at = resumed.ResumedAt;
                await PauseAsync(transaction, deviceId, resumed.Source, paused: false, until: null, cancellationToken).ConfigureAwait(false);
                break;
            case DeviceSpecificationsDeclared declared:
                at = declared.DeclaredAt;
                await using (var command = new NpgsqlCommand(DeclareDeviceSql, transaction.Connection, transaction))
                {
                    command.Parameters.AddWithValue("device_id", deviceId);
                    command.Parameters.AddWithValue(
                        "sensor_ids",
                        NpgsqlDbType.Array | NpgsqlDbType.Uuid,
                        declared.Sensors.Select(sensor => sensor.SensorId).ToArray());
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                break;
            default:
                return;
        }

        await EvaluateAsync(transaction, "l.claimed_by = @key", deviceId, at.ToUniversalTime(), cancellationToken).ConfigureAwait(false);
    }

    private static async Task ApplySensorAsync(Guid sensorId, object data, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        string? deviceId;
        DateTimeOffset at;

        switch (data)
        {
            case SensorDeclared declared:
                deviceId = declared.DeviceId;
                at = declared.DeclaredAt;
                await ExecuteAsync(
                    transaction,
                    DeclareSensorSql,
                    cancellationToken,
                    ("sensor_id", sensorId),
                    ("device_id", declared.DeviceId),
                    ("quantity", declared.Specification.Quantity),
                    ("calibration", declared.Specification.Calibration)).ConfigureAwait(false);
                break;
            case SensorSpecificationChanged changed:
                at = changed.ChangedAt;
                await using (var command = new NpgsqlCommand(ChangeSensorSql, transaction.Connection, transaction))
                {
                    command.Parameters.AddWithValue("sensor_id", sensorId);
                    command.Parameters.AddWithValue("quantity", changed.Specification.Quantity);
                    command.Parameters.AddWithValue("calibration", changed.Specification.Calibration);
                    deviceId = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
                }

                break;
            case SensorCalibrated calibrated:
                at = calibrated.CalibratedAt;
                await ExecuteAsync(
                    transaction,
                    CalibrationSql,
                    cancellationToken,
                    ("calibration_id", calibrated.CalibrationId),
                    ("sensor_id", sensorId),
                    ("dry_raw", calibrated.DryRaw),
                    ("wet_raw", calibrated.WetRaw),
                    ("calibrated_at", calibrated.CalibratedAt.ToUniversalTime())).ConfigureAwait(false);
                await using (var command = new NpgsqlCommand(CalibratedSensorSql, transaction.Connection, transaction))
                {
                    command.Parameters.AddWithValue("sensor_id", sensorId);
                    deviceId = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
                }

                break;
            default:
                return;
        }

        if (deviceId is not null)
        {
            await EvaluateAsync(transaction, "l.claimed_by = @key", deviceId, at.ToUniversalTime(), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task PauseAsync(
        NpgsqlTransaction transaction,
        string deviceId,
        DevicePauseSource source,
        bool paused,
        DateTimeOffset? until,
        CancellationToken cancellationToken)
    {
        var sql = source == DevicePauseSource.Site ? PauseSiteSql : PauseDeviceSql;

        await using var command = new NpgsqlCommand(sql, transaction.Connection, transaction);
        command.Parameters.AddWithValue("device_id", deviceId);
        command.Parameters.AddWithValue("paused", paused);
        command.Parameters.AddWithValue("until", NpgsqlDbType.TimestampTz, until is { } end ? end.ToUniversalTime() : DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    // Evaluates the Lots the filter selects through the rule and stores what it decides. The time is used
    // only when the status changes.
    private static async Task EvaluateAsync(
        NpgsqlTransaction transaction,
        string filter,
        string key,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        var decisions = new List<(string LotId, LotStatusResult Result, DateTimeOffset? PausedUntil)>();

        await using (var select = new NpgsqlCommand($"{InputsSql} {filter}", transaction.Connection, transaction))
        {
            select.Parameters.AddWithValue("key", key);
            select.Parameters.AddWithValue("soil_moisture", CryptoSpec.SensorQuantitySoilMoisture);

            await using var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var sources = LotPauseSources.None;
                var ends = new List<DateTimeOffset?>(2);

                if (reader.GetBoolean(3))
                {
                    sources |= LotPauseSources.Device;
                    ends.Add(await ReadTimeAsync(reader, 4, cancellationToken).ConfigureAwait(false));
                }

                if (reader.GetBoolean(5))
                {
                    sources |= LotPauseSources.Site;
                    ends.Add(await ReadTimeAsync(reader, 6, cancellationToken).ConfigureAwait(false));
                }

                var result = LotStatusRule.Evaluate(InputsOf(reader.GetBoolean(1), reader.GetBoolean(2), sources, reader.GetBoolean(7)));
                var pausedUntil = result.Status == LotStatusRule.Paused ? LotStatusRule.PausedUntil(ends) : null;

                decisions.Add((reader.GetString(0), result, pausedUntil));
            }
        }

        foreach (var (lotId, result, pausedUntil) in decisions)
        {
            await using var store = new NpgsqlCommand(StoreStatusSql, transaction.Connection, transaction);
            store.Parameters.AddWithValue("lot_id", lotId);
            store.Parameters.AddWithValue("status", result.Status);
            store.Parameters.AddWithValue("at", at);
            store.Parameters.AddWithValue("unknown_cause", NpgsqlDbType.Text, (object?)result.UnknownCause ?? DBNull.Value);
            store.Parameters.AddWithValue(
                "paused_by",
                NpgsqlDbType.Array | NpgsqlDbType.Text,
                result.PausedBy.Count == 0 ? DBNull.Value : result.PausedBy.ToArray());
            store.Parameters.AddWithValue("paused_until", NpgsqlDbType.TimestampTz, pausedUntil is { } until ? until.ToUniversalTime() : DBNull.Value);
            await store.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<DateTimeOffset?> ReadTimeAsync(NpgsqlDataReader reader, int ordinal, CancellationToken cancellationToken) =>
        await reader.IsDBNullAsync(ordinal, cancellationToken).ConfigureAwait(false) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);

    private static async Task ExecuteAsync(
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, transaction.Connection, transaction);

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
