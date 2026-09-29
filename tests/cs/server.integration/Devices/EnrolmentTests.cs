using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Sites;
using Coldframe.Crypto;
using Coldframe.DeviceSimulator;
using Coldframe.Server.Devices;
using Coldframe.Server.IntegrationTests.Edge;
using Curve25519 = Coldframe.Crypto.X25519;
using ProtocolKind = Coldframe.Protocol.Setup.V1.DeviceKind;

namespace Coldframe.Server.IntegrationTests.Devices;

/// <summary>
/// Device enrolment on the AppHost (Story 3.3; FR-1, AD-12, AD-18): <c>GET /enrolment-key</c> and
/// <c>POST /sites/{siteId}/devices</c>, driven through the Device simulator only. "Nothing persisted" is
/// checked on the journal streams of the Site and the Device.
/// </summary>
public sealed class EnrolmentTests(EdgeApiFixture edge) : IClassFixture<EdgeApiFixture>
{
    private const string EnrolmentKeySeedParameter = "enrolment-key-seed";
    private const string DeviceKekParameter = "device-kek";

    private static readonly string[] SeededSite = ["site.created", "site.membership-granted", "site.membership-granted", "site.membership-granted"];

    [Fact]
    public async Task AnySignedInCallerReadsTheEnrolmentKeyAndItsFingerprint()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = await edge.CreateUserAsync("enrolment-reader", cancellationToken);
        using var server = edge.CreateServerClient(user.AccessToken);

        using var response = await server.GetAsync(new Uri("/enrolment-key", UriKind.Relative), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await EdgeApiFixture.ReadJsonAsync(response, cancellationToken);
        Assert.Equal(["fingerprint", "publicKey"], body.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));

        var text = body.RootElement.GetProperty("publicKey").GetString()!;
        Assert.Matches("^[A-Za-z0-9_-]{43}$", text);
        var publicKey = Base64Url.DecodeFromChars(text);
        Assert.Equal(Enrolment.Fingerprint(publicKey), body.RootElement.GetProperty("fingerprint").GetString());

        // The key the AppHost derives from its generated seed: the Server read it from the PEM.
        var seed = await edge.GetParameterValueAsync(EnrolmentKeySeedParameter, cancellationToken);
        Assert.Equal(Curve25519.PublicKey(SHA256.HashData(Encoding.UTF8.GetBytes(seed))), publicKey);

