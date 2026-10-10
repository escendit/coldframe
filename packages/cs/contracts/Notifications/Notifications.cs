using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Sites;

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
/// The push provider of a device (Story 6.5).
/// </summary>
[GenerateSerializer]
[Alias("coldframe.push-platform")]
public enum PushPlatform
{
    /// <summary>
    /// Apple Push Notification service: an iPhone.
    /// </summary>
    Apns = 0,

    /// <summary>
    /// Firebase Cloud Messaging: an Android phone.
    /// </summary>
    Fcm = 1,
}

/// <summary>
/// The APNs environment a device token belongs to (Story 6.5).
/// </summary>
[GenerateSerializer]
[Alias("coldframe.apns-environment")]
public enum ApnsEnvironment
{
    /// <summary>
    /// A build distributed through TestFlight or the App Store.
    /// </summary>
    Production = 0,

    /// <summary>
    /// A development build.
    /// </summary>
    Sandbox = 1,
}

/// <summary>
/// One device a User registered for push (Story 6.5). The User grain owns it.
/// </summary>
/// <param name="InstallationId">The app's own ID of one installation; a User has one registration per installation.</param>
/// <param name="Platform">The push provider.</param>
/// <param name="Token">The provider's device token.</param>
/// <param name="Environment">The APNs environment of the token; <see langword="null"/> for <see cref="PushPlatform.Fcm"/>.</param>
/// <param name="RegisteredAt">When the token was registered.</param>
[GenerateSerializer]
[Alias("coldframe.push-registration")]
public sealed record PushRegistration(
    [property: Id(0)] string InstallationId,
    [property: Id(1)] PushPlatform Platform,
    [property: Id(2)] string Token,
    [property: Id(3)] ApnsEnvironment? Environment,
    [property: Id(4)] DateTimeOffset RegisteredAt);

/// <summary>
/// The limits of a push registration (Story 6.5).
/// </summary>
public static class PushRegistrationLimits
{
    /// <summary>
    /// The longest installation ID.
    /// </summary>
    public const int MaxInstallationIdLength = 64;

    /// <summary>
    /// The longest device token.
    /// </summary>
    public const int MaxTokenLength = 4096;

    /// <summary>
    /// How many registrations a User keeps: the one registered longest ago makes room for a new one.
    /// </summary>
    public const int MaxRegistrations = 20;

    /// <summary>
    /// Whether <paramref name="installationId"/> is 1 to <see cref="MaxInstallationIdLength"/> characters of
    /// <c>A-Z</c>, <c>a-z</c>, <c>0-9</c>, <c>.</c>, <c>_</c> and <c>-</c>.
    /// </summary>
    public static bool IsInstallationId(string? installationId) =>
        installationId is { Length: > 0 and <= MaxInstallationIdLength }
        && installationId.All(static character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');

    /// <summary>
    /// Whether <paramref name="token"/> is 1 to <see cref="MaxTokenLength"/> printable ASCII characters without a space.
    /// </summary>
    public static bool IsToken(string? token) =>
        token is { Length: > 0 and <= MaxTokenLength } && token.All(static character => character is > ' ' and <= '~');
}

/// <summary>
/// A notification the User grain hands to the Notifier (Story 6.4). The User grain has already decided that it
/// is due, inside the Notification Window and not muted: nothing behind the Notifier filters, delays or
/// schedules.
/// </summary>
/// <remarks>
/// A channel runs inside the User grain's turn and so can never ask that grain for anything (Story 6.5): the
/// grain puts in what only it knows, the push registrations and the time zone and window a text needs.
/// </remarks>
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
/// <param name="Registrations">The User's push registrations; <see langword="null"/> or empty without a device.</param>
/// <param name="TimeZone">The User's IANA time zone, chosen or detected; <see langword="null"/> reads as UTC.</param>
/// <param name="Window">The User's Notification Window, which a summary's footer names; <see langword="null"/> is the default.</param>
[GenerateSerializer]
[Alias("coldframe.notification")]
public sealed record Notification(
    [property: Id(0)] string UserId,
    [property: Id(1)] string SiteId,
    [property: Id(2)] NotificationKind Kind,
    [property: Id(3)] DateTimeOffset DueAt,
    [property: Id(4)] DateTimeOffset? HeldFrom,
    [property: Id(5)] IReadOnlyList<NotificationEntry> Entries,
    [property: Id(6)] IReadOnlyList<PushRegistration>? Registrations = null,
    [property: Id(7)] string? TimeZone = null,
    [property: Id(8)] NotificationWindow? Window = null);
