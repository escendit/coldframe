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
[Collection(JourneyTests.Collection)]
public sealed class MorningNudgeJourneyTests(MorningNudgeFixture stack) : JourneyTests(stack)
{
    private const string Zone = "Europe/Zurich";
    private const string SiteName = "Home garden";
    private const string LotName = "Tomatoes";

    // The Calibration: 1000 raw counts read 0 %, 2000 read 100 %.
    private const long DryRaw = 1000;
    private const long WetRaw = 2000;
    private const long TwentyPercent = 1200;
    private const long FiftyPercent = 1500;

    [Fact]
    public async Task Uj2AReadingBelowTheLowAtNightBecomesOneBrowserNotificationWhenTheWindowOpensAndNoReminderAfterRecovery()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(Zone);
        var user = await Stack.CreateUserAsync("morning", Ct);

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

        // From its declaration to its Calibration the soil Sensor had its uncalibrated Alert (Story 7.4), opened and
        // closed outside the Notification Window: whether its delivery was held meanwhile is left open here. It is
        // closed, so nothing more is held or sent for it.
        var heldBefore = await JournalCountAsync(user, "user.delivery-held");

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
        var server = Stack.App.GetEndpoint("server", "http");
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

            await UntilAsync(user, () => JournalCountAsync(user, "user.delivery-held"), count => count == heldBefore + 1, "the delivery is held");
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
                $"{exception.Message}{Environment.NewLine}Page: {page.Url}{Environment.NewLine}Web app output:{Environment.NewLine}{Stack.WebOutput()}",
                exception);
        }
    }
}
