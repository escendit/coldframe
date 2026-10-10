using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;
using Coldframe.Server.Notifications;
using Coldframe.Server.Notifications.Push;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Coldframe.Server.Tests.Notifications.PushTestKit;

namespace Coldframe.Server.Tests.Notifications;

/// <summary>
/// The FCM channel (Story 6.5, UX-DR115, UX-DR121) against a stub provider: the message is the contract of
/// <c>packages/asyncapi</c>, a data message the app builds its own grouped notification from; an unregistered
/// token comes back through the seam, a transient failure is retried within a bound and then thrown.
/// </summary>
public sealed class FcmChannelTests
{
    private const string TokenPath = "/token";

    [Theory]
    [InlineData("push.alert.json")]
    [InlineData("push.reminder.json")]
    [InlineData("push.summary.json")]
    public async Task UxDr115TheRequestToFcmEqualsTheContractFixture(string fixtureName)
    {
        var fixture = Fixture(fixtureName);
        var lookups = TomatoesLookups();
        var notification = fixtureName switch
        {
            "push.alert.json" => Alert(IPhone, Android),
            "push.reminder.json" => Reminder(IPhone, Android),
            _ => Summary(lookups, IPhone, Android),
        };
        using var host = new FcmHost(lookups, _ => new HttpResponseMessage(HttpStatusCode.OK));

        var delivery = await host.Channel.SendAsync(notification, SentAt(fixture), TestContext.Current.CancellationToken);

        Assert.Equal(new ChannelDelivery(1, []), delivery, DeliveryComparer.Instance);
        var request = Assert.Single(host.Sends);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://fcm.googleapis.com/v1/projects/coldframe-test/messages:send", request.Uri.AbsoluteUri);
        Assert.Equal("Bearer access-1", request.Headers["authorization"]);
        AssertJsonEqual(fixture["fcm"], request.Json);
    }

    [Fact]
    public async Task UxDr121TheMessageIsDataOnlySoThatTheAppGroupsPerSiteAndShowsNoBadge()
    {
        using var host = new FcmHost(TomatoesLookups(), _ => new HttpResponseMessage(HttpStatusCode.OK));

        await host.Channel.SendAsync(Alert(Android), AlertDueAt, TestContext.Current.CancellationToken);

        var message = Assert.Single(host.Sends).Json!["message"]!.AsObject();

        // No display notification: FCM could not set its group, and it would carry a badge count of its own.
        Assert.False(message.ContainsKey("notification"));
        Assert.False(message["android"]!.AsObject().ContainsKey("notification"));
        Assert.False(message["android"]!.AsObject().ContainsKey("collapse_key"));
        var data = message["data"]!.AsObject();
        Assert.Equal((Site, "Home garden"), (data["siteId"]!.GetValue<string>(), data["siteName"]!.GetValue<string>()));
        Assert.All(data, pair => Assert.Equal(System.Text.Json.JsonValueKind.String, pair.Value!.GetValueKind()));
    }

    [Fact]
    public async Task TheAccessTokenComesFromAnRs256AssertionOfTheServiceAccountAndIsReusedUntilItExpires()
    {
        var clock = new FakeTimeProvider(AlertDueAt);
        using var host = new FcmHost(TomatoesLookups(), _ => new HttpResponseMessage(HttpStatusCode.OK), clock);

        await host.Channel.SendAsync(Alert(Android), AlertDueAt, TestContext.Current.CancellationToken);
        await host.Channel.SendAsync(Reminder(Android), AlertDueAt, TestContext.Current.CancellationToken);
        Assert.Single(host.TokenRequests);

        clock.Advance(TimeSpan.FromSeconds(3600) - FcmServiceAccount.RenewBeforeExpiry);
        await host.Channel.SendAsync(Alert(Android) with { DueAt = AlertDueAt.AddHours(1) }, AlertDueAt, TestContext.Current.CancellationToken);
        Assert.Equal(2, host.TokenRequests.Count);
        Assert.Equal(["Bearer access-1", "Bearer access-1", "Bearer access-2"], host.Sends.Select(request => request.Headers["authorization"]));

        var form = HttpUtility.ParseQueryString(host.TokenRequests[0].Body);
        Assert.Equal("urn:ietf:params:oauth:grant-type:jwt-bearer", form["grant_type"]);
        var parts = form["assertion"]!.Split('.');
        var header = JsonNode.Parse(Base64Url.DecodeFromChars(parts[0]))!;
        var claims = JsonNode.Parse(Base64Url.DecodeFromChars(parts[1]))!;
        Assert.Equal(("RS256", "key-1"), (header["alg"]!.GetValue<string>(), header["kid"]!.GetValue<string>()));
        Assert.Equal("push@coldframe-test.iam.gserviceaccount.com", claims["iss"]!.GetValue<string>());
        Assert.Equal(FcmServiceAccount.Scope, claims["scope"]!.GetValue<string>());
        Assert.Equal("https://oauth.test/token", claims["aud"]!.GetValue<string>());
        Assert.Equal((AlertDueAt.ToUnixTimeSeconds(), AlertDueAt.AddHours(1).ToUnixTimeSeconds()), (claims["iat"]!.GetValue<long>(), claims["exp"]!.GetValue<long>()));
        Assert.True(host.PublicKey.VerifyData(
            Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"),
            Base64Url.DecodeFromChars(parts[2]),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1));
    }

