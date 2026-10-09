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
/// </summary>
public sealed class AlertsReadModel(NpgsqlDataSource dataSource)
{
    /// <summary>
    /// How long a closed Alert stays in the list.
    /// </summary>
    public static readonly TimeSpan ClosedRetention = TimeSpan.FromDays(7);

    private const string CursorVersion = "a1";

    private const long TicksPerMicrosecond = 10;

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
          AND (a.closed_at IS NULL OR a.closed_at >= @closed_since)
          AND (NOT @has_after
               OR (a.closed_at IS NOT NULL)::int > @after_section
               OR ((a.closed_at IS NOT NULL)::int = @after_section
                   AND (COALESCE(a.closed_at, a.opened_at) < @after_at
                        OR (COALESCE(a.closed_at, a.opened_at) = @after_at AND a.alert_id > @after_id))))
        ORDER BY (a.closed_at IS NOT NULL)::int, COALESCE(a.closed_at, a.opened_at) DESC, a.alert_id
        LIMIT @take
        """;

    // The same rows as the open section of the list, whatever the page.
    private const string CountOpenSql =
        """
        SELECT count(*)
        FROM alerts a
        JOIN lots l ON l.site_id = a.site_id AND l.lot_id = a.lot_id
        WHERE a.site_id = @site_id AND a.closed_at IS NULL
        """;

    /// <summary>
    /// Returns a page of the Site's Alerts: every open Alert, newest first, then the Alerts closed at or after
    /// <paramref name="closedSince"/>, newest close first.
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
    /// Counts the Site's open Alerts.
    /// </summary>
    /// <param name="siteId">The Site ID, in its canonical form.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<int> CountOpenAsync(string siteId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(siteId);

        await using var command = dataSource.CreateCommand(CountOpenSql);
        command.Parameters.AddWithValue("site_id", siteId);

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
