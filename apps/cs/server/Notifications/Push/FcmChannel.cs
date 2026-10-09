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
/// The Firebase service account of the Secret <c>coldframe-push</c>: it signs an RS256 assertion (RFC 7523),
/// trades it for an OAuth access token with the scope <c>firebase.messaging</c> and reuses that token until
/// shortly before it expires, timed on the injected <see cref="TimeProvider"/>.
/// </summary>
public sealed class FcmServiceAccount : IDisposable
{
    /// <summary>
    /// The named HTTP client of the token request and of the channel.
    /// </summary>
    public const string HttpClientName = "push-fcm";

    /// <summary>
    /// The OAuth scope of FCM.
    /// </summary>
    public const string Scope = "https://www.googleapis.com/auth/firebase.messaging";

    /// <summary>
    /// A token is renewed this long before it expires.
    /// </summary>
    public static readonly TimeSpan RenewBeforeExpiry = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly RSA _key;
    private readonly string _clientEmail;
    private readonly string? _keyId;
    private readonly Uri _tokenUrl;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _clock;
    private string? _token;
    private DateTimeOffset _renewAt;

    /// <summary>
    /// Creates the service account from <paramref name="options"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The service-account JSON is missing, is no JSON, or lacks <c>project_id</c>, <c>client_email</c> or an RSA
    /// <c>private_key</c> in PEM.
    /// </exception>
    public FcmServiceAccount(FcmOptions options, IHttpClientFactory httpClientFactory, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(clock);

        JsonObject? account;

        try
        {
            account = options.HasCredentials ? JsonNode.Parse(options.ServiceAccountJson!) as JsonObject : null;
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("The FCM service account is no JSON.", nameof(options), exception);
        }

        if (account is null
            || Text(account, "project_id") is not { Length: > 0 } projectId
            || Text(account, "client_email") is not { Length: > 0 } clientEmail
            || Text(account, "private_key") is not { Length: > 0 } privateKey)
        {
            throw new ArgumentException("The FCM service account needs project_id, client_email and private_key.", nameof(options));
        }

        var tokenUrl = options.TokenUrl
            ?? (Uri.TryCreate(Text(account, "token_uri"), UriKind.Absolute, out var own) ? own : new Uri("https://oauth2.googleapis.com/token"));

        _key = RSA.Create();

        try
        {
            _key.ImportFromPem(privateKey);
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            _key.Dispose();
            throw new ArgumentException("The FCM service account's private_key is no RSA private key in PEM.", nameof(options), exception);
        }

        ProjectId = projectId;
        _clientEmail = clientEmail;
        _keyId = Text(account, "private_key_id");
        _tokenUrl = tokenUrl;
        _httpClientFactory = httpClientFactory;
        _clock = clock;
    }

    /// <summary>
    /// The Firebase project the messages are sent through.
    /// </summary>
    public string ProjectId { get; }

    /// <summary>
    /// Returns a valid access token of the service account.
    /// </summary>
    /// <exception cref="HttpRequestException">The token endpoint did not issue one.</exception>
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var now = _clock.GetUtcNow();

