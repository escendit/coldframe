using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Coldframe.Protocol.Device.V1;
using Microsoft.Playwright;
using ProtocolKind = Coldframe.Protocol.Setup.V1.DeviceKind;

namespace Coldframe.Web.E2ETests;

/// <summary>
/// UJ-5, a second pair of eyes (Stories 9.1 and 9.2): the Owner invites a neighbour as Member from the web app's
/// Members page, the neighbour accepts on the Phase Two page, signs in to the web app, turns on browser
/// notifications and sets their own Notification Window. A dry Reading for Cucumbers then reaches the neighbour's
/// browser as "Cucumbers needs water" inside their window, and Garden, Cucumbers' Lot detail and Devices show the
/// neighbour none of the admin controls (hidden, not disabled). Time is the Server's test-only clock.
/// </summary>
[Collection(JourneyTests.Collection)]
public sealed partial class NeighbourJourneyTests(MorningNudgeFixture stack) : JourneyTests(stack)
{
    private const string Zone = "Europe/Zurich";
    private const string SiteName = "Home garden";
    private const string LotName = "Cucumbers";

    // The Calibration: 1000 raw counts read 0 %, 2000 read 100 %.
    private const long DryRaw = 1000;
    private const long WetRaw = 2000;
    private const long TwentyPercent = 1200;
    private const long FiftyPercent = 1500;

    // Membership events arrive asynchronously (AD-3): Keycloak → Temporal → the Server's worker.
    private static readonly TimeSpan ArrivalTimeout = TimeSpan.FromSeconds(90);

    [Fact]
    public async Task Uj5AnInvitedNeighbourGetsCucumbersNeedsWaterInTheirOwnWindowAndSeesNoAdminControls()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(Zone);
        var owner = await Stack.CreateUserAsync("simon", Ct);
        var neighbour = await Stack.CreateUserAsync("neighbour", Ct);
        var neighbourEmail = $"{neighbour.Username}@example.org";

        // Simon's garden, through the Edge API as the apps would: a calibrated Node on Cucumbers.
        var siteId = await PostAsync(owner, "/sites", new { name = SiteName }, idempotent: true);
        using (await SendAsync(owner, HttpMethod.Patch, "/me/notification-settings", new { timeZone = Zone }))
        {
        }

        var lotId = await PostAsync(owner, $"/sites/{siteId}/lots", new { name = LotName }, idempotent: true);

        // 09:45 on the next day that has one, in the garden's zone: the dry spell starts at 10:00, inside the window.
        var crossing = NextLocal(zone, await ServerNowAsync(owner), new TimeOnly(10, 0));
        await SetClockAsync(owner, crossing.AddMinutes(-15));

        var hub = await EnrolAsync(owner, siteId, ProtocolKind.Hub, lotId: null);
        var node = await EnrolAsync(owner, siteId, ProtocolKind.Node, lotId);
        var dry = await WakeAsync(owner, hub, node, DryRaw, await ServerNowAsync(owner));
        var wet = await WakeAsync(owner, hub, node, WetRaw, await ServerNowAsync(owner));
        var soil = node.SensorId(0, Quantity.SoilMoisture);
        using (await SendAsync(
            owner,
            HttpMethod.Post,
            $"/sites/{siteId}/sensors/{soil:D}/calibration",
            new { dry = new { readingSeq = dry }, wet = new { readingSeq = wet } }))
        {
        }

