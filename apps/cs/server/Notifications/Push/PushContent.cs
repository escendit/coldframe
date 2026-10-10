using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Notifications;
using Coldframe.Contracts.Sites;
using Microsoft.Extensions.Options;

namespace Coldframe.Server.Notifications.Push;

/// <summary>
/// What one push says and where a tap on it leads (Story 6.5): the same for every device of the User, whatever
/// the provider. The contract is <c>packages/asyncapi</c>.
/// </summary>
/// <param name="Kind">The contract's kind: <c>alert</c>, <c>reminder</c> or <c>summary</c>.</param>
/// <param name="SiteId">The Site the notification is about, by which notifications are grouped.</param>
/// <param name="SiteName">The Site's name; <see langword="null"/> when it was not found.</param>
/// <param name="LotId">The Lot of the Alert; <see langword="null"/> for a summary.</param>
/// <param name="AlertId">The Alert; <see langword="null"/> for a summary.</param>
/// <param name="CollapseId">The stable identity of the send, so that a repeated one replaces the earlier one.</param>
/// <param name="Title">The condition.</param>
/// <param name="Body">Value and context; <see langword="null"/> when the Server found neither.</param>
public sealed record PushContent(
    string Kind,
    string SiteId,
    string? SiteName,
    string? LotId,
    Guid? AlertId,
    string CollapseId,
    string Title,
    string? Body);

