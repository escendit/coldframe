using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Lots;
using Coldframe.Contracts.Notifications;

namespace Coldframe.Contracts.Sites;

/// <summary>
/// A User, keyed by the OIDC <c>sub</c>. Creates Sites on the User's behalf, idempotently per key.
/// </summary>
[Alias("coldframe.user")]
public interface IUserGrain : IGrainWithStringKey
{
    /// <summary>
    /// Creates a Site named <paramref name="name"/> with the User as its Owner. A repeated call with the
    /// same <paramref name="idempotencyKey"/> within 24 h returns the original result.
    /// </summary>
    /// <param name="idempotencyKey">The request's <c>Idempotency-Key</c>, already validated.</param>
    /// <param name="name">The Site name, already trimmed and validated.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("create-site")]
    Task<SiteCreationResult> CreateSite(string idempotencyKey, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the User's Role on a Site as the Site grain reports it. Idempotent: journals only when the
    /// Role differs from the one the User grain holds.
    /// </summary>
    /// <param name="siteId">The Site ID.</param>
    /// <param name="role">The Role; <see langword="null"/> when the User is no member or the Site is deleted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("sync-site-membership")]
    Task SyncSiteMembership(string siteId, SiteRole? role, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the User's own notification settings (Story 6.3): the Notification Window and the time zone.
    /// A User without any change has the default window and no time zone. Journals nothing.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("get-notification-settings")]
    Task<UserNotificationSettings> GetNotificationSettings(CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes the User's own notification settings (Story 6.3). Each accepted change is one event; a value
    /// that is already in force journals nothing. A chosen time zone always wins; a detected one is kept only
    /// while the User has chosen none. A window that is not valid refuses the whole request.
    /// </summary>
    /// <param name="update">What to change; the time zones are already validated.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("update-notification-settings")]
    Task<UpdateNotificationSettingsResult> UpdateNotificationSettings(UpdateNotificationSettings update, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the User's own notification settings for a Site (Story 6.3). Journals nothing.
    /// </summary>
    /// <param name="siteId">The Site ID.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("get-site-notification-settings")]
    Task<UserSiteNotificationSettings> GetSiteNotificationSettings(string siteId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the User's own mute and Reminder cadence for a Site (Story 6.3). Each value that changes is one
    /// event; a value already in force journals nothing. The caller has checked the Membership (the Edge
    /// policy), so the grain does not ask for one.
    /// </summary>
    /// <param name="siteId">The Site ID.</param>
    /// <param name="muted">Whether the User mutes the Site.</param>
    /// <param name="reminderCadence">The User's own cadence; <see langword="null"/> to use the Site setting.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("set-site-notification-settings")]
    Task<UserSiteNotificationSettings> SetSiteNotificationSettings(
        string siteId,
        bool muted,
        ReminderCadence? reminderCadence,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the Reminder cadence of a Site the User is a member of, as the Site grain holds it (Story 6.3).
    /// Idempotent: journals only when the cadence differs from the one the User grain resolves for the Site
    /// setting (<see cref="ReminderCadence.Daily"/> while it never heard of one).
    /// </summary>
    /// <param name="siteId">The Site ID.</param>
    /// <param name="cadence">The Site's Reminder cadence.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("sync-site-reminder-cadence")]
    Task SyncSiteReminderCadence(string siteId, ReminderCadence cadence, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells the User that an Alert opened on a Site (Story 6.4). Only the Alert grain calls it, for every member
    /// its Site grain named, again until it returns. Idempotent: an Alert the User already tracks journals
    /// nothing, and a Site that is not in the User's own Site set is acknowledged and nothing is stored (the
    /// pull on joining covers it). Otherwise the User grain journals <see cref="AlertTracked"/> and the Alert
    /// is due at once; the User grain alone decides when the notification is sent.
    /// </summary>
    /// <param name="siteId">The Site the Alert opened on.</param>
    /// <param name="alert">The Alert that opened.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("alert-opened")]
    Task AlertOpened(string siteId, SiteAlert alert, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells the User that an Alert closed (Story 6.4). Only the Alert grain calls it, again until it returns.
    /// Idempotent: an Alert the User does not track journals nothing; otherwise the User grain journals
    /// <see cref="AlertDropped"/>, which also drops what was held for it. Closing never notifies.
    /// </summary>
    /// <param name="siteId">The Site the Alert was opened on.</param>
    /// <param name="alertId">The Alert that closed.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("alert-closed")]
    Task AlertClosed(string siteId, Guid alertId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a device of the User for push (Story 6.5); the User grain owns the registrations. Registering
    /// the same installation again replaces its token (<see cref="PushDeviceRegistered"/>); a registration that
    /// is already in force journals nothing. A token another installation holds moves to this one, and beyond
    /// <see cref="PushRegistrationLimits.MaxRegistrations"/> the one registered longest ago is removed. A
    /// registration that is not valid journals nothing.
    /// </summary>
    /// <param name="request">The registration.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("register-push-device")]
    Task<PushRegistrationOutcome> RegisterPushDevice(RegisterPushDevice request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the User's push registration of an installation (Story 6.5), as the apps do at sign-out.
    /// Idempotent: an installation the User holds no registration for journals nothing.
    /// </summary>
    /// <param name="installationId">The installation.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>Whether a registration was removed.</returns>
    [Alias("remove-push-device")]
    Task<bool> RemovePushDevice(string installationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the User's push registrations, by installation ID (Story 6.5). Journals nothing. No endpoint
    /// serves it: a token never leaves the Server.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("get-push-registrations")]
    Task<IReadOnlyList<PushRegistration>> GetPushRegistrations(CancellationToken cancellationToken = default);
}

/// <summary>
/// A device registration as <see cref="IUserGrain.RegisterPushDevice"/> takes it (Story 6.5).
/// </summary>
/// <param name="InstallationId">The app's own ID of the installation.</param>
/// <param name="Platform">The push provider.</param>
/// <param name="Token">The provider's device token.</param>
/// <param name="Environment">The APNs environment; required for <see cref="PushPlatform.Apns"/>, absent otherwise.</param>
[GenerateSerializer]
[Alias("coldframe.register-push-device")]
public sealed record RegisterPushDevice(
    [property: Id(0)] string InstallationId,
    [property: Id(1)] PushPlatform Platform,
    [property: Id(2)] string Token,
    [property: Id(3)] ApnsEnvironment? Environment = null);

/// <summary>
/// How <see cref="IUserGrain.RegisterPushDevice"/> ended.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.push-registration-outcome")]
public enum PushRegistrationOutcome
{
    /// <summary>
    /// The registration is in force and was journaled.
    /// </summary>
    Registered = 0,

    /// <summary>
    /// The same registration was in force already. Nothing was journaled.
    /// </summary>
    Unchanged = 1,

    /// <summary>
    /// The installation ID, the token, the platform or the environment is not valid. Nothing was journaled.
    /// </summary>
    Invalid = 2,
}

/// <summary>
/// A Site, keyed by its Site ID (the Phase Two Organization ID). The only writer of the Site's
/// Memberships and Roles, in its journal and in Phase Two.
/// </summary>
[Alias("coldframe.site")]
public interface ISiteGrain : IGrainWithStringKey
{
    /// <summary>
    /// Makes the Site <see cref="SiteLifecycle.Active"/> with <paramref name="ownerId"/> as its Owner, and
    /// brings the identity projection up to date before it returns. Idempotent for the same Owner.
    /// </summary>
    /// <param name="name">The Site name.</param>
    /// <param name="ownerId">The User ID of the Owner.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("initialize")]
    Task<SiteInitializationResult> Initialize(string name, string ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the Organization's current roster from Keycloak and journals only the differences to the
    /// Site's own state (AD-3, AD-20). It only reads Keycloak. An <see cref="SiteLifecycle.Active"/> Site
    /// keeps its Owners when Keycloak shows none, and becomes <see cref="SiteLifecycle.Deleted"/> when the
    /// Organization is gone. Any other Site ignores the call. Brings the identity projection up to date
    /// before it returns.
    /// </summary>
    /// <param name="expectation">What the Keycloak event says the roster now shows, or <see langword="null"/>.</param>
    /// <param name="acceptUnconfirmed">
    /// <see langword="true"/> applies the pulled roster even when it contradicts <paramref name="expectation"/>.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("reconcile")]
    Task<SiteReconciliationResult> Reconcile(
        RosterExpectation? expectation,
        bool acceptUnconfirmed,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames an <see cref="SiteLifecycle.Active"/> Site: sets the Organization's <c>displayName</c> in
    /// Keycloak, then journals the rename and brings the identity projection up to date (AD-3). The current
    /// name changes nothing and calls nothing.
    /// </summary>
    /// <param name="name">The Site name, already trimmed and validated.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("rename")]
    Task<SiteRenameResult> Rename(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a Lot on an <see cref="SiteLifecycle.Active"/> Site, idempotently per caller and key for
    /// 24 h: the request is journaled with its new Lot ID before the Lot grain is called, so a retry
    /// resumes with the same Lot.
    /// </summary>
    /// <param name="callerId">The caller's User ID (the OIDC <c>sub</c>).</param>
    /// <param name="idempotencyKey">The request's <c>Idempotency-Key</c>, already validated.</param>
    /// <param name="name">The Lot name, already trimmed and validated.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("create-lot")]
    Task<LotCreationResult> CreateLot(string callerId, string idempotencyKey, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a Device to the roster of an <see cref="SiteLifecycle.Active"/> Site (AD-18). Only the Device
    /// grain calls it, before it journals its enrolment. A key used within 24 h for another Device answers
    /// <see cref="DeviceRegistrationOutcome.IdempotencyKeyReused"/>; a Device already on the roster journals
    /// nothing. Nothing is persisted on a refusal. The reply carries the Site's Pause (AD-8).
    /// </summary>
    /// <param name="deviceId">The Device ID, 16 lowercase hex digits.</param>
    /// <param name="kind">Hub or Node.</param>
    /// <param name="idempotencyKey">
    /// The request's idempotency key, scoped to its caller: <c>{sub}:{Idempotency-Key}</c>, as for Lots.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("register-device")]
    Task<DeviceRegistrationResult> RegisterDevice(
        string deviceId,
        DeviceKind kind,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that an Alert opened on the Site (Story 6.1). Only the Alert grain calls it, again until it
    /// returns. Idempotent: an Alert the Site already lists journals nothing; otherwise the Site journals
    /// <see cref="SiteAlertOpened"/>. A Site that is not <see cref="SiteLifecycle.Active"/> keeps no Alerts and
    /// journals nothing. The Site grain never calls User grains: the answer names the Site's current members, and
    /// the Alert grain tells each member's User grain (Story 6.4).
    /// </summary>
    /// <param name="alert">The Alert that opened.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("alert-opened")]
    Task<SiteAlertReportResult> AlertOpened(SiteAlert alert, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that an Alert closed (Story 6.1). Only the Alert grain calls it, after the Site acknowledged the
    /// open, again until it returns. Idempotent: an Alert the Site does not list journals nothing; otherwise
    /// the Site journals <see cref="SiteAlertClosed"/>. The answer names the Site's current members either way,
    /// whom the Alert grain tells (Story 6.4).
    /// </summary>
    /// <param name="alertId">The Alert that closed.</param>
    /// <param name="reason">Why it closed.</param>
    /// <param name="closedAt">When it closed.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("alert-closed")]
    Task<SiteAlertReportResult> AlertClosed(Guid alertId, AlertCloseReason reason, DateTimeOffset closedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the Site's open Alerts, oldest first, from the state the grain replays from its own stream
    /// (AD-1). Empty for a Site that is not <see cref="SiteLifecycle.Active"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("open-alerts")]
    Task<IReadOnlyList<SiteAlert>> OpenAlerts(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the Site's Reminder cadence (Story 6.3): <see cref="ReminderCadence.Daily"/> until it was
    /// changed, <see langword="null"/> for a Site that is not <see cref="SiteLifecycle.Active"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("get-reminder-cadence")]
    Task<ReminderCadence?> GetReminderCadence(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the Reminder cadence of an <see cref="SiteLifecycle.Active"/> Site (Story 6.3) and journals
    /// <see cref="SiteReminderCadenceChanged"/>; the cadence already in force journals nothing. The result
    /// names the members, because the Site grain never calls User grains: the caller hands them the cadence.
    /// </summary>
    /// <param name="cadence">The cadence.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("set-reminder-cadence")]
    Task<SiteReminderCadenceResult> SetReminderCadence(ReminderCadence cadence, CancellationToken cancellationToken = default);
}

/// <summary>
/// How often a Reminder repeats while a Threshold Alert stays open (Story 6.3). There is no "never".
/// </summary>
[GenerateSerializer]
[Alias("coldframe.reminder-cadence")]
public enum ReminderCadence
{
    /// <summary>
    /// Once per day: the default of a Site.
    /// </summary>
    Daily = 0,

    /// <summary>
    /// Every 2 days.
    /// </summary>
    Every2Days = 1,
}

/// <summary>
/// A daily Notification Window as wall-clock minutes since midnight in the User's time zone (Story 6.3). It
/// lies within one day: a window across midnight does not exist.
/// </summary>
/// <param name="FromMinutes">When the window opens, 0 to 1438.</param>
/// <param name="ToMinutes">When the window closes, after <paramref name="FromMinutes"/>, at most 1439.</param>
[GenerateSerializer]
[Alias("coldframe.notification-window")]
public sealed record NotificationWindow([property: Id(0)] int FromMinutes, [property: Id(1)] int ToMinutes)
{
    /// <summary>
    /// The last minute of a day, 23:59.
    /// </summary>
    public const int LastMinute = (24 * 60) - 1;

    /// <summary>
    /// When a window closes that names only its start: 22:00.
    /// </summary>
    public const int DefaultToMinutes = 22 * 60;

    /// <summary>
    /// The window of a User who never changed it: 07:00 to 22:00.
    /// </summary>
    public static NotificationWindow Default { get; } = new(7 * 60, DefaultToMinutes);

    /// <summary>
    /// Whether both times are minutes of one day and the window opens before it closes.
    /// </summary>
    public bool IsValid => FromMinutes >= 0 && FromMinutes < ToMinutes && ToMinutes <= LastMinute;
}

/// <summary>
/// A User's own notification settings (Story 6.3).
/// </summary>
/// <param name="Window">The Notification Window.</param>
/// <param name="TimeZone">The IANA time zone the User chose, else the detected one, else <see langword="null"/>.</param>
/// <param name="TimeZoneConfirmed">Whether the User chose <paramref name="TimeZone"/>.</param>
[GenerateSerializer]
[Alias("coldframe.user-notification-settings")]
public sealed record UserNotificationSettings(
    [property: Id(0)] NotificationWindow Window,
    [property: Id(1)] string? TimeZone,
    [property: Id(2)] bool TimeZoneConfirmed);

/// <summary>
/// A change to a User's own notification settings. A part that is <see langword="null"/> stays as it is.
/// </summary>
/// <param name="Window">The new Notification Window.</param>
/// <param name="TimeZone">The IANA time zone the User chose.</param>
/// <param name="DetectedTimeZone">The IANA time zone a device or browser reports.</param>
[GenerateSerializer]
[Alias("coldframe.update-notification-settings")]
public sealed record UpdateNotificationSettings(
    [property: Id(0)] NotificationWindow? Window = null,
    [property: Id(1)] string? TimeZone = null,
    [property: Id(2)] string? DetectedTimeZone = null);

/// <summary>
/// How a change to a User's notification settings ended.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.notification-settings-outcome")]
public enum NotificationSettingsOutcome
{
    /// <summary>
    /// At least one value changed and was journaled.
    /// </summary>
    Changed = 0,

    /// <summary>
    /// Every value was in force already. Nothing was journaled.
    /// </summary>
    Unchanged = 1,

    /// <summary>
    /// The window does not open before it closes within one day. Nothing was journaled.
    /// </summary>
    InvalidWindow = 2,

    /// <summary>
    /// A time zone is empty or longer than <see cref="UserNotificationLimits.MaxTimeZoneLength"/>. Nothing was journaled.
    /// </summary>
    InvalidTimeZone = 3,
}

/// <summary>
/// The limits of a User's notification settings.
/// </summary>
public static class UserNotificationLimits
{
    /// <summary>
    /// The longest time zone ID.
    /// </summary>
    public const int MaxTimeZoneLength = 64;
}

/// <summary>
/// The result of <see cref="IUserGrain.UpdateNotificationSettings"/>: the outcome and the settings in force.
/// </summary>
/// <param name="Outcome">How the change ended.</param>
/// <param name="Settings">The settings in force afterwards.</param>
[GenerateSerializer]
[Alias("coldframe.update-notification-settings-result")]
public sealed record UpdateNotificationSettingsResult(
    [property: Id(0)] NotificationSettingsOutcome Outcome,
    [property: Id(1)] UserNotificationSettings Settings);

/// <summary>
/// A User's own notification settings for one Site (Story 6.3).
/// </summary>
/// <param name="Muted">Whether the User muted the Site.</param>
/// <param name="ReminderCadence">The User's own cadence; <see langword="null"/> when the User uses the Site setting.</param>
/// <param name="ResolvedReminderCadence">
/// The cadence the User grain reminds at: the User's own, else the Site's as the grain last heard it, else
/// <see cref="Sites.ReminderCadence.Daily"/>.
/// </param>
[GenerateSerializer]
[Alias("coldframe.user-site-notification-settings")]
public sealed record UserSiteNotificationSettings(
    [property: Id(0)] bool Muted,
    [property: Id(1)] ReminderCadence? ReminderCadence,
    [property: Id(2)] ReminderCadence ResolvedReminderCadence);

/// <summary>
/// How a change of a Site's Reminder cadence ended.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.site-reminder-cadence-outcome")]
public enum SiteReminderCadenceOutcome
{
    /// <summary>
    /// The Site has the new cadence in its journal.
    /// </summary>
    Changed = 0,

    /// <summary>
    /// The Site already had the cadence. Nothing was journaled.
    /// </summary>
    Unchanged = 1,

    /// <summary>
    /// The Site is not <see cref="SiteLifecycle.Active"/>. Nothing changed.
    /// </summary>
    NotFound = 2,
}

/// <summary>
/// The result of <see cref="ISiteGrain.SetReminderCadence"/>.
/// </summary>
/// <param name="Outcome">How the change ended.</param>
/// <param name="Cadence">The Site's cadence afterwards.</param>
/// <param name="Members">The User IDs of the Site's members, who are to be handed the cadence.</param>
[GenerateSerializer]
[Alias("coldframe.site-reminder-cadence-result")]
public sealed record SiteReminderCadenceResult(
    [property: Id(0)] SiteReminderCadenceOutcome Outcome,
    [property: Id(1)] ReminderCadence Cadence,
    [property: Id(2)] IReadOnlyList<string> Members);

/// <summary>
/// How a Site creation ended.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.site-creation-outcome")]
public enum SiteCreationOutcome
{
    /// <summary>
    /// The Site exists; <see cref="SiteCreationResult.Site"/> describes it.
    /// </summary>
    Created = 0,

    /// <summary>
    /// The key was used for a request with a different name within 24 h. Nothing was created.
    /// </summary>
    IdempotencyKeyReused = 1,

    /// <summary>
    /// Keycloak could not be reached. The request stays pending; a retry with the same key resumes it.
    /// </summary>
    IdentityProviderUnavailable = 2,
}

/// <summary>
/// The result of <see cref="IUserGrain.CreateSite"/>.
/// </summary>
/// <param name="Outcome">How the creation ended.</param>
/// <param name="Site">The Site, when <paramref name="Outcome"/> is <see cref="SiteCreationOutcome.Created"/>.</param>
[GenerateSerializer]
[Alias("coldframe.site-creation-result")]
public sealed record SiteCreationResult([property: Id(0)] SiteCreationOutcome Outcome, [property: Id(1)] SiteSummary? Site = null);

/// <summary>
/// A Site as its caller sees it.
/// </summary>
/// <param name="Id">The Site ID.</param>
/// <param name="Name">The Site name.</param>
/// <param name="Role">The caller's Role on the Site.</param>
[GenerateSerializer]
[Alias("coldframe.site-summary")]
public sealed record SiteSummary([property: Id(0)] string Id, [property: Id(1)] string Name, [property: Id(2)] SiteRole Role);

/// <summary>
/// How a Site initialization ended.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.site-initialization-outcome")]
public enum SiteInitializationOutcome
{
    /// <summary>
    /// The Site is active with the requested Owner, now or already.
    /// </summary>
    Initialized = 0,

    /// <summary>
    /// Keycloak could not be reached. Nothing was journaled; calling again resumes.
    /// </summary>
    IdentityProviderUnavailable = 1,

    /// <summary>
    /// The Site is already active with another Owner, or deleted. Nothing changed.
    /// </summary>
    Conflict = 2,
}

/// <summary>
/// The result of <see cref="ISiteGrain.Initialize"/>.
/// </summary>
/// <param name="Outcome">How the initialization ended.</param>
/// <param name="Name">The Site name, when <paramref name="Outcome"/> is <see cref="SiteInitializationOutcome.Initialized"/>.</param>
[GenerateSerializer]
[Alias("coldframe.site-initialization-result")]
public sealed record SiteInitializationResult([property: Id(0)] SiteInitializationOutcome Outcome, [property: Id(1)] string? Name = null);

/// <summary>
/// How a Site reconciliation ended.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.site-reconciliation-outcome")]
public enum SiteReconciliationOutcome
{
    /// <summary>
    /// The Site is not <see cref="SiteLifecycle.Active"/>. Keycloak was not called; nothing changed.
    /// </summary>
    Ignored = 0,

    /// <summary>
    /// The Site already matches Keycloak. Nothing was journaled.
    /// </summary>
    Unchanged = 1,

    /// <summary>
    /// The differences were journaled.
    /// </summary>
    Changed = 2,

    /// <summary>
    /// Keycloak does not show yet what the event says. Nothing was journaled; calling again later resumes.
    /// </summary>
    NotYetVisible = 3,

    /// <summary>
    /// Keycloak could not be reached. Nothing was journaled; calling again resumes.
    /// </summary>
    IdentityProviderUnavailable = 4,
}

/// <summary>
/// The result of <see cref="ISiteGrain.Reconcile"/>: the outcome and the Site's state afterwards.
/// </summary>
/// <param name="Outcome">How the reconciliation ended.</param>
/// <param name="Lifecycle">The Site's lifecycle.</param>
/// <param name="Members">Each member's Role, by User ID.</param>
/// <param name="FormerMembers">The User IDs that held a Role on the Site and hold none now.</param>
/// <param name="ReminderCadence">The Site's Reminder cadence, which every member's User grain is handed (Story 6.3).</param>
[GenerateSerializer]
[Alias("coldframe.site-reconciliation-result")]
public sealed record SiteReconciliationResult(
    [property: Id(0)] SiteReconciliationOutcome Outcome,
    [property: Id(1)] SiteLifecycle Lifecycle,
    [property: Id(2)] IReadOnlyDictionary<string, SiteRole> Members,
    [property: Id(3)] IReadOnlyList<string> FormerMembers,
    [property: Id(4)] ReminderCadence ReminderCadence = ReminderCadence.Daily);

/// <summary>
/// What a Keycloak event says about an Organization.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.roster-expectation-kind")]
public enum RosterExpectationKind
{
    /// <summary>
    /// The Organization no longer exists.
    /// </summary>
    OrganizationAbsent = 0,

    /// <summary>
    /// <see cref="RosterExpectation.UserId"/> is a member.
    /// </summary>
    MemberPresent = 1,

    /// <summary>
    /// <see cref="RosterExpectation.UserId"/> is no member.
    /// </summary>
    MemberAbsent = 2,

    /// <summary>
    /// <see cref="RosterExpectation.UserId"/> holds the Organization role of <see cref="RosterExpectation.Role"/>.
    /// </summary>
    RoleHeld = 3,

    /// <summary>
    /// <see cref="RosterExpectation.UserId"/> does not hold the Organization role of <see cref="RosterExpectation.Role"/>.
    /// </summary>
    RoleNotHeld = 4,
}

/// <summary>
/// What a Keycloak event says the Organization's roster now shows. It only tells a reconciliation whether
/// its pull ran too early; the pulled roster is the truth.
/// </summary>
/// <param name="Kind">What is expected.</param>
/// <param name="UserId">The User the expectation is about, if any.</param>
/// <param name="Role">The Role whose Organization role the expectation is about, if any.</param>
[GenerateSerializer]
[Alias("coldframe.roster-expectation")]
public sealed record RosterExpectation(
    [property: Id(0)] RosterExpectationKind Kind,
    [property: Id(1)] string? UserId = null,
    [property: Id(2)] SiteRole? Role = null);

/// <summary>
/// How a Site rename ended.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.site-rename-outcome")]
public enum SiteRenameOutcome
{
    /// <summary>
    /// The Site has the new name, in Keycloak and in its journal.
    /// </summary>
    Renamed = 0,

    /// <summary>
    /// The Site already had the name. Keycloak was not called; nothing was journaled.
    /// </summary>
    Unchanged = 1,

    /// <summary>
    /// The Site is not <see cref="SiteLifecycle.Active"/>. Nothing changed.
    /// </summary>
    NotFound = 2,

    /// <summary>
    /// Keycloak could not be reached. Nothing was journaled; the Site keeps its name.
    /// </summary>
    IdentityProviderUnavailable = 3,
}

/// <summary>
/// The result of <see cref="ISiteGrain.Rename"/>.
/// </summary>
/// <param name="Outcome">How the rename ended.</param>
/// <param name="Name">The Site name afterwards, unless <paramref name="Outcome"/> is <see cref="SiteRenameOutcome.NotFound"/>.</param>
[GenerateSerializer]
[Alias("coldframe.site-rename-result")]
public sealed record SiteRenameResult([property: Id(0)] SiteRenameOutcome Outcome, [property: Id(1)] string? Name = null);

/// <summary>
/// How a Lot creation ended.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.lot-creation-outcome")]
public enum LotCreationOutcome
{
    /// <summary>
    /// The Lot exists; <see cref="LotCreationResult.Lot"/> describes it.
    /// </summary>
    Created = 0,

    /// <summary>
    /// The key was used by the same caller for a different name within 24 h. Nothing was created.
    /// </summary>
    IdempotencyKeyReused = 1,

    /// <summary>
    /// The Site is not <see cref="SiteLifecycle.Active"/>. Nothing was created.
    /// </summary>
    NotFound = 2,
}

/// <summary>
/// The result of <see cref="ISiteGrain.CreateLot"/>.
/// </summary>
/// <param name="Outcome">How the creation ended.</param>
/// <param name="Lot">The Lot, when <paramref name="Outcome"/> is <see cref="LotCreationOutcome.Created"/>.</param>
[GenerateSerializer]
[Alias("coldframe.lot-creation-result")]
public sealed record LotCreationResult([property: Id(0)] LotCreationOutcome Outcome, [property: Id(1)] LotSummary? Lot = null);

/// <summary>
/// How a Device registration ended.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.device-registration-outcome")]
public enum DeviceRegistrationOutcome
{
    /// <summary>
    /// The Device is on the Site's roster, now or already.
    /// </summary>
    Registered = 0,

    /// <summary>
    /// The Site is not <see cref="SiteLifecycle.Active"/>. Nothing was journaled.
    /// </summary>
    NotFound = 1,

    /// <summary>
    /// The caller used the key within 24 h for another Device. Nothing was journaled.
    /// </summary>
    IdempotencyKeyReused = 2,
}

/// <summary>
/// The Site's Pause as a joining Device receives it (AD-8). Site Pause arrives in Epic 8; until then a Site
/// is never paused.
/// </summary>
/// <param name="Paused">Whether the Site is paused.</param>
/// <param name="EndsAt">When the Pause ends, if it has an end.</param>
[GenerateSerializer]
[Alias("coldframe.site-pause")]
public sealed record SitePause([property: Id(0)] bool Paused, [property: Id(1)] DateTimeOffset? EndsAt = null)
{
    /// <summary>
    /// A Site that is not paused.
    /// </summary>
    public static SitePause NotPaused { get; } = new(false);
}

/// <summary>
/// The result of <see cref="ISiteGrain.RegisterDevice"/>.
/// </summary>
/// <param name="Outcome">How the registration ended.</param>
/// <param name="Pause">The Site's Pause, when <paramref name="Outcome"/> is <see cref="DeviceRegistrationOutcome.Registered"/>.</param>
[GenerateSerializer]
[Alias("coldframe.device-registration-result")]
public sealed record DeviceRegistrationResult(
    [property: Id(0)] DeviceRegistrationOutcome Outcome,
    [property: Id(1)] SitePause? Pause = null);

/// <summary>
/// An open Alert as its Site lists it (Story 6.1).
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
[Alias("coldframe.site-alert")]
public sealed record SiteAlert(
    [property: Id(0)] Guid AlertId,
    [property: Id(1)] AlertKind Kind,
    [property: Id(2)] ThresholdSide? Side,
    [property: Id(3)] string LotId,
    [property: Id(4)] Guid SensorId,
    [property: Id(5)] string DeviceId,
    [property: Id(6)] string Quantity,
    [property: Id(7)] DateTimeOffset OpenedAt);

/// <summary>
/// The Site grain's answer to an Alert report (Story 6.4): the Site knows the open or the close, and these are
/// the members the Alert grain is to tell, because the Site grain never calls User grains.
/// </summary>
/// <param name="Members">
/// The User IDs of the Site's current members, in ordinal order; empty for a Site that is not
/// <see cref="SiteLifecycle.Active"/>.
/// </param>
[GenerateSerializer]
[Alias("coldframe.site-alert-report-result")]
public sealed record SiteAlertReportResult([property: Id(0)] IReadOnlyList<string> Members)
{
    /// <summary>
    /// The answer of a Site that has nobody to tell.
    /// </summary>
    public static SiteAlertReportResult Nobody { get; } = new([]);
}
