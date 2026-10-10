using Coldframe.Contracts.Events;
using Coldframe.Contracts.Notifications;

namespace Coldframe.Contracts.Sites;

/// <summary>
/// The User asked to create a Site. Persisted before any call to Keycloak, so a retry with the same
/// idempotency key resumes with the same Site ID.
/// </summary>
/// <param name="IdempotencyKey">The <c>Idempotency-Key</c> of the request.</param>
/// <param name="SiteId">The Site ID (the Phase Two Organization ID) chosen for the request, a UUIDv7.</param>
/// <param name="Name">The requested Site name.</param>
/// <param name="RequestedAt">When the request was received; the key expires 24 h later.</param>
[EventType("user.site-creation-requested")]
[GenerateSerializer]
[Alias("coldframe.user-site-creation-requested")]
public sealed record SiteCreationRequested(
    [property: Id(0)] string IdempotencyKey,
    [property: Id(1)] string SiteId,
    [property: Id(2)] string Name,
    [property: Id(3)] DateTimeOffset RequestedAt);

/// <summary>
/// The Site requested under <paramref name="IdempotencyKey"/> exists and the User is its Owner.
/// </summary>
/// <param name="IdempotencyKey">The <c>Idempotency-Key</c> of the request.</param>
/// <param name="SiteId">The Site ID.</param>
[EventType("user.site-creation-completed")]
[GenerateSerializer]
[Alias("coldframe.user-site-creation-completed")]
public sealed record SiteCreationCompleted([property: Id(0)] string IdempotencyKey, [property: Id(1)] string SiteId);

/// <summary>
/// The User's Role on a Site changed, or the User left it.
/// </summary>
/// <param name="SiteId">The Site ID.</param>
/// <param name="Role">The Role from now on; <see langword="null"/> when the User left the Site or it was deleted.</param>
[EventType("user.site-membership-changed")]
[GenerateSerializer]
[Alias("coldframe.user-site-membership-changed")]
public sealed record SiteMembershipChanged([property: Id(0)] string SiteId, [property: Id(1)] SiteRole? Role);

/// <summary>
/// The User changed the Notification Window (Story 6.3).
/// </summary>
/// <param name="FromMinutes">When the window opens: wall-clock minutes since midnight in the User's time zone.</param>
/// <param name="ToMinutes">When the window closes, after <paramref name="FromMinutes"/>.</param>
/// <param name="ChangedAt">When the change was made.</param>
[EventType("user.notification-window-changed")]
[GenerateSerializer]
[Alias("coldframe.user-notification-window-changed")]
public sealed record NotificationWindowChanged(
    [property: Id(0)] int FromMinutes,
    [property: Id(1)] int ToMinutes,
    [property: Id(2)] DateTimeOffset ChangedAt);

/// <summary>
/// A device or browser reported its time zone while the User had chosen none (Story 6.3). It is a proposal:
/// it gives the User a zone before one is chosen and never confirms it.
/// </summary>
/// <param name="TimeZone">The IANA time zone ID.</param>
/// <param name="DetectedAt">When it was reported.</param>
[EventType("user.time-zone-detected")]
[GenerateSerializer]
[Alias("coldframe.user-time-zone-detected")]
public sealed record TimeZoneDetected([property: Id(0)] string TimeZone, [property: Id(1)] DateTimeOffset DetectedAt);

/// <summary>
/// The User chose a time zone (Story 6.3). A chosen zone is never replaced by a detected one.
/// </summary>
/// <param name="TimeZone">The IANA time zone ID.</param>
/// <param name="ChosenAt">When it was chosen.</param>
[EventType("user.time-zone-chosen")]
[GenerateSerializer]
[Alias("coldframe.user-time-zone-chosen")]
public sealed record TimeZoneChosen([property: Id(0)] string TimeZone, [property: Id(1)] DateTimeOffset ChosenAt);

/// <summary>
/// The User muted a Site or ended the mute (Story 6.3). It affects only this User.
/// </summary>
/// <param name="SiteId">The Site ID.</param>
/// <param name="Muted">Whether the Site is muted from now on.</param>
/// <param name="ChangedAt">When the change was made.</param>
[EventType("user.site-mute-changed")]
[GenerateSerializer]
[Alias("coldframe.user-site-mute-changed")]
public sealed record SiteMuteChanged(
    [property: Id(0)] string SiteId,
    [property: Id(1)] bool Muted,
    [property: Id(2)] DateTimeOffset ChangedAt);