/// <summary>
/// Turns a notification into its <see cref="PushContent"/> (Story 6.5): looks the names and values up through
/// <see cref="IPushLookups"/> and has <see cref="PushText"/> write the text. It holds no timing, window, mute or
/// cadence logic. Both push channels are handed the same notification by the Notifier and share one result.
/// </summary>
public sealed partial class PushContentBuilder(
    IPushLookups lookups,
    IOptions<PushOptions> options,
    TimeProvider clock,
    ILogger<PushContentBuilder> logger)
{
    /// <summary>
    /// How far back the newest Reading of an Alert's Sensor is looked for.
    /// </summary>
    public static readonly TimeSpan ReadingAge = TimeSpan.FromDays(2);

    private readonly ConditionalWeakTable<Notification, Task<PushContent?>> _built = [];

    /// <summary>
    /// The stable identity of a send: the first 32 hex digits of SHA-256 over
    /// <c>&lt;kind&gt;:&lt;subject&gt;:&lt;dueAt in Unix ms&gt;</c>, the subject being the Alert for an Alert or a
    /// Reminder and the Site for a summary.
    /// </summary>
    public static string CollapseIdOf(Notification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var subject = notification.Kind == NotificationKind.Summary || notification.Entries.Count == 0
            ? notification.SiteId
            : notification.Entries[0].AlertId.ToString("D");
        var identity = string.Create(
            CultureInfo.InvariantCulture,
            $"{Notifier.NameOf(notification.Kind)}:{subject}:{notification.DueAt.ToUnixTimeMilliseconds()}");

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)).AsSpan(0, 16));
    }

    /// <summary>
    /// The content of <paramref name="notification"/>, or <see langword="null"/> when there is nothing to say
    /// (a summary none of whose Lots exists any more). Built once per notification, within
    /// <see cref="PushOptions.LookupBudget"/>.
    /// </summary>
    /// <param name="notification">The notification as the Notifier handed it over.</param>
    /// <param name="sentAt">When the Notifier sent it.</param>
    public Task<PushContent?> BuildAsync(Notification notification, DateTimeOffset sentAt)
    {
        ArgumentNullException.ThrowIfNull(notification);

        return _built.GetValue(notification, key => BuildCoreAsync(key, sentAt));
    }

    private async Task<PushContent?> BuildCoreAsync(Notification notification, DateTimeOffset sentAt)
    {
        using var budget = new CancellationTokenSource(options.Value.LookupBudget, clock);
        var kind = Notifier.NameOf(notification.Kind);
        var collapseId = CollapseIdOf(notification);
        var text = PushText.English;
        var summary = notification.Kind == NotificationKind.Summary;

        var siteName = FoundAsync(lookups.SiteNameAsync(notification.SiteId, notification.UserId, budget.Token), notification, "Site name", budget.Token);
        var facts = await Task.WhenAll(notification.Entries.Select(entry => FactsAsync(notification, entry, sentAt, summary, budget.Token))).ConfigureAwait(false);

        if (summary)
        {
            // A Lot that is gone has no line. One whose name was not found in time keeps its line, unnamed: a
            // database that stalls must not turn the summary into nothing.
            var listed = facts.Where(alert => !alert.LotGone).ToList();

            if (listed.Count == 0)
            {
                return null;
            }

            var needWater = listed.Count(alert => PushText.NeedsWater(alert.Entry));

            return new PushContent(
                kind,
                notification.SiteId,
                await siteName.ConfigureAwait(false),
                LotId: null,
                AlertId: null,
                collapseId,
                text.SummaryTitle(await siteName.ConfigureAwait(false), needWater, listed.Count - needWater),
                text.SummaryBody(listed, notification.Window ?? NotificationWindow.Default));
        }

        if (facts.Length == 0)
        {
            return null;
        }

        var alert = facts[0];

        return new PushContent(
            kind,
            notification.SiteId,
            await siteName.ConfigureAwait(false),
            alert.Entry.LotId,
            alert.Entry.AlertId,
            collapseId,
            text.Title(alert),
            text.Body(alert, notification.Kind == NotificationKind.Reminder, NotificationWindowRule.ZoneOf(notification.TimeZone)));
    }

    // What the text of one Alert needs. Soil moisture names no Threshold in its title, so a summary line of it
    // needs only the Lot; a summary has no body, so it needs no Reading.
    private async Task<PushEntryFacts> FactsAsync(
        Notification notification,
        NotificationEntry entry,
        DateTimeOffset sentAt,
        bool summary,
        CancellationToken cancellationToken)
    {
        var soil = string.Equals(entry.Quantity, "soil_moisture", StringComparison.Ordinal);
        var threshold = entry is { Kind: AlertKind.Threshold, Side: { } side } && !(summary && soil)
            ? FoundAsync(lookups.ThresholdAsync(entry.SensorId, side, cancellationToken), notification, "Threshold", cancellationToken)
            : Task.FromResult<PushValue?>(null);
        var reading = summary
            ? Task.FromResult<PushReading?>(null)
            : FoundAsync(lookups.NewestReadingAsync(entry.SensorId, entry.Quantity, sentAt - ReadingAge, cancellationToken), notification, "Reading", cancellationToken);
        var (lotName, lotGone) = await LotAsync(notification, entry, cancellationToken).ConfigureAwait(false);

        return new PushEntryFacts(
            entry,
            lotName,
            await reading.ConfigureAwait(false),
            await threshold.ConfigureAwait(false),
            lotGone);
    }

    // The Lot's name, and whether the Lot is gone: only a lookup that answered can say so.
    private async Task<(string? Name, bool Gone)> LotAsync(Notification notification, NotificationEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            var name = await lookups.LotNameAsync(notification.SiteId, entry.LotId, cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);

            return (name, name is null);
        }
#pragma warning disable CA1031 // Whatever failed the lookup, the notification is still sent, with the Lot unnamed.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogLookupFailed(logger, "Lot name", Notifier.NameOf(notification.Kind), notification.SiteId, exception);
            return (null, false);
        }
    }

    // A lookup that fails or runs out of time leaves its part out of the text; it never fails the push.
    private async Task<T?> FoundAsync<T>(Task<T?> lookup, Notification notification, string what, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            // The budget ends the wait even when the lookup itself does not watch the token.
            return await lookup.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Whatever failed one lookup, the notification is still sent, without that part.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogLookupFailed(logger, what, Notifier.NameOf(notification.Kind), notification.SiteId, exception);
            return null;
        }
    }

    [LoggerMessage(EventId = 1, EventName = "PushLookupFailed", Level = LogLevel.Warning, Message = "The {What} of a {Kind} notification for Site {SiteId} was not found in time; the push is sent without it.")]
    private static partial void LogLookupFailed(ILogger logger, string what, string kind, string siteId, Exception exception);
}
