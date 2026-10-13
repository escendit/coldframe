using System.Buffers.Text;
using System.Globalization;
using System.Text;
using Npgsql;

namespace Coldframe.Server.Alerts;

/// <summary>
/// An Alert as the alerts projection holds it, with the current name of its Lot.
/// </summary>
/// <param name="AlertId">The Alert ID.</param>
/// <param name="Kind">The contract's <c>AlertKind</c>, such as <c>threshold</c>.</param>
/// <param name="Side">The contract's <c>AlertSide</c> (<c>low</c> or <c>high</c>), or <see langword="null"/>.</param>
/// <param name="Quantity">What the Sensor measures, such as <c>soil_moisture</c>.</param>
/// <param name="LotId">The Lot the Alert was opened for.</param>
/// <param name="LotName">The name of that Lot, read from <c>lots</c> with the Alert.</param>
/// <param name="DeviceId">The Device ID of the Sensor's Node.</param>
/// <param name="OpenedAt">When the Alert opened.</param>
/// <param name="ClosedAt">When the Alert closed, or <see langword="null"/> while it is open.</param>
/// <param name="Reason">The contract's <c>AlertCloseReason</c>, or <see langword="null"/> while it is open.</param>
public sealed record AlertView(
    Guid AlertId,
    string Kind,
    string? Side,
    string Quantity,
    string LotId,
    string LotName,
    string DeviceId,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    string? Reason);

/// <summary>
/// A Health Alert as the Alerts list returns it (Story 7.2): an Alert about a Device, or (Story 7.4, kind
/// <c>uncalibrated</c>) about a Sensor of a Node, which is then the Device. The names are the contract's.
/// </summary>
/// <param name="AlertId">The Alert ID.</param>
/// <param name="Kind">The kind, such as <c>silent</c>; never <c>threshold</c>.</param>
/// <param name="DeviceId">The Device the Alert is about.</param>
/// <param name="DeviceKind">
/// <c>hub</c> or <c>node</c>: what the Alert recorded, else the kind of the enrolled Device (an Alert opened
/// before Story 7.2 recorded none).
/// </param>
/// <param name="LotId">The Lot the Alert was opened for; <see langword="null"/> for a Hub, a Node on no Lot, and while the Lot cannot be read.</param>
/// <param name="LotName">The current name of that Lot; <see langword="null"/> exactly when <paramref name="LotId"/> is.</param>
/// <param name="SilentSince">When a silent Device was last heard; <see langword="null"/> for an Alert that did not record it.</param>
/// <param name="OpenedAt">When the Alert opened.</param>
/// <param name="ClosedAt">When the Alert closed, or <see langword="null"/> while it is open.</param>
/// <param name="Reason">Why the Alert closed, or <see langword="null"/> while it is open.</param>
/// <param name="BatteryPercent">The battery level a <c>battery</c> Alert opened with (Story 7.3); <see langword="null"/> for another kind.</param>
public sealed record HealthAlertView(
    Guid AlertId,
    string Kind,
    string DeviceId,
    string DeviceKind,
    string? LotId,
    string? LotName,
    DateTimeOffset? SilentSince,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    string? Reason,
    int? BatteryPercent = null);

/// <summary>
/// Where a page of the Alerts list ended: the section, the time the section is ordered by and the Alert ID.
/// </summary>
/// <param name="Closed">Whether the last Alert of the page was a closed one.</param>
/// <param name="At">Its <c>closedAt</c> when closed, otherwise its <c>openedAt</c>; to the microsecond.</param>
/// <param name="AlertId">Its Alert ID, which orders Alerts of the same time.</param>
public readonly record struct AlertCursor(bool Closed, DateTimeOffset At, Guid AlertId);

/// <summary>
/// A page of the Alerts list.
/// </summary>
/// <param name="Alerts">The Alerts of the page, in the list's order.</param>
/// <param name="Next">Where the page ended, or <see langword="null"/> when no Alert follows.</param>
public sealed record AlertPage(IReadOnlyList<AlertView> Alerts, AlertCursor? Next);

/// <summary>
/// Reads the alerts projection (Story 6.2). Edge API handlers read Alerts only from here; grains never read it.
/// The table also holds Health Alerts (<c>silent</c> from Story 7.1, <c>battery</c> from Story 7.3), which have
/// no Sensor, no quantity and maybe no Lot. The contract's <c>Alert</c> requires all three, so the Threshold list
/// selects Threshold Alerts only and Health Alerts are listed on their own (Story 7.2). An <c>uncalibrated</c>
/// Alert (Story 7.4) has a Sensor, a quantity and a Lot, and is a Health Alert all the same: it has no side.
/// </summary>
public sealed class AlertsReadModel(NpgsqlDataSource dataSource)
{
    /// <summary>
    /// How long a closed Alert stays in the list.
    /// </summary>
    public static readonly TimeSpan ClosedRetention = TimeSpan.FromDays(7);