            if (_token is not null && now < _renewAt)
            {
                return _token;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, _tokenUrl)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                    ["assertion"] = Assertion(now),
                }),
            };

            using var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    string.Create(CultureInfo.InvariantCulture, $"The FCM token endpoint answered {(int)response.StatusCode}."),
                    inner: null,
                    response.StatusCode);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (ParseObject(body) is not { } issued || Text(issued, "access_token") is not { Length: > 0 } token)
            {
                throw new HttpRequestException("The FCM token endpoint issued no access token.");
            }

            var lifetime = issued["expires_in"] is JsonValue expiresIn && expiresIn.TryGetValue<double>(out var seconds) && seconds > 0
                ? TimeSpan.FromSeconds(seconds)
                : TimeSpan.FromMinutes(5);

            _token = token;
            _renewAt = now + lifetime - RenewBeforeExpiry;

            return token;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Forgets <paramref name="token"/> when it is still the current one: FCM refused it.
    /// </summary>
    public async Task InvalidateAsync(string token, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (string.Equals(_token, token, StringComparison.Ordinal))
            {
                _token = null;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _key.Dispose();
        _lock.Dispose();
    }

    internal static JsonObject? ParseObject(string json)
    {
        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string? Text(JsonObject json, string name) =>
        json[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private string Assertion(DateTimeOffset issuedAt)
    {
        var header = new JsonObject { ["alg"] = "RS256", ["typ"] = "JWT" };

        if (_keyId is { Length: > 0 })
        {
            header["kid"] = _keyId;
        }

        var claims = new JsonObject
        {
            ["iss"] = _clientEmail,
            ["scope"] = Scope,
            ["aud"] = _tokenUrl.AbsoluteUri,
            ["iat"] = issuedAt.ToUnixTimeSeconds(),
            ["exp"] = issuedAt.AddHours(1).ToUnixTimeSeconds(),
        };

        var signed = $"{Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(header))}.{Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(claims))}";
        var signature = _key.SignData(Encoding.ASCII.GetBytes(signed), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return $"{signed}.{Base64Url.EncodeToString(signature)}";
    }
}

/// <summary>
/// The FCM channel (Story 6.5): one HTTP v1 request per Android registration. It sends a data message, because
/// an FCM display notification cannot set a notification group: the app's messaging service builds the
/// notification (group = Site, a channel without badge) from title, body and the routing data. The message is
/// the contract of <c>packages/asyncapi</c>.
/// </summary>
public sealed class FcmChannel(
    IHttpClientFactory httpClientFactory,
    FcmServiceAccount serviceAccount,
    PushContentBuilder content,
    IOptions<PushOptions> options,
    TimeProvider clock,
    ILogger<FcmChannel> logger) : PushChannel(content, options, clock, logger)
{
    /// <inheritdoc />
    protected override PushPlatform Platform => PushPlatform.Fcm;

    /// <summary>
    /// The FCM request body of <paramref name="push"/> for <paramref name="token"/>, as the contract fixes it.
    /// Every data value is a string.
    /// </summary>
    public static JsonObject Message(PushContent push, string token)
    {
        ArgumentNullException.ThrowIfNull(push);
        ArgumentNullException.ThrowIfNull(token);

        var data = new JsonObject { ["kind"] = push.Kind, ["siteId"] = push.SiteId };

        if (push.SiteName is not null)
        {
            data["siteName"] = push.SiteName;
        }

        if (push.LotId is not null)
        {
            data["lotId"] = push.LotId;
        }

        if (push.AlertId is { } alertId)
        {
            data["alertId"] = alertId.ToString("D");
        }

        data["collapseId"] = push.CollapseId;
        data["title"] = push.Title;

        if (push.Body is not null)
        {
            data["body"] = push.Body;
        }

        return new JsonObject
        {
            ["message"] = new JsonObject
            {
                ["token"] = token,
                ["data"] = data,

                // High priority wakes a phone in Doze; the phone drops the message after a day.
                ["android"] = new JsonObject { ["priority"] = "HIGH", ["ttl"] = "86400s" },
            },
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

        string accessToken;

        try
        {
            accessToken = await serviceAccount.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception) when (exception.StatusCode is { } status && (int)status is >= 400 and < 500 and not 429)
        {
            // The service account itself was refused: another try does not change that.
            return new PushSendResult(PushSendOutcome.Refused, string.Create(CultureInfo.InvariantCulture, $"FCM token {(int)status}"));
        }

        var url = new Uri(Options.Fcm.BaseUrl, $"/v1/projects/{Uri.EscapeDataString(serviceAccount.ProjectId)}/messages:send");

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(Message(push, registration.Token))),
        };
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.Authorization = new("Bearer", accessToken);

        using var client = httpClientFactory.CreateClient(FcmServiceAccount.HttpClientName);
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            return new PushSendResult(PushSendOutcome.Delivered);
        }

        var error = FcmServiceAccount.ParseObject(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false))?["error"] as JsonObject;
        var code = ErrorCode(error);
        var detail = string.Create(CultureInfo.InvariantCulture, $"FCM {(int)response.StatusCode} {code}");

        // Only what FCM says about the token itself removes a registration: a wrong project or sender is the
        // Server's own configuration and must not cost a User their devices.
        if (code == "UNREGISTERED" || (code == "INVALID_ARGUMENT" && NamesTheToken(error)))
        {
            return new PushSendResult(PushSendOutcome.InvalidToken, detail);
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            // A new access token is requested for the next try.
            await serviceAccount.InvalidateAsync(accessToken, cancellationToken).ConfigureAwait(false);
            return new PushSendResult(PushSendOutcome.Transient, detail);
        }

        return (int)response.StatusCode is 429 or >= 500
            ? new PushSendResult(PushSendOutcome.Transient, detail)
            : new PushSendResult(PushSendOutcome.Refused, detail);
    }

    // The FCM error code is in error.details[].errorCode; error.status is the canonical gRPC code.
    private static string ErrorCode(JsonObject? error)
    {
        if (error is null)
        {
            return string.Empty;
        }

        var fcmCode = (error["details"] as JsonArray)?
            .OfType<JsonObject>()
            .Select(detail => FcmServiceAccount.Text(detail, "errorCode"))
            .FirstOrDefault(code => code is not null);

        return fcmCode ?? FcmServiceAccount.Text(error, "status") ?? string.Empty;
    }

    // INVALID_ARGUMENT is also what a malformed message gets: only one that names the token says the
    // registration is invalid.
    private static bool NamesTheToken(JsonObject? error)
    {
        if (error is null)
        {
            return false;
        }

        var violatesToken = (error["details"] as JsonArray)?
            .OfType<JsonObject>()
            .SelectMany(detail => (detail["fieldViolations"] as JsonArray)?.OfType<JsonObject>() ?? [])
            .Any(violation => string.Equals(FcmServiceAccount.Text(violation, "field"), "message.token", StringComparison.Ordinal)) ?? false;

        return violatesToken
            || (FcmServiceAccount.Text(error, "message")?.Contains("registration token", StringComparison.OrdinalIgnoreCase) ?? false);
    }
}
