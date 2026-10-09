using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Coldframe.Contracts.Sites;

namespace Coldframe.Server.IntegrationTests.Edge;

/// <summary>
/// The notification settings endpoints on the AppHost (Story 6.3): <c>/me/notification-settings</c> (the
/// caller's Notification Window and time zone), <c>/sites/{siteId}/notification-settings</c> (the caller's mute
/// and Reminder cadence for one Site) and <c>/sites/{siteId}/reminder-cadence</c> (the Site's cadence). Every
/// write answers 200 with the state in force, a refusal is a 400 Problem Details that journals nothing, and
/// one User's settings never change another's. The authorization matrix covers every Role and Site.
/// </summary>
public sealed class NotificationSettingsEndpointTests(EdgeApiFixture edge) : IClassFixture<EdgeApiFixture>
{
    private const string Validation = "urn:coldframe:problem:validation";

    private static readonly Uri Mine = new("/me/notification-settings", UriKind.Relative);

    [Fact]
    public async Task ANewUserReadsTheDefaultWindowAndNoTimeZone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = await edge.CreateUserAsync("notify-new", cancellationToken);
        using var server = edge.CreateServerClient(user.AccessToken);

        using var response = await server.GetAsync(Mine, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await EdgeApiFixture.ReadJsonAsync(response, cancellationToken);
        Assert.Equal(("07:00", "22:00"), Window(body));
        Assert.False(body.RootElement.TryGetProperty("timeZone", out _));
        Assert.False(body.RootElement.GetProperty("timeZoneConfirmed").GetBoolean());
        Assert.Empty(await edge.AliasesAsync($"user/{user.UserId}", cancellationToken));
    }

    [Fact]
    public async Task AWindowNamingOnlyItsStartClosesAt2200AndIsOneEvent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = await edge.CreateUserAsync("notify-from", cancellationToken);
        using var server = edge.CreateServerClient(user.AccessToken);

        using var response = await server.PatchAsJsonAsync(Mine, new { window = new { from = "06:30" } }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var body = await EdgeApiFixture.ReadJsonAsync(response, cancellationToken))
        {
            Assert.Equal(("06:30", "22:00"), Window(body));
        }

        Assert.Equal(["user.notification-window-changed"], await edge.AliasesAsync($"user/{user.UserId}", cancellationToken));
        var changed = Assert.IsType<NotificationWindowChanged>((await edge.ReadStreamAsync($"user/{user.UserId}", cancellationToken))[0].Data);
        Assert.Equal((390, 1320), (changed.FromMinutes, changed.ToMinutes));

        // The next load from the Server shows it, and the same request again saves nothing.
        using var again = await server.PatchAsJsonAsync(Mine, new { window = new { from = "06:30", to = "22:00" } }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        using var read = await server.GetAsync(Mine, cancellationToken);
        using var readBody = await EdgeApiFixture.ReadJsonAsync(read, cancellationToken);
        Assert.Equal(("06:30", "22:00"), Window(readBody));
        Assert.Single(await edge.AliasesAsync($"user/{user.UserId}", cancellationToken));
    }

    [Fact]
    public async Task EveryBadRequestIsA400ProblemDetailsAndNothingIsJournaled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = await edge.CreateUserAsync("notify-bad", cancellationToken);
        using var server = edge.CreateServerClient(user.AccessToken);

