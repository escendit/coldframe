using Coldframe.Contracts.Events;

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
