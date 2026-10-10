using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Coldframe.Server.Notifications.Push;

namespace Coldframe.Server.IntegrationTests.Identity;

/// <summary>
/// One push the stub APNs or FCM received.
/// </summary>
/// <param name="Provider"><c>apns</c> or <c>fcm</c>.</param>
/// <param name="Host">The host the request went to.</param>
/// <param name="Token">The device token the push was for.</param>
/// <param name="Headers">The request headers, by lowercase name.</param>
/// <param name="Body">The JSON body.</param>
public sealed record ReceivedPush(string Provider, string Host, string Token, IReadOnlyDictionary<string, string> Headers, JsonNode Body);

/// <summary>
/// The push provider test double (Story 6.5): a stub APNs and a stub FCM with its token endpoint, behind the
/// HTTP clients of the real channels. They record every push by device token and answer 200 unless a test says
/// what a token is answered with. No request leaves the process.
/// </summary>
public sealed class PushProviderStubs
{
    private const string ApnsHost = "apns.test";
    private const string ApnsSandboxHost = "apns-sandbox.test";
    private const string FcmHost = "fcm.test";
    private const string TokenHost = "oauth.test";

    // One generated key pair for the test run: no credential is committed.
    private static readonly Lazy<(string ApnsKeyPem, string ServiceAccountJson)> Credentials = new(() =>
    {
        using var apns = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var fcm = RSA.Create(2048);
        var account = new JsonObject
        {
            ["type"] = "service_account",
            ["project_id"] = "coldframe-test",
            ["private_key_id"] = "key-1",
            ["private_key"] = fcm.ExportPkcs8PrivateKeyPem(),
            ["client_email"] = "push@coldframe-test.iam.gserviceaccount.com",
        };

        return (apns.ExportPkcs8PrivateKeyPem(), account.ToJsonString());
    });

    private readonly ConcurrentQueue<ReceivedPush> _received = new();
    private readonly ConcurrentDictionary<string, Func<HttpResponseMessage>> _answers = new(StringComparer.Ordinal);

    /// <summary>
    /// Points the push channels at the stubs, with generated credentials and no pause between tries (the fake
    /// clock would never end one).
    /// </summary>
    public static void Configure(PushOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.RetryDelay = TimeSpan.Zero;
        options.Apns.KeyId = "KEY1234567";
        options.Apns.TeamId = "TEAM123456";
        options.Apns.PrivateKeyPem = Credentials.Value.ApnsKeyPem;
        options.Apns.ProductionBaseUrl = new Uri($"https://{ApnsHost}");
        options.Apns.SandboxBaseUrl = new Uri($"https://{ApnsSandboxHost}");
        options.Fcm.ServiceAccountJson = Credentials.Value.ServiceAccountJson;
        options.Fcm.BaseUrl = new Uri($"https://{FcmHost}");
        options.Fcm.TokenUrl = new Uri($"https://{TokenHost}/token");
    }

    /// <summary>
    /// Every push received for <paramref name="token"/>, in the order it arrived, failed tries included.
    /// </summary>
    public IReadOnlyList<ReceivedPush> For(string token) =>
        [.. _received.Where(push => string.Equals(push.Token, token, StringComparison.Ordinal))];

    /// <summary>
    /// Answers every push for <paramref name="token"/> with <paramref name="status"/> and <paramref name="json"/>
    /// from now on.
    /// </summary>
    public void Answer(string token, HttpStatusCode status, string json = "{}") =>
        _answers[token] = () => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>
    /// Accepts pushes for <paramref name="token"/> again.
    /// </summary>
    public void Accept(string token) => _answers.TryRemove(token, out _);

    /// <summary>
    /// A handler for one HTTP client; the client factory owns and disposes it.
    /// </summary>
    public HttpMessageHandler CreateHandler() => new Handler(this);

    private async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("The request has no URI.");

        if (uri.Host == TokenHost)
        {
            return Json(HttpStatusCode.OK, """{"access_token":"stub-access-token","expires_in":3600,"token_type":"Bearer"}""");
        }

        var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))
            ?? throw new InvalidOperationException("The push has no JSON body.");
        var headers = request.Headers.ToDictionary(header => header.Key.ToLowerInvariant(), header => string.Join(",", header.Value), StringComparer.Ordinal);
        var (provider, token) = uri.Host switch
        {
            ApnsHost or ApnsSandboxHost => ("apns", Uri.UnescapeDataString(uri.AbsolutePath["/3/device/".Length..])),
            FcmHost => ("fcm", body["message"]!["token"]!.GetValue<string>()),
            _ => throw new InvalidOperationException($"A push channel called {uri.Host}, which is no stub."),
        };

        _received.Enqueue(new ReceivedPush(provider, uri.Host, token, headers, body));

        return _answers.TryGetValue(token, out var answer) ? answer() : Json(HttpStatusCode.OK, "{}");
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class Handler(PushProviderStubs stubs) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            stubs.RespondAsync(request, cancellationToken);
    }
}
