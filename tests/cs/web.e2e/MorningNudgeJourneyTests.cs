using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting.Testing;
using Coldframe.DeviceSimulator;
using Coldframe.Protocol.Device.V1;
using Microsoft.Playwright;
using ProtocolKind = Coldframe.Protocol.Setup.V1.DeviceKind;

[assembly: AssemblyFixture(typeof(Coldframe.Web.E2ETests.MorningNudgeFixture))]

namespace Coldframe.Web.E2ETests;

/// <summary>
/// UJ-2, the morning nudge, from a Reading to a browser notification (Story 6.6): a Keycloak User signs in
/// to the built web app, turns on "Browser notifications while Coldframe is open", and a simulated Node's
/// Readings fall below the low Threshold at 02:40 in the User's zone. The Alert is held overnight; when the
/// Notification Window opens at 07:00 exactly one browser notification arrives with the Server's words, and
/// the open overview shows <em>needs water</em> without a reload. Readings that recover close the Alert, and
/// a day later no Reminder arrives. Time is the Server's test-only clock; nothing sleeps through a night.
/// The clock jumps by hours, which the Server would read as a silent Hub and a silent Node (Story 7.1), so the
/// Hub heartbeats right after every jump, as a real Hub does every minute, and the Node reports after the last.
/// </summary>
public sealed class MorningNudgeJourneyTests(MorningNudgeFixture stack)
{
    private const string Zone = "Europe/Zurich";
    private const string SiteName = "Home garden";
    private const string LotName = "Tomatoes";

    // The Calibration: 1000 raw counts read 0 %, 2000 read 100 %.
    private const long DryRaw = 1000;
    private const long WetRaw = 2000;
    private const long TwentyPercent = 1200;
    private const long FiftyPercent = 1500;

    // window.Notification replaced by a recorder. The permission starts as "default", is granted when the page
    // asks, and is kept across page loads like a browser keeps it; every request and every notification is listed.
    private const string RecordNotifications =
        """
        (() => {
          const read = (key) => JSON.parse(window.localStorage.getItem(key) ?? '[]');
          const add = (key, value) => window.localStorage.setItem(key, JSON.stringify([...read(key), value]));
          class RecordingNotification extends EventTarget {
            static get permission() { return window.localStorage.getItem('__cf_permission') ?? 'default'; }
            static requestPermission() {
              add('__cf_permission_requests', window.location.pathname);
              window.localStorage.setItem('__cf_permission', 'granted');
              return Promise.resolve('granted');
            }
            constructor(title, options = {}) {
              super();
              this.onclick = null;
              add('__cf_notifications', { title, body: options.body ?? '', tag: options.tag ?? '' });
            }
            close() {}
          }
          Object.defineProperty(window, 'Notification', { configurable: true, writable: true, value: RecordingNotification });
        })();
        """;

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Uj2AReadingBelowTheLowAtNightBecomesOneBrowserNotificationWhenTheWindowOpensAndNoReminderAfterRecovery()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(Zone);
        var user = await stack.CreateUserAsync("morning", Ct);

        // The User's Site, zone and Lot, through the Edge API as the apps would.
        var siteId = await PostAsync(user, "/sites", new { name = SiteName }, idempotent: true);
        using (await SendAsync(user, HttpMethod.Patch, "/me/notification-settings", new { timeZone = Zone }))
        {
        }

        var lotId = await PostAsync(user, $"/sites/{siteId}/lots", new { name = LotName }, idempotent: true);

        // The night begins: 02:25 in the User's zone, on the next day that has one.
        var crossing = NextLocal(zone, await ServerNowAsync(user), new TimeOnly(2, 40));
        await SetClockAsync(user, crossing.AddMinutes(-15));

        var hub = await EnrolAsync(user, siteId, ProtocolKind.Hub, lotId: null);
        var node = await EnrolAsync(user, siteId, ProtocolKind.Node, lotId);