    private const string CursorVersion = "a1";

    private const long TicksPerMicrosecond = 10;

    /// <summary>
    /// The most Health Alerts one answer lists (Story 7.2). A Device has at most one open Alert per kind, a Sensor
    /// at most one open <c>uncalibrated</c> Alert (Story 7.4), and a closed one stays for
    /// <see cref="ClosedRetention"/>; the list is not paged.
    /// </summary>
    public const int HealthLimit = 200;

    // The only kind the contract's Alert can describe: every other kind is a Health Alert, listed on its own.
    private static readonly string ListedKind = AlertNames.Kind(Coldframe.Contracts.Alerts.AlertKind.Threshold);

    // One ordered list: the open Alerts (section 0) newest openedAt first, then the closed ones (section 1)
    // newest closedAt first, both by Alert ID within one time. The Lot name is joined at read time, so a rename
    // shows at once; the join hides a row only while the lots read model is being rebuilt. The cut-off for
    // closed Alerts and the page's start are parameters: this query never reads the database clock.
    private const string ListSql =
        """
        SELECT a.alert_id, a.kind, a.side, a.quantity, a.lot_id, l.name, a.device_id, a.opened_at, a.closed_at, a.reason
        FROM alerts a
        JOIN lots l ON l.site_id = a.site_id AND l.lot_id = a.lot_id
        WHERE a.site_id = @site_id
          AND a.kind = @kind
          AND (a.closed_at IS NULL OR a.closed_at >= @closed_since)
          AND (NOT @has_after
               OR (a.closed_at IS NOT NULL)::int > @after_section
               OR ((a.closed_at IS NOT NULL)::int = @after_section
                   AND (COALESCE(a.closed_at, a.opened_at) < @after_at
                        OR (COALESCE(a.closed_at, a.opened_at) = @after_at AND a.alert_id > @after_id))))
        ORDER BY (a.closed_at IS NOT NULL)::int, COALESCE(a.closed_at, a.opened_at) DESC, a.alert_id
        LIMIT @take
        """;

    // The Health Alerts (Story 7.2): every kind but the Threshold Alert, in the order of the list above. A Hub's
    // Alert and that of a Node on no Lot have no Lot, so the Lot is left-joined; the Device kind is the one the
    // Alert recorded, else the one of the enrolled Device. A row whose Device kind cannot be told is left out:
    // the contract cannot describe it.
    private const string HealthSql =
        """
        SELECT a.alert_id, a.kind, a.device_id, COALESCE(a.device_kind, d.kind), l.lot_id, l.name, a.silent_since,
               a.opened_at, a.closed_at, a.reason, a.battery_percent
        FROM alerts a
        LEFT JOIN lots l ON l.site_id = a.site_id AND l.lot_id = a.lot_id
        LEFT JOIN devices d ON d.device_id = a.device_id
        WHERE a.site_id = @site_id
          AND a.kind <> @kind
          AND COALESCE(a.device_kind, d.kind) IS NOT NULL
          AND (a.closed_at IS NULL OR a.closed_at >= @closed_since)
        ORDER BY (a.closed_at IS NOT NULL)::int, COALESCE(a.closed_at, a.opened_at) DESC, a.alert_id
        LIMIT @take
        """;

    // Every open Alert of the Site, whatever the page: the open section of the Threshold list (with its join)
    // and the open Health Alerts, which need no Lot.
    private const string CountOpenSql =
        """
        SELECT (SELECT count(*)
                FROM alerts a
                JOIN lots l ON l.site_id = a.site_id AND l.lot_id = a.lot_id
                WHERE a.site_id = @site_id AND a.kind = @kind AND a.closed_at IS NULL)
             + (SELECT count(*)
                FROM alerts a
                WHERE a.site_id = @site_id AND a.kind <> @kind AND a.closed_at IS NULL)
        """;

    /// <summary>
    /// Returns a page of the Site's Threshold Alerts: every open one, newest first, then the ones closed at or
    /// after <paramref name="closedSince"/>, newest close first.
    /// </summary>
    /// <param name="siteId">The Site ID, in its canonical form.</param>
    /// <param name="closedSince">The oldest close that is still listed; the caller derives it from the Server clock.</param>
    /// <param name="after">Where the previous page ended, or <see langword="null"/> for the first page.</param>
    /// <param name="limit">The most Alerts in the page.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<AlertPage> ListAlertsAsync(
        string siteId,
        DateTimeOffset closedSince,
        AlertCursor? after,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(siteId);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        await using var command = dataSource.CreateCommand(ListSql);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("kind", ListedKind);
        command.Parameters.AddWithValue("closed_since", closedSince.ToUniversalTime());
        command.Parameters.AddWithValue("has_after", after is not null);
        command.Parameters.AddWithValue("after_section", after is { Closed: true } ? 1 : 0);
        command.Parameters.AddWithValue("after_at", (after?.At ?? DateTimeOffset.UnixEpoch).ToUniversalTime());
        command.Parameters.AddWithValue("after_id", after?.AlertId ?? Guid.Empty);
        command.Parameters.AddWithValue("take", limit + 1);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var alerts = new List<AlertView>();

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            alerts.Add(new AlertView(
                reader.GetGuid(0),
                reader.GetString(1),
                await reader.IsDBNullAsync(2, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetFieldValue<DateTimeOffset>(7),
                await reader.IsDBNullAsync(8, cancellationToken).ConfigureAwait(false) ? null : reader.GetFieldValue<DateTimeOffset>(8),
                await reader.IsDBNullAsync(9, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(9)));
        }

        if (alerts.Count <= limit)
        {
            return new AlertPage(alerts, null);
        }

        var last = alerts[limit - 1];

        return new AlertPage(alerts[..limit], new AlertCursor(last.ClosedAt is not null, last.ClosedAt ?? last.OpenedAt, last.AlertId));
    }

