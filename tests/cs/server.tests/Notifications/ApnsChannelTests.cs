using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Coldframe.Contracts.Notifications;
using Coldframe.Server.Notifications;
using Coldframe.Server.Notifications.Push;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Coldframe.Server.Tests.Notifications.PushTestKit;

namespace Coldframe.Server.Tests.Notifications;

/// <summary>
/// The APNs channel (Story 6.5, UX-DR115, UX-DR121) against a stub provider: the request is the contract of
/// <c>packages/asyncapi</c>, grouped per Site, without a badge; an invalid token comes back through the seam, a
/// transient failure is retried within a bound and then thrown.
/// </summary>
public sealed class ApnsChannelTests
{
    [Theory]
    [InlineData("push.alert.json")]
    [InlineData("push.reminder.json")]
    [InlineData("push.summary.json")]
    public async Task UxDr115TheRequestToApnsEqualsTheContractFixture(string fixtureName)
    {
        var fixture = Fixture(fixtureName);
        var lookups = TomatoesLookups();
        var notification = fixtureName switch
        {
            "push.alert.json" => Alert(IPhone, Android),
            "push.reminder.json" => Reminder(IPhone, Android),
            _ => Summary(lookups, IPhone, Android),
        };
        using var apns = new StubProvider(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var host = new ApnsHost(apns, lookups);

        var delivery = await host.Channel.SendAsync(notification, SentAt(fixture), TestContext.Current.CancellationToken);

        Assert.Equal(new ChannelDelivery(1, []), delivery, DeliveryComparer.Instance);
        var request = Assert.Single(apns.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("api.push.apple.com", request.Uri.Host);
        Assert.Equal(fixture["apns"]!["path"]!.GetValue<string>(), request.Uri.AbsolutePath);
        Assert.Equal(HttpVersion.Version20, request.Version);

        foreach (var (name, value) in fixture["apns"]!["headers"]!.AsObject())
        {
            Assert.Equal(value!.GetValue<string>(), request.Headers[name]);
        }

        AssertJsonEqual(fixture["apns"]!["payload"], request.Json);
    }

    [Fact]
    public async Task UxDr121ThePushIsGroupedPerSiteAtTheStandardInterruptionLevelWithoutABadge()
    {
        using var apns = new StubProvider(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var host = new ApnsHost(apns, TomatoesLookups());

        await host.Channel.SendAsync(Alert(IPhone), AlertDueAt, TestContext.Current.CancellationToken);

        var aps = Assert.Single(apns.Requests).Json!["aps"]!.AsObject();
        Assert.Equal(Site, aps["thread-id"]!.GetValue<string>());
        Assert.Equal("active", aps["interruption-level"]!.GetValue<string>());
        Assert.False(aps.ContainsKey("badge"));
        Assert.False(aps.ContainsKey("category"));
    }

    [Fact]
    public async Task TheProviderTokenIsAnEs256JwtOfTheTeamSignedWithTheAuthKeyAndReusedUntilItIsOld()
    {
        var clock = new FakeTimeProvider(AlertDueAt);
        using var apns = new StubProvider(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var host = new ApnsHost(apns, TomatoesLookups(), clock);

        await host.Channel.SendAsync(Alert(IPhone), AlertDueAt, TestContext.Current.CancellationToken);
        await host.Channel.SendAsync(Reminder(IPhone), AlertDueAt, TestContext.Current.CancellationToken);
        clock.Advance(ApnsProviderToken.Lifetime);
        await host.Channel.SendAsync(Alert(IPhone) with { DueAt = AlertDueAt.AddHours(1) }, AlertDueAt, TestContext.Current.CancellationToken);

        var tokens = apns.Requests.Select(request => request.Headers["authorization"]).ToList();
        Assert.All(tokens, token => Assert.StartsWith("bearer ", token, StringComparison.Ordinal));
        Assert.Equal(tokens[0], tokens[1]);
        Assert.NotEqual(tokens[0], tokens[2]);

        var parts = tokens[0]["bearer ".Length..].Split('.');
        var header = JsonNode.Parse(Base64Url.DecodeFromChars(parts[0]))!;
        var claims = JsonNode.Parse(Base64Url.DecodeFromChars(parts[1]))!;
        Assert.Equal(("ES256", "KEY1234567"), (header["alg"]!.GetValue<string>(), header["kid"]!.GetValue<string>()));
        Assert.Equal(("TEAM123456", AlertDueAt.ToUnixTimeSeconds()), (claims["iss"]!.GetValue<string>(), claims["iat"]!.GetValue<long>()));

        using var key = ECDsa.Create();
        key.ImportFromPem(host.KeyPem);
        Assert.True(key.VerifyData(
            Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"),
            Base64Url.DecodeFromChars(parts[2]),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    [Fact]
    public async Task ASandboxTokenGoesToTheSandboxEndpoint()
    {
        using var apns = new StubProvider(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var host = new ApnsHost(apns, TomatoesLookups());

        await host.Channel.SendAsync(
            Alert(IPhone with { Environment = ApnsEnvironment.Sandbox }),
            AlertDueAt,
            TestContext.Current.CancellationToken);

        Assert.Equal("api.sandbox.push.apple.com", Assert.Single(apns.Requests).Uri.Host);
    }

    [Fact]
    public async Task AUserWithoutAnIPhoneIsASuccessWithNothingSentAndNothingLookedUp()
    {
        var lookups = TomatoesLookups();
        using var apns = new StubProvider(_ => throw new InvalidOperationException("No request expected."));
        using var host = new ApnsHost(apns, lookups);

        var delivery = await host.Channel.SendAsync(Alert(Android), AlertDueAt, TestContext.Current.CancellationToken);

        Assert.Equal(ChannelDelivery.Nothing, delivery, DeliveryComparer.Instance);
        Assert.Empty(apns.Requests);
        Assert.Equal(0, lookups.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Gone, "Unregistered")]
    [InlineData(HttpStatusCode.BadRequest, "BadDeviceToken")]
    [InlineData(HttpStatusCode.BadRequest, "DeviceTokenNotForTopic")]
    public async Task ATokenApnsReportsAsInvalidComesBackThroughTheSeamAndIsNoFailure(HttpStatusCode status, string reason)
    {
        var other = IPhone with { InstallationId = "ipad", Token = "aa" + ApnsToken[2..] };
        using var apns = new StubProvider(request => request.Uri.AbsolutePath.EndsWith(ApnsToken, StringComparison.Ordinal)
            ? StubProvider.Json(status, $$"""{"reason":"{{reason}}"}""")
            : new HttpResponseMessage(HttpStatusCode.OK));
        using var host = new ApnsHost(apns, TomatoesLookups());

        var delivery = await host.Channel.SendAsync(Alert(IPhone, other), AlertDueAt, TestContext.Current.CancellationToken);

        Assert.Equal(new ChannelDelivery(1, ["iphone"]), delivery, DeliveryComparer.Instance);

        // Not tried again: the token will not become valid.
        Assert.Equal(2, apns.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task ATransientFailureIsTriedAgainWithinTheBoundAndThenTheChannelThrows(HttpStatusCode status)
    {
        using var apns = new StubProvider(_ => StubProvider.Json(status, """{"reason":"ServiceUnavailable"}"""));
        using var host = new ApnsHost(apns, TomatoesLookups());

        var thrown = await Assert.ThrowsAsync<PushProviderException>(
            () => host.Channel.SendAsync(Alert(IPhone), AlertDueAt, TestContext.Current.CancellationToken));

        Assert.Equal(3, apns.Requests.Count);
        Assert.DoesNotContain(ApnsToken, thrown.Message, StringComparison.Ordinal);

        // Every try carried the same collapse identity, so a repeated send replaces the earlier one.
        Assert.Single(apns.Requests.Select(request => request.Headers["apns-collapse-id"]).Distinct());
    }

    [Fact]
    public async Task AProviderThatRecoversWithinTheTriesDelivers()
    {
        var calls = 0;
        using var apns = new StubProvider(_ => Interlocked.Increment(ref calls) == 1
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : new HttpResponseMessage(HttpStatusCode.OK));
        using var host = new ApnsHost(apns, TomatoesLookups());

        var delivery = await host.Channel.SendAsync(Alert(IPhone), AlertDueAt, TestContext.Current.CancellationToken);

        Assert.Equal(1, delivery.Delivered);
        Assert.Equal(2, apns.Requests.Count);
    }

    [Fact]
    public async Task AProviderThatCannotBeReachedCountsAsTransient()
    {
        using var apns = new StubProvider(_ => throw new HttpRequestException("Connection refused."));
        using var host = new ApnsHost(apns, TomatoesLookups());

        await Assert.ThrowsAsync<PushProviderException>(
            () => host.Channel.SendAsync(Alert(IPhone), AlertDueAt, TestContext.Current.CancellationToken));

        Assert.Equal(3, apns.Requests.Count);
    }

    [Fact]
    public async Task AnExpiredProviderTokenIsSignedAgainForTheNextTry()
    {
        var clock = new FakeTimeProvider(AlertDueAt);
        var calls = 0;
        using var apns = new StubProvider(_ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                // The next token is signed a second later, so it differs.
                clock.Advance(TimeSpan.FromSeconds(1));
                return StubProvider.Json(HttpStatusCode.Forbidden, """{"reason":"ExpiredProviderToken"}""");
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var host = new ApnsHost(apns, TomatoesLookups(), clock);

        var delivery = await host.Channel.SendAsync(Alert(IPhone), AlertDueAt, TestContext.Current.CancellationToken);

        Assert.Equal(1, delivery.Delivered);
        Assert.Equal(2, apns.Requests.Select(request => request.Headers["authorization"]).Distinct().Count());
    }

    [Fact]
    public async Task ARefusalThatAnotherTryDoesNotChangeIsLoggedAsAnErrorNotRepeatedAndRemovesNoRegistration()
    {
        using var apns = new StubProvider(_ => StubProvider.Json(HttpStatusCode.Forbidden, """{"reason":"InvalidProviderToken"}"""));
        using var host = new ApnsHost(apns, TomatoesLookups());

        // No exception: the delivery must not stay due, or every wake would send it to APNs again without end.
        var delivery = await host.Channel.SendAsync(Alert(IPhone), AlertDueAt, TestContext.Current.CancellationToken);

        Assert.Equal(ChannelDelivery.Nothing, delivery, DeliveryComparer.Instance);
        Assert.Single(apns.Requests);
        var (level, message) = Assert.Single(host.Log.Entries);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Error, level);
        Assert.Contains("403 InvalidProviderToken", message, StringComparison.Ordinal);
        Assert.DoesNotContain(ApnsToken, message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADeviceThatHasThePushAndASiblingThatFailedIsADeliveryThatIsNotRepeated()
    {
        var ipad = IPhone with { InstallationId = "ipad", Token = "aa" + ApnsToken[2..] };
        var old = IPhone with { InstallationId = "old", Token = "bb" + ApnsToken[2..] };
        using var apns = new StubProvider(request =>
            request.Uri.AbsolutePath.EndsWith(ipad.Token, StringComparison.Ordinal) ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : request.Uri.AbsolutePath.EndsWith(old.Token, StringComparison.Ordinal) ? StubProvider.Json(HttpStatusCode.Gone, """{"reason":"Unregistered"}""")
            : new HttpResponseMessage(HttpStatusCode.OK));
        using var host = new ApnsHost(apns, TomatoesLookups());

        var delivery = await host.Channel.SendAsync(Alert(IPhone, ipad, old), AlertDueAt, TestContext.Current.CancellationToken);

        // The iPhone has the push and the dead token is reported, although the iPad could not be reached: a
        // repeat would reach the iPhone again.
        Assert.Equal(new ChannelDelivery(1, ["old"]), delivery, DeliveryComparer.Instance);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, Assert.Single(host.Log.Entries).Level);
    }

    [Fact]
    public async Task WhenNoDeviceHasThePushAFailedSendStillNamesTheTokenApnsNoLongerKnows()
    {
        var ipad = IPhone with { InstallationId = "ipad", Token = "aa" + ApnsToken[2..] };
        using var apns = new StubProvider(request => request.Uri.AbsolutePath.EndsWith(ApnsToken, StringComparison.Ordinal)
            ? StubProvider.Json(HttpStatusCode.Gone, """{"reason":"Unregistered"}""")
            : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var host = new ApnsHost(apns, TomatoesLookups());

        var thrown = await Assert.ThrowsAsync<PushProviderException>(
            () => host.Channel.SendAsync(Alert(IPhone, ipad), AlertDueAt, TestContext.Current.CancellationToken));

        Assert.Equal(["iphone"], thrown.InvalidInstallations);
    }

    [Fact]
    public async Task AProviderThatNeverAnswersEndsTheChannelWithinItsSendBudget()
    {
        var clock = new FakeTimeProvider(AlertDueAt);
        using var apns = StubProvider.Silent();
        using var host = new ApnsHost(apns, TomatoesLookups(), clock);

        var send = host.Channel.SendAsync(Alert(IPhone), AlertDueAt, TestContext.Current.CancellationToken);
        await AdvanceUntilDoneAsync(clock, send);

        // The User grain waits for the channel: the budget ends it, and the delivery stays due.
        await Assert.ThrowsAsync<PushProviderException>(() => send);
        Assert.InRange(clock.GetUtcNow() - AlertDueAt, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(7));
        Assert.Single(apns.Requests);
    }

    [Fact]
    public void AnAuthKeyThatIsNoP256KeyIsRefusedWhenTheChannelIsSetUp()
    {
        using var rsa = RSA.Create(2048);
        var options = new ApnsOptions { KeyId = "KEY1234567", TeamId = "TEAM123456", PrivateKeyPem = rsa.ExportPkcs8PrivateKeyPem() };

        Assert.Throws<ArgumentException>(() => new ApnsProviderToken(options, TimeProvider.System));
        Assert.Throws<ArgumentException>(() => new ApnsProviderToken(new ApnsOptions { KeyId = "KEY1234567", TeamId = "TEAM123456", PrivateKeyPem = "not a key" }, TimeProvider.System));
        Assert.Throws<ArgumentException>(() => new ApnsProviderToken(new ApnsOptions(), TimeProvider.System));
    }

    private sealed class ApnsHost : IDisposable
    {
        private readonly ApnsProviderToken _token;

        public ApnsHost(StubProvider apns, FakeLookups lookups, FakeTimeProvider? clock = null)
        {
            clock ??= new FakeTimeProvider(AlertDueAt);
            KeyPem = NewP256Pem();
            var options = TestOptions();
            options.Apns.KeyId = "KEY1234567";
            options.Apns.TeamId = "TEAM123456";
            options.Apns.PrivateKeyPem = KeyPem;
            _token = new ApnsProviderToken(options.Apns, clock);
            Channel = new ApnsChannel(apns, _token, Builder(lookups, options, clock), Options.Create(options), clock, Log);
        }

        public ChannelLog<ApnsChannel> Log { get; } = new();

        public string KeyPem { get; }

        public ApnsChannel Channel { get; }

        public void Dispose() => _token.Dispose();
    }
}

/// <summary>
/// Compares two deliveries by what they say: a record compares its list by reference.
/// </summary>
internal sealed class DeliveryComparer : IEqualityComparer<ChannelDelivery>
{
    public static DeliveryComparer Instance { get; } = new();

    public bool Equals(ChannelDelivery? x, ChannelDelivery? y) =>
        x is not null && y is not null && x.Delivered == y.Delivered && x.InvalidInstallations.SequenceEqual(y.InvalidInstallations);

    public int GetHashCode(ChannelDelivery obj) => obj.Delivered;
}