        await WakeAsync(owner, hub, node, FiftyPercent, await ServerNowAsync(owner));

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args = [MorningNudgeFixture.BrowserHostRule],
        });

        // Two people, two browsers: nothing of Simon's session reaches the neighbour's.
        await using var ownerContext = await browser.NewContextAsync(new BrowserNewContextOptions { TimezoneId = Zone, Locale = "en-US" });
        await using var neighbourContext = await browser.NewContextAsync(new BrowserNewContextOptions { TimezoneId = Zone, Locale = "en-US" });
        await neighbourContext.AddInitScriptAsync(RecordNotifications);
        var page = await ownerContext.NewPageAsync();

        try
        {
            // 1. Settings → Members: Simon invites the neighbour as Member; the Members list is unchanged.
            await SignInAsync(page, owner);
            await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Members", Exact = true }).ClickAsync();
            await page.GetByLabel("Email", new PageGetByLabelOptions { Exact = true }).FillAsync(neighbourEmail);
            await Expect(page.GetByText("Member sees everything and gets the same Alerts, but can't change settings.")).ToBeVisibleAsync();
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Send invite" }).ClickAsync();
            await Expect(page.GetByText($"Invitation sent to {neighbourEmail}. They accept from the email, on the garden's Wi-Fi.")).ToBeVisibleAsync();

            // 2. The neighbour accepts on the Phase Two page; the Membership reaches the Server.
            await Stack.AcceptInvitationAsync(neighbour, siteId, Ct);
            await WaitForMembershipAsync(neighbour, siteId);

            // Simon's Members list now shows the neighbour as Member, a tile he can change.
            await page.ReloadAsync();
            await Expect(page.GetByText("neighbour Test", new PageGetByTextOptions { Exact = true })).ToBeVisibleAsync();
            await ownerContext.CloseAsync();

            // 3. The neighbour signs in and lands on Home garden's overview.
            page = await neighbourContext.NewPageAsync();
            await SignInAsync(page, neighbour);
            await Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Nothing needs water" })).ToBeVisibleAsync();

            // 4. Their own zone and Notification Window, and browser notifications while Coldframe is open.
            using (await SendAsync(neighbour, HttpMethod.Patch, "/me/notification-settings", new { timeZone = Zone }))
            {
            }

            await page.GotoAsync($"{MorningNudgeFixture.WebOrigin}/settings/notifications");
            await page.GetByLabel("From", new PageGetByLabelOptions { Exact = true }).FillAsync("08:00");
            await page.GetByLabel("To", new PageGetByLabelOptions { Exact = true }).FillAsync("21:00");
            await page.Locator("form[action='?/saveWindow']").GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save", Exact = true }).ClickAsync();
            await UntilAsync(neighbour, () => WindowAsync(neighbour), window => window == "08:00–21:00", "the neighbour's window is saved");

            var toggle = page.GetByRole(AriaRole.Switch, new PageGetByRoleOptions { Name = "Browser notifications while Coldframe is open" });
            await toggle.CheckAsync();
            await Expect(toggle).ToBeCheckedAsync();

            await page.GotoAsync($"{MorningNudgeFixture.WebOrigin}/garden");
            await Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Nothing needs water" })).ToBeVisibleAsync();

            // 5. 10:00, inside the neighbour's window: three Readings in a row below the low of 30 %.
            await SetClockAsync(owner, crossing);
            await HeartbeatAsync(owner, hub);
            await WakeAsync(owner, hub, node, TwentyPercent, crossing.AddMinutes(-2));
            await WakeAsync(owner, hub, node, TwentyPercent, crossing.AddMinutes(-1));
            await WakeAsync(owner, hub, node, TwentyPercent, crossing);

            await UntilAsync(neighbour, () => ListAsync(page, "__cf_notifications"), shown => shown.Count >= 1, "the neighbour's browser notification arrives");
            var shown = Assert.Single(await ListAsync(page, "__cf_notifications"));
            Assert.Equal($"{LotName} needs water", shown.GetProperty("title").GetString());
            Assert.Contains("in the soil", shown.GetProperty("body").GetString(), StringComparison.Ordinal);
            Assert.Equal(1, await JournalCountAsync(neighbour, "user.notification-sent"));

            // Garden, Cucumbers' Lot detail and Devices: the same read surfaces, no admin control anywhere.
            await Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = $"{LotName} needs water" })).ToBeVisibleAsync();
            await AssertNoAdminControlsAsync(page, "Garden");
            await Expect(page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Members", Exact = true })).ToHaveCountAsync(0);

            await page.GotoAsync($"{MorningNudgeFixture.WebOrigin}/garden/{lotId}");
            await Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = LotName, Exact = true, Level = 1 })).ToBeVisibleAsync();
            await AssertNoAdminControlsAsync(page, "Cucumbers' Lot detail");

            await page.GotoAsync($"{MorningNudgeFixture.WebOrigin}/devices");
            await Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Devices", Exact = true, Level = 1 })).ToBeVisibleAsync();
            await AssertNoAdminControlsAsync(page, "Devices");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new Xunit.Sdk.XunitException(
                $"{exception.Message}{Environment.NewLine}Page: {page.Url}{Environment.NewLine}Web app output:{Environment.NewLine}{Stack.WebOutput()}",
                exception);
        }
    }

    // Every admin action the Experience spine hides from a Member: setting Thresholds, Calibrate, Pause, Add/move
    // Devices, Silence Window, Lots and Members. "View Thresholds", the read-only Thresholds, is a read surface.
    [GeneratedRegex(@"\b(Set (a low )?Thresholds?|Calibrate|Pause|Resume|Add a (Hub|Node)|Move|Unassign|Actions for Node|Silence Window|Remove Lot|Add Lot|New Lot|Members)\b")]
    private static partial Regex AdminControl();

    private static async Task AssertNoAdminControlsAsync(IPage page, string surface)
    {
        foreach (var role in new[] { AriaRole.Button, AriaRole.Link, AriaRole.Menuitem, AriaRole.Switch })
        {
            var controls = page.GetByRole(role, new PageGetByRoleOptions { NameRegex = AdminControl() });
            var names = await controls.EvaluateAllAsync<string[]>(
                "nodes => nodes.map(node => node.getAttribute('aria-label') ?? node.textContent?.trim() ?? '')");
            Assert.True(names.Length == 0, $"{surface} shows the neighbour admin controls: {string.Join(", ", names)}.");
        }
    }

    private static async Task SignInAsync(IPage page, JourneyUser user)
    {
        await page.GotoAsync(MorningNudgeFixture.WebOrigin);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.Locator("#username").FillAsync(user.Username);
        await page.Locator("#password").FillAsync(user.Password);
        await page.Locator("#kc-login").ClickAsync();
    }

    // The neighbour's Sites include the garden once the accepted invitation reached the Server.
    private async Task WaitForMembershipAsync(JourneyUser user, string siteId)
    {
        var deadline = TimeProvider.System.GetUtcNow() + ArrivalTimeout;

        while (true)
        {
            using var server = await Stack.CreateServerClientAsync(user, Ct);
            using var response = await server.GetAsync(new Uri($"/sites/{siteId}", UriKind.Relative), Ct);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
                Assert.Equal("Member", body.RootElement.GetProperty("role").GetString());
                return;
            }

            Assert.True(TimeProvider.System.GetUtcNow() < deadline, $"The neighbour's Membership did not arrive within {ArrivalTimeout.TotalSeconds} s.");
            await Task.Delay(TimeSpan.FromMilliseconds(500), Ct);
        }
    }

    private async Task<string> WindowAsync(JourneyUser user)
    {
        using var server = await Stack.CreateServerClientAsync(user, Ct);
        using var response = await server.GetAsync(new Uri("/me/notification-settings", UriKind.Relative), Ct);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));

        return body.RootElement.TryGetProperty("window", out var window)
            ? $"{window.GetProperty("from").GetString()}–{(window.TryGetProperty("to", out var to) ? to.GetString() : string.Empty)}"
            : string.Empty;
    }
}
