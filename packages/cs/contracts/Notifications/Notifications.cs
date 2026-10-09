using Coldframe.Contracts.Alerts;

namespace Coldframe.Contracts.Notifications;

/// <summary>
/// What a notification is (Story 6.4).
/// </summary>
[GenerateSerializer]
[Alias("coldframe.notification-kind")]
public enum NotificationKind
{
    /// <summary>
    /// An Alert opened and the User was told inside the Notification Window.
    /// </summary>
    Alert = 0,

    /// <summary>
    /// An Alert is still open: a Reminder at the User's resolved cadence.
    /// </summary>
    Reminder = 1,

    /// <summary>
    /// What fell due for one Site outside the Notification Window, sent when the window opens: one entry per
    /// Alert that is still open.
    /// </summary>
    Summary = 2,
}

/// <summary>
/// One Alert in a notification: the facts the grains hold. Names, values and wording belong to the channels.
/// </summary>
/// <param name="AlertId">The Alert ID.</param>
/// <param name="Kind">What the Alert is about.</param>
/// <param name="Side">The Threshold that was crossed; <see langword="null"/> for an Alert that is not a Threshold Alert.</param>
/// <param name="LotId">The Lot the Alert was opened for.</param>
/// <param name="SensorId">The Sensor that opened the Alert.</param>
/// <param name="DeviceId">The Device ID of that Sensor's Node.</param>
/// <param name="Quantity">What the Sensor measures, such as <c>soil_moisture</c>.</param>
/// <param name="OpenedAt">When the Alert opened.</param>
[GenerateSerializer]
[Alias("coldframe.notification-entry")]
public sealed record NotificationEntry(
    [property: Id(0)] Guid AlertId,
    [property: Id(1)] AlertKind Kind,
    [property: Id(2)] ThresholdSide? Side,
    [property: Id(3)] string LotId,
    [property: Id(4)] Guid SensorId,
    [property: Id(5)] string DeviceId,
    [property: Id(6)] string Quantity,
    [property: Id(7)] DateTimeOffset OpenedAt);

/// <summary>
/// A notification the User grain hands to the Notifier (Story 6.4). The User grain has already decided that it
/// is due, inside the Notification Window and not muted: nothing behind the Notifier filters, delays or
/// schedules.
/// </summary>
/// <param name="UserId">The User to notify (the OIDC <c>sub</c>).</param>
/// <param name="SiteId">The Site the notification is about.</param>
/// <param name="Kind">What the notification is.</param>
/// <param name="DueAt">When the delivery fell due (UTC); for a summary, when the window opened.</param>
/// <param name="HeldFrom">
/// For a summary, when the first of its deliveries fell due (UTC); <see langword="null"/> otherwise.
/// </param>
/// <param name="Entries">
/// The Alerts: exactly one for an <see cref="NotificationKind.Alert"/> or a
/// <see cref="NotificationKind.Reminder"/>; for a summary one per Alert still open, oldest first.
/// </param>
[GenerateSerializer]
[Alias("coldframe.notification")]
public sealed record Notification(
    [property: Id(0)] string UserId,
    [property: Id(1)] string SiteId,
    [property: Id(2)] NotificationKind Kind,
    [property: Id(3)] DateTimeOffset DueAt,
    [property: Id(4)] DateTimeOffset? HeldFrom,
    [property: Id(5)] IReadOnlyList<NotificationEntry> Entries);