    /// <summary>
    /// Returns the Site's Health Alerts (Story 7.2): open ones newest <c>openedAt</c> first, then those closed at
    /// or after <paramref name="closedSince"/> newest <c>closedAt</c> first, at most <see cref="HealthLimit"/>.
    /// </summary>
    /// <param name="siteId">The Site ID, in its canonical form.</param>
    /// <param name="closedSince">The cut-off for closed Alerts: the Server clock minus <see cref="ClosedRetention"/>.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<IReadOnlyList<HealthAlertView>> ListHealthAlertsAsync(string siteId, DateTimeOffset closedSince, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(siteId);

        await using var command = dataSource.CreateCommand(HealthSql);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("kind", ListedKind);
        command.Parameters.AddWithValue("closed_since", closedSince.ToUniversalTime());
        command.Parameters.AddWithValue("take", HealthLimit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var alerts = new List<HealthAlertView>();

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            // The Lot's ID and name come from the same joined row: both or neither.
            alerts.Add(new HealthAlertView(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                await reader.IsDBNullAsync(4, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(4),
                await reader.IsDBNullAsync(5, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(5),
                await reader.IsDBNullAsync(6, cancellationToken).ConfigureAwait(false) ? null : reader.GetFieldValue<DateTimeOffset>(6),
                reader.GetFieldValue<DateTimeOffset>(7),
                await reader.IsDBNullAsync(8, cancellationToken).ConfigureAwait(false) ? null : reader.GetFieldValue<DateTimeOffset>(8),
                await reader.IsDBNullAsync(9, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(9),
                await reader.IsDBNullAsync(10, cancellationToken).ConfigureAwait(false) ? null : reader.GetInt16(10)));
        }

        return alerts;
    }

    /// <summary>
    /// Counts the Site's open Alerts of every kind: Threshold and Health (Story 7.2).
    /// </summary>
    /// <param name="siteId">The Site ID, in its canonical form.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<int> CountOpenAsync(string siteId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(siteId);

        await using var command = dataSource.CreateCommand(CountOpenSql);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("kind", ListedKind);

        return checked((int)(long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!);
    }

    /// <summary>
    /// Writes an Alerts cursor, opaque to clients and versioned: the section, the time in microseconds since the
    /// Unix epoch (what PostgreSQL keeps) and the Alert ID.
    /// </summary>
    /// <param name="cursor">Where the page just returned ended.</param>
    public static string EncodeCursor(AlertCursor cursor) =>
        Base64Url.EncodeToString(Encoding.ASCII.GetBytes(string.Create(
            CultureInfo.InvariantCulture,
            $"{CursorVersion}:{(cursor.Closed ? 'c' : 'o')}:{(cursor.At.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / TicksPerMicrosecond}:{cursor.AlertId:N}")));

    /// <summary>
    /// Reads an Alerts cursor, or returns <see langword="null"/> when it is not one this Server wrote.
    /// </summary>
    /// <param name="cursor">The cursor as a client sent it.</param>
    public static AlertCursor? DecodeCursor(string cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);

        string text;

        try
        {
            text = Encoding.ASCII.GetString(Base64Url.DecodeFromChars(cursor));
        }
        catch (FormatException)
        {
            return null;
        }

        var parts = text.Split(':');

        if (parts.Length != 4
            || parts[0] != CursorVersion
            || parts[1] is not ("o" or "c")
            || !long.TryParse(parts[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var microseconds)
            || !Guid.TryParseExact(parts[3], "N", out var alertId))
        {
            return null;
        }

        var maxMicroseconds = (DateTimeOffset.MaxValue.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / TicksPerMicrosecond;
        var minMicroseconds = (DateTimeOffset.MinValue.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / TicksPerMicrosecond;

        return microseconds < minMicroseconds || microseconds > maxMicroseconds
            ? null
            : new AlertCursor(parts[1] == "c", DateTimeOffset.UnixEpoch.AddTicks(microseconds * TicksPerMicrosecond), alertId);
    }
}