        var bodies = new[]
        {
            // A window needs from before to, both HH:mm within one day.
            """{"window":{"from":"07:00","to":"07:00"}}""",
            """{"window":{"from":"22:00","to":"07:00"}}""",
            """{"window":{"from":"7:00"}}""",
            """{"window":{"from":"07:00","to":"24:00"}}""",
            """{"window":{"from":"22:30"}}""",
            """{"window":{"to":"22:00"}}""",
            """{"window":{}}""",
            """{"window":"07:00"}""",
            """{"window":{"from":420}}""",
            // An unknown zone, a Windows ID, an empty or overlong one; chosen or detected.
            """{"timeZone":"Mars/Olympus"}""",
            """{"timeZone":"W. Europe Standard Time"}""",
            """{"timeZone":""}""",
            """{"timeZone":"Europe/ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ"}""",
            """{"detectedTimeZone":"Mars/Olympus"}""",
            """{"timeZone":7}""",
            // A valid part does not save when another part is refused.
            """{"window":{"from":"06:30"},"timeZone":"Mars/Olympus"}""",
            """{"window":{"from":"23:00"},"timeZone":"Europe/Zurich"}""",
            // Nothing to change, no JSON object.
            "{}",
            "[]",
            "null",
        };
        foreach (var json in bodies)
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await server.PatchAsync(Mine, content, cancellationToken);
            await EdgeApiTests.AssertProblemAsync(response, HttpStatusCode.BadRequest, Validation, cancellationToken);
        }

        using var notJson = await server.PatchAsync(Mine, new StringContent("window", Encoding.UTF8, "text/plain"), cancellationToken);
        await EdgeApiTests.AssertProblemAsync(notJson, HttpStatusCode.BadRequest, Validation, cancellationToken);

        Assert.Empty(await edge.AliasesAsync($"user/{user.UserId}", cancellationToken));
        using var read = await server.GetAsync(Mine, cancellationToken);
        using var body = await EdgeApiFixture.ReadJsonAsync(read, cancellationToken);
        Assert.Equal(("07:00", "22:00"), Window(body));
        Assert.False(body.RootElement.TryGetProperty("timeZone", out _));
    }

    [Fact]
    public async Task ADetectedZoneIsStoredUnconfirmedAndAChosenZoneWinsForGood()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = await edge.CreateUserAsync("notify-zone", cancellationToken);
        using var server = edge.CreateServerClient(user.AccessToken);

        using (var detected = await server.PatchAsJsonAsync(Mine, new { detectedTimeZone = "Europe/Zurich" }, cancellationToken))
        using (var body = await EdgeApiFixture.ReadJsonAsync(detected, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, detected.StatusCode);
            Assert.Equal(("Europe/Zurich", false), Zone(body));
        }

        using (var chosen = await server.PatchAsJsonAsync(Mine, new { timeZone = "Europe/Vienna" }, cancellationToken))
        using (var body = await EdgeApiFixture.ReadJsonAsync(chosen, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, chosen.StatusCode);
            Assert.Equal(("Europe/Vienna", true), Zone(body));
        }

        // A client that starts on a device in another zone: the chosen zone stays, and nothing is journaled.
        using (var later = await server.PatchAsJsonAsync(Mine, new { detectedTimeZone = "America/New_York" }, cancellationToken))
        using (var body = await EdgeApiFixture.ReadJsonAsync(later, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, later.StatusCode);
            Assert.Equal(("Europe/Vienna", true), Zone(body));
        }

        using var read = await server.GetAsync(Mine, cancellationToken);
        using var readBody = await EdgeApiFixture.ReadJsonAsync(read, cancellationToken);
        Assert.Equal(("Europe/Vienna", true), Zone(readBody));
        Assert.Equal(["user.time-zone-detected", "user.time-zone-chosen"], await edge.AliasesAsync($"user/{user.UserId}", cancellationToken));
    }

    [Fact]
    public async Task ADetectedZoneAfterAChoiceChangesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = await edge.CreateUserAsync("notify-chosen", cancellationToken);
        using var server = edge.CreateServerClient(user.AccessToken);
        using var chosen = await server.PatchAsJsonAsync(Mine, new { timeZone = "Europe/Zurich" }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, chosen.StatusCode);

        using var detected = await server.PatchAsJsonAsync(Mine, new { detectedTimeZone = "America/New_York" }, cancellationToken);

        using var body = await EdgeApiFixture.ReadJsonAsync(detected, cancellationToken);
        Assert.Equal(("Europe/Zurich", true), Zone(body));
        Assert.Equal(["user.time-zone-chosen"], await edge.AliasesAsync($"user/{user.UserId}", cancellationToken));
    }

    [Fact]
    public async Task MySettingsAreMineAlone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var a = await edge.CreateUserAsync("notify-a", cancellationToken);
        var b = await edge.CreateUserAsync("notify-b", cancellationToken);
        using var serverA = edge.CreateServerClient(a.AccessToken);
        using var serverB = edge.CreateServerClient(b.AccessToken);

        using var changed = await serverA.PatchAsJsonAsync(Mine, new { window = new { from = "08:00", to = "20:00" }, timeZone = "Europe/Zurich" }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);

        using var read = await serverB.GetAsync(Mine, cancellationToken);
        using var body = await EdgeApiFixture.ReadJsonAsync(read, cancellationToken);
        Assert.Equal(("07:00", "22:00"), Window(body));
        Assert.False(body.RootElement.TryGetProperty("timeZone", out _));
        Assert.Empty(await edge.AliasesAsync($"user/{b.UserId}", cancellationToken));
    }

    [Fact]
    public async Task MutingASiteMutesItOnlyForMe()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var site = await SeedSiteAsync(cancellationToken);
        using var a = edge.CreateServerClient(site.Member.AccessToken);
        using var b = edge.CreateServerClient(site.Administrator.AccessToken);

        using (var fresh = await a.GetAsync(SiteSettings(site.Id), cancellationToken))
        using (var body = await EdgeApiFixture.ReadJsonAsync(fresh, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
            Assert.Equal((false, null, "daily"), Settings(body));
        }

        using var muted = await a.PutAsJsonAsync(SiteSettings(site.Id), new { muted = true }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, muted.StatusCode);
        using (var body = await EdgeApiFixture.ReadJsonAsync(muted, cancellationToken))
        {
            Assert.Equal((true, null, "daily"), Settings(body));
        }

        using (var other = await b.GetAsync(SiteSettings(site.Id), cancellationToken))
        using (var body = await EdgeApiFixture.ReadJsonAsync(other, cancellationToken))
        {
            Assert.Equal((false, null, "daily"), Settings(body));
        }

        Assert.Equal(["user.site-mute-changed"], await edge.AliasesAsync($"user/{site.Member.UserId}", cancellationToken));
        Assert.Empty(await edge.AliasesAsync($"user/{site.Administrator.UserId}", cancellationToken));

        // The same request again saves nothing; another Site of mine is not muted.
        using var again = await a.PutAsJsonAsync(SiteSettings(site.Id), new { muted = true }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Single(await edge.AliasesAsync($"user/{site.Member.UserId}", cancellationToken));
    }

    [Fact]
    public async Task MyCadenceIsSetAndWithoutTheFieldIFollowTheSiteAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var site = await SeedSiteAsync(cancellationToken);
        using var member = edge.CreateServerClient(site.Member.AccessToken);

        using (var own = await member.PutAsJsonAsync(SiteSettings(site.Id), new { muted = false, reminderCadence = "every2Days" }, cancellationToken))
        using (var body = await EdgeApiFixture.ReadJsonAsync(own, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, own.StatusCode);
            Assert.Equal((false, "every2Days", "daily"), Settings(body));
        }

        using (var back = await member.PutAsJsonAsync(SiteSettings(site.Id), new { muted = false }, cancellationToken))
        using (var body = await EdgeApiFixture.ReadJsonAsync(back, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, back.StatusCode);
            Assert.Equal((false, null, "daily"), Settings(body));
        }

        Assert.Equal(
            ["user.site-reminder-cadence-changed", "user.site-reminder-cadence-changed"],
            await edge.AliasesAsync($"user/{site.Member.UserId}", cancellationToken));
    }

    [Fact]
    public async Task ABadSiteSettingsBodyIsA400AndNothingIsJournaled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var site = await SeedSiteAsync(cancellationToken);
        using var member = edge.CreateServerClient(site.Member.AccessToken);

        var bodies = new[]
        {
            "{}",
            """{"reminderCadence":"daily"}""",
            """{"muted":"yes"}""",
            """{"muted":true,"reminderCadence":"never"}""",
            """{"muted":true,"reminderCadence":"Daily"}""",
            """{"muted":true,"reminderCadence":2}""",
            "[]",
        };
        foreach (var json in bodies)
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await member.PutAsync(SiteSettings(site.Id), content, cancellationToken);
            await EdgeApiTests.AssertProblemAsync(response, HttpStatusCode.BadRequest, Validation, cancellationToken);
        }

        Assert.Empty(await edge.AliasesAsync($"user/{site.Member.UserId}", cancellationToken));
    }

    [Fact]
    public async Task AnAdministratorSetsTheSitesCadenceAndEveryMemberResolvesItUnlessTheyHaveTheirOwn()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var site = await SeedSiteAsync(cancellationToken);
        using var admin = edge.CreateServerClient(site.Administrator.AccessToken);
        using var member = edge.CreateServerClient(site.Member.AccessToken);
        using var owner = edge.CreateServerClient(site.Owner.AccessToken);

        using (var fresh = await member.GetAsync(Cadence(site.Id), cancellationToken))
        using (var body = await EdgeApiFixture.ReadJsonAsync(fresh, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
            Assert.Equal("daily", body.RootElement.GetProperty("cadence").GetString());
        }

        // The Owner keeps a cadence of their own.
        using var own = await owner.PutAsJsonAsync(SiteSettings(site.Id), new { muted = false, reminderCadence = "daily" }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);

        using var set = await admin.PutAsJsonAsync(Cadence(site.Id), new { cadence = "every2Days" }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        using (var body = await EdgeApiFixture.ReadJsonAsync(set, cancellationToken))
        {
            Assert.Equal("every2Days", body.RootElement.GetProperty("cadence").GetString());
        }

        Assert.Equal("site.reminder-cadence-changed", (await edge.AliasesAsync($"site/{site.Id}", cancellationToken))[^1]);

        // Every member's User grain was handed it: one event each, on its own stream.
        Assert.Equal(["user.site-reminder-cadence-synced"], await edge.AliasesAsync($"user/{site.Member.UserId}", cancellationToken));
        Assert.Equal(["user.site-reminder-cadence-synced"], await edge.AliasesAsync($"user/{site.Administrator.UserId}", cancellationToken));
        Assert.Equal(
            ["user.site-reminder-cadence-changed", "user.site-reminder-cadence-synced"],
            await edge.AliasesAsync($"user/{site.Owner.UserId}", cancellationToken));
        var synced = Assert.IsType<SiteReminderCadenceSynced>((await edge.ReadStreamAsync($"user/{site.Member.UserId}", cancellationToken))[0].Data);
        Assert.Equal((site.Id, ReminderCadence.Every2Days), (synced.SiteId, synced.Cadence));

        using (var mine = await member.GetAsync(SiteSettings(site.Id), cancellationToken))
        using (var body = await EdgeApiFixture.ReadJsonAsync(mine, cancellationToken))
        {
            Assert.Equal((false, null, "every2Days"), Settings(body));
        }

        using (var theirs = await owner.GetAsync(SiteSettings(site.Id), cancellationToken))
        using (var body = await EdgeApiFixture.ReadJsonAsync(theirs, cancellationToken))
        {
            Assert.Equal((false, "daily", "every2Days"), Settings(body));
        }

        // The same request again answers 200, saves nothing on the Site and hands nobody a second event.
        using var again = await admin.PutAsJsonAsync(Cadence(site.Id), new { cadence = "every2Days" }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(1, (await edge.AliasesAsync($"site/{site.Id}", cancellationToken)).Count(alias => alias == "site.reminder-cadence-changed"));
        Assert.Single(await edge.AliasesAsync($"user/{site.Member.UserId}", cancellationToken));
    }

    [Fact]
    public async Task AMemberReadsTheSitesCadenceButGets403OnAChangeAndAnUnknownValueIsA400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var site = await SeedSiteAsync(cancellationToken);
        using var admin = edge.CreateServerClient(site.Administrator.AccessToken);
        using var member = edge.CreateServerClient(site.Member.AccessToken);
        var events = (await edge.AliasesAsync($"site/{site.Id}", cancellationToken)).Count;

        using var read = await member.GetAsync(Cadence(site.Id), cancellationToken);
        using var write = await member.PutAsJsonAsync(Cadence(site.Id), new { cadence = "every2Days" }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        await EdgeApiTests.AssertProblemAsync(write, HttpStatusCode.Forbidden, "urn:coldframe:problem:forbidden", cancellationToken);

        foreach (var json in new[] { """{"cadence":"never"}""", """{"cadence":"Every2Days"}""", """{"cadence":2}""", "{}", "[]" })
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await admin.PutAsync(Cadence(site.Id), content, cancellationToken);
            await EdgeApiTests.AssertProblemAsync(response, HttpStatusCode.BadRequest, Validation, cancellationToken);
        }

        Assert.Equal(events, (await edge.AliasesAsync($"site/{site.Id}", cancellationToken)).Count);
    }

    [Fact]
    public async Task ASiteIAmNoMemberOfIs403AndAMissingOneIs404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var site = await SeedSiteAsync(cancellationToken);
        var stranger = await edge.CreateUserAsync("notify-stranger", cancellationToken);
        using var server = edge.CreateServerClient(stranger.AccessToken);
        var missing = Guid.CreateVersion7().ToString();

        using var read = await server.GetAsync(SiteSettings(site.Id), cancellationToken);
        using var write = await server.PutAsJsonAsync(SiteSettings(site.Id), new { muted = true }, cancellationToken);
        using var cadence = await server.GetAsync(Cadence(site.Id), cancellationToken);
        using var gone = await server.PutAsJsonAsync(SiteSettings(missing), new { muted = true }, cancellationToken);

        await EdgeApiTests.AssertProblemAsync(read, HttpStatusCode.Forbidden, "urn:coldframe:problem:forbidden", cancellationToken);
        await EdgeApiTests.AssertProblemAsync(write, HttpStatusCode.Forbidden, "urn:coldframe:problem:forbidden", cancellationToken);
        await EdgeApiTests.AssertProblemAsync(cadence, HttpStatusCode.Forbidden, "urn:coldframe:problem:forbidden", cancellationToken);
        await EdgeApiTests.AssertProblemAsync(gone, HttpStatusCode.NotFound, "urn:coldframe:problem:site-not-found", cancellationToken);
        Assert.Empty(await edge.AliasesAsync($"user/{stranger.UserId}", cancellationToken));
    }

    private static Uri SiteSettings(string siteId) => new($"/sites/{siteId}/notification-settings", UriKind.Relative);

    private static Uri Cadence(string siteId) => new($"/sites/{siteId}/reminder-cadence", UriKind.Relative);

    private static (string? From, string? To) Window(JsonDocument body)
    {
        var window = body.RootElement.GetProperty("window");
        return (window.GetProperty("from").GetString(), window.GetProperty("to").GetString());
    }

    private static (string? TimeZone, bool Confirmed) Zone(JsonDocument body) =>
        (body.RootElement.TryGetProperty("timeZone", out var zone) ? zone.GetString() : null,
            body.RootElement.GetProperty("timeZoneConfirmed").GetBoolean());

    private static (bool Muted, string? Own, string? Site) Settings(JsonDocument body) =>
        (body.RootElement.GetProperty("muted").GetBoolean(),
            body.RootElement.TryGetProperty("reminderCadence", out var own) ? own.GetString() : null,
            body.RootElement.GetProperty("siteReminderCadence").GetString());

    // A Site as raw site.* events, like the authorization matrix: the User grains hold no Membership of it.
    private async Task<SeededSite> SeedSiteAsync(CancellationToken cancellationToken)
    {
        var owner = await edge.CreateUserAsync("notify-owner", cancellationToken);
        var administrator = await edge.CreateUserAsync("notify-admin", cancellationToken);
        var member = await edge.CreateUserAsync("notify-member", cancellationToken);
        var siteId = Guid.CreateVersion7().ToString();

        var last = await edge.AppendAsync(
            $"site/{siteId}",
            [
                new SiteCreated("Notifications", owner.UserId),
                new MembershipGranted(owner.UserId, SiteRole.Owner),
                new MembershipGranted(administrator.UserId, SiteRole.Administrator),
                new MembershipGranted(member.UserId, SiteRole.Member),
            ],
            cancellationToken);
        await edge.WaitForIdentityCheckpointAsync(last);

        return new SeededSite(siteId, owner, administrator, member);
    }

    private sealed record SeededSite(string Id, TestUser Owner, TestUser Administrator, TestUser Member);
}
