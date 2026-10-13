using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Sites;
using Coldframe.DeviceSimulator;
using Coldframe.Server.IntegrationTests.Edge;
using ProtocolKind = Coldframe.Protocol.Setup.V1.DeviceKind;

namespace Coldframe.Server.IntegrationTests.Devices;

/// <summary>
/// <c>PUT</c> and <c>DELETE</c> on <c>/sites/{siteId}/devices/{deviceId}/pause</c> and <c>/sites/{siteId}/pause</c>
/// on the AppHost (Story 8.1): the endpoint rows of the story's matrix. An Administrator or Owner pauses and
/// resumes a Device or the whole Site, with or without an end date in their own time zone; the Lot reads
/// <c>paused</c> with its sources through <c>GET</c> lots; a bad date, a Member and an unknown Device change
/// nothing, and the journals show it. The authorization matrix covers every Role and Site. The Server runs on the
/// real clock here, so the end dates lie ahead of today; what an end date does when it passes is in
/// <c>PauseGrainTests</c> and <c>SitePauseGrainTests</c>.
/// </summary>
public sealed class PauseEndpointTests(EdgeApiFixture edge) : IClassFixture<EdgeApiFixture>
{
    private const string DevicePaused = "device.paused";

    private const string DeviceResumed = "device.resumed";

    private const string ServerTimeFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAdministratorPausesAndResumesANodeAndTheSameRequestAgainJournalsNothing()
    {
        var site = await SeedSiteAsync();
        var (node, lotId) = await EnrolNodeAsync(site, "Strawberries");
        using var server = edge.CreateServerClient(site.Administrator.AccessToken);

        using var paused = await server.PutAsJsonAsync(DevicePauseUri(site.Id, node), new { }, Ct);

        Assert.Equal(("device", null), await DevicePauseAsync(paused));
        var lot = await LotAsync(site, lotId, status => status == "paused");
        Assert.Equal("device", Sources(lot));
        Assert.False(lot.TryGetProperty("pausedUntil", out _));

        using var again = await server.PutAsJsonAsync(DevicePauseUri(site.Id, node), new { }, Ct);

        Assert.Equal(("device", null), await DevicePauseAsync(again));
        Assert.Single(await edge.AliasesAsync($"device/{node}", Ct), alias => alias == DevicePaused);

        // Resumed: nothing pauses it any more, and the Lot has a live status again.
        using var resumed = await server.DeleteAsync(DevicePauseUri(site.Id, node), Ct);

        Assert.Equal(("", null), await DevicePauseAsync(resumed));
        lot = await LotAsync(site, lotId, status => status != "paused");
        Assert.False(lot.TryGetProperty("pausedBy", out _));

        // Resuming a Device that is not paused answers the same and journals nothing.
        using var resumedAgain = await server.DeleteAsync(DevicePauseUri(site.Id, node), Ct);

        Assert.Equal(("", null), await DevicePauseAsync(resumedAgain));
        Assert.Single(await edge.AliasesAsync($"device/{node}", Ct), alias => alias == DeviceResumed);
    }

    [Fact]
    public async Task AnEndDateIsMidnightInTheCallersTimeZoneAndAnotherRequestChangesOrRemovesIt()
    {
        var site = await SeedSiteAsync();
        var (node, lotId) = await EnrolNodeAsync(site, "Strawberries");
        var hub = await EnrolAsync(site, ProtocolKind.Hub);
        var today = DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime);
        var first = today.AddDays(40);
        var second = today.AddDays(200);

        // The Administrator lives in Zurich; the Owner told the Server no zone, which reads as UTC.
        using var administrator = edge.CreateServerClient(site.Administrator.AccessToken);
        using var zone = await administrator.PatchAsJsonAsync(new Uri("/me/notification-settings", UriKind.Relative), new { timeZone = "Europe/Zurich" }, Ct);
        Assert.Equal(HttpStatusCode.OK, zone.StatusCode);
        using var owner = edge.CreateServerClient(site.Owner.AccessToken);

