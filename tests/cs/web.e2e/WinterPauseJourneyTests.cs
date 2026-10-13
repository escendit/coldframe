using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Coldframe.DeviceSimulator;
using Coldframe.Protocol.Device.V1;
using Microsoft.Playwright;
using ProtocolKind = Coldframe.Protocol.Setup.V1.DeviceKind;

namespace Coldframe.Web.E2ETests;

/// <summary>
/// UJ-4, the garden is put away for the winter (Story 8.2), on the real stack: the Owner of "Home garden", with a
/// Hub and the Nodes of the Lots "Strawberries" and "Herbs" reporting and OK, opens the Site menu of the web app,
/// chooses "Pause Home garden" and saves the Pause sheet with an Until date. The overview then says "Paused until
/// &lt;day&gt;" in paused ink and "No Alerts are sent while the Site is paused.", every tile reads "Paused by Site" and
/// the menu offers Resume, in the light and in the dark theme. Nothing reports while the Server's clock passes the
/// Hub's and the Nodes' Silence Windows: no Health Alert opens. The clock then passes the date and the Site is
/// woken: the Site is no longer paused, the Devices report again, the tiles are OK, the paused line is gone and
/// the menu offers Pause, in both themes, and the resume raised no Health Alert either. Time is the Server's
/// test-only clock.
/// </summary>
/// <remarks>
/// The Until date is the first midnight of the User's zone that leaves room for a Node's Silence Window, so the
/// clock, which every journey shares, moves by a day and a half at most. Both soil Sensors are calibrated before
/// the Pause: their uncalibrated Alerts (Story 7.4) are closed by then, and those two closed Alerts are all the
/// Site ever lists.
/// </remarks>
[Collection(JourneyTests.Collection)]
public sealed partial class WinterPauseJourneyTests(MorningNudgeFixture stack) : JourneyTests(stack)
{
    private const string Zone = "Europe/Zurich";
    private const string SiteName = "Home garden";
    private const string FirstLot = "Strawberries";
    private const string SecondLot = "Herbs";

    private const string MenuName = $"Site menu for {SiteName}";
    private const string PauseItem = $"Pause {SiteName}";
    private const string ResumeItem = $"Resume {SiteName}";
    private const string PausedLine = "No Alerts are sent while the Site is paused.";

    // The Calibration of both soil Sensors: 1000 raw counts read 0 %, 2000 read 100 %.
    private const long DryRaw = 1000;
    private const long WetRaw = 2000;
    private const long FiftyPercent = 1500;

    // The colour of the headline, the paused ink and the primary text ink of the theme in force.
    private const string HeadlineInks =
        """
        headline => {
          const ink = (value) => {
            const probe = document.createElement('span');
            probe.style.color = value;
            document.body.append(probe);
            const colour = getComputedStyle(probe).color;
            probe.remove();
            return colour;
          };
          return [getComputedStyle(headline).color, ink('var(--cf-color-status-paused-ink)'), ink('var(--cf-color-text-primary)')];
        }
        """;

    private static readonly string[] Themes = ["light", "dark"];

    private static readonly TimeSpan NodeWindow = TimeSpan.FromHours(6);

    [Fact]
    public async Task Uj4ASitePausedUntilADateRaisesNoHealthAlertWhileItsDevicesAreSilentAndIsLiveAgainAfterTheDate()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(Zone);
        var user = await Stack.CreateUserAsync("winter", Ct);

        var siteId = await PostAsync(user, "/sites", new { name = SiteName }, idempotent: true);
        using (await SendAsync(user, HttpMethod.Patch, "/me/notification-settings", new { timeZone = Zone }))
        {
        }

        var firstLot = await PostAsync(user, $"/sites/{siteId}/lots", new { name = FirstLot }, idempotent: true);
        var secondLot = await PostAsync(user, $"/sites/{siteId}/lots", new { name = SecondLot }, idempotent: true);

        var hub = await EnrolAsync(user, siteId, ProtocolKind.Hub, lotId: null);
        var first = await EnrolAsync(user, siteId, ProtocolKind.Node, firstLot);
        var second = await EnrolAsync(user, siteId, ProtocolKind.Node, secondLot);
        SimulatedDevice[] nodes = [first, second];
        SimulatedDevice[] devices = [hub, first, second];
        await HeartbeatAsync(user, hub);

