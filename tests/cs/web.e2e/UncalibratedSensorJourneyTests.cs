using Coldframe.DeviceSimulator;
using Coldframe.Protocol.Device.V1;
using Microsoft.Playwright;
using ProtocolKind = Coldframe.Protocol.Setup.V1.DeviceKind;

namespace Coldframe.Web.E2ETests;

/// <summary>
/// A soil Sensor that was never calibrated, from the Node's first report to the browser (Story 7.4), on the real
/// stack: the Node on Lot "Peppers" reports and declares its Sensors, the Server opens the Health Alert of kind
/// <c>uncalibrated</c>, and the Alerts page of the web app lists it under Health Alerts with the tools icon. The
/// Owner follows the row to Lot detail, where Calibrate is offered. Once the Calibration is saved the Lot leaves
/// <em>needs calibration</em> and the Alert is listed as closed.
/// </summary>
/// <remarks>
/// The clock is not moved: the frames are measured at the Server's time as it passes, and the Hub heartbeats
/// before them, so nothing is silent.
/// </remarks>
[Collection(JourneyTests.Collection)]
public sealed class UncalibratedSensorJourneyTests(MorningNudgeFixture stack) : JourneyTests(stack)
{
    private const string SiteName = "Home garden";
    private const string Lot = "Peppers";
    private const string Title = "Soil Sensor on Lot 'Peppers' needs Calibration";

    // The two points the Owner will save: 1000 raw counts read 0 %, 2000 read 100 %.
    private const long DryRaw = 1000;
    private const long WetRaw = 2000;

    [Fact]
    public async Task ANodeOnALotWhoseSoilSensorWasNeverCalibratedIsListedUnderHealthAlertsUntilItIsCalibrated()
    {
        var user = await Stack.CreateUserAsync("uncalibrated", Ct);
        var siteId = await PostAsync(user, "/sites", new { name = SiteName }, idempotent: true);
        var lotId = await PostAsync(user, $"/sites/{siteId}/lots", new { name = Lot }, idempotent: true);

        var hub = await EnrolAsync(user, siteId, ProtocolKind.Hub, lotId: null);
        var node = await EnrolAsync(user, siteId, ProtocolKind.Node, lotId);
        await HeartbeatAsync(user, hub);

        // The Node reports: its second frame declares its Sensors, and the soil Sensor has no Calibration.
        var dry = await WakeAsync(user, hub, node, DryRaw, await ServerNowAsync(user));
        await Task.Delay(TimeSpan.FromMilliseconds(20), Ct);
        var wet = await WakeAsync(user, hub, node, WetRaw, await ServerNowAsync(user));
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
            // The Owner signs in through the real Keycloak.
            await page.GotoAsync(MorningNudgeFixture.WebOrigin);
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
            await page.Locator("#username").FillAsync(user.Username);
            await page.Locator("#password").FillAsync(user.Password);
            await page.Locator("#kc-login").ClickAsync();
            await Expect(page.GetByRole(AriaRole.List, new PageGetByRoleOptions { Name = "Lots" })).ToBeVisibleAsync();

            // The alerts projection follows the journal: the page is read again until the row is there.
            var health = page.GetByRole(AriaRole.List, new PageGetByRoleOptions { Name = "Health Alerts" }).GetByRole(AriaRole.Link);
            await UntilAsync(
                user,
                async () =>
                {
                    await page.GotoAsync($"{MorningNudgeFixture.WebOrigin}/alerts");
                    return await health.CountAsync();
                },
                count => count == 1,
                "the uncalibrated Sensor Alert is listed");

            // One Health row: it names the Lot, has the tools icon, and leads to Lot detail.
            await Expect(health).ToHaveCountAsync(1);
            await Expect(health.First).ToContainTextAsync(Title);
            await Expect(health.First.Locator("[data-icon=\"tools\"]")).ToBeVisibleAsync();
            await Expect(health.First).ToHaveAttributeAsync("href", $"/garden/{lotId}");

            // The Lot needs calibration, which the Server computes from the Sensor, not from the Alert.
            await page.GotoAsync($"{MorningNudgeFixture.WebOrigin}/garden");
            Assert.Equal(["needsCalibration"], await StatusesAsync(page));

            // The Owner follows the row: Lot detail of Peppers offers Calibrate.
            await page.GotoAsync($"{MorningNudgeFixture.WebOrigin}/alerts");
            await health.First.ClickAsync();
            await page.WaitForURLAsync($"**/garden/{lotId}");
            await Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = Lot, Level = 1 })).ToBeVisibleAsync();
            var calibrate = page.Locator("#cf-detail-calibrate");
            await Expect(calibrate).ToBeVisibleAsync();
            await Expect(calibrate).ToHaveTextAsync("Calibrate");
            await Expect(calibrate).ToHaveAttributeAsync("href", $"/garden/{lotId}/calibrate");

            // The Calibration is saved from the two Readings the Server stored.
            using (await SendAsync(
                user,
                HttpMethod.Post,
                $"/sites/{siteId}/sensors/{node.SensorId(0, Quantity.SoilMoisture):D}/calibration",
                new { dry = new { readingSeq = dry }, wet = new { readingSeq = wet } }))
            {
            }

            // The Alert is listed as closed, and no Health Alert is open.
            var closed = page.GetByRole(AriaRole.List, new PageGetByRoleOptions { Name = "Closed" }).GetByRole(AriaRole.Link);
            await UntilAsync(
                user,
                async () =>
                {
                    await page.GotoAsync($"{MorningNudgeFixture.WebOrigin}/alerts");
                    return await closed.CountAsync();
                },
                count => count == 1,
                "the uncalibrated Sensor Alert is listed as closed");

            await Expect(health).ToHaveCountAsync(0);
            await Expect(closed.First).ToContainTextAsync(Title);
            await Expect(closed.First).ToContainTextAsync("closed");
            await Expect(closed.First.Locator("[data-icon=\"tools\"]")).ToBeVisibleAsync();

            // The Lot left needs calibration.
            await page.GotoAsync($"{MorningNudgeFixture.WebOrigin}/garden");
            Assert.DoesNotContain("needsCalibration", await StatusesAsync(page));
            Assert.Single(await StatusesAsync(page));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new Xunit.Sdk.XunitException(
                $"{exception.Message}{Environment.NewLine}Page: {page.Url}{Environment.NewLine}Web app output:{Environment.NewLine}{Stack.WebOutput()}",
                exception);
        }
    }

    // The statuses of the Lot tiles of the overview, in the Server's order.
    private static Task<string[]> StatusesAsync(IPage page) =>
        page.Locator("ul.cf-lot-grid > li.cf-lot-grid__cell").EvaluateAllAsync<string[]>(
            "cells => cells.map(cell => cell.getAttribute('data-status') ?? '')");
}