    [Fact]
    public async Task AUserWithoutAnAndroidPhoneIsASuccessWithNothingSentAndNoTokenRequested()
    {
        using var host = new FcmHost(TomatoesLookups(), _ => throw new InvalidOperationException("No request expected."));

        var delivery = await host.Channel.SendAsync(Alert(IPhone), AlertDueAt, TestContext.Current.CancellationToken);

        Assert.Equal(ChannelDelivery.Nothing, delivery, DeliveryComparer.Instance);
        Assert.Empty(host.Provider.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, """{"error":{"code":404,"message":"Requested entity was not found.","status":"NOT_FOUND","details":[{"@type":"type.googleapis.com/google.firebase.fcm.v1.FcmError","errorCode":"UNREGISTERED"}]}}""")]
    [InlineData(HttpStatusCode.BadRequest, """{"error":{"code":400,"message":"The registration token is not a valid FCM registration token","status":"INVALID_ARGUMENT","details":[{"@type":"type.googleapis.com/google.firebase.fcm.v1.FcmError","errorCode":"INVALID_ARGUMENT"}]}}""")]
    [InlineData(HttpStatusCode.BadRequest, """{"error":{"code":400,"message":"Invalid value","status":"INVALID_ARGUMENT","details":[{"@type":"type.googleapis.com/google.rpc.BadRequest","fieldViolations":[{"field":"message.token","description":"Invalid registration token"}]}]}}""")]
    public async Task ATokenFcmReportsAsUnregisteredOrInvalidComesBackThroughTheSeamAndIsNoFailure(HttpStatusCode status, string error)
    {
        var tablet = Android with { InstallationId = "tablet", Token = "another-token" };
        using var host = new FcmHost(TomatoesLookups(), request => request.Json!["message"]!["token"]!.GetValue<string>() == FcmToken
            ? StubProvider.Json(status, error)
            : new HttpResponseMessage(HttpStatusCode.OK));

        var delivery = await host.Channel.SendAsync(Alert(Android, tablet), AlertDueAt, TestContext.Current.CancellationToken);

        Assert.Equal(new ChannelDelivery(1, ["android"]), delivery, DeliveryComparer.Instance);
        Assert.Equal(2, host.Sends.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{"error":{"code":400,"message":"Invalid JSON payload received.","status":"INVALID_ARGUMENT"}}""")]
    [InlineData(HttpStatusCode.Forbidden, """{"error":{"code":403,"message":"SenderId mismatch","status":"PERMISSION_DENIED","details":[{"errorCode":"SENDER_ID_MISMATCH"}]}}""")]
    [InlineData(HttpStatusCode.NotFound, """{"error":{"code":404,"message":"Project not found.","status":"NOT_FOUND"}}""")]
    public async Task ARefusalThatIsNotAboutTheTokenIsLoggedAsAnErrorNotRepeatedAndRemovesNoRegistration(HttpStatusCode status, string error)
    {
        using var host = new FcmHost(TomatoesLookups(), _ => StubProvider.Json(status, error));

        // No exception: the delivery must not stay due, or every wake would send it to FCM again without end.
        var delivery = await host.Channel.SendAsync(Alert(Android), AlertDueAt, TestContext.Current.CancellationToken);

        Assert.Equal(ChannelDelivery.Nothing, delivery, DeliveryComparer.Instance);
        Assert.Single(host.Sends);
        var (level, message) = Assert.Single(host.Log.Entries);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Error, level);
        Assert.DoesNotContain(FcmToken, message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task ATransientFailureIsTriedAgainWithinTheBoundAndThenTheChannelThrows(HttpStatusCode status)
    {
        using var host = new FcmHost(TomatoesLookups(), _ => StubProvider.Json(status, """{"error":{"status":"UNAVAILABLE"}}"""));

        var thrown = await Assert.ThrowsAsync<PushProviderException>(
            () => host.Channel.SendAsync(Alert(Android), AlertDueAt, TestContext.Current.CancellationToken));

        Assert.Equal(3, host.Sends.Count);
        Assert.DoesNotContain(FcmToken, thrown.Message, StringComparison.Ordinal);
        Assert.Single(host.Sends.Select(request => request.Json!["message"]!["data"]!["collapseId"]!.GetValue<string>()).Distinct());
    }

    [Fact]
    public async Task ATokenEndpointThatIsDownCountsAsTransient()
    {
        using var host = new FcmHost(TomatoesLookups(), _ => new HttpResponseMessage(HttpStatusCode.OK)) { TokenStatus = HttpStatusCode.ServiceUnavailable };

        await Assert.ThrowsAsync<PushProviderException>(
            () => host.Channel.SendAsync(Alert(Android), AlertDueAt, TestContext.Current.CancellationToken));

        Assert.Equal(3, host.TokenRequests.Count);
        Assert.Empty(host.Sends);
    }

    [Fact]
    public async Task AnAccessTokenFcmRefusesIsRequestedAgainForTheNextTry()
    {
        var calls = 0;
        using var host = new FcmHost(TomatoesLookups(), _ => Interlocked.Increment(ref calls) == 1
            ? StubProvider.Json(HttpStatusCode.Unauthorized, """{"error":{"status":"UNAUTHENTICATED"}}""")
            : new HttpResponseMessage(HttpStatusCode.OK));

        var delivery = await host.Channel.SendAsync(Alert(Android), AlertDueAt, TestContext.Current.CancellationToken);

        Assert.Equal(1, delivery.Delivered);
        Assert.Equal(["Bearer access-1", "Bearer access-2"], host.Sends.Select(request => request.Headers["authorization"]));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"project_id":"p","client_email":"e"}""")]
    [InlineData("""{"project_id":"p","client_email":"e","private_key":"not a key"}""")]
    public void AServiceAccountThatCannotSignIsRefusedWhenTheChannelIsSetUp(string json)
    {
        using var provider = new StubProvider(_ => new HttpResponseMessage(HttpStatusCode.OK));

        Assert.Throws<ArgumentException>(() => new FcmServiceAccount(new FcmOptions { ServiceAccountJson = json }, provider, TimeProvider.System));
    }