        // Both Nodes declare their Sensors, are calibrated and read 50 %: both Lots are OK.
        foreach (var node in nodes)
        {
            var dry = await WakeAsync(user, hub, node, DryRaw, await ServerNowAsync(user));
            var wet = await WakeAsync(user, hub, node, WetRaw, await ServerNowAsync(user));
            using (await SendAsync(
                user,
                HttpMethod.Post,
                $"/sites/{siteId}/sensors/{node.SensorId(0, Quantity.SoilMoisture):D}/calibration",
                new { dry = new { readingSeq = dry }, wet = new { readingSeq = wet } }))
            {
            }

            await WakeAsync(user, hub, node, FiftyPercent, await ServerNowAsync(user));
        }

        await UntilAsync(user, () => LotStatusesAsync(user, siteId), statuses => statuses is ["ok", "ok"], "both Lots are OK");

        // All the Site ever lists: the uncalibrated Alert of each soil Sensor, closed by its Calibration.
        IReadOnlyList<JsonElement> health = [];
        await UntilAsync(
            user,
            async () => health = await HealthAlertsAsync(user, siteId),
            alerts => alerts.Count == 2 && alerts.All(alert => alert.TryGetProperty("closedAt", out _)),
            "the two uncalibrated Sensor Alerts are listed as closed");
        var healthBefore = AlertIds(health);
        Assert.All(health, alert => Assert.Equal("uncalibrated", alert.GetProperty("kind").GetString()));
        Assert.Equal(0, await OpenCountAsync(user, siteId));
        Assert.Equal(2, await StreamCountAsync($"site/{siteId}", "site.alert-opened"));