        using var anonymous = edge.CreateServerClient();
        using var refused = await anonymous.GetAsync(new Uri("/enrolment-key", UriKind.Relative), cancellationToken);
        await EdgeApiTests.AssertProblemAsync(refused, HttpStatusCode.Unauthorized, "urn:coldframe:problem:unauthorized", cancellationToken);
    }

    [Fact]
    public async Task TheVectorHubEnrolsAndTheServerHoldsItsKeyWrapped()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var site = await SeedSiteAsync(cancellationToken);
        var vector = Vectors.Enrolment(0);
        var device = SimulatedDevice.Create(vector.Bytes("rootKey"), ProtocolKind.Hub);
        var deviceId = vector.Text("deviceId");
        Assert.Equal(deviceId, device.DeviceId.ToString());

        using var server = edge.CreateServerClient(site.Administrator.AccessToken);
        var sealedEnrolment = device.SealEnrolment(await edge.GetEnrolmentPublicKeyAsync(cancellationToken), site.Id);

        using var response = await PostAsync(server, site.Id, Guid.NewGuid().ToString(), Body(sealedEnrolment), cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(new DeviceBody(deviceId, "hub", site.Id), await ReadDeviceAsync(response, cancellationToken));

        var siteEvents = await edge.ReadStreamAsync($"site/{site.Id}", cancellationToken);
        var registered = Assert.IsType<DeviceRegistered>(siteEvents[^1].Data);
        Assert.Equal((deviceId, DeviceKind.Hub), (registered.DeviceId, registered.Kind));
        Assert.Equal([.. SeededSite, "site.device-registered"], await edge.AliasesAsync($"site/{site.Id}", cancellationToken));

        var deviceEvents = await edge.ReadStreamAsync($"device/{deviceId}", cancellationToken);
        var enrolled = Assert.IsType<DeviceEnrolled>(Assert.Single(deviceEvents).Data);
        Assert.Equal((site.Id, DeviceKind.Hub), (enrolled.SiteId, enrolled.Kind));

        // K_dev is at rest only wrapped: the journal row never holds it, the Server's vault unwraps it.
        var vault = new DeviceKeyVault(await edge.GetParameterValueAsync(DeviceKekParameter, cancellationToken));
        Assert.Equal(vector.Text("deviceKey"), Convert.ToHexStringLower(vault.Unwrap(device.DeviceId, enrolled.WrappedKey)));
        Assert.DoesNotContain(vector.Text("deviceKey"), await RawPayloadAsync($"device/{deviceId}", cancellationToken), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            Convert.ToBase64String(vector.Bytes("deviceKey")),
            await RawPayloadAsync($"device/{deviceId}", cancellationToken),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARetryOrANewKeyForTheSameDeviceAnswersTheSameAndJournalsNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var site = await SeedSiteAsync(cancellationToken);
        var device = SimulatedDevice.Create(ProtocolKind.Node);
        using var server = edge.CreateServerClient(site.Administrator.AccessToken);
        var body = Body(await SealAsync(device, site.Id, cancellationToken));
        var key = Guid.NewGuid().ToString();

        using var first = await PostAsync(server, site.Id, key, body, cancellationToken);
        using var retry = await PostAsync(server, site.Id, key, body, cancellationToken);

        // A new key and a freshly sealed enrolment of the same Device on the same Site.
        using var again = await PostAsync(server, site.Id, Guid.NewGuid().ToString(), Body(await SealAsync(device, site.Id, cancellationToken)), cancellationToken);

        var expected = new DeviceBody(device.DeviceId.ToString(), "node", site.Id);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(expected, await ReadDeviceAsync(first, cancellationToken));
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal(expected, await ReadDeviceAsync(retry, cancellationToken));
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        Assert.Equal(expected, await ReadDeviceAsync(again, cancellationToken));

        Assert.Equal([.. SeededSite, "site.device-registered"], await edge.AliasesAsync($"site/{site.Id}", cancellationToken));
        Assert.Equal(["device.enrolled"], await edge.AliasesAsync($"device/{device.DeviceId}", cancellationToken));
    }

    [Fact]
    public async Task AKeyReusedForAnotherDeviceIsRefusedAndNothingIsPersisted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var site = await SeedSiteAsync(cancellationToken);
        var first = SimulatedDevice.Create();
        var second = SimulatedDevice.Create();
        using var server = edge.CreateServerClient(site.Administrator.AccessToken);
        var key = Guid.NewGuid().ToString();

        using var enrolled = await PostAsync(server, site.Id, key, Body(await SealAsync(first, site.Id, cancellationToken)), cancellationToken);
        Assert.Equal(HttpStatusCode.Created, enrolled.StatusCode);

        using var reused = await PostAsync(server, site.Id, key, Body(await SealAsync(second, site.Id, cancellationToken)), cancellationToken);

        await EdgeApiTests.AssertProblemAsync(reused, HttpStatusCode.UnprocessableEntity, "urn:coldframe:problem:idempotency-key-reused", cancellationToken);
        Assert.Equal([.. SeededSite, "site.device-registered"], await edge.AliasesAsync($"site/{site.Id}", cancellationToken));
        Assert.Empty(await edge.AliasesAsync($"device/{second.DeviceId}", cancellationToken));
    }

    [Fact]
    public async Task ADeviceOnAnotherSiteIsRefusedAndNothingIsPersisted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var siteA = await SeedSiteAsync(cancellationToken);
        var siteB = await SeedSiteAsync(cancellationToken);
        var device = SimulatedDevice.Create();

        using (var serverA = edge.CreateServerClient(siteA.Administrator.AccessToken))
        {
            using var enrolled = await PostAsync(serverA, siteA.Id, Guid.NewGuid().ToString(), Body(await SealAsync(device, siteA.Id, cancellationToken)), cancellationToken);
            Assert.Equal(HttpStatusCode.Created, enrolled.StatusCode);
        }

        using var serverB = edge.CreateServerClient(siteB.Administrator.AccessToken);
        using var refused = await PostAsync(serverB, siteB.Id, Guid.NewGuid().ToString(), Body(await SealAsync(device, siteB.Id, cancellationToken)), cancellationToken);

        await EdgeApiTests.AssertProblemAsync(refused, HttpStatusCode.Conflict, "urn:coldframe:problem:device-on-another-site", cancellationToken);
        Assert.Equal([.. SeededSite, "site.device-registered"], await edge.AliasesAsync($"site/{siteA.Id}", cancellationToken));
        Assert.Equal(SeededSite, await edge.AliasesAsync($"site/{siteB.Id}", cancellationToken));
        Assert.Equal(["device.enrolled"], await edge.AliasesAsync($"device/{device.DeviceId}", cancellationToken));
    }

    [Fact]
    public async Task AMalformedRequestIsRefusedAndNothingIsPersisted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var site = await SeedSiteAsync(cancellationToken);
        var device = SimulatedDevice.Create();
        using var server = edge.CreateServerClient(site.Administrator.AccessToken);
        var valid = await SealAsync(device, site.Id, cancellationToken);
        var deviceId = valid.DeviceId;

        object[] bodies =
        [
            new { kind = "hub", enc = valid.Enc, ciphertext = valid.Ciphertext },
            new { deviceId, enc = valid.Enc, ciphertext = valid.Ciphertext },
            new { deviceId, kind = "hub", ciphertext = valid.Ciphertext },
            new { deviceId, kind = "hub", enc = valid.Enc },
            new { deviceId = 42, kind = "hub", enc = valid.Enc, ciphertext = valid.Ciphertext },
            new { deviceId, kind = "sensor", enc = valid.Enc, ciphertext = valid.Ciphertext },
            new { deviceId, kind = "Hub", enc = valid.Enc, ciphertext = valid.Ciphertext },
            new { deviceId = "A" + deviceId[1..], kind = "hub", enc = valid.Enc, ciphertext = valid.Ciphertext },
            new { deviceId = "zz" + deviceId[2..], kind = "hub", enc = valid.Enc, ciphertext = valid.Ciphertext },
            new { deviceId = deviceId[..15], kind = "hub", enc = valid.Enc, ciphertext = valid.Ciphertext },
            new { deviceId, kind = "hub", enc = valid.Enc + "=", ciphertext = valid.Ciphertext },
            new { deviceId, kind = "hub", enc = valid.Enc[..42], ciphertext = valid.Ciphertext },
            new { deviceId, kind = "hub", enc = valid.Enc[..42] + "+", ciphertext = valid.Ciphertext },
            new { deviceId, kind = "hub", enc = valid.Enc, ciphertext = valid.Ciphertext[..63] },
            new { deviceId, kind = "hub", enc = valid.Enc, ciphertext = valid.Ciphertext[..63] + "/" },
        ];

        var failures = new List<string>();

        foreach (var body in bodies)
        {
            using var response = await PostAsync(server, site.Id, Guid.NewGuid().ToString(), body, cancellationToken);
            if (response.StatusCode != HttpStatusCode.BadRequest)
            {
                failures.Add($"{JsonSerializer.Serialize(body)}: {(int)response.StatusCode}");
                continue;
            }

            await EdgeApiTests.AssertProblemAsync(response, HttpStatusCode.BadRequest, "urn:coldframe:problem:validation", cancellationToken);
        }

        // Not JSON at all, and JSON of another shape.
        foreach (var content in new HttpContent[]
        {
            new StringContent("{\"deviceId\":", Encoding.UTF8, "application/json"),
            new StringContent("[]", Encoding.UTF8, "application/json"),
            new StringContent(JsonSerializer.Serialize(Body(valid)), Encoding.UTF8, "text/plain"),
        })
        {
            using (content)
            {
                using var response = await PostContentAsync(server, site.Id, Guid.NewGuid().ToString(), content, cancellationToken);
                await EdgeApiTests.AssertProblemAsync(response, HttpStatusCode.BadRequest, "urn:coldframe:problem:validation", cancellationToken);
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        Assert.Equal(SeededSite, await edge.AliasesAsync($"site/{site.Id}", cancellationToken));
        Assert.Empty(await edge.AliasesAsync($"device/{deviceId}", cancellationToken));
    }

    [Fact]
    public async Task AnEnrolmentThatDoesNotOpenIsRefusedAndNothingIsPersisted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var site = await SeedSiteAsync(cancellationToken);
        var device = SimulatedDevice.Create();
        var other = SimulatedDevice.Create();
        var serverKey = await edge.GetEnrolmentPublicKeyAsync(cancellationToken);
        using var server = edge.CreateServerClient(site.Administrator.AccessToken);
        var valid = device.SealEnrolment(serverKey, site.Id);

        // A Device that claims another Device's ID, sealing its own K_dev with that ID as associated data:
        // the enrolment opens, but K_dev does not derive the claimed ID.
        var lying = Hpke.SealBase(
            serverKey,
            RandomNumberGenerator.GetBytes(CryptoSpec.X25519KeyLength),
            Encoding.ASCII.GetBytes(CryptoSpec.EnrolmentInfo),
            other.DeviceId.ToBytes(),
            device.Keys.DeviceKey);

        object[] bodies =
        [
            Body(valid) with { Ciphertext = Flip(valid.Ciphertext) },
            Body(valid) with { Enc = Flip(valid.Enc) },
            Body(device.SealEnrolment(Curve25519.PublicKey(Curve25519.GeneratePrivateKey()), site.Id)),
            Body(valid) with { DeviceId = other.DeviceId.ToString() },
            new EnrolBody(other.DeviceId.ToString(), "hub", Base64Url.EncodeToString(lying.Enc), Base64Url.EncodeToString(lying.Ciphertext)),
        ];

        foreach (var body in bodies)
        {
            using var response = await PostAsync(server, site.Id, Guid.NewGuid().ToString(), body, cancellationToken);
            await EdgeApiTests.AssertProblemAsync(response, HttpStatusCode.BadRequest, "urn:coldframe:problem:validation", cancellationToken);

            // The crypto failure is never echoed.
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            Assert.DoesNotContain("authenticate", problem.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(SeededSite, await edge.AliasesAsync($"site/{site.Id}", cancellationToken));
        Assert.Empty(await edge.AliasesAsync($"device/{device.DeviceId}", cancellationToken));
        Assert.Empty(await edge.AliasesAsync($"device/{other.DeviceId}", cancellationToken));
    }

    [Fact]
    public async Task AMissingKeyIsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var site = await SeedSiteAsync(cancellationToken);
        var device = SimulatedDevice.Create();
        using var server = edge.CreateServerClient(site.Administrator.AccessToken);

        using var response = await PostAsync(server, site.Id, null, Body(await SealAsync(device, site.Id, cancellationToken)), cancellationToken);

        await EdgeApiTests.AssertProblemAsync(response, HttpStatusCode.BadRequest, "urn:coldframe:problem:idempotency-key-missing", cancellationToken);
        Assert.Equal(SeededSite, await edge.AliasesAsync($"site/{site.Id}", cancellationToken));
        Assert.Empty(await edge.AliasesAsync($"device/{device.DeviceId}", cancellationToken));
    }

    [Fact]
    public async Task AMemberAnotherSiteOrAnUnknownSiteIsRefusedBeforeTheBodyIsRead()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var site = await SeedSiteAsync(cancellationToken);
        var otherSite = await SeedSiteAsync(cancellationToken);

        // A body that would be 400: the policy answers first.
        var malformed = new { deviceId = "not-a-device" };

        using (var member = edge.CreateServerClient(site.Member.AccessToken))
        {
            using var response = await PostAsync(member, site.Id, Guid.NewGuid().ToString(), malformed, cancellationToken);
            await EdgeApiTests.AssertProblemAsync(response, HttpStatusCode.Forbidden, "urn:coldframe:problem:forbidden", cancellationToken);
        }

        using var administrator = edge.CreateServerClient(site.Administrator.AccessToken);
        using (var response = await PostAsync(administrator, otherSite.Id, Guid.NewGuid().ToString(), malformed, cancellationToken))
        {
            await EdgeApiTests.AssertProblemAsync(response, HttpStatusCode.Forbidden, "urn:coldframe:problem:forbidden", cancellationToken);
        }

        using (var response = await PostAsync(administrator, Guid.CreateVersion7().ToString(), Guid.NewGuid().ToString(), malformed, cancellationToken))
        {
            await EdgeApiTests.AssertProblemAsync(response, HttpStatusCode.NotFound, "urn:coldframe:problem:site-not-found", cancellationToken);
        }

        Assert.Equal(SeededSite, await edge.AliasesAsync($"site/{site.Id}", cancellationToken));
        Assert.Equal(SeededSite, await edge.AliasesAsync($"site/{otherSite.Id}", cancellationToken));
    }

    /// <summary>
    /// Posts a sealed enrolment of a fresh simulated Device to the Site, sealed to the Server's key.
    /// </summary>
    internal static async Task<HttpResponseMessage> PostFreshDeviceAsync(
        EdgeApiFixture edge,
        HttpClient server,
        string siteId,
        CancellationToken cancellationToken) =>
        await PostAsync(
            server,
            siteId,
            Guid.NewGuid().ToString(),
            Body(SimulatedDevice.Create().SealEnrolment(await edge.GetEnrolmentPublicKeyAsync(cancellationToken), siteId)),
            cancellationToken);

    internal static async Task<HttpResponseMessage> PostAsync(HttpClient server, string siteId, string? key, object body, CancellationToken cancellationToken)
    {
        using var content = JsonContent.Create(body, body.GetType());
        return await PostContentAsync(server, siteId, key, content, cancellationToken);
    }

    private static async Task<HttpResponseMessage> PostContentAsync(HttpClient server, string siteId, string? key, HttpContent content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/sites/{siteId}/devices", UriKind.Relative)) { Content = content };

        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        var response = await server.SendAsync(request, cancellationToken);

        // The request owns the content; keep it alive for the caller by detaching it.
        request.Content = null;
        return response;
    }

    private static EnrolBody Body(SimulatedEnrolment enrolment) => new(enrolment.DeviceId, enrolment.Kind, enrolment.Enc, enrolment.Ciphertext);

    // Flips one bit in the middle of a base64url value, keeping its length and alphabet.
    private static string Flip(string text)
    {
        var bytes = Base64Url.DecodeFromChars(text);
        bytes[bytes.Length / 2] ^= 1;
        return Base64Url.EncodeToString(bytes);
    }

    private static async Task<DeviceBody> ReadDeviceAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var body = await EdgeApiFixture.ReadJsonAsync(response, cancellationToken);
        Assert.Equal(["id", "kind", "siteId"], body.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));

        return new DeviceBody(
            body.RootElement.GetProperty("id").GetString()!,
            body.RootElement.GetProperty("kind").GetString()!,
            body.RootElement.GetProperty("siteId").GetString()!);
    }

    private async Task<SimulatedEnrolment> SealAsync(SimulatedDevice device, string siteId, CancellationToken cancellationToken) =>
        device.SealEnrolment(await edge.GetEnrolmentPublicKeyAsync(cancellationToken), siteId);

    private async Task<string> RawPayloadAsync(string streamId, CancellationToken cancellationToken)
    {
        await using var command = edge.Database.CreateCommand("SELECT string_agg(payload::text, ' ') FROM journal_events WHERE stream_id = @stream_id");
        command.Parameters.AddWithValue("stream_id", streamId);
        return (string)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    // A Site with an Owner, an Administrator and a Member, seeded as events.
    private async Task<SeededSiteUsers> SeedSiteAsync(CancellationToken cancellationToken)
    {
        var owner = await edge.CreateUserAsync("devices-owner", cancellationToken);
        var administrator = await edge.CreateUserAsync("devices-admin", cancellationToken);
        var member = await edge.CreateUserAsync("devices-member", cancellationToken);
        var siteId = Guid.CreateVersion7().ToString();

        var last = await edge.AppendAsync(
            $"site/{siteId}",
            [
                new SiteCreated("Devices", owner.UserId),
                new MembershipGranted(owner.UserId, SiteRole.Owner),
                new MembershipGranted(administrator.UserId, SiteRole.Administrator),
                new MembershipGranted(member.UserId, SiteRole.Member),
            ],
            cancellationToken);
        await edge.WaitForIdentityCheckpointAsync(last);

        return new SeededSiteUsers(siteId, administrator, member);
    }

    private sealed record SeededSiteUsers(string Id, TestUser Administrator, TestUser Member);

    private sealed record DeviceBody(string Id, string Kind, string SiteId);
}

/// <summary>
/// The body of <c>POST /sites/{siteId}/devices</c>, as the app relays it.
/// </summary>
internal sealed record EnrolBody(string DeviceId, string Kind, string Enc, string Ciphertext);
