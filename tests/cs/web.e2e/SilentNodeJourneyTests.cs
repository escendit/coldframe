using System.Text.RegularExpressions;
using Coldframe.DeviceSimulator;
using Coldframe.Protocol.Device.V1;
using Microsoft.Playwright;
using ProtocolKind = Coldframe.Protocol.Setup.V1.DeviceKind;

namespace Coldframe.Web.E2ETests;

/// <summary>
/// UJ-3, a Node dies quietly, from the last Reading to the morning summary (Story 7.2): the Node on Lot "Beans"
/// reports at 00:55 in the User's zone and then stops, while the Hub keeps heartbeating and the Node on
/// "Tomatoes" keeps reporting. Six hours on, at 06:55, the Beans Node is woken: its Silent Device Alert opens
/// outside the Notification Window and is held. In the browser the Beans tile is the unknown tile with "6 h" and
/// stands before every OK tile, in the light and in the dark theme. When the window opens at 07:00 the one
/// summary of the night says "Beans: Node silent for 6 h". Time is the Server's test-only clock.
/// The soil Sensor of Beans was never calibrated, so its uncalibrated Alert (Story 7.4) is open all night too: the
/// summary has two lines to check, the older one first, and the tile is unknown all the same.
/// </summary>
/// <remarks>
/// The clock is walked through the six hours in steps shorter than a Hub's Silence Window, with a heartbeat
/// after each, so the Hub is never overdue: a Hub that went silent and recovered would restart the Node's
/// window. The Tomatoes Node reports halfway and at the end, so it is never silent either.
/// </remarks>
[Collection(JourneyTests.Collection)]
public sealed partial class SilentNodeJourneyTests(MorningNudgeFixture stack) : JourneyTests(stack)
{
    private const string Zone = "Europe/Zurich";
    private const string SiteName = "Home garden";
    private const string SilentLot = "Beans";
    private const string HealthyLot = "Tomatoes";

    // The Calibration of the Tomatoes soil Sensor: 1000 raw counts read 0 %, 2000 read 100 %.
    private const long DryRaw = 1000;
    private const long WetRaw = 2000;
    private const long FiftyPercent = 1500;

    // Shorter than a Hub's Silence Window of 5 min.
    private static readonly TimeSpan Step = TimeSpan.FromMinutes(4);

    private static readonly TimeSpan NodeWindow = TimeSpan.FromHours(6);

    [Fact]
    public async Task Uj3ANodeThatStopsReportingTurnsItsLotUnknownAndIsNamedInTheMorningSummary()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(Zone);
        var user = await Stack.CreateUserAsync("silent", Ct);

        var siteId = await PostAsync(user, "/sites", new { name = SiteName }, idempotent: true);
        using (await SendAsync(user, HttpMethod.Patch, "/me/notification-settings", new { timeZone = Zone }))
        {
        }

        // Tomatoes first: were both Lots of one status, it would stand first.
        var tomatoesLot = await PostAsync(user, $"/sites/{siteId}/lots", new { name = HealthyLot }, idempotent: true);
        var beansLot = await PostAsync(user, $"/sites/{siteId}/lots", new { name = SilentLot }, idempotent: true);

        // The night begins a quarter of an hour before the Beans Node is heard for the last time, at 00:55.
        var arrival = await ServerNowAsync(user);
        var lastHeard = NextLocal(zone, arrival, new TimeOnly(0, 55));

        if (lastHeard.AddMinutes(-15) > arrival)
        {
            await SetClockAsync(user, lastHeard.AddMinutes(-15));
        }

        var hub = await EnrolAsync(user, siteId, ProtocolKind.Hub, lotId: null);
        var tomatoes = await EnrolAsync(user, siteId, ProtocolKind.Node, tomatoesLot);
        var beans = await EnrolAsync(user, siteId, ProtocolKind.Node, beansLot);
        await HeartbeatAsync(user, hub);

        // Tomatoes declares its Sensors, is calibrated and reads 50 %: OK. Beans declares its Sensors too.
        var dry = await WakeAsync(user, hub, tomatoes, DryRaw, await ServerNowAsync(user));
        var wet = await WakeAsync(user, hub, tomatoes, WetRaw, await ServerNowAsync(user));
        using (await SendAsync(
            user,
            HttpMethod.Post,
            $"/sites/{siteId}/sensors/{tomatoes.SensorId(0, Quantity.SoilMoisture):D}/calibration",
            new { dry = new { readingSeq = dry }, wet = new { readingSeq = wet } }))
        {
        }

        await WakeAsync(user, hub, beans, FiftyPercent, await ServerNowAsync(user));
        await WakeAsync(user, hub, beans, FiftyPercent, await ServerNowAsync(user));

        // The soil Sensor of Beans needs calibration and nobody calibrates it: its Alert opens at night and is held.
        var uncalibrated = await UncalibratedAlertIdAsync(user, siteId, beansLot);
        await UntilAsync(user, () => HeldCountAsync(user, uncalibrated), count => count == 1, "the uncalibrated Sensor Alert is held");
        var heldBefore = await JournalCountAsync(user, "user.delivery-held");

        // 00:55: the last frame of the Beans Node.
        await WalkAsync(user, hub, lastHeard);
        await WakeAsync(user, hub, beans, FiftyPercent, lastHeard);
        await WakeAsync(user, hub, tomatoes, FiftyPercent, lastHeard);