        // The Node reports, declares its Sensors, and its soil Sensor is calibrated from two stored Readings.
        var dry = await WakeAsync(user, hub, node, DryRaw, await ServerNowAsync(user));
        var wet = await WakeAsync(user, hub, node, WetRaw, await ServerNowAsync(user));
        var soil = node.SensorId(0, Quantity.SoilMoisture);
        using (await SendAsync(
            user,
            HttpMethod.Post,
            $"/sites/{siteId}/sensors/{soil:D}/calibration",
            new { dry = new { readingSeq = dry }, wet = new { readingSeq = wet } }))
        {
        }

        await WakeAsync(user, hub, node, FiftyPercent, await ServerNowAsync(user));

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args = [MorningNudgeFixture.BrowserHostRule],
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions { TimezoneId = Zone, Locale = "en-US" });
        await context.AddInitScriptAsync(RecordNotifications);
        var page = await context.NewPageAsync();
        var serverRequests = new List<string>();
        var server = stack.App.GetEndpoint("server", "http");
        page.Request += (_, request) =>
        {
            if (new Uri(request.Url).Port == server.Port)
            {
                serverRequests.Add(request.Url);
            }
        };

        try
        {
            // Sign in through the real Keycloak.
            await page.GotoAsync(MorningNudgeFixture.WebOrigin);
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
            await page.Locator("#username").FillAsync(user.Username);
            await page.Locator("#password").FillAsync(user.Password);
            await page.Locator("#kc-login").ClickAsync();
            await Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Nothing needs water" })).ToBeVisibleAsync();

            // Nothing asked for permission yet. The toggle asks, once, and stays on.
            Assert.Empty(await ListAsync(page, "__cf_permission_requests"));
            await page.GotoAsync($"{MorningNudgeFixture.WebOrigin}/settings/notifications");
            var toggle = page.GetByRole(AriaRole.Switch, new PageGetByRoleOptions { Name = "Browser notifications while Coldframe is open" });
            await Expect(toggle).Not.ToBeCheckedAsync();
            await toggle.CheckAsync();
            await Expect(toggle).ToBeCheckedAsync();
            Assert.Single(await ListAsync(page, "__cf_permission_requests"));

            // The overview stays open from here on: a mark on the document shows that it is never loaded again.
            await page.GotoAsync($"{MorningNudgeFixture.WebOrigin}/garden");
            await Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Nothing needs water" })).ToBeVisibleAsync();
            await page.EvaluateAsync("() => { window.__cfOpenedOnce = true; }");

            // 02:40: three Readings in a row below the low of 30 %. The Alert opens, outside the window.
            await SetClockAsync(user, crossing);
            await HeartbeatAsync(user, hub);
            await WakeAsync(user, hub, node, TwentyPercent, crossing.AddMinutes(-2));
            await WakeAsync(user, hub, node, TwentyPercent, crossing.AddMinutes(-1));
            await WakeAsync(user, hub, node, TwentyPercent, crossing);

            await UntilAsync(user, () => JournalCountAsync(user, "user.delivery-held"), count => count == 1, "the delivery is held");
            Assert.Equal(0, await JournalCountAsync(user, "user.notification-sent"));
            Assert.Empty(await ListAsync(page, "__cf_notifications"));

            // 07:00: the window opens, and the one notification of the night arrives with the Server's words.
            var opening = NextLocal(zone, crossing, new TimeOnly(7, 0));
            await SetClockAsync(user, opening);
            await HeartbeatAsync(user, hub);
            await UntilAsync(user, () => ListAsync(page, "__cf_notifications"), shown => shown.Count >= 1, "the browser notification arrives");

            var shown = Assert.Single(await ListAsync(page, "__cf_notifications"));
            Assert.Equal($"{SiteName}: 1 needs water", shown.GetProperty("title").GetString());
            Assert.Equal($"{LotName} needs water\nHeld overnight, 22:00–07:00", shown.GetProperty("body").GetString());
            Assert.Matches("^[0-9a-f]{32}$", shown.GetProperty("tag").GetString());

            // The open overview shows needs water, live.
            await Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = $"{LotName} needs water" })).ToBeVisibleAsync();
            Assert.True(await page.EvaluateAsync<bool>("() => window.__cfOpenedOnce === true"), "The overview was loaded again.");

            // The User waters: three Readings back within the Thresholds close the Alert.
            foreach (var _ in Enumerable.Range(0, 3))
            {
                await WakeAsync(user, hub, node, FiftyPercent, await ServerNowAsync(user));
            }

            await Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Nothing needs water" })).ToBeVisibleAsync();
            Assert.True(await page.EvaluateAsync<bool>("() => window.__cfOpenedOnce === true"), "The overview was loaded again.");

            // A day later, inside the window: a closed Alert sends no Reminder and closing sent nothing.
            await SetClockAsync(user, opening.AddHours(25));
            await HeartbeatAsync(user, hub);
            await WakeAsync(user, hub, node, FiftyPercent, await ServerNowAsync(user));
            await WakeUserAsync(user);
            await Task.Delay(TimeSpan.FromSeconds(8), Ct);
            await WakeUserAsync(user);
            await Task.Delay(TimeSpan.FromSeconds(2), Ct);

            Assert.Single(await ListAsync(page, "__cf_notifications"));
            Assert.Equal(1, await JournalCountAsync(user, "user.notification-sent"));
            Assert.Single(await ListAsync(page, "__cf_permission_requests"));

            // AD-14: the browser spoke to the web origin only, the hub included.
            Assert.Empty(serverRequests);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new Xunit.Sdk.XunitException(
                $"{exception.Message}{Environment.NewLine}Page: {page.Url}{Environment.NewLine}Web app output:{Environment.NewLine}{stack.WebOutput()}",
                exception);
        }
    }

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);

    // The next time the wall clock of the zone shows the given time, strictly after the given instant.
    private static DateTimeOffset NextLocal(TimeZoneInfo zone, DateTimeOffset after, TimeOnly time)
    {
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(after, zone).DateTime);

        while (true)
        {
            var local = day.ToDateTime(time, DateTimeKind.Unspecified);

            if (!zone.IsInvalidTime(local))
            {
                var instant = new DateTimeOffset(local, zone.GetUtcOffset(local));

                if (instant > after)
                {
                    return instant;
                }
            }

            day = day.AddDays(1);
        }
    }

    private static async Task<IReadOnlyList<JsonElement>> ListAsync(IPage page, string key)
    {
        var json = await page.EvaluateAsync<string>("key => window.localStorage.getItem(key) ?? '[]'", key);
        using var document = JsonDocument.Parse(json);

        return [.. document.RootElement.EnumerateArray().Select(element => element.Clone())];
    }

    // Waits for what the Server does on its own timers, and wakes the User's grain meanwhile as its reminder would.
    private async Task UntilAsync<T>(JourneyUser user, Func<Task<T>> read, Func<T, bool> done, string what)
    {
        var deadline = TimeProvider.System.GetUtcNow() + Patience;

        while (true)
        {
            await WakeUserAsync(user);

            if (done(await read()))
            {
                return;
            }

            Assert.True(TimeProvider.System.GetUtcNow() < deadline, $"Waited {Patience.TotalSeconds} s and it did not happen: {what}.");
            await Task.Delay(TimeSpan.FromMilliseconds(500), Ct);
        }
    }

    private async Task<int> JournalCountAsync(JourneyUser user, string alias)
    {
        await using var command = stack.Database.CreateCommand(
            "SELECT COUNT(*) FROM journal_events WHERE stream_id = @stream_id AND type_alias = @alias");
        command.Parameters.AddWithValue("stream_id", $"user/{user.UserId}");
        command.Parameters.AddWithValue("alias", alias);

        return (int)(long)(await command.ExecuteScalarAsync(Ct))!;
    }

    private async Task<DateTimeOffset> ServerNowAsync(JourneyUser user)
    {
        using var server = await stack.CreateServerClientAsync(user, Ct);
        using var response = await server.GetAsync(new Uri("/testing/clock", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));

        // To the millisecond, as a frame carries it.
        return DateTimeOffset.FromUnixTimeMilliseconds(body.RootElement.GetProperty("now").GetDateTimeOffset().ToUnixTimeMilliseconds());
    }

    private async Task SetClockAsync(JourneyUser user, DateTimeOffset now)
    {
        using var response = await SendAsync(user, HttpMethod.Post, "/testing/clock", new { now });
    }

    private async Task WakeUserAsync(JourneyUser user)
    {
        using var server = await stack.CreateServerClientAsync(user, Ct);
        using var response = await server.PostAsync(new Uri($"/testing/users/{user.UserId}/wake", UriKind.Relative), content: null, Ct);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task<string> PostAsync(JourneyUser user, string path, object body, bool idempotent)
    {
        using var response = await SendAsync(user, HttpMethod.Post, path, body, idempotent);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));

        return json.RootElement.GetProperty("id").GetString()!;
    }

    private async Task<HttpResponseMessage> SendAsync(JourneyUser user, HttpMethod method, string path, object body, bool idempotent = false)
    {
        using var server = await stack.CreateServerClientAsync(user, Ct);
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = JsonContent.Create(body) };

        if (idempotent)
        {
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        }

        var response = await server.SendAsync(request, Ct);
        Assert.True(response.IsSuccessStatusCode, $"{method} {path} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");

        return response;
    }

    private async Task<SimulatedDevice> EnrolAsync(JourneyUser user, string siteId, ProtocolKind kind, string? lotId)
    {
        using var server = await stack.CreateServerClientAsync(user, Ct);
        using var key = await server.GetAsync(new Uri("/enrolment-key", UriKind.Relative), Ct);
        using var keyBody = JsonDocument.Parse(await key.Content.ReadAsStringAsync(Ct));
        var publicKey = Base64Url.DecodeFromChars(keyBody.RootElement.GetProperty("publicKey").GetString());

        var device = SimulatedDevice.Create(kind);
        var enrolment = device.SealEnrolment(publicKey, siteId);
        var body = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["deviceId"] = enrolment.DeviceId,
            ["kind"] = enrolment.Kind,
            ["enc"] = enrolment.Enc,
            ["ciphertext"] = enrolment.Ciphertext,
        };

        if (lotId is not null)
        {
            body["lotId"] = lotId;
        }

        using var enrolled = await SendAsync(user, HttpMethod.Post, $"/sites/{siteId}/devices", body, idempotent: true);

        return device;
    }

    // One heartbeat of the Hub, stamped from the Server's clock: the Hub is alive, whatever the clock jumped over.
    private async Task HeartbeatAsync(JourneyUser user, SimulatedDevice hub)
    {
        using var server = stack.CreateDeviceClient();
        using var request = hub.HeartbeatRequest(await ServerNowAsync(user));
        using var response = await server.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // One wake of the Node, relayed by the Hub and stamped from the Server's clock. The Node follows the
    // downlink, so it attaches its Specification set when the Server asks. Returns the soil Reading's reading_seq.
    private async Task<ulong> WakeAsync(JourneyUser user, SimulatedDevice hub, SimulatedDevice node, long soilRaw, DateTimeOffset measuredAt)
    {
        var readings = SimulatedDevice.DefaultReadings
            .Select(reading => reading.Quantity == Quantity.SoilMoisture ? reading with { Value = soilRaw } : reading)
            .ToList();
        var frame = node.Wake(measuredAt, readings: readings);

        using var server = stack.CreateDeviceClient();
        using var request = hub.IngestRequest(await ServerNowAsync(user), SimulatedDevice.IngestBody(node.SealFrame(frame)));
        using var response = await server.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = Assert.Single(SimulatedDevice.ReadIngestResponse(await response.Content.ReadAsByteArrayAsync(Ct)));
        Assert.Equal("stored", result.Status);
        node.OpenDownlink(result.Downlink!);

        return frame.Readings.Single(reading => reading.Quantity == Quantity.SoilMoisture).ReadingSeq;
    }
}
