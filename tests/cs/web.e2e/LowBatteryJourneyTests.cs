using Coldframe.Protocol.Device.V1;
using Microsoft.Playwright;
using ProtocolKind = Coldframe.Protocol.Setup.V1.DeviceKind;

namespace Coldframe.Web.E2ETests;

/// <summary>
/// A battery that runs low, from the Node's reports to the browser (Story 7.3), on the real stack: the Node on
/// Lot "Lettuce" sends three device reports at 14 % while not charging, the Server opens its Health Alert of kind
/// <c>battery</c>, and the Alerts page of the web app lists it under Health Alerts as "Node battery 14 %, not
/// charging — Lot 'Lettuce'", linked to the Node's row on Devices. The Node's soil Sensor was never calibrated, so
/// its uncalibrated Alert (Story 7.4) is the other Health row.
/// </summary>
/// <remarks>
/// The clock is not moved: the three frames are measured at the Server's time as it passes, each newer than the
/// last, and the Hub heartbeats before them, so nothing is silent.
/// </remarks>
[Collection(JourneyTests.Collection)]
public sealed class LowBatteryJourneyTests(MorningNudgeFixture stack) : JourneyTests(stack)
{
    private const string SiteName = "Home garden";
    private const string Lot = "Lettuce";
    private const string Title = "Node battery 14 %, not charging — Lot 'Lettuce'";
    private const long SoilRaw = 1500;

    [Fact]
    public async Task ANodeThatReportsALowBatteryThreeTimesIsListedUnderHealthAlertsWithItsLevel()
    {
        var user = await Stack.CreateUserAsync("battery", Ct);
        var siteId = await PostAsync(user, "/sites", new { name = SiteName }, idempotent: true);
        var lotId = await PostAsync(user, $"/sites/{siteId}/lots", new { name = Lot }, idempotent: true);

        var hub = await EnrolAsync(user, siteId, ProtocolKind.Hub, lotId: null);
        var node = await EnrolAsync(user, siteId, ProtocolKind.Node, lotId);
        await HeartbeatAsync(user, hub);

        // The Node declares its Sensors while it is well.
        await WakeAsync(user, hub, node, SoilRaw, await ServerNowAsync(user));
        await WakeAsync(user, hub, node, SoilRaw, await ServerNowAsync(user));

        // Three reports at 14 %, not charging: the third opens the Alert.
        for (var report = 0; report < 3; report++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), Ct);
            await WakeAsync(user, hub, node, SoilRaw, await ServerNowAsync(user), batteryPercent: 14, ChargeStatus.NotCharging);
        }

        await HeartbeatAsync(user, hub);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args = [MorningNudgeFixture.BrowserHostRule],
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions { Locale = "en-US" });
        var page = await context.NewPageAsync();

        try
        {
            await page.GotoAsync(MorningNudgeFixture.WebOrigin);
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
            await page.Locator("#username").FillAsync(user.Username);
            await page.Locator("#password").FillAsync(user.Password);
            await page.Locator("#kc-login").ClickAsync();
            await Expect(page.GetByRole(AriaRole.List, new PageGetByRoleOptions { Name = "Lots" })).ToBeVisibleAsync();

            // The alerts projection follows the journal: the page is read again until the row is there.
            var rows = page.GetByRole(AriaRole.List, new PageGetByRoleOptions { Name = "Health Alerts" }).GetByRole(AriaRole.Link);
            await UntilAsync(
                user,
                async () =>
                {
                    await page.GotoAsync($"{MorningNudgeFixture.WebOrigin}/alerts");
                    return await rows.CountAsync();
                },
                count => count == 2,
                "the low-battery Alert is listed");

            // Two Health rows: the low battery, opened last and so listed first, and the Sensor that needs
            // calibration since the Node declared it.
            await Expect(rows).ToHaveCountAsync(2);
            await Expect(rows.Nth(1)).ToContainTextAsync($"Soil Sensor on Lot '{Lot}' needs Calibration");

            // The battery row has the level the Alert opened with, the low-battery icon, and the way to its Node.
            var battery = rows.Filter(new LocatorFilterOptions { HasText = "battery" });
            await Expect(battery).ToHaveCountAsync(1);
            await Expect(rows.First).ToContainTextAsync(Title);
            await Expect(rows.First.Locator("[data-icon=\"battery--low\"]")).ToBeVisibleAsync();
            await Expect(rows.First).ToHaveAttributeAsync("href", $"/devices#device-{node.DeviceId}");

            // The Lot keeps its status: a low battery never turns it unknown.
            await page.GotoAsync($"{MorningNudgeFixture.WebOrigin}/garden");
            var statuses = await page.Locator("ul.cf-lot-grid > li.cf-lot-grid__cell").EvaluateAllAsync<string[]>(
                "cells => cells.map(cell => cell.getAttribute('data-status') ?? '')");
            Assert.DoesNotContain("unknown", statuses);
            Assert.Single(statuses);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new Xunit.Sdk.XunitException(
                $"{exception.Message}{Environment.NewLine}Page: {page.Url}{Environment.NewLine}Web app output:{Environment.NewLine}{Stack.WebOutput()}",
                exception);
        }
    }
}
