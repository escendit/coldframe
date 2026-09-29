using System.Globalization;
using System.Net;
using System.Text;
using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Sites;
using Coldframe.Crypto;
using Coldframe.DeviceSimulator;
using Coldframe.Server.IntegrationTests.Edge;
using ProtocolKind = Coldframe.Protocol.Setup.V1.DeviceKind;

namespace Coldframe.Server.IntegrationTests.Devices;

/// <summary>
/// <c>POST /device/heartbeat</c> on the AppHost (Story 3.5; FR-13, AD-12): one test per Server row of the
/// story's matrix, driven through the Device simulator only. The vector Hub is <c>heartbeat[1]</c> of the
/// shared vectors (<c>heartbeat[0]</c>'s root is the Hub <see cref="EnrolmentTests"/> enrols elsewhere).
/// "Nothing journaled" is checked on the Device's stream.
/// </summary>
public sealed class HeartbeatTests(EdgeApiFixture edge) : IClassFixture<EdgeApiFixture>
{
    private const string Unauthorized = "urn:coldframe:problem:device-unauthorized";
    private const string Validation = "urn:coldframe:problem:validation";

    private static readonly SemaphoreSlim HubLock = new(1, 1);
    private static SimulatedDevice? _hub;

    [Fact]
    public async Task AValidHeartbeatIsSeenAndAnswersTheServerTime()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var hub = await VectorHubAsync(cancellationToken);
        var before = await SeenCountAsync(hub, cancellationToken);
        var sent = Now();

        using var server = edge.CreateServerClient();
        using var request = hub.HeartbeatRequest(sent, SimulatedDevice.HeartbeatBody(123_456));
        using var response = await server.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var body = await EdgeApiFixture.ReadJsonAsync(response, cancellationToken);
        Assert.Equal(["serverTime"], body.RootElement.EnumerateObject().Select(property => property.Name));
        var serverTime = body.RootElement.GetProperty("serverTime").GetString()!;
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$", serverTime);
        var parsed = DateTimeOffset.ParseExact(serverTime, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        Assert.InRange(parsed, sent.AddSeconds(-5), Now().AddSeconds(5));

        var events = await edge.ReadStreamAsync($"device/{hub.DeviceId}", cancellationToken);
        Assert.Equal(before + 1, events.Count(item => item.Data is DeviceSeen));
        var seen = Assert.IsType<DeviceSeen>(events[^1].Data);
        Assert.Equal(sent.ToUnixTimeMilliseconds(), seen.DeviceTimestampMs);
        Assert.Equal(123_456, seen.UptimeMs);
        Assert.InRange(seen.SeenAt, sent.AddSeconds(-5), Now().AddSeconds(5));
        Assert.Equal("device.seen", (await edge.AliasesAsync($"device/{hub.DeviceId}", cancellationToken))[^1]);
    }

    [Fact]
    public async Task ATamperedBodyIsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var hub = await VectorHubAsync(cancellationToken);

        using var request = hub.HeartbeatRequest(Now(), SimulatedDevice.HeartbeatBody(1_000));
        // Signed over one body, sent with another: the HMAC no longer holds.
        request.Content = new ByteArrayContent(SimulatedDevice.HeartbeatBody(2_000));
        request.Content.Headers.ContentType = new("application/json");

