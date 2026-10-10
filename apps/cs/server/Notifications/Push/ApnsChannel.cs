using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coldframe.Contracts.Notifications;
using Microsoft.Extensions.Options;

namespace Coldframe.Server.Notifications.Push;

/// <summary>
/// The APNs provider token (ES256, RFC 7519): signed with the auth key of the Secret <c>coldframe-push</c> and
/// reused for <see cref="Lifetime"/>, timed on the injected <see cref="TimeProvider"/>. APNs refuses a token
/// older than an hour and one that is renewed more often than every 20 minutes.
/// </summary>
public sealed class ApnsProviderToken : IDisposable
{
    /// <summary>
    /// How long one token is used.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(45);

    private readonly Lock _lock = new();
    private readonly ECDsa _key;
    private readonly string _keyId;
    private readonly string _teamId;
    private readonly TimeProvider _clock;
    private string? _token;
    private DateTimeOffset _issuedAt;

    /// <summary>
    /// Creates the token source from <paramref name="options"/>.
    /// </summary>
    /// <exception cref="ArgumentException">A credential is missing or the key is no P-256 private key in PEM.</exception>
    public ApnsProviderToken(ApnsOptions options, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);

        if (!options.HasCredentials)
        {
            throw new ArgumentException("The APNs key ID, team ID and auth key are required.", nameof(options));
        }

        _key = ECDsa.Create();

        try
        {
            _key.ImportFromPem(options.PrivateKeyPem);

            if (_key.KeySize != 256)
            {
                throw new ArgumentException("The APNs auth key must be a P-256 key.", nameof(options));
            }
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            _key.Dispose();
            throw new ArgumentException("The APNs auth key is no P-256 private key in PEM.", nameof(options), exception);
        }

        _keyId = options.KeyId!.Trim();
        _teamId = options.TeamId!.Trim();
        _clock = clock;
    }

    /// <summary>
    /// The current token, a new one when the last is older than <see cref="Lifetime"/>.
    /// </summary>
    public string Current()
    {
        lock (_lock)
        {
            var now = _clock.GetUtcNow();

            if (_token is null || now - _issuedAt >= Lifetime || now < _issuedAt)
            {
                _issuedAt = now;
                _token = Sign(now);
            }

            return _token;
        }
    }

    /// <summary>
    /// Forgets <paramref name="token"/> when it is still the current one: APNs said it expired.
    /// </summary>
    public void Invalidate(string token)
    {
        lock (_lock)
        {
            if (string.Equals(_token, token, StringComparison.Ordinal))
            {
                _token = null;
            }
        }
    }

    /// <inheritdoc />
    public void Dispose() => _key.Dispose();

    private string Sign(DateTimeOffset issuedAt)
    {
        var header = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new JsonObject { ["alg"] = "ES256", ["kid"] = _keyId }));
        var claims = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new JsonObject { ["iss"] = _teamId, ["iat"] = issuedAt.ToUnixTimeSeconds() }));
        var signature = _key.SignData(Encoding.ASCII.GetBytes($"{header}.{claims}"), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return $"{header}.{claims}.{Base64Url.EncodeToString(signature)}";
    }
}

/// <summary>
/// The APNs channel (Story 6.5): one HTTP/2 request per iPhone registration, to the environment the token
/// belongs to. The payload is the contract of <c>packages/asyncapi</c>: the text, the Site as
/// <c>thread-id</c>, the standard interruption level, no badge, and the routing data under <c>coldframe</c>.
/// </summary>
public sealed class ApnsChannel(
    IHttpClientFactory httpClientFactory,
    ApnsProviderToken providerToken,
    PushContentBuilder content,
    IOptions<PushOptions> options,
    TimeProvider clock,
    ILogger<ApnsChannel> logger) : PushChannel(content, options, clock, logger)
{
    /// <summary>
    /// The named HTTP client of the channel.
    /// </summary>
    public const string HttpClientName = "push-apns";

    /// <summary>
    /// How long APNs keeps trying to deliver a push to a phone it cannot reach.
    /// </summary>
    public static readonly TimeSpan Expiry = TimeSpan.FromDays(1);

    /// <inheritdoc />
    protected override PushPlatform Platform => PushPlatform.Apns;

    /// <summary>
    /// The APNs payload of <paramref name="push"/>, as the contract fixes it.
    /// </summary>
    public static JsonObject Payload(PushContent push)
    {
        ArgumentNullException.ThrowIfNull(push);

        var alert = new JsonObject { ["title"] = push.Title };

        if (push.Body is not null)
        {
            alert["body"] = push.Body;
        }

        var route = new JsonObject { ["kind"] = push.Kind, ["siteId"] = push.SiteId };

        if (push.LotId is not null)
        {
            route["lotId"] = push.LotId;
        }

        if (push.AlertId is { } alertId)
        {
            route["alertId"] = alertId.ToString("D");
        }

        route["collapseId"] = push.CollapseId;

        // No badge: Coldframe never sets the app-icon badge (UX-DR121).
        return new JsonObject
        {
            ["aps"] = new JsonObject
            {
                ["alert"] = alert,
                ["sound"] = "default",
                ["thread-id"] = push.SiteId,
                ["interruption-level"] = "active",
            },
            ["coldframe"] = route,
        };
    }

    /// <inheritdoc />
    protected override async Task<PushSendResult> SendOnceAsync(
        PushRegistration registration,
        PushContent push,
        DateTimeOffset sentAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(push);

        var apns = Options.Apns;
        var baseUrl = registration.Environment == ApnsEnvironment.Sandbox ? apns.SandboxBaseUrl : apns.ProductionBaseUrl;
        var token = providerToken.Current();

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUrl, $"/3/device/{Uri.EscapeDataString(registration.Token)}"))
        {
            // APNs speaks HTTP/2 only.
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
            Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(Payload(push))),
        };
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.Authorization = new("bearer", token);
        request.Headers.TryAddWithoutValidation("apns-push-type", "alert");
        request.Headers.TryAddWithoutValidation("apns-priority", "10");
        request.Headers.TryAddWithoutValidation("apns-topic", apns.Topic);
        request.Headers.TryAddWithoutValidation("apns-collapse-id", push.CollapseId);
        request.Headers.TryAddWithoutValidation("apns-expiration", (sentAt + Expiry).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));

        using var client = httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            return new PushSendResult(PushSendOutcome.Delivered);
        }

        var reason = await ReasonAsync(response, cancellationToken).ConfigureAwait(false);
        var detail = string.Create(CultureInfo.InvariantCulture, $"APNs {(int)response.StatusCode} {reason}");

        if (response.StatusCode == HttpStatusCode.Gone || reason is "BadDeviceToken" or "DeviceTokenNotForTopic" or "Unregistered")
        {
            return new PushSendResult(PushSendOutcome.InvalidToken, detail);
        }

        if (reason == "ExpiredProviderToken")
        {
            // A new token is signed for the next try.
            providerToken.Invalidate(token);
            return new PushSendResult(PushSendOutcome.Transient, detail);
        }

        return (int)response.StatusCode is 429 or >= 500
            ? new PushSendResult(PushSendOutcome.Transient, detail)
            : new PushSendResult(PushSendOutcome.Refused, detail);
    }

    // APNs answers an error with {"reason": "..."}.
    private static async Task<string> ReasonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return JsonNode.Parse(body) is JsonObject error && error["reason"] is JsonValue reason && reason.TryGetValue<string>(out var text)
                ? text
                : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }
}