        using var paused = await administrator.PutAsJsonAsync(DevicePauseUri(site.Id, node), new { until = Date(first) }, Ct);

        var inZurich = MidnightIn("Europe/Zurich", first);
        Assert.Equal(("device", Format(inZurich)), await DevicePauseAsync(paused));
        Assert.NotEqual(TimeSpan.Zero, inZurich.TimeOfDay);
        var journaled = Assert.IsType<DevicePaused>((await edge.ReadStreamAsync($"device/{node}", Ct))[^1].Data);
        Assert.Equal((DevicePauseSource.Device, (DateTimeOffset?)inZurich), (journaled.Source, journaled.EndsAt));
        var lot = await LotAsync(site, lotId, status => status == "paused");
        Assert.Equal(Format(inZurich), lot.GetProperty("pausedUntil").GetString());

        // The same date again changes nothing; another date is the new end; no date is no end.
        using var same = await administrator.PutAsJsonAsync(DevicePauseUri(site.Id, node), new { until = Date(first) }, Ct);
        Assert.Equal(("device", Format(inZurich)), await DevicePauseAsync(same));
        Assert.Single(await edge.AliasesAsync($"device/{node}", Ct), alias => alias == DevicePaused);

        using var later = await administrator.PutAsJsonAsync(DevicePauseUri(site.Id, node), new { until = Date(second) }, Ct);
        Assert.Equal(("device", Format(MidnightIn("Europe/Zurich", second))), await DevicePauseAsync(later));

        using var open = await administrator.PutAsJsonAsync(DevicePauseUri(site.Id, node), new { }, Ct);
        Assert.Equal(("device", null), await DevicePauseAsync(open));
        Assert.Equal(3, (await edge.AliasesAsync($"device/{node}", Ct)).Count(alias => alias == DevicePaused));

