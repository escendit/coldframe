using System.Net;
using System.Net.Http.Json;
using System.Text;
using Coldframe.Contracts.Notifications;
using Coldframe.Contracts.Sites;
using Coldframe.Server.IntegrationTests.Devices;

namespace Coldframe.Server.IntegrationTests.Edge;

/// <summary>
/// The push registration endpoints on the AppHost (Story 6.5): a signed-in User registers and removes a device
/// under <c>/me/push-registrations/{installationId}</c>. The User grain owns the registration as events; the same
/// registration again changes nothing, a refusal is a 400 Problem Details that journals nothing, and one User's
/// registration never touches another's. The AppHost runs without push credentials. The authorization matrix
/// covers every Role and Site.
/// </summary>
/// <remarks>
/// In the serial collection: the AppHost's PostgreSQL has a fixed number of connections, which the suites that
/// run in parallel already come close to.
/// </remarks>
[Collection(IngestSuites.Name)]
public sealed class PushRegistrationTests(EdgeApiFixture edge) : IClassFixture<EdgeApiFixture>
{
    private const string Validation = "urn:coldframe:problem:validation";
    private const string Registered = "user.push-device-registered";
    private const string Removed = "user.push-device-removed";
    private const string ApnsToken = "0011223300112233001122330011223300112233001122330011223300112233";

    [Fact]
    public async Task ASignedInUserRegistersAndRemovesADeviceAndASecondIdenticalRegistrationChangesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = await edge.CreateUserAsync("push-register", cancellationToken);
        using var server = edge.CreateServerClient(user.AccessToken);
        var device = Device("0199c1f0-5a00-7000-8000-00000000d001");

        using (var registered = await server.PutAsJsonAsync(device, new { platform = "apns", token = ApnsToken, environment = "sandbox" }, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, registered.StatusCode);
        }

