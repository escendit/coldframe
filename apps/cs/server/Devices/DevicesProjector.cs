using Coldframe.Contracts.Devices;
using Coldframe.Server.Edge;
using Coldframe.Server.Journal;
using Npgsql;

namespace Coldframe.Server.Devices;

/// <summary>
/// Projects the Device streams into <c>devices</c>, the read model of the Devices list. It is the only writer
/// of that table. A row exists only for an enrolled Device: <c>device.enrolled</c> creates it,
/// <c>device.assigned</c> and <c>device.moved</c> set its Lot, <c>device.unassigned</c> clears it and
/// <c>device.seen</c> sets its last-seen time. The Site's roster (<c>site.device-registered</c>) never creates a row, since it can exist without an enrolment.
/// </summary>
public sealed class DevicesProjector : IProjector
{
    /// <summary>
    /// The projector's stable name; its checkpoint is stored under it.
    /// </summary>
    public const string ProjectorName = "devices";

    private const string DeviceStreamPrefix = "device/";

    // An enrolment event is the first of its stream; applying it again keeps the Lot and the last-seen time.
    private const string EnrolSql =
        """
        INSERT INTO devices (device_id, site_id, kind, lot_id, enrolled_at, last_seen_at)
        VALUES (@device_id, @site_id, @kind, NULL, @enrolled_at, NULL)
        ON CONFLICT (device_id) DO UPDATE
        SET site_id = EXCLUDED.site_id, kind = EXCLUDED.kind, enrolled_at = EXCLUDED.enrolled_at
        RETURNING site_id
        """;

    private const string AssignSql = "UPDATE devices SET lot_id = @lot_id WHERE device_id = @device_id RETURNING site_id";

    private const string UnassignSql = "UPDATE devices SET lot_id = NULL WHERE device_id = @device_id RETURNING site_id";

    // Never moves backwards, so applying an older heartbeat again changes nothing.
    private const string SeenSql =
        """
        UPDATE devices SET last_seen_at = @seen_at
        WHERE device_id = @device_id AND (last_seen_at IS NULL OR last_seen_at < @seen_at)
        RETURNING site_id
        """;

    /// <inheritdoc />
    public string Name => ProjectorName;

    /// <summary>
    /// Returns the Device ID of a Device stream (<c>device/{id}</c>), or <see langword="null"/> for any other stream.
    /// </summary>
    public static string? DeviceIdOf(string streamId)
    {
        ArgumentNullException.ThrowIfNull(streamId);

        return streamId.StartsWith(DeviceStreamPrefix, StringComparison.Ordinal) ? streamId[DeviceStreamPrefix.Length..] : null;
    }

    /// <inheritdoc />
    public Task ApplyAsync(JournalEvent journalEvent, NpgsqlTransaction transaction, ReadModelChanges changes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(journalEvent);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(changes);

        if (DeviceIdOf(journalEvent.StreamId) is not { } deviceId)
        {
            return Task.CompletedTask;
        }

        return journalEvent.Data switch
        {
            DeviceEnrolled enrolled => ExecuteAsync(
                transaction,
                EnrolSql,
                deviceId,
                changes,
                cancellationToken,
                ("device_id", deviceId),
                ("site_id", enrolled.SiteId),
                ("kind", EdgeValidation.DeviceKindName(enrolled.Kind)),
                ("enrolled_at", enrolled.EnrolledAt.ToUniversalTime())),
            DeviceAssigned assigned => ExecuteAsync(
                transaction,
                AssignSql,
                deviceId,
                changes,
                cancellationToken,
                ("device_id", deviceId),
                ("lot_id", assigned.LotId)),
            DeviceMoved moved => ExecuteAsync(
                transaction,
                AssignSql,
                deviceId,
                changes,
                cancellationToken,
                ("device_id", deviceId),
                ("lot_id", moved.ToLotId)),
            DeviceUnassigned => ExecuteAsync(
                transaction,
                UnassignSql,
                deviceId,
                changes,
                cancellationToken,
                ("device_id", deviceId)),
            DeviceSeen seen => ExecuteAsync(
                transaction,
                SeenSql,
                deviceId,
                changes,
                cancellationToken,
                ("device_id", deviceId),
                ("seen_at", seen.SeenAt.ToUniversalTime())),
            _ => Task.CompletedTask,
        };
    }

    // Every statement returns the Site of the row it changed, and nothing when it changed none: only a row
    // that changed is reported (Story 6.6).
    private static async Task ExecuteAsync(
        NpgsqlTransaction transaction,
        string sql,
        string deviceId,
        ReadModelChanges changes,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, transaction.Connection, transaction);

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string siteId)
        {
            changes.Touch(siteId, ReadModelResources.Devices, deviceId);
        }
    }
}
