using System.Diagnostics.Metrics;
using Coldframe.Contracts.Notifications;

namespace Coldframe.Server.Notifications;

/// <summary>
/// The one <see cref="INotifier"/> (Story 6.4): stamps <c>sentAt</c> from the <see cref="TimeProvider"/>, calls
/// every registered <see cref="INotificationChannel"/> in registration order, then records <c>dueAt</c> and
/// <c>sentAt</c> as structured log fields and on the meter <see cref="MeterName"/>. It decides nothing. A channel
/// that fails is logged and the others are still tried; the send fails only when a channel failed and none
/// delivered. It hands back the push registrations a provider reported as invalid (Story 6.5).
/// </summary>
public sealed partial class Notifier : INotifier
{
    /// <summary>
    /// The name of the Coldframe notifications meter, which the Server exports.
    /// </summary>
    public const string MeterName = "Coldframe.Server.Notifications";

    /// <summary>
    /// The counter of notifications sent, tagged with <see cref="KindTag"/>.
    /// </summary>
    public const string SentCounterName = "coldframe.notifications.sent";

    /// <summary>
    /// The histogram of <c>sentAt - dueAt</c> in seconds, tagged with <see cref="KindTag"/>.
    /// </summary>
    public const string DelayHistogramName = "coldframe.notifications.delay";

    /// <summary>
    /// The tag that carries the notification kind: <c>alert</c>, <c>reminder</c> or <c>summary</c>.
    /// </summary>
    public const string KindTag = "kind";

    private readonly INotificationChannel[] _channels;
    private readonly TimeProvider _clock;
    private readonly ILogger<Notifier> _logger;
    private readonly Counter<long> _sent;
    private readonly Histogram<double> _delay;

    /// <summary>
    /// Creates the Notifier over the registered channels.
    /// </summary>
    public Notifier(
        IEnumerable<INotificationChannel> channels,
        IMeterFactory meters,
        TimeProvider clock,
        ILogger<Notifier> logger)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(meters);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _channels = [.. channels];
        _clock = clock;
        _logger = logger;

        // Owned by the factory, which disposes it with the container.
        var meter = meters.Create(MeterName);
        _sent = meter.CreateCounter<long>(SentCounterName, "{notification}", "Notifications handed to every channel.");
        _delay = meter.CreateHistogram<double>(DelayHistogramName, "s", "How long after it fell due a notification was sent.");
    }

    /// <summary>
    /// The name of <paramref name="kind"/> in logs and metrics.
    /// </summary>
    public static string NameOf(NotificationKind kind) => kind switch
    {
        NotificationKind.Alert => "alert",
        NotificationKind.Reminder => "reminder",
        NotificationKind.Summary => "summary",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown notification kind."),
    };

    /// <inheritdoc />
    public async Task<NotifierResult> SendAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var kind = NameOf(notification.Kind);
        var sentAt = _clock.GetUtcNow();

        // Every channel is tried, also after one failed. The send fails only when a channel failed and none
        // delivered: a retry while one channel is down would hand the others the same notification again and
        // again, and a User without a device on any channel has nothing to retry for.
        List<Exception>? failures = null;
        List<string>? invalid = null;
        var delivered = 0;

        foreach (var channel in _channels)
        {
            try
            {
                var delivery = await channel.SendAsync(notification, sentAt, cancellationToken).ConfigureAwait(false);
                delivered += delivery.Delivered;

                if (delivery.InvalidInstallations.Count > 0)
                {
                    (invalid ??= []).AddRange(delivery.InvalidInstallations);
                }
            }
#pragma warning disable CA1031 // Whatever failed one channel, the others are still tried.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                (failures ??= []).Add(exception);

                if (exception is IInvalidInstallationsSource { InvalidInstallations.Count: > 0 } source)
                {
                    (invalid ??= []).AddRange(source.InvalidInstallations);
                }
                LogChannelFailed(_logger, channel.GetType().Name, kind, notification.UserId, notification.SiteId, exception);
            }
        }

        IReadOnlyList<string> invalidInstallations = invalid is null ? [] : [.. invalid.Distinct(StringComparer.Ordinal)];

        if (failures is not null && delivered == 0)
        {
            throw new NotificationNotDeliveredException($"No channel delivered the {kind} notification.", failures, invalidInstallations);
        }

        var tag = new KeyValuePair<string, object?>(KindTag, kind);
        _sent.Add(1, tag);
        _delay.Record((sentAt - notification.DueAt).TotalSeconds, tag);

        LogSent(_logger, kind, notification.UserId, notification.SiteId, notification.Entries.Count, notification.DueAt, sentAt);

        return new NotifierResult(sentAt, invalidInstallations);
    }

    [LoggerMessage(
        EventId = 2,
        EventName = "NotificationChannelFailed",
        Level = LogLevel.Warning,
        Message = "Channel {Channel} did not deliver the {Kind} notification to User {UserId} for Site {SiteId}.")]
    private static partial void LogChannelFailed(ILogger logger, string channel, string kind, string userId, string siteId, Exception exception);

    [LoggerMessage(
        EventId = 1,
        EventName = "NotificationSent",
        Level = LogLevel.Information,
        Message = "Sent {Kind} notification to User {UserId} for Site {SiteId} with {Entries} Alert(s): dueAt {DueAt:O}, sentAt {SentAt:O}.")]
    private static partial void LogSent(
        ILogger logger,
        string kind,
        string userId,
        string siteId,
        int entries,
        DateTimeOffset dueAt,
        DateTimeOffset sentAt);
}