        // The Pause ends at the first midnight of the User's zone that is more than a Node's Silence Window away.
        var end = NextLocal(zone, await ServerNowAsync(user) + NodeWindow + TimeSpan.FromHours(1), TimeOnly.MinValue);
        var endLocal = TimeZoneInfo.ConvertTime(end, zone);
        var until = endLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var pausedHeadline = $"Paused until {endLocal.ToString("MMM d", CultureInfo.InvariantCulture)}";

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args = [MorningNudgeFixture.BrowserHostRule],
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions { TimezoneId = Zone, Locale = "en-US" });
        var page = await context.NewPageAsync();

        try
        {
            // The Owner signs in through the real Keycloak. The browser shows the Server's time, as a phone
            // beside the garden would; timers keep running, only "now" is pinned.
            await page.GotoAsync(MorningNudgeFixture.WebOrigin);
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
            await page.Locator("#username").FillAsync(user.Username);
            await page.Locator("#password").FillAsync(user.Password);
            await page.Locator("#kc-login").ClickAsync();
            await Expect(page.GetByRole(AriaRole.List, new PageGetByRoleOptions { Name = "Lots" })).ToBeVisibleAsync();
            await page.Clock.SetFixedTimeAsync((await ServerNowAsync(user)).UtcDateTime);

            // Before the Pause the garden is live: two OK tiles, and the menu offers Pause.
            await ExpectLiveOverviewAsync(context, page);

            // The Owner opens the Site menu and chooses Pause: the sheet has Whole Site as its only scope and a
            // primary button that names the result. The Owner picks the date and saves.
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = MenuName }).ClickAsync();
            await page.GetByRole(AriaRole.Menu, new PageGetByRoleOptions { Name = MenuName })
                .GetByRole(AriaRole.Menuitem, new LocatorGetByRoleOptions { Name = PauseItem })
                .ClickAsync();

            var sheet = page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Pause", Exact = true });
            await Expect(sheet).ToBeVisibleAsync();
            var scope = sheet.GetByRole(AriaRole.Group, new LocatorGetByRoleOptions { Name = "Pause scope" });
            await Expect(scope.GetByRole(AriaRole.Button)).ToHaveTextAsync(["Whole Site"]);
            await Expect(scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Whole Site" })).ToHaveAttributeAsync("aria-pressed", "true");
            await Expect(sheet.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Cancel" })).ToBeVisibleAsync();

            var date = sheet.GetByLabel("Until", new LocatorGetByLabelOptions { Exact = true });
            await Expect(date).ToHaveAttributeAsync("type", "date");
            await date.FillAsync(until);
            await sheet.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = PauseItem }).ClickAsync();

            await Expect(sheet).ToBeHiddenAsync();
            await Expect(page.GetByRole(AriaRole.Status).Filter(new LocatorFilterOptions { HasText = $"{SiteName} paused." })).ToHaveCountAsync(1);

            // The Server holds the Pause with the end the date resolves to: 00:00 on it in the Owner's zone.
            var paused = await ReadAsync(user, $"/sites/{siteId}");
            Assert.True(paused.GetProperty("paused").GetBoolean());
            Assert.Equal(end, paused.GetProperty("pausedUntil").GetDateTimeOffset());
            Assert.Equal(1, await StreamCountAsync($"site/{siteId}", "site.paused"));

            // The Site delivers its Pause to the Hub and to both Nodes, and the Lots follow.
            await UntilAsync(user, () => PauseSourcesAsync(user, siteId), sources => sources is ["site", "site", "site"], "every Device is paused by the Site");
            await UntilAsync(user, () => LotStatusesAsync(user, siteId), statuses => statuses is ["paused", "paused"], "both Lots are paused");

            await ExpectPausedOverviewAsync(context, page, pausedHeadline);

            // More than six hours on nothing has reported, neither the Nodes nor the Hub, and every Device is
            // woken as its reminder would wake it: no silence is found and no Alert opens.
            var pausedAt = await ServerNowAsync(user);
            await SetClockAsync(user, pausedAt + NodeWindow + TimeSpan.FromMinutes(30));

            foreach (var device in devices)
            {
                await WakeDeviceAsync(user, device);
            }

            await ExpectNoNewAlertAsync(user, siteId, devices, healthBefore);
            Assert.Equal(["paused", "paused"], await LotStatusesAsync(user, siteId));
            Assert.Equal(["site", "site", "site"], await PauseSourcesAsync(user, siteId));

            // The overview still shows a paused garden, not Lots it cannot read.
            await page.Clock.SetFixedTimeAsync((await ServerNowAsync(user)).UtcDateTime);
            await ExpectPausedOverviewAsync(context, page, pausedHeadline);

            // The date comes and the Site wakes: its Pause ends by itself and reaches every Device.
            await SetClockAsync(user, end);
            await WakeSiteAsync(user, siteId);

            var resumed = await ReadAsync(user, $"/sites/{siteId}");
            Assert.False(resumed.GetProperty("paused").GetBoolean());
            Assert.False(resumed.TryGetProperty("pausedUntil", out _));
            Assert.Equal(1, await StreamCountAsync($"site/{siteId}", "site.resumed"));
            await UntilAsync(user, () => PauseSourcesAsync(user, siteId), sources => sources is ["", "", ""], "no Device is paused");

            foreach (var device in devices)
            {
                Assert.Equal(1, await StreamCountAsync($"device/{device.DeviceId}", "device.paused"));
                Assert.Equal(1, await StreamCountAsync($"device/{device.DeviceId}", "device.resumed"));
            }

            // Nothing has reported since before the Pause, yet no Device is silent: its Silence Window counts
            // from the resume.
            foreach (var device in devices)
            {
                await WakeDeviceAsync(user, device);
            }

            await ExpectNoNewAlertAsync(user, siteId, devices, healthBefore);

            // The Devices report again, stamped with the Server's clock, and their frames are kept.
            await HeartbeatAsync(user, hub);

            foreach (var node in nodes)
            {
                await WakeAsync(user, hub, node, FiftyPercent, await ServerNowAsync(user));
            }

            await UntilAsync(user, () => LotStatusesAsync(user, siteId), statuses => statuses is ["ok", "ok"], "both Lots are OK again");

            // The garden is live again, and the menu offers Pause.
            await page.Clock.SetFixedTimeAsync((await ServerNowAsync(user)).UtcDateTime);
            await ExpectLiveOverviewAsync(context, page);

            // The resume raised nothing either.
            foreach (var device in devices)
            {
                await WakeDeviceAsync(user, device);
            }

            await ExpectNoNewAlertAsync(user, siteId, devices, healthBefore);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new Xunit.Sdk.XunitException(
                $"{exception.Message}{Environment.NewLine}Page: {page.Url}{Environment.NewLine}Web app output:{Environment.NewLine}{Stack.WebOutput()}",
                exception);
        }
    }

    [GeneratedRegex(@"(^|\s)cf-lot-tile--paused(\s|$)")]
    private static partial Regex PausedTile();

    [GeneratedRegex(@"(^|\s)cf-lot-tile--ok(\s|$)")]
    private static partial Regex OkTile();

    [GeneratedRegex(@"(^|\s)cf-site-summary__headline--paused(\s|$)")]
    private static partial Regex PausedHeadline();

    private static string[] AlertIds(IEnumerable<JsonElement> alerts) =>
        [.. alerts.Select(alert => alert.GetProperty("id").GetString()!).Order(StringComparer.Ordinal)];

    // The overview of the paused Site, in the light and in the dark theme: the headline with the day in paused
    // ink, the line, every tile the paused variant that reads "Paused by Site", and a menu that offers Resume.
    private static async Task ExpectPausedOverviewAsync(IBrowserContext context, IPage page, string headlineText)
    {
        foreach (var theme in Themes)
        {
            await OpenOverviewAsync(context, page, theme);

            var headline = page.Locator("h2#cf-site-summary-headline");
            await Expect(headline).ToHaveTextAsync(headlineText);
            await Expect(headline).ToHaveClassAsync(PausedHeadline());
            var inks = await headline.EvaluateAsync<string[]>(HeadlineInks);
            Assert.Equal(inks[1], inks[0]);
            Assert.NotEqual(inks[2], inks[0]);
            await Expect(page.Locator("p.cf-site-summary__line")).ToHaveTextAsync(PausedLine);

            var tiles = page.Locator("a.cf-lot-tile");
            await Expect(tiles).ToHaveClassAsync([PausedTile(), PausedTile()]);
            await Expect(tiles.Locator(".cf-lot-tile__name")).ToHaveTextAsync([FirstLot, SecondLot]);
            await Expect(tiles.Locator(".cf-lot-tile__label")).ToHaveTextAsync(["Paused by Site", "Paused by Site"]);
            Assert.Equal(["paused", "paused"], await TileStatusesAsync(page));

            await ExpectSiteMenuAsync(page, ResumeItem);
        }
    }

    // The overview of the live Site, in the light and in the dark theme: two OK tiles, a headline that is not the
    // paused one, no paused line, and a menu that offers Pause.
    private static async Task ExpectLiveOverviewAsync(IBrowserContext context, IPage page)
    {
        foreach (var theme in Themes)
        {
            await OpenOverviewAsync(context, page, theme);

            var tiles = page.Locator("a.cf-lot-tile");
            await Expect(tiles).ToHaveClassAsync([OkTile(), OkTile()]);
            await Expect(tiles.Locator(".cf-lot-tile__name")).ToHaveTextAsync([FirstLot, SecondLot]);
            await Expect(tiles.Locator(".cf-lot-tile__label")).ToHaveTextAsync(["OK", "OK"]);
            Assert.Equal(["ok", "ok"], await TileStatusesAsync(page));

            var headline = page.Locator("h2#cf-site-summary-headline");
            await Expect(headline).ToHaveTextAsync("Nothing needs water");
            await Expect(headline).Not.ToHaveClassAsync(PausedHeadline());
            var inks = await headline.EvaluateAsync<string[]>(HeadlineInks);
            Assert.Equal(inks[2], inks[0]);
            Assert.NotEqual(inks[1], inks[0]);
            await Expect(page.Locator("p.cf-site-summary__line")).ToHaveCountAsync(0);

            await ExpectSiteMenuAsync(page, PauseItem);
        }
    }

    private static async Task OpenOverviewAsync(IBrowserContext context, IPage page, string theme)
    {
        await context.AddCookiesAsync([new Cookie { Name = "cf_theme", Value = theme, Url = MorningNudgeFixture.WebOrigin }]);
        await page.EmulateMediaAsync(new PageEmulateMediaOptions { ColorScheme = theme == "dark" ? ColorScheme.Dark : ColorScheme.Light });
        await page.GotoAsync($"{MorningNudgeFixture.WebOrigin}/garden");
        await Expect(page.Locator("html")).ToHaveAttributeAsync("data-theme", theme);
    }

    // Opens the Site menu, reads its items and closes it: Pause or Resume, never both, then Site settings.
    private static async Task ExpectSiteMenuAsync(IPage page, string offered)
    {
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = MenuName }).ClickAsync();
        var menu = page.GetByRole(AriaRole.Menu, new PageGetByRoleOptions { Name = MenuName });
        await Expect(menu.GetByRole(AriaRole.Menuitem)).ToHaveTextAsync([offered, "Site settings"]);
        await page.Keyboard.PressAsync("Escape");
        await Expect(menu).ToBeHiddenAsync();
    }

    // The statuses of the Lot tiles of the overview, in the Server's order.
    private static Task<string[]> TileStatusesAsync(IPage page) =>
        page.Locator("ul.cf-lot-grid > li.cf-lot-grid__cell").EvaluateAllAsync<string[]>(
            "cells => cells.map(cell => cell.getAttribute('data-status') ?? '')");

    // No Health Alert exists that was not there before the Pause, none is open, and none was opened and closed
    // meanwhile: the Site's journal lists no other Alert and no Device journaled a silence.
    private async Task ExpectNoNewAlertAsync(JourneyUser user, string siteId, IEnumerable<SimulatedDevice> devices, string[] healthBefore)
    {
        foreach (var device in devices)
        {
            Assert.Equal(0, await StreamCountAsync($"device/{device.DeviceId}", "device.silence-started"));
        }

        Assert.Equal(healthBefore.Length, await StreamCountAsync($"site/{siteId}", "site.alert-opened"));

        var health = await HealthAlertsAsync(user, siteId);
        Assert.Equal(healthBefore, AlertIds(health));
        Assert.All(health, alert => Assert.True(alert.TryGetProperty("closedAt", out _)));
        Assert.Equal(0, await OpenCountAsync(user, siteId));
    }

    private async Task<int> OpenCountAsync(JourneyUser user, string siteId) =>
        (await ReadAsync(user, $"/sites/{siteId}/alerts")).GetProperty("openCount").GetInt32();

    // The statuses of the Site's Lots as the Edge API lists them, in the Server's order.
    private async Task<string[]> LotStatusesAsync(JourneyUser user, string siteId) =>
        [.. (await ReadAsync(user, $"/sites/{siteId}/lots")).GetProperty("lots").EnumerateArray().Select(lot => lot.GetProperty("status").GetString()!)];

    // The Pause sources of each Device of the Site as the Devices list says them, "" for one that is not paused.
    private async Task<string[]> PauseSourcesAsync(JourneyUser user, string siteId) =>
        [.. (await ReadAsync(user, $"/sites/{siteId}/devices")).GetProperty("devices").EnumerateArray().Select(device =>
            device.TryGetProperty("pausedBy", out var sources) ? string.Join(',', sources.EnumerateArray().Select(source => source.GetString())) : string.Empty)];

    private async Task<JsonElement> ReadAsync(JourneyUser user, string path)
    {
        using var server = await Stack.CreateServerClientAsync(user, Ct);
        using var response = await server.GetAsync(new Uri(path, UriKind.Relative), Ct);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));

        return body.RootElement.Clone();
    }

    // How many events of one kind a stream of the Server's journal holds.
    private async Task<int> StreamCountAsync(string streamId, string alias)
    {
        await using var command = Stack.Database.CreateCommand(
            "SELECT COUNT(*) FROM journal_events WHERE stream_id = @stream_id AND type_alias = @alias");
        command.Parameters.AddWithValue("stream_id", streamId);
        command.Parameters.AddWithValue("alias", alias);

        return (int)(long)(await command.ExecuteScalarAsync(Ct))!;
    }
}