        // Six hours on, 06:55: the Hub heartbeated all night and Tomatoes reported; Beans did not.
        var silentAt = lastHeard + NodeWindow;
        await WalkAsync(user, hub, lastHeard + (NodeWindow / 2));
        await WakeAsync(user, hub, tomatoes, FiftyPercent, await ServerNowAsync(user));
        await WalkAsync(user, hub, silentAt);
        await WakeAsync(user, hub, tomatoes, FiftyPercent, await ServerNowAsync(user));
        await WakeDeviceAsync(user, beans);

        // The Alert opens outside the Notification Window: held, nothing sent.
        await UntilAsync(user, () => JournalCountAsync(user, "user.delivery-held"), count => count == heldBefore + 1, "the delivery is held");
        Assert.Equal(0, await JournalCountAsync(user, "user.notification-sent"));

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args = [MorningNudgeFixture.BrowserHostRule],
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions { TimezoneId = Zone, Locale = "en-US" });
        await context.AddInitScriptAsync(RecordNotifications);
        var page = await context.NewPageAsync();

        try
        {
            // Sign in through the real Keycloak and turn the browser notifications on.
            await page.GotoAsync(MorningNudgeFixture.WebOrigin);
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
            await page.Locator("#username").FillAsync(user.Username);
            await page.Locator("#password").FillAsync(user.Password);
            await page.Locator("#kc-login").ClickAsync();
            await Expect(page.GetByRole(AriaRole.List, new PageGetByRoleOptions { Name = "Lots" })).ToBeVisibleAsync();

            await page.GotoAsync($"{MorningNudgeFixture.WebOrigin}/settings/notifications");
            var toggle = page.GetByRole(AriaRole.Switch, new PageGetByRoleOptions { Name = "Browser notifications while Coldframe is open" });
            await toggle.CheckAsync();
            await Expect(toggle).ToBeCheckedAsync();

            // The overview counts a silence on the browser's clock: it shows the Server's time, as a phone
            // beside the garden would. Timers keep running, only "now" is pinned.
            await page.Clock.SetFixedTimeAsync((await ServerNowAsync(user)).UtcDateTime);

            foreach (var theme in new[] { "light", "dark" })
            {
                await context.AddCookiesAsync([new Cookie { Name = "cf_theme", Value = theme, Url = MorningNudgeFixture.WebOrigin }]);
                await page.EmulateMediaAsync(new PageEmulateMediaOptions { ColorScheme = theme == "dark" ? ColorScheme.Dark : ColorScheme.Light });
                await page.GotoAsync($"{MorningNudgeFixture.WebOrigin}/garden");
                await Expect(page.Locator("html")).ToHaveAttributeAsync("data-theme", theme);

                // The Beans tile is the unknown tile, with how long its Node has been silent.
                var tile = page.Locator(".cf-lot-tile", new PageLocatorOptions
                {
                    Has = page.Locator(".cf-lot-tile__name", new PageLocatorOptions { HasTextRegex = ExactlyBeans() }),
                });
                await Expect(tile).ToHaveClassAsync(UnknownTile());
                await Expect(tile.Locator(".cf-lot-tile__value")).ToHaveTextAsync("6 h");

                // It stands before every OK tile, in the Server's order.
                var statuses = await page.Locator("ul.cf-lot-grid > li.cf-lot-grid__cell").EvaluateAllAsync<string[]>(
                    "cells => cells.map(cell => cell.getAttribute('data-status') ?? '')");
                Assert.Equal(["unknown", "ok"], statuses);
                await Expect(page.Locator("a.cf-lot-tile .cf-lot-tile__name")).ToHaveTextAsync([SilentLot, HealthyLot]);
            }

            // 07:00: the window opens, and the one summary of the night names the silent Node by its Lot, after
            // the Sensor that has needed calibration since the evening.
            var opening = NextLocal(zone, silentAt, new TimeOnly(7, 0));
            await WalkAsync(user, hub, opening);
            await WakeAsync(user, hub, tomatoes, FiftyPercent, await ServerNowAsync(user));
            await UntilAsync(user, () => ListAsync(page, "__cf_notifications"), shown => shown.Count >= 1, "the browser notification arrives");

            var shown = Assert.Single(await ListAsync(page, "__cf_notifications"));
            Assert.Equal($"{SiteName}: 2 to check", shown.GetProperty("title").GetString());
            Assert.Equal(
                $"{SilentLot}: soil sensor needs calibration\n{SilentLot}: Node silent for 6 h\nHeld overnight, 22:00–07:00",
                shown.GetProperty("body").GetString());
            Assert.Equal(1, await JournalCountAsync(user, "user.notification-sent"));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new Xunit.Sdk.XunitException(
                $"{exception.Message}{Environment.NewLine}Page: {page.Url}{Environment.NewLine}Web app output:{Environment.NewLine}{Stack.WebOutput()}",
                exception);
        }
    }

    [GeneratedRegex("^Beans$")]
    private static partial Regex ExactlyBeans();

    [GeneratedRegex(@"(^|\s)cf-lot-tile--unknown(\s|$)")]
    private static partial Regex UnknownTile();

    // Moves the Server's clock forward to the given time in steps shorter than a Hub's Silence Window, with a
    // heartbeat after each: the Hub is heard all the way and never overdue, whenever its reminder wakes it.
    private async Task WalkAsync(JourneyUser user, SimulatedDevice hub, DateTimeOffset to)
    {
        var now = await ServerNowAsync(user);

        while (now < to)
        {
            now = now + Step < to ? now + Step : to;
            await SetClockAsync(user, now);
            await HeartbeatAsync(user, hub);
        }
    }
}