        // A Hub is paused the same way, by the Owner, at midnight UTC.
        using var hubPaused = await owner.PutAsJsonAsync(DevicePauseUri(site.Id, hub), new { until = Date(first) }, Ct);
        Assert.Equal(("device", Format(new DateTimeOffset(first.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero))), await DevicePauseAsync(hubPaused));
    }

    [Fact]
    public async Task ASitePauseReachesEveryDeviceAndADevicePausedByBothResumesOnlyWhenBothAreLifted()
    {
        var site = await SeedSiteAsync();
        var (node, lotId) = await EnrolNodeAsync(site, "Strawberries");
        var hub = await EnrolAsync(site, ProtocolKind.Hub);
        var until = DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime).AddDays(140);
        var siteEnd = Format(new DateTimeOffset(until.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        using var server = edge.CreateServerClient(site.Administrator.AccessToken);

        using var sitePaused = await server.PutAsJsonAsync(SitePauseUri(site.Id), new { until = Date(until) }, Ct);

        // The Site answers once it holds the Pause; its Devices follow.
        Assert.Equal((true, siteEnd), await SitePauseAsync(sitePaused));
        var lot = await LotAsync(site, lotId, status => status == "paused");
        Assert.Equal(("site", siteEnd), (Sources(lot), lot.GetProperty("pausedUntil").GetString()));
        await JournaledAsync(hub, DevicePaused, 1);

        using var sitePausedAgain = await server.PutAsJsonAsync(SitePauseUri(site.Id), new { until = Date(until) }, Ct);
        Assert.Equal((true, siteEnd), await SitePauseAsync(sitePausedAgain));
        Assert.Single(await edge.AliasesAsync($"site/{site.Id}", Ct), alias => alias == "site.paused");

        // The Node is paused by itself as well, without an end: both sources, and so no end.
        using var devicePaused = await server.PutAsJsonAsync(DevicePauseUri(site.Id, node), new { }, Ct);
        Assert.Equal(("device,site", null), await DevicePauseAsync(devicePaused));
        lot = await LotAsync(site, lotId, _ => true, pausedBy: 2);
        Assert.Equal(["device", "site"], Strings(lot.GetProperty("pausedBy")));
        Assert.False(lot.TryGetProperty("pausedUntil", out _));

        // Its own source lifted: still paused, by the Site, until the Site's end.
        using var deviceResumed = await server.DeleteAsync(DevicePauseUri(site.Id, node), Ct);
        Assert.Equal(("site", siteEnd), await DevicePauseAsync(deviceResumed));
        lot = await LotAsync(site, lotId, _ => true, pausedBy: 1);
        Assert.Equal(("paused", "site"), (lot.GetProperty("status").GetString(), Sources(lot)));

        // Paused by itself again, then the Site resumes: still paused, by the Device alone.
        using var devicePausedAgain = await server.PutAsJsonAsync(DevicePauseUri(site.Id, node), new { }, Ct);
        Assert.Equal(("device,site", null), await DevicePauseAsync(devicePausedAgain));
        using var siteResumed = await server.DeleteAsync(SitePauseUri(site.Id), Ct);
        Assert.Equal((false, null), await SitePauseAsync(siteResumed));
        await JournaledAsync(node, DeviceResumed, 2);
        await JournaledAsync(hub, DeviceResumed, 1);
        lot = await LotAsync(site, lotId, _ => true, pausedBy: 1);
        Assert.Equal(("paused", "device"), (lot.GetProperty("status").GetString(), Sources(lot)));

        // Only lifting the second source returns the Lot's live status.
        using var last = await server.DeleteAsync(DevicePauseUri(site.Id, node), Ct);
        Assert.Equal(("", null), await DevicePauseAsync(last));
        await LotAsync(site, lotId, status => status != "paused");

        // Resuming a Site that is not paused answers the same and journals nothing.
        using var siteResumedAgain = await server.DeleteAsync(SitePauseUri(site.Id), Ct);
        Assert.Equal((false, null), await SitePauseAsync(siteResumedAgain));
        Assert.Single(await edge.AliasesAsync($"site/{site.Id}", Ct), alias => alias == "site.resumed");
    }

    [Fact]
    public async Task ADeviceEnrolledIntoAPausedSiteStartsPaused()
    {
        var site = await SeedSiteAsync();
        using var server = edge.CreateServerClient(site.Owner.AccessToken);
        using var sitePaused = await server.PutAsJsonAsync(SitePauseUri(site.Id), new { }, Ct);
        Assert.Equal((true, null), await SitePauseAsync(sitePaused));

        var (node, lotId) = await EnrolNodeAsync(site, "Strawberries");

        var journaled = Assert.IsType<DevicePaused>(Assert.Single(await edge.ReadStreamAsync($"device/{node}", Ct), row => row.Data is DevicePaused).Data);
        Assert.Equal((DevicePauseSource.Site, (DateTimeOffset?)null), (journaled.Source, journaled.EndsAt));
        var lot = await LotAsync(site, lotId, status => status == "paused");
        Assert.Equal("site", Sources(lot));

        // Its own resume does not lift the Site's Pause.
        using var resumed = await server.DeleteAsync(DevicePauseUri(site.Id, node), Ct);
        Assert.Equal(("site", null), await DevicePauseAsync(resumed));
    }

    [Theory]
    [InlineData("""{"until": "tomorrow"}""")]
    [InlineData("""{"until": "2026-11-1"}""")]
    [InlineData("""{"until": "2099-02-30"}""")]
    [InlineData("""{"until": "2099-11-01T00:00:00Z"}""")]
    [InlineData("""{"until": 20991101}""")]
    [InlineData("""{"until": true}""")]
    [InlineData("""{"until": "2020-01-01"}""")]
    [InlineData("""["2099-11-01"]""")]
    [InlineData("not json")]
    [InlineData("")]
    public async Task AnUntilThatIsNoDateOrHasBegunIs400ValidationAndChangesNothing(string body)
    {
        var site = await SeedSiteAsync();
        var node = await EnrolAsync(site, ProtocolKind.Node);
        var device = await edge.AliasesAsync($"device/{node}", Ct);
        var stream = await edge.AliasesAsync($"site/{site.Id}", Ct);
        using var server = edge.CreateServerClient(site.Administrator.AccessToken);

        foreach (var uri in new[] { DevicePauseUri(site.Id, node), SitePauseUri(site.Id) })
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await server.PutAsync(uri, content, Ct);

            await EdgeApiTests.AssertProblemAsync(response, HttpStatusCode.BadRequest, "urn:coldframe:problem:validation", Ct);
        }

        Assert.Equal(device, await edge.AliasesAsync($"device/{node}", Ct));
        Assert.Equal(stream, await edge.AliasesAsync($"site/{site.Id}", Ct));
    }

    [Fact]
    public async Task TodayHasBegunInUtcAndIsRefused()
    {
        var site = await SeedSiteAsync();
        var node = await EnrolAsync(site, ProtocolKind.Node);
        var today = DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime);
        using var server = edge.CreateServerClient(site.Owner.AccessToken);

        using var device = await server.PutAsJsonAsync(DevicePauseUri(site.Id, node), new { until = Date(today) }, Ct);
        using var whole = await server.PutAsJsonAsync(SitePauseUri(site.Id), new { until = Date(today) }, Ct);

        await EdgeApiTests.AssertProblemAsync(device, HttpStatusCode.BadRequest, "urn:coldframe:problem:validation", Ct);
        await EdgeApiTests.AssertProblemAsync(whole, HttpStatusCode.BadRequest, "urn:coldframe:problem:validation", Ct);
        Assert.DoesNotContain(DevicePaused, await edge.AliasesAsync($"device/{node}", Ct));
        Assert.DoesNotContain("site.paused", await edge.AliasesAsync($"site/{site.Id}", Ct));
    }

    [Fact]
    public async Task AMemberGets403OnAllFourAndNothingIsJournaled()
    {
        var site = await SeedSiteAsync();
        var node = await EnrolAsync(site, ProtocolKind.Node);
        var device = await edge.AliasesAsync($"device/{node}", Ct);
        var stream = await edge.AliasesAsync($"site/{site.Id}", Ct);
        using var server = edge.CreateServerClient(site.Member.AccessToken);

        using var pauseDevice = await server.PutAsJsonAsync(DevicePauseUri(site.Id, node), new { }, Ct);
        using var resumeDevice = await server.DeleteAsync(DevicePauseUri(site.Id, node), Ct);
        using var pauseSite = await server.PutAsJsonAsync(SitePauseUri(site.Id), new { }, Ct);
        using var resumeSite = await server.DeleteAsync(SitePauseUri(site.Id), Ct);

        foreach (var response in new[] { pauseDevice, resumeDevice, pauseSite, resumeSite })
        {
            await EdgeApiTests.AssertProblemAsync(response, HttpStatusCode.Forbidden, "urn:coldframe:problem:forbidden", Ct);
        }

        Assert.Equal(device, await edge.AliasesAsync($"device/{node}", Ct));
        Assert.Equal(stream, await edge.AliasesAsync($"site/{site.Id}", Ct));
    }

    [Fact]
    public async Task ADeviceOfAnotherSiteAndAnUnenrolledOneAre404DeviceNotFoundAndAMalformedIdIs400()
    {
        var site = await SeedSiteAsync();
        var other = await SeedSiteAsync();
        var theirs = await EnrolAsync(other, ProtocolKind.Node);
        var before = await edge.AliasesAsync($"device/{theirs}", Ct);
        var unenrolled = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8));
        using var server = edge.CreateServerClient(site.Administrator.AccessToken);

        foreach (var deviceId in new[] { theirs, unenrolled })
        {
            using var pause = await server.PutAsJsonAsync(DevicePauseUri(site.Id, deviceId), new { }, Ct);
            using var resume = await server.DeleteAsync(DevicePauseUri(site.Id, deviceId), Ct);

            await EdgeApiTests.AssertProblemAsync(pause, HttpStatusCode.NotFound, "urn:coldframe:problem:device-not-found", Ct);
            await EdgeApiTests.AssertProblemAsync(resume, HttpStatusCode.NotFound, "urn:coldframe:problem:device-not-found", Ct);
        }

        foreach (var deviceId in new[] { "not-a-device-id", "3F2A9C0D1E4B5A67" })
        {
            using var pause = await server.PutAsJsonAsync(DevicePauseUri(site.Id, deviceId), new { }, Ct);
            using var resume = await server.DeleteAsync(DevicePauseUri(site.Id, deviceId), Ct);

            await EdgeApiTests.AssertProblemAsync(pause, HttpStatusCode.BadRequest, "urn:coldframe:problem:validation", Ct);
            await EdgeApiTests.AssertProblemAsync(resume, HttpStatusCode.BadRequest, "urn:coldframe:problem:validation", Ct);
        }

        // The other Site's Node was not touched, and the unenrolled Device got no stream.
        Assert.Equal(before, await edge.AliasesAsync($"device/{theirs}", Ct));
        Assert.Empty(await edge.AliasesAsync($"device/{unenrolled}", Ct));
    }

    private static Uri DevicePauseUri(string siteId, string deviceId) => new($"/sites/{siteId}/devices/{deviceId}/pause", UriKind.Relative);

    private static Uri SitePauseUri(string siteId) => new($"/sites/{siteId}/pause", UriKind.Relative);

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Format(DateTimeOffset instant) => instant.UtcDateTime.ToString(ServerTimeFormat, CultureInfo.InvariantCulture);

    // 00:00 on the date in the zone, worked out apart from the Server's rule. No tested date is a day the zone's
    // clocks change at midnight.
    private static DateTimeOffset MidnightIn(string timeZone, DateOnly date)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
        var midnight = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(midnight, zone.GetUtcOffset(midnight)).ToUniversalTime();
    }

    private static List<string> Strings(JsonElement array) => [.. array.EnumerateArray().Select(item => item.GetString()!)];

    // The Lot's Pause sources in the order the Server sent them, as one string: a tuple compares it by value.
    private static string Sources(JsonElement lot) => string.Join(',', Strings(lot.GetProperty("pausedBy")));

    // The Device's Pause sources in the order the Server sent them, joined by a comma, and their end.
    private static async Task<(string PausedBy, string? PausedUntil)> DevicePauseAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await EdgeApiFixture.ReadJsonAsync(response, Ct);
        var until = body.RootElement.TryGetProperty("pausedUntil", out var value) ? value.GetString() : null;
        Assert.Equal(
            until is null ? ["pausedBy"] : ["pausedBy", "pausedUntil"],
            body.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));

        return (string.Join(',', Strings(body.RootElement.GetProperty("pausedBy"))), until);
    }

    private static async Task<(bool Paused, string? PausedUntil)> SitePauseAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await EdgeApiFixture.ReadJsonAsync(response, Ct);
        var until = body.RootElement.TryGetProperty("pausedUntil", out var value) ? value.GetString() : null;
        Assert.Equal(
            until is null ? ["paused"] : ["paused", "pausedUntil"],
            body.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));

        return (body.RootElement.GetProperty("paused").GetBoolean(), until);
    }

    // The Lot as GET lots/{lotId} shows it, read until its status passes and, when asked, it names that many
    // Pause sources: a Site's Pause reaches its Devices after the Site answered.
    private async Task<JsonElement> LotAsync(SeededSite site, string lotId, Func<string, bool> status, int? pausedBy = null)
    {
        using var server = edge.CreateServerClient(site.Member.AccessToken);
        var deadline = TimeProvider.System.GetUtcNow().AddSeconds(30);

        while (true)
        {
            using var response = await server.GetAsync(new Uri($"/sites/{site.Id}/lots/{lotId}", UriKind.Relative), Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = await EdgeApiFixture.ReadJsonAsync(response, Ct);
            var lot = body.RootElement;
            var sources = lot.TryGetProperty("pausedBy", out var named) ? named.GetArrayLength() : 0;

            if (status(lot.GetProperty("status").GetString()!) && (pausedBy is null || pausedBy == sources))
            {
                return lot.Clone();
            }

            Assert.True(TimeProvider.System.GetUtcNow() < deadline, $"Lot {lotId} still reads {lot.GetRawText()}.");
            await Task.Delay(TimeSpan.FromMilliseconds(100), Ct);
        }
    }

    // Waits until the Device's stream holds `count` events of the alias: what a Site's delivery journals there.
    private async Task JournaledAsync(string deviceId, string alias, int count)
    {
        var deadline = TimeProvider.System.GetUtcNow().AddSeconds(30);

        while (true)
        {
            var journaled = (await edge.AliasesAsync($"device/{deviceId}", Ct)).Count(item => item == alias);

            if (journaled == count)
            {
                return;
            }

            Assert.True(TimeProvider.System.GetUtcNow() < deadline, $"Device {deviceId} journaled {journaled} {alias}, expected {count}.");
            await Task.Delay(TimeSpan.FromMilliseconds(100), Ct);
        }
    }

    private async Task<(string DeviceId, string LotId)> EnrolNodeAsync(SeededSite site, string lotName)
    {
        var lotId = await edge.SeedLotAsync(site.Id, lotName, Ct);
        return (await EnrolAsync(site, ProtocolKind.Node, lotId), lotId);
    }

    private async Task<string> EnrolAsync(SeededSite site, ProtocolKind kind, string? lotId = null)
    {
        var device = SimulatedDevice.Create(kind);
        var key = await edge.GetEnrolmentPublicKeyAsync(Ct);
        var sealedEnrolment = lotId is null ? device.SealEnrolment(key, site.Id) : device.SealEnrolment(key, site.Id, lotId);
        using var server = edge.CreateServerClient(site.Administrator.AccessToken);
        using var enrolled = await EnrolmentTests.PostAsync(
            server,
            site.Id,
            Guid.NewGuid().ToString(),
            new EnrolBody(sealedEnrolment.DeviceId, sealedEnrolment.Kind, sealedEnrolment.Enc, sealedEnrolment.Ciphertext, sealedEnrolment.LotId),
            Ct);
        Assert.Equal(HttpStatusCode.Created, enrolled.StatusCode);

        return device.DeviceId.ToString();
    }

    private async Task<SeededSite> SeedSiteAsync()
    {
        var owner = await edge.CreateUserAsync("pause-owner", Ct);
        var administrator = await edge.CreateUserAsync("pause-admin", Ct);
        var member = await edge.CreateUserAsync("pause-member", Ct);
        var siteId = Guid.CreateVersion7().ToString();

        var last = await edge.AppendAsync(
            $"site/{siteId}",
            [
                new SiteCreated("Pause", owner.UserId),
                new MembershipGranted(owner.UserId, SiteRole.Owner),
                new MembershipGranted(administrator.UserId, SiteRole.Administrator),
                new MembershipGranted(member.UserId, SiteRole.Member),
            ],
            Ct);
        await edge.WaitForIdentityCheckpointAsync(last);

        return new SeededSite(siteId, owner, administrator, member);
    }

    private sealed record SeededSite(string Id, TestUser Owner, TestUser Administrator, TestUser Member);
}