/// <summary>
/// The User changed their own Reminder cadence for a Site (Story 6.3).
/// </summary>
/// <param name="SiteId">The Site ID.</param>
/// <param name="Cadence">The User's cadence from now on; <see langword="null"/> when the User uses the Site setting.</param>
/// <param name="ChangedAt">When the change was made.</param>
[EventType("user.site-reminder-cadence-changed")]
[GenerateSerializer]
[Alias("coldframe.user-site-reminder-cadence-changed")]
public sealed record PersonalReminderCadenceChanged(
    [property: Id(0)] string SiteId,
    [property: Id(1)] ReminderCadence? Cadence,
    [property: Id(2)] DateTimeOffset ChangedAt);

/// <summary>
/// The User grain was handed another Reminder cadence of a Site than the one it held (Story 6.3). The Site
/// grain owns the cadence; this is the User grain's copy of it.
/// </summary>
/// <param name="SiteId">The Site ID.</param>
/// <param name="Cadence">The Site's Reminder cadence.</param>
[EventType("user.site-reminder-cadence-synced")]
[GenerateSerializer]
[Alias("coldframe.user-site-reminder-cadence-synced")]
public sealed record SiteReminderCadenceSynced([property: Id(0)] string SiteId, [property: Id(1)] ReminderCadence Cadence);

/// <summary>
/// The User grain learned of an open Alert on one of its Sites (Story 6.4): the Alert grain told it, or it found
/// the Alert in <see cref="ISiteGrain.OpenAlerts"/> when it joined the Site or reconciled on activation.
/// </summary>
/// <param name="SiteId">The Site the Alert is open on.</param>
/// <param name="Alert">The Alert as its Site lists it.</param>
/// <param name="Told">
/// <see langword="true"/> when the Alert grain told the User: the opening notification is due at
/// <paramref name="RemindFrom"/>. <see langword="false"/> for an Alert that was pulled: it gets no opening
/// notification.
/// </param>
/// <param name="RemindFrom">
/// The due-at (UTC) the Reminders are counted from. When told, the due-at of the opening notification, which is
/// when the User was told. Otherwise the last <c>openedAt + n × interval</c> not after
/// <paramref name="TrackedAt"/>, so the first Reminder is due one interval later.
/// </param>
/// <param name="TrackedAt">When the User grain learned of the Alert.</param>
[EventType("user.alert-tracked")]
[GenerateSerializer]
[Alias("coldframe.user-alert-tracked")]
public sealed record AlertTracked(
    [property: Id(0)] string SiteId,
    [property: Id(1)] SiteAlert Alert,
    [property: Id(2)] bool Told,
    [property: Id(3)] DateTimeOffset RemindFrom,
    [property: Id(4)] DateTimeOffset TrackedAt);

/// <summary>
/// The User grain stopped tracking an Alert (Story 6.4): it closed, or its Site no longer lists it. What was held
/// for it and its Reminder deadline go with it; nothing is notified.
/// </summary>
/// <param name="AlertId">The Alert.</param>
/// <param name="DroppedAt">When the User grain dropped it.</param>
[EventType("user.alert-dropped")]
[GenerateSerializer]
[Alias("coldframe.user-alert-dropped")]
public sealed record AlertDropped([property: Id(0)] Guid AlertId, [property: Id(1)] DateTimeOffset DroppedAt);

/// <summary>
/// A delivery fell due outside the User's Notification Window and is held for the summary of its Site
/// (Story 6.4). The Alert's next Reminder is counted from <paramref name="DueAt"/>, not from the summary.
/// </summary>
/// <param name="SiteId">The Site of the Alert.</param>
/// <param name="AlertId">The Alert the delivery is for.</param>
/// <param name="DueAt">When the delivery fell due (UTC).</param>
/// <param name="WindowOpensAt">The window-opening due-at (UTC): when the User's summaries are due.</param>
[EventType("user.delivery-held")]
[GenerateSerializer]
[Alias("coldframe.user-delivery-held")]
public sealed record DeliveryHeld(
    [property: Id(0)] string SiteId,
    [property: Id(1)] Guid AlertId,
    [property: Id(2)] DateTimeOffset DueAt,
    [property: Id(3)] DateTimeOffset WindowOpensAt);