        await AssertRefusedAsync(hub, request, cancellationToken);
    }

    [Fact]
    public async Task AnotherDevicesKeyIsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var hub = await VectorHubAsync(cancellationToken);

        // The other Device signs, claiming the vector Hub's ID: the signature does not verify under its key.
        var impostor = SimulatedDevice.Create();
        using var request = impostor.HeartbeatRequest(Now());
        request.Headers.Remove(CryptoSpec.HeartbeatDeviceHeader);
        request.Headers.Add(CryptoSpec.HeartbeatDeviceHeader, hub.DeviceId.ToString());

        await AssertRefusedAsync(hub, request, cancellationToken);
    }

    [Theory]
    [InlineData(-301)]
    [InlineData(301)]
    [InlineData(-3_600)]
    public async Task ATimestampBeyondFiveMinutesIsRefused(int offsetSeconds)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var hub = await VectorHubAsync(cancellationToken);

        using var request = hub.HeartbeatRequest(Now().AddSeconds(offsetSeconds));

        await AssertRefusedAsync(hub, request, cancellationToken);
    }

    [Fact]
    public async Task ARepeatedNonceIsRefusedEvenWithANewerTimestamp()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var hub = await VectorHubAsync(cancellationToken);
        var nonce = System.Security.Cryptography.RandomNumberGenerator.GetBytes(CryptoSpec.HeartbeatNonceLength);
        var first = Now().ToUnixTimeMilliseconds();
        using var server = edge.CreateServerClient();

        using (var request = hub.HeartbeatRequest(first, nonce))
        using (var response = await server.SendAsync(request, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // The same request again, and the same nonce above the last timestamp: both replays.
        using (var replay = hub.HeartbeatRequest(first, nonce))
        {
            await AssertRefusedAsync(hub, replay, cancellationToken);
        }

        using var sameNonce = hub.HeartbeatRequest(first + 1_000, nonce);
        await AssertRefusedAsync(hub, sameNonce, cancellationToken);
    }

    [Fact]
    public async Task ATimestampNotAboveTheLastIsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var hub = await VectorHubAsync(cancellationToken);
        var accepted = Now().ToUnixTimeMilliseconds();
        using var server = edge.CreateServerClient();

        using (var request = hub.HeartbeatRequest(accepted, Nonce()))
        using (var response = await server.SendAsync(request, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using (var same = hub.HeartbeatRequest(accepted, Nonce()))
        {
            await AssertRefusedAsync(hub, same, cancellationToken);
        }

        using (var older = hub.HeartbeatRequest(accepted - 1, Nonce()))
        {
            await AssertRefusedAsync(hub, older, cancellationToken);
        }

        using var newer = hub.HeartbeatRequest(accepted + 1, Nonce());
        using var answer = await server.SendAsync(newer, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
    }

    [Fact]
    public async Task AnUnknownDeviceIsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var stranger = SimulatedDevice.Create();

        using var request = stranger.HeartbeatRequest(Now());

        await AssertRefusedAsync(stranger, request, cancellationToken);
        Assert.Empty(await edge.AliasesAsync($"device/{stranger.DeviceId}", cancellationToken));
    }

    [Fact]
    public async Task ADeviceWhoseKeyDoesNotUnwrapIsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var device = SimulatedDevice.Create();

        // Enrolled with K_dev wrapped under another key-encryption key: the grain cannot unwrap it.
        var otherVault = new Coldframe.Server.Devices.DeviceKeyVault($"another-kek-{Guid.NewGuid():N}");
        await edge.AppendAsync(
            $"device/{device.DeviceId}",
            [new DeviceEnrolled(Guid.CreateVersion7().ToString(), DeviceKind.Hub, otherVault.Wrap(device.DeviceId, device.Keys.DeviceKey), Now())],
            cancellationToken);

        using var request = device.HeartbeatRequest(Now());

        await AssertRefusedAsync(device, request, cancellationToken);
    }

    [Theory]
    [InlineData("X-Coldframe-Device")]
    [InlineData("X-Coldframe-Timestamp")]
    [InlineData("X-Coldframe-Nonce")]
    [InlineData("X-Coldframe-Signature")]
    public async Task AMissingHeaderIsRefused(string header)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var hub = await VectorHubAsync(cancellationToken);

        using var request = hub.HeartbeatRequest(Now());
        request.Headers.Remove(header);

        await AssertRefusedAsync(hub, request, cancellationToken);
    }

    [Theory]
    [InlineData("X-Coldframe-Timestamp", "9223372036854775808")]
    [InlineData("X-Coldframe-Timestamp", "-1")]
    [InlineData("X-Coldframe-Nonce", "00112233445566778899AABBCCDDEEFF")]
    [InlineData("X-Coldframe-Device", "not-a-device")]
    public async Task AMalformedHeaderIsRefused(string header, string value)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var hub = await VectorHubAsync(cancellationToken);

        using var request = hub.HeartbeatRequest(Now());
        request.Headers.Remove(header);
        request.Headers.TryAddWithoutValidation(header, value);

        await AssertRefusedAsync(hub, request, cancellationToken);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"protocolVersion\":0,\"uptimeMs\":1}")]
    [InlineData("{\"protocolVersion\":2,\"uptimeMs\":1}")]
    [InlineData("{\"uptimeMs\":1}")]
    public async Task ABodyThatIsNotAVersionOneHeartbeatIsValidation(string body)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var hub = await VectorHubAsync(cancellationToken);

        // Authentic headers over the body as sent: only the body is wrong.
        using var request = hub.HeartbeatRequest(Now(), Encoding.UTF8.GetBytes(body));

        await AssertNothingJournaledAsync(hub, request, HttpStatusCode.BadRequest, Validation, cancellationToken);
    }

    [Fact]
    public async Task ABodyOver4KiBIsValidation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var hub = await VectorHubAsync(cancellationToken);
        var body = Encoding.UTF8.GetBytes("{\"protocolVersion\":1,\"uptimeMs\":1}" + new string(' ', 4096));

        using var request = hub.HeartbeatRequest(Now(), body);

        await AssertNothingJournaledAsync(hub, request, HttpStatusCode.BadRequest, Validation, cancellationToken);
    }

    [Fact]
    public async Task AUserTokenDoesNotAuthenticateADevice()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var hub = await VectorHubAsync(cancellationToken);
        var user = await edge.CreateUserAsync("heartbeat-user", cancellationToken);
        using var server = edge.CreateServerClient(user.AccessToken);

        using var unsigned = new HttpRequestMessage(HttpMethod.Post, new Uri(SimulatedDevice.HeartbeatPath, UriKind.Relative))
        {
            Content = new ByteArrayContent(SimulatedDevice.HeartbeatBody(1)),
        };
        using var response = await server.SendAsync(unsigned, cancellationToken);
        await EdgeApiTests.AssertProblemAsync(response, HttpStatusCode.Unauthorized, Unauthorized, cancellationToken);

        // A signed heartbeat with a user token alongside still works: the token is ignored.
        using var signed = hub.HeartbeatRequest(Now());
        using var accepted = await server.SendAsync(signed, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    /// <summary>
    /// A heartbeat request without any Device header, for the authorization matrix.
    /// </summary>
    internal static Task<HttpResponseMessage> PostUnsignedAsync(HttpClient server, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(SimulatedDevice.HeartbeatPath, UriKind.Relative))
        {
            Content = new ByteArrayContent(SimulatedDevice.HeartbeatBody(1)),
        };
        request.Content.Headers.ContentType = new("application/json");
        return server.SendAsync(request, cancellationToken);
    }

    private static DateTimeOffset Now() => TimeProvider.System.GetUtcNow();

    private static byte[] Nonce() => System.Security.Cryptography.RandomNumberGenerator.GetBytes(CryptoSpec.HeartbeatNonceLength);

    private Task AssertRefusedAsync(SimulatedDevice device, HttpRequestMessage request, CancellationToken cancellationToken) =>
        AssertNothingJournaledAsync(device, request, HttpStatusCode.Unauthorized, Unauthorized, cancellationToken);

    private async Task AssertNothingJournaledAsync(
        SimulatedDevice device,
        HttpRequestMessage request,
        HttpStatusCode status,
        string type,
        CancellationToken cancellationToken)
    {
        var before = await edge.AliasesAsync($"device/{device.DeviceId}", cancellationToken);
        using var server = edge.CreateServerClient();

        using var response = await server.SendAsync(request, cancellationToken);

        await EdgeApiTests.AssertProblemAsync(response, status, type, cancellationToken);
        Assert.Equal(before, await edge.AliasesAsync($"device/{device.DeviceId}", cancellationToken));
    }

    private async Task<int> SeenCountAsync(SimulatedDevice device, CancellationToken cancellationToken) =>
        (await edge.AliasesAsync($"device/{device.DeviceId}", cancellationToken)).Count(alias => alias == "device.seen");

    // The vector Hub, enrolled once on a Site of its own for the whole run.
    private async Task<SimulatedDevice> VectorHubAsync(CancellationToken cancellationToken)
    {
        await HubLock.WaitAsync(cancellationToken);

        try
        {
            if (_hub is not null)
            {
                return _hub;
            }

            var vector = Vectors.Heartbeat(1);
            var hub = SimulatedDevice.Create(vector.Bytes("rootKey"), ProtocolKind.Hub);
            Assert.Equal(vector.Text("deviceId"), hub.DeviceId.ToString());

            var administrator = await edge.CreateUserAsync("heartbeat-admin", cancellationToken);
            var siteId = Guid.CreateVersion7().ToString();
            var last = await edge.AppendAsync(
                $"site/{siteId}",
                [
                    new SiteCreated("Heartbeats", administrator.UserId),
                    new MembershipGranted(administrator.UserId, SiteRole.Owner),
                ],
                cancellationToken);
            await edge.WaitForIdentityCheckpointAsync(last);

            using var server = edge.CreateServerClient(administrator.AccessToken);
            var sealedEnrolment = hub.SealEnrolment(await edge.GetEnrolmentPublicKeyAsync(cancellationToken), siteId);
            using var enrolled = await EnrolmentTests.PostAsync(
                server,
                siteId,
                Guid.NewGuid().ToString(),
                new EnrolBody(sealedEnrolment.DeviceId, sealedEnrolment.Kind, sealedEnrolment.Enc, sealedEnrolment.Ciphertext),
                cancellationToken);
            Assert.Equal(HttpStatusCode.Created, enrolled.StatusCode);

            _hub = hub;
            return hub;
        }
        finally
        {
            HubLock.Release();
        }
    }
}