        using (var again = await server.PutAsJsonAsync(device, new { platform = "apns", token = ApnsToken, environment = "sandbox" }, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        }

        // One event: the User grain owns the registration, and the second request wrote nothing.
        Assert.Equal([Registered], await edge.AliasesAsync($"user/{user.UserId}", cancellationToken));
        var journaled = Assert.IsType<PushDeviceRegistered>((await edge.ReadStreamAsync($"user/{user.UserId}", cancellationToken))[0].Data);
        Assert.Equal(
            ("0199c1f0-5a00-7000-8000-00000000d001", PushPlatform.Apns, ApnsToken, (ApnsEnvironment?)ApnsEnvironment.Sandbox),
            (journaled.InstallationId, journaled.Platform, journaled.Token, journaled.Environment));

        using (var removed = await server.DeleteAsync(device, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        }

        // Removing again, as a sign-out that is repeated does, succeeds and writes nothing.
        using (var removedAgain = await server.DeleteAsync(device, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, removedAgain.StatusCode);
        }

        Assert.Equal([Registered, Removed], await edge.AliasesAsync($"user/{user.UserId}", cancellationToken));
        var removal = Assert.IsType<PushDeviceRemoved>((await edge.ReadStreamAsync($"user/{user.UserId}", cancellationToken))[1].Data);
        Assert.Equal(PushDeviceRemovalReason.Requested, removal.Reason);
    }

    [Fact]
    public async Task ATokenThatRotatedReplacesTheRegistrationOfItsInstallation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = await edge.CreateUserAsync("push-rotate", cancellationToken);
        using var server = edge.CreateServerClient(user.AccessToken);
        var device = Device("android-1");

        using var first = await server.PutAsJsonAsync(device, new { platform = "fcm", token = "token-1" }, cancellationToken);
        using var rotated = await server.PutAsJsonAsync(device, new { platform = "fcm", token = "token-2" }, cancellationToken);

        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.NoContent), (first.StatusCode, rotated.StatusCode));
        var events = await edge.ReadStreamAsync($"user/{user.UserId}", cancellationToken);
        Assert.Equal(
            [("android-1", "token-1"), ("android-1", "token-2")],
            events.Select(journaled => Assert.IsType<PushDeviceRegistered>(journaled.Data)).Select(registered => (registered.InstallationId, registered.Token)));
    }

    [Fact]
    public async Task EveryBadRegistrationIsA400ProblemDetailsAndNothingIsJournaled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = await edge.CreateUserAsync("push-bad", cancellationToken);
        using var server = edge.CreateServerClient(user.AccessToken);
        var device = Device("android-1");

        var bodies = new[]
        {
            // An empty token, one with a space, one that is too long, none at all.
            """{"platform":"fcm","token":""}""",
            """{"platform":"fcm","token":"with space"}""",
            $$"""{"platform":"fcm","token":"{{new string('a', 4097)}}"}""",
            """{"platform":"fcm"}""",
            """{"platform":"fcm","token":7}""",
            // An unknown platform, or none.
            """{"platform":"wns","token":"token"}""",
            """{"platform":"APNS","token":"token","environment":"production"}""",
            """{"token":"token"}""",
            // APNs needs its environment; FCM has none.
            """{"platform":"apns","token":"token"}""",
            """{"platform":"apns","token":"token","environment":"staging"}""",
            """{"platform":"fcm","token":"token","environment":"production"}""",
            // No JSON object.
            "{}",
            "[]",
            "null",
        };

        foreach (var json in bodies)
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await server.PutAsync(device, content, cancellationToken);
            await EdgeApiTests.AssertProblemAsync(response, HttpStatusCode.BadRequest, Validation, cancellationToken);
        }

        using var notJson = await server.PutAsync(device, new StringContent("token", Encoding.UTF8, "text/plain"), cancellationToken);
        await EdgeApiTests.AssertProblemAsync(notJson, HttpStatusCode.BadRequest, Validation, cancellationToken);

        // An installation ID that is too long or has other characters, for both operations.
        foreach (var installationId in new[] { new string('a', 65), "has%20space", "semi;colon" })
        {
            using var put = await server.PutAsJsonAsync(Device(installationId), new { platform = "fcm", token = "token" }, cancellationToken);
            await EdgeApiTests.AssertProblemAsync(put, HttpStatusCode.BadRequest, Validation, cancellationToken);
            using var delete = await server.DeleteAsync(Device(installationId), cancellationToken);
            await EdgeApiTests.AssertProblemAsync(delete, HttpStatusCode.BadRequest, Validation, cancellationToken);
        }

        Assert.Empty(await edge.AliasesAsync($"user/{user.UserId}", cancellationToken));
    }

    [Fact]
    public async Task OneUsersRegistrationNeverTouchesAnothersAndNoTokenIsNeededToBeRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var first = await edge.CreateUserAsync("push-first", cancellationToken);
        var second = await edge.CreateUserAsync("push-second", cancellationToken);
        using var firstServer = edge.CreateServerClient(first.AccessToken);
        using var secondServer = edge.CreateServerClient(second.AccessToken);
        using var anonymous = edge.CreateServerClient();
        var device = Device("shared-phone");

        // The same phone, signed in as one User and later as another: each User has its own registration.
        using var one = await firstServer.PutAsJsonAsync(device, new { platform = "fcm", token = "token-1" }, cancellationToken);
        using var two = await secondServer.PutAsJsonAsync(device, new { platform = "fcm", token = "token-1" }, cancellationToken);
        using var removed = await secondServer.DeleteAsync(device, cancellationToken);

        Assert.Equal(
            (HttpStatusCode.NoContent, HttpStatusCode.NoContent, HttpStatusCode.NoContent),
            (one.StatusCode, two.StatusCode, removed.StatusCode));
        Assert.Equal([Registered], await edge.AliasesAsync($"user/{first.UserId}", cancellationToken));
        Assert.Equal([Registered, Removed], await edge.AliasesAsync($"user/{second.UserId}", cancellationToken));

        using var unauthenticatedPut = await anonymous.PutAsJsonAsync(device, new { platform = "fcm", token = "token-1" }, cancellationToken);
        using var unauthenticatedDelete = await anonymous.DeleteAsync(device, cancellationToken);
        await EdgeApiTests.AssertProblemAsync(unauthenticatedPut, HttpStatusCode.Unauthorized, "urn:coldframe:problem:unauthorized", cancellationToken);
        await EdgeApiTests.AssertProblemAsync(unauthenticatedDelete, HttpStatusCode.Unauthorized, "urn:coldframe:problem:unauthorized", cancellationToken);
    }

    private static Uri Device(string installationId) => new($"/me/push-registrations/{installationId}", UriKind.Relative);
}