    private sealed class FcmHost : IDisposable
    {
        private readonly FcmServiceAccount _account;
        private int _issued;

        public FcmHost(FakeLookups lookups, Func<ProviderRequest, HttpResponseMessage> respond, FakeTimeProvider? clock = null)
        {
            clock ??= new FakeTimeProvider(AlertDueAt);
            Provider = new StubProvider(request =>
            {
                if (request.Uri.AbsolutePath != TokenPath)
                {
                    return respond(request);
                }

                return TokenStatus == HttpStatusCode.OK
                    ? StubProvider.Json(HttpStatusCode.OK, $$"""{"access_token":"access-{{Interlocked.Increment(ref _issued)}}","expires_in":3600,"token_type":"Bearer"}""")
                    : new HttpResponseMessage(TokenStatus);
            });
            var (json, publicKey) = NewServiceAccount("https://oauth.test/token");
            PublicKey = publicKey;
            var options = TestOptions();
            options.Fcm.ServiceAccountJson = json;
            _account = new FcmServiceAccount(options.Fcm, Provider, clock);
            Channel = new FcmChannel(Provider, _account, Builder(lookups, options, clock), Options.Create(options), clock, Log);
        }

        public ChannelLog<FcmChannel> Log { get; } = new();

        public HttpStatusCode TokenStatus { get; init; } = HttpStatusCode.OK;

        public StubProvider Provider { get; }

        public RSA PublicKey { get; }

        public FcmChannel Channel { get; }

        public IReadOnlyList<ProviderRequest> TokenRequests => [.. Provider.Requests.Where(request => request.Uri.AbsolutePath == TokenPath)];

        public IReadOnlyList<ProviderRequest> Sends => [.. Provider.Requests.Where(request => request.Uri.AbsolutePath != TokenPath)];

        public void Dispose()
        {
            _account.Dispose();
            PublicKey.Dispose();
            Provider.Dispose();
        }
    }
}
