namespace Coldframe.Server.Notifications.Push;

/// <summary>
/// The push channels' settings (Story 6.5), configuration section <see cref="SectionName"/>. The credentials
/// come from the optional Secret <c>coldframe-push</c>; a provider without them gets no channel, and the Server
/// starts all the same.
/// </summary>
public sealed class PushOptions
{
    /// <summary>
    /// The configuration section.
    /// </summary>
    public const string SectionName = "Push";

    /// <summary>
    /// Apple Push Notification service.
    /// </summary>
    public ApnsOptions Apns { get; set; } = new();

    /// <summary>
    /// Firebase Cloud Messaging.
    /// </summary>
    public FcmOptions Fcm { get; set; } = new();

    /// <summary>
    /// How long one channel may take for one notification, retries included: the User grain waits for it.
    /// </summary>
    public TimeSpan SendBudget { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long one request to a provider may take.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How often a send to one device is tried while the provider fails transiently.
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// How long a channel waits before it tries again, doubled after each try.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// How long the names and values of one notification may take to look up. What was not found by then is
    /// left out of the text.
    /// </summary>
    public TimeSpan LookupBudget { get; set; } = TimeSpan.FromSeconds(2);
}

/// <summary>
/// The APNs provider token credentials and endpoints.
/// </summary>
public sealed class ApnsOptions
{
    /// <summary>
    /// The 10-character ID of the APNs auth key (Secret key <c>apns-key-id</c>).
    /// </summary>
    public string? KeyId { get; set; }

    /// <summary>
    /// The Apple Developer Team ID (Secret key <c>apns-team-id</c>).
    /// </summary>
    public string? TeamId { get; set; }

    /// <summary>
    /// The APNs auth key, a PKCS#8 P-256 private key in PEM as Apple's <c>AuthKey_….p8</c> holds it (Secret
    /// key <c>apns-key.p8</c>).
    /// </summary>
    public string? PrivateKeyPem { get; set; }

    /// <summary>
    /// The app's bundle ID, sent as <c>apns-topic</c>.
    /// </summary>
    public string Topic { get; set; } = "com.escendit.coldframe";

    /// <summary>
    /// APNs for device tokens of the production environment.
    /// </summary>
    public Uri ProductionBaseUrl { get; set; } = new("https://api.push.apple.com");

    /// <summary>
    /// APNs for device tokens of the sandbox environment.
    /// </summary>
    public Uri SandboxBaseUrl { get; set; } = new("https://api.sandbox.push.apple.com");

    /// <summary>
    /// Whether every credential is present.
    /// </summary>
    public bool HasCredentials =>
        !string.IsNullOrWhiteSpace(KeyId) && !string.IsNullOrWhiteSpace(TeamId) && !string.IsNullOrWhiteSpace(PrivateKeyPem);
}

/// <summary>
/// The FCM service account and endpoints.
/// </summary>
public sealed class FcmOptions
{
    /// <summary>
    /// The Firebase service-account key file, as JSON (Secret key <c>fcm-service-account.json</c>).
    /// </summary>
    public string? ServiceAccountJson { get; set; }

    /// <summary>
    /// The FCM HTTP v1 API.
    /// </summary>
    public Uri BaseUrl { get; set; } = new("https://fcm.googleapis.com");

    /// <summary>
    /// Where the service account's access token is requested; the service account's own <c>token_uri</c> when
    /// not set.
    /// </summary>
    public Uri? TokenUrl { get; set; }

    /// <summary>
    /// Whether the credential is present.
    /// </summary>
    public bool HasCredentials => !string.IsNullOrWhiteSpace(ServiceAccountJson);
}