/// <summary>
/// The Notifier returned for a notification (Story 6.4). Journaled only after it returned, so a delivery the
/// Notifier failed stays due. An <see cref="NotificationKind.Alert"/> or <see cref="NotificationKind.Reminder"/>
/// names its one Alert, whose next Reminder is counted from <paramref name="DueAt"/>; a
/// <see cref="NotificationKind.Summary"/> names every Alert it listed, and nothing is held for them any more.
/// </summary>
/// <param name="SiteId">The Site the notification was about.</param>
/// <param name="Kind">What was sent.</param>
/// <param name="AlertIds">The Alerts the notification listed.</param>
/// <param name="DueAt">When the delivery fell due (UTC); for a summary, when the window opened.</param>
/// <param name="SentAt">When the Notifier sent it (UTC).</param>
[EventType("user.notification-sent")]
[GenerateSerializer]
[Alias("coldframe.user-notification-sent")]
public sealed record NotificationSent(
    [property: Id(0)] string SiteId,
    [property: Id(1)] NotificationKind Kind,
    [property: Id(2)] IReadOnlyList<Guid> AlertIds,
    [property: Id(3)] DateTimeOffset DueAt,
    [property: Id(4)] DateTimeOffset SentAt);

/// <summary>
/// The User grain pulled the open Alerts of a Site it joined (Story 6.4). Until this event follows the
/// <see cref="SiteMembershipChanged"/> that started the Membership, the pull is repeated on every wake.
/// </summary>
/// <param name="SiteId">The Site.</param>
/// <param name="PulledAt">When the Site answered.</param>
[EventType("user.site-alerts-pulled")]
[GenerateSerializer]
[Alias("coldframe.user-site-alerts-pulled")]
public sealed record SiteAlertsPulled([property: Id(0)] string SiteId, [property: Id(1)] DateTimeOffset PulledAt);

/// <summary>
/// Why a push registration ended (Story 6.5).
/// </summary>
[GenerateSerializer]
[Alias("coldframe.push-device-removal-reason")]
public enum PushDeviceRemovalReason
{
    /// <summary>
    /// The app removed it, as at sign-out.
    /// </summary>
    Requested = 0,

    /// <summary>
    /// The provider reported the token as invalid or unregistered.
    /// </summary>
    Invalid = 1,

    /// <summary>
    /// Another installation of the User registered the same token, or the User reached
    /// <see cref="PushRegistrationLimits.MaxRegistrations"/> and this was the one registered longest ago.
    /// </summary>
    Replaced = 2,
}

/// <summary>
/// The User registered a device for push, or the device's token changed (Story 6.5). One installation has one
/// registration: this replaces the one it had.
/// </summary>
/// <param name="InstallationId">The app's own ID of the installation.</param>
/// <param name="Platform">The push provider.</param>
/// <param name="Token">The provider's device token.</param>
/// <param name="Environment">The APNs environment of the token; <see langword="null"/> for <see cref="PushPlatform.Fcm"/>.</param>
/// <param name="RegisteredAt">When it was registered.</param>
[EventType("user.push-device-registered")]
[GenerateSerializer]
[Alias("coldframe.user-push-device-registered")]
public sealed record PushDeviceRegistered(
    [property: Id(0)] string InstallationId,
    [property: Id(1)] PushPlatform Platform,
    [property: Id(2)] string Token,
    [property: Id(3)] ApnsEnvironment? Environment,
    [property: Id(4)] DateTimeOffset RegisteredAt);

/// <summary>
/// A push registration of the User ended (Story 6.5).
/// </summary>
/// <param name="InstallationId">The installation.</param>
/// <param name="Reason">Why it ended.</param>
/// <param name="RemovedAt">When it ended.</param>
[EventType("user.push-device-removed")]
[GenerateSerializer]
[Alias("coldframe.user-push-device-removed")]
public sealed record PushDeviceRemoved(
    [property: Id(0)] string InstallationId,
    [property: Id(1)] PushDeviceRemovalReason Reason,
    [property: Id(2)] DateTimeOffset RemovedAt);
