using System.Net;
using System.Text.Json.Nodes;
using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Notifications;
using Coldframe.Contracts.Sensors;
using Coldframe.Contracts.Sites;
using Coldframe.DeviceSimulator;
using Coldframe.Protocol.Device.V1;
using Coldframe.Server.Alerts;
using Coldframe.Server.IntegrationTests.Devices;
using Coldframe.Server.IntegrationTests.Journal;
using ProtocolKind = Coldframe.Protocol.Setup.V1.DeviceKind;

namespace Coldframe.Server.IntegrationTests.Identity;

/// <summary>
/// Push delivery on a TestCluster with a fake clock (Story 6.5; UX-DR115, UX-DR116, UX-DR121): the User grain owns
/// the push registrations as events and hands them to the Notifier with every notification; the real APNs and FCM
/// channels look names and values up, write the text and send it to stub providers. A token a provider no
/// longer knows comes back through the seam and is journaled as removed; a provider that is down leaves the
/// delivery due.
/// </summary>
[Collection(IngestSuites.Name)]
public sealed class PushDeliveryGrainTests(IdentityCluster identity) : IClassFixture<IdentityCluster>
{
    private const string HubA = "92064422c012f481";
    private const string Registered = "user.push-device-registered";
    private const string Removed = "user.push-device-removed";
    private const string Sent = "user.notification-sent";

    // The soil probe is calibrated dry 3000, wet 1000: 20 raw counts per percent.
    private const long DryRaw = 3000;
    private const long WetRaw = 1000;

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTimeOffset Now => identity.Time.GetUtcNow();

    [Fact]
    public async Task UxDr115ARealSensorCrossingItsLowPushesOnceToTheIPhoneAndOnceToTheAndroidPhoneAsTheContractSays()
    {
        var fixture = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "asyncapi", "push.alert.json"), Ct))!;
        var (siteId, ownerId) = await SiteAsync("Home garden");
        var (iphone, android) = await RegisterBothAsync(ownerId);
        var lot = await identity.Site(siteId).CreateLot(ownerId, "k1", "Tomatoes", Ct);
        var lotId = lot.Lot!.Id;
        var (node, soil) = await CalibratedNodeAsync(siteId, lotId, ownerId);

        // Noon is inside the default Notification Window, in UTC while the User has no time zone.
        var noon = new DateTimeOffset(Now.UtcDateTime.Date, TimeSpan.Zero).AddHours(36);
        identity.Time.Advance(noon - Now - (3 * Interval));

        // Three Readings at 20 percent, below the default low of 30: the Alert opens and the Alert grain tells
        // the member. Nothing calls the User grain; its own timer hands the Notifier what is due.
        await ReadAsync(node, Soil(20));
        await ReadAsync(node, Soil(20));
        await ReadAsync(node, Soil(20));
        var alertId = AlertIds.Threshold(soil, 1);
        await JournalWait.UntilAsync(
            async () => (await identity.AliasesAsync($"user/{ownerId}")).Contains(Sent),
            "the member was notified of the Alert");

        var apns = Assert.Single(identity.Push.For(iphone));
        var fcm = Assert.Single(identity.Push.For(android));

        // The text is the contract's example: written on the Server, self-contained, the same on both phones.
        var expected = fixture["apns"]!["payload"]!["aps"]!["alert"]!;
        var alert = apns.Body["aps"]!["alert"]!;
        Assert.Equal(
            (expected["title"]!.GetValue<string>(), expected["body"]!.GetValue<string>()),
            (alert["title"]!.GetValue<string>(), alert["body"]!.GetValue<string>()));
        var data = fcm.Body["message"]!["data"]!.AsObject();
        Assert.Equal(
            (expected["title"]!.GetValue<string>(), expected["body"]!.GetValue<string>(), "Home garden"),
            (data["title"]!.GetValue<string>(), data["body"]!.GetValue<string>(), data["siteName"]!.GetValue<string>()));

        // Grouped per Site, the standard interruption level, no badge (UX-DR121).
        var aps = apns.Body["aps"]!.AsObject();
        Assert.Equal((siteId, "active"), (aps["thread-id"]!.GetValue<string>(), aps["interruption-level"]!.GetValue<string>()));
        Assert.False(aps.ContainsKey("badge"));
        Assert.False(fcm.Body["message"]!.AsObject().ContainsKey("notification"));
        Assert.Equal(siteId, data["siteId"]!.GetValue<string>());

        // The routing data: the same keys as the contract's example, with this Site, Lot and Alert.
        var route = apns.Body["coldframe"]!.AsObject();
        Assert.Equal(fixture["apns"]!["payload"]!["coldframe"]!.AsObject().Select(pair => pair.Key), route.Select(pair => pair.Key));
        Assert.Equal(
            ("alert", siteId, lotId, alertId.ToString("D")),
            (route["kind"]!.GetValue<string>(), route["siteId"]!.GetValue<string>(), route["lotId"]!.GetValue<string>(), route["alertId"]!.GetValue<string>()));
        Assert.Equal(fixture["fcm"]!["message"]!["data"]!.AsObject().Select(pair => pair.Key), data.Select(pair => pair.Key));
        Assert.Equal(
            ("alert", lotId, alertId.ToString("D")),
            (data["kind"]!.GetValue<string>(), data["lotId"]!.GetValue<string>(), data["alertId"]!.GetValue<string>()));

        // One collapse identity for the send, on both phones.
        var collapseId = route["collapseId"]!.GetValue<string>();
        Assert.Matches("^[0-9a-f]{32}$", collapseId);
        Assert.Equal((collapseId, collapseId), (apns.Headers["apns-collapse-id"], data["collapseId"]!.GetValue<string>()));
        Assert.Equal(("alert", "10", "com.escendit.coldframe"), (apns.Headers["apns-push-type"], apns.Headers["apns-priority"], apns.Headers["apns-topic"]));
        Assert.Equal("apns.test", apns.Host);
    }

    [Fact]
    public async Task ATokenTheProviderNoLongerKnowsIsRemovedFromTheUserStreamAndTheNextNotificationIsNotSentToIt()
    {
        var (siteId, ownerId) = await SiteAsync();
        var (iphone, android) = await RegisterBothAsync(ownerId);
        identity.Push.Answer(iphone, HttpStatusCode.Gone, """{"reason":"Unregistered"}""");
        GoTo(12, 0);

        await OpenAsync(siteId);
        await identity.WakeUserAsync(ownerId);

        // The send counts as delivered: the removal is no failure, and the Android phone got the push.
        Assert.Equal([Registered, Registered, Removed, Sent], await PushAliasesAsync(ownerId));
        Assert.Single(identity.Push.For(iphone));
        Assert.Single(identity.Push.For(android));
        var left = Assert.Single(await identity.User(ownerId).GetPushRegistrations(Ct));
        Assert.Equal(("android", android), (left.InstallationId, left.Token));
        var removed = (await identity.Store.ReadStreamAsync($"user/{ownerId}", Ct)).Select(journaled => journaled.Data).OfType<PushDeviceRemoved>().Single();
        Assert.Equal(("iphone", PushDeviceRemovalReason.Invalid, Now), (removed.InstallationId, removed.Reason, removed.RemovedAt));

        // The Reminder a day later goes to the Android phone only.
        identity.Time.Advance(TimeSpan.FromHours(24));
        await identity.WakeUserAsync(ownerId);

        Assert.Single(identity.Push.For(iphone));
        var pushes = identity.Push.For(android);
        Assert.Equal(["alert", "reminder"], pushes.Select(push => push.Body["message"]!["data"]!["kind"]!.GetValue<string>()));
        Assert.StartsWith("Still", pushes[1].Body["message"]!["data"]!["body"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnFcmTokenReportedAsUnregisteredIsRemovedToo()
    {
        var (siteId, ownerId) = await SiteAsync();
        var (iphone, android) = await RegisterBothAsync(ownerId);
        identity.Push.Answer(
            android,
            HttpStatusCode.NotFound,
            """{"error":{"code":404,"status":"NOT_FOUND","details":[{"@type":"type.googleapis.com/google.firebase.fcm.v1.FcmError","errorCode":"UNREGISTERED"}]}}""");
        GoTo(12, 0);

        await OpenAsync(siteId);
        await identity.WakeUserAsync(ownerId);

        Assert.Equal([Registered, Registered, Removed, Sent], await PushAliasesAsync(ownerId));
        Assert.Equal(["iphone"], (await identity.User(ownerId).GetPushRegistrations(Ct)).Select(registration => registration.InstallationId));
        Assert.Single(identity.Push.For(iphone));
    }

    [Fact]
    public async Task ADeadTokenIsRemovedAlsoWhenTheOtherProviderIsDownAndTheDeliveryStaysDue()
    {
        var (siteId, ownerId) = await SiteAsync();
        var (iphone, android) = await RegisterBothAsync(ownerId);
        identity.Notifications.WithoutDevice(ownerId);
        identity.Push.Answer(iphone, HttpStatusCode.Gone, """{"reason":"Unregistered"}""");
        identity.Push.Answer(android, HttpStatusCode.ServiceUnavailable, """{"error":{"status":"UNAVAILABLE"}}""");
        GoTo(12, 0);

        await OpenAsync(siteId);
        await identity.WakeUserAsync(ownerId);

        // No phone has the push, so nothing is journaled as sent; the removal came back through the failed send.
        Assert.Equal([Registered, Registered, Removed], await PushAliasesAsync(ownerId));
        var removed = (await identity.Store.ReadStreamAsync($"user/{ownerId}", Ct)).Select(journaled => journaled.Data).OfType<PushDeviceRemoved>().Single();
        Assert.Equal(("iphone", PushDeviceRemovalReason.Invalid), (removed.InstallationId, removed.Reason));

        // The next wake sends to the Android phone only, and once it is taken the notification is sent.
        identity.Push.Accept(android);
        await identity.WakeUserAsync(ownerId);

        Assert.Single(identity.Push.For(iphone));
        Assert.Equal([Registered, Registered, Removed, Sent], await PushAliasesAsync(ownerId));
    }

    [Fact]
    public async Task UxDr116AQuantityThatDoesNotCalibrateIsPushedInItsDisplayUnitThroughTheRealLookups()
    {
        var (siteId, ownerId) = await SiteAsync();
        var android = NewToken();
        await RegisterAsync(ownerId, "android", PushPlatform.Fcm, android);
        var lotId = (await identity.Site(siteId).CreateLot(ownerId, "k1", "Tomatoes", Ct)).Lot!.Id;
        var (node, _) = await CalibratedNodeAsync(siteId, lotId, ownerId);

        // The air-temperature Sensor stores milli-degrees Celsius: a low of 5 °C, three Readings at 4 °C.
        var air = node.SensorId(1, Quantity.AirTemperature);
        Assert.Equal(
            SensorThresholdsOutcome.Changed,
            (await identity.Sensor(air).SetThresholds(new SetSensorThresholds(Low: new ThresholdSetting(ThresholdKind.Override, 5_000)), Ct)).Outcome);
        var noon = new DateTimeOffset(Now.UtcDateTime.Date, TimeSpan.Zero).AddHours(36);
        identity.Time.Advance(noon - Now - (3 * Interval));

        for (var reading = 0; reading < 3; reading++)
        {
            await ReadAsync(node, Soil(50), new SimulatedReading(1, Quantity.AirTemperature, 4_000));
        }

        await identity.WakeUserAsync(ownerId);

        // The Threshold divided into degrees, the Reading too, at its time in UTC (the User has no zone).
        var data = Assert.Single(identity.Push.For(android)).Body["message"]!["data"]!;
        Assert.Equal(
            ("Tomatoes temperature below 5 °C", "Reading 4 °C at 12:00."),
            (data["title"]!.GetValue<string>(), data["body"]!.GetValue<string>()));
    }

    [Fact]
    public async Task UxDr118ASummaryLeavesOutTheLotThatWasRemovedThroughTheRealLookups()
    {
        var (siteId, ownerId) = await SiteAsync("Home garden");
        var android = NewToken();
        await RegisterAsync(ownerId, "android", PushPlatform.Fcm, android);
        var tomatoes = (await identity.Site(siteId).CreateLot(ownerId, "k1", "Tomatoes", Ct)).Lot!.Id;
        var beans = (await identity.Site(siteId).CreateLot(ownerId, "k2", "Beans", Ct)).Lot!.Id;

        // Both Alerts open at night and are held for the morning.
        GoTo(23, 30);
        await OpenAsync(siteId, tomatoes);
        await OpenAsync(siteId, beans);
        await identity.WakeUserAsync(ownerId);
        Assert.Empty(identity.Push.For(android));

        // Beans is removed overnight; its Alert stays open, and its line has no Lot to name.
        Assert.Equal(Coldframe.Contracts.Lots.LotOutcome.Removed, (await identity.Lot(beans).Remove(siteId, Ct)).Outcome);
        GoTo(7, 0);
        await identity.WakeUserAsync(ownerId);

        var data = Assert.Single(identity.Push.For(android)).Body["message"]!["data"]!;
        Assert.Equal(
            ("summary", "Home garden: 1 needs water", "Tomatoes needs water\nHeld overnight, 22:00–07:00"),
            (data["kind"]!.GetValue<string>(), data["title"]!.GetValue<string>(), data["body"]!.GetValue<string>()));
    }

    [Fact]
    public async Task AProviderThatIsDownOnEveryTryLeavesTheDeliveryDueAndTheNextWakeSendsItAgainUnderTheSameCollapseIdentity()
    {
        var (siteId, ownerId) = await SiteAsync();
        var android = NewToken();
        await RegisterAsync(ownerId, "android", PushPlatform.Fcm, android);

        // The phone is the only way to reach this User: the recording channel stands for no device.
        identity.Notifications.WithoutDevice(ownerId);
        identity.Push.Answer(android, HttpStatusCode.ServiceUnavailable, """{"error":{"status":"UNAVAILABLE"}}""");
        GoTo(12, 0);

        await OpenAsync(siteId);
        await identity.WakeUserAsync(ownerId);

        // Tried three times within the wake, then the channel threw: nothing is journaled as sent. The grain's
        // own timer may have woken it as well, which is three more tries each time.
        var failed = identity.Push.For(android).Count;
        Assert.True(failed >= 3 && failed % 3 == 0, $"{failed} tries");
        Assert.Equal([Registered], await PushAliasesAsync(ownerId));
        Assert.Single(await identity.User(ownerId).GetPushRegistrations(Ct));

        identity.Push.Accept(android);
        await identity.WakeUserAsync(ownerId);

        // One more request, the one that was taken.
        var pushes = identity.Push.For(android);
        Assert.True(pushes.Count >= 4 && pushes.Count % 3 == 1, $"{pushes.Count} requests");
        Assert.Equal([Registered, Sent], await PushAliasesAsync(ownerId));

        // The repeat replaces the earlier one on the phone.
        Assert.Single(pushes.Select(push => push.Body["message"]!["data"]!["collapseId"]!.GetValue<string>()).Distinct());

        // And it is not sent again.
        await identity.WakeUserAsync(ownerId);
        Assert.Equal(pushes.Count, identity.Push.For(android).Count);
    }

    [Fact]
    public async Task WhenOneProviderIsDownTheOtherStillDeliversAndTheNotificationCountsAsSent()
    {
        var (siteId, ownerId) = await SiteAsync();
        var (iphone, android) = await RegisterBothAsync(ownerId);
        identity.Notifications.WithoutDevice(ownerId);
        identity.Push.Answer(iphone, HttpStatusCode.ServiceUnavailable, """{"reason":"ServiceUnavailable"}""");
        GoTo(12, 0);

        await OpenAsync(siteId);
        await identity.WakeUserAsync(ownerId);

        Assert.Single(identity.Push.For(android));
        Assert.Equal(3, identity.Push.For(iphone).Count);
        Assert.Equal([Registered, Registered, Sent], await PushAliasesAsync(ownerId));

        // Not handed over again: the Android phone would get it twice. Both registrations stay.
        await identity.WakeUserAsync(ownerId);
        Assert.Single(identity.Push.For(android));
        Assert.Equal(2, (await identity.User(ownerId).GetPushRegistrations(Ct)).Count);
    }

    [Fact]
    public async Task AUserWithoutADeviceIsSentNothingAndNothingIsRepeated()
    {
        var (siteId, ownerId) = await SiteAsync();
        identity.Notifications.WithoutDevice(ownerId);
        GoTo(12, 0);

        await OpenAsync(siteId);
        await identity.WakeUserAsync(ownerId);
        await identity.WakeUserAsync(ownerId);

        // No failure and no retry: the notification is journaled as sent once, and the channels saw it once.
        Assert.Equal([Sent], await PushAliasesAsync(ownerId));
        var handed = Assert.Single(identity.Notifications.For(ownerId));
        Assert.Empty(handed.Notification.Registrations!);
    }

    [Fact]
    public async Task TheNotificationCarriesWhatOnlyTheUserGrainKnows()
    {
        var (siteId, ownerId) = await SiteAsync();
        var (iphone, android) = await RegisterBothAsync(ownerId);
        var window = new NotificationWindow(6 * 60, 23 * 60);
        await identity.User(ownerId).UpdateNotificationSettings(new UpdateNotificationSettings(window, "Europe/Zurich", null), Ct);
        GoTo(12, 0);

        await OpenAsync(siteId);
        await identity.WakeUserAsync(ownerId);

        var handed = Assert.Single(identity.Notifications.For(ownerId)).Notification;
        Assert.Equal([("iphone", iphone), ("android", android)], handed.Registrations!.Select(registration => (registration.InstallationId, registration.Token)));
        Assert.Equal(("Europe/Zurich", window), (handed.TimeZone, handed.Window));
    }

    [Fact]
    public async Task RegisteringTheSameInstallationAgainReplacesItsTokenAndAnUnchangedRegistrationWritesNoEvent()
    {
        var ownerId = Guid.NewGuid().ToString();
        var user = identity.User(ownerId);
        var (first, rotated) = (NewToken(), NewToken());

        Assert.Equal(PushRegistrationOutcome.Registered, await user.RegisterPushDevice(new RegisterPushDevice("android", PushPlatform.Fcm, first), Ct));
        Assert.Equal(PushRegistrationOutcome.Unchanged, await user.RegisterPushDevice(new RegisterPushDevice("android", PushPlatform.Fcm, first), Ct));
        Assert.Equal([Registered], await PushAliasesAsync(ownerId));

        identity.Time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(PushRegistrationOutcome.Registered, await user.RegisterPushDevice(new RegisterPushDevice("android", PushPlatform.Fcm, rotated), Ct));

        var registration = Assert.Single(await user.GetPushRegistrations(Ct));
        Assert.Equal(new PushRegistration("android", PushPlatform.Fcm, rotated, null, Now), registration);
        Assert.Equal([Registered, Registered], await PushAliasesAsync(ownerId));

        // Removing is idempotent.
        Assert.True(await user.RemovePushDevice("android", Ct));
        Assert.False(await user.RemovePushDevice("android", Ct));
        Assert.Empty(await user.GetPushRegistrations(Ct));
        Assert.Equal([Registered, Registered, Removed], await PushAliasesAsync(ownerId));
    }

    [Fact]
    public async Task ARegistrationThatIsNotValidWritesNoEvent()
    {
        var ownerId = Guid.NewGuid().ToString();
        var user = identity.User(ownerId);

        RegisterPushDevice[] refused =
        [
            new("android", PushPlatform.Fcm, string.Empty),
            new("android", PushPlatform.Fcm, "with space"),
            new("bad id", PushPlatform.Fcm, "token"),
            new("android", (PushPlatform)7, "token"),
            new("iphone", PushPlatform.Apns, "token"),
            new("android", PushPlatform.Fcm, "token", ApnsEnvironment.Production),
        ];

        foreach (var request in refused)
        {
            Assert.Equal(PushRegistrationOutcome.Invalid, await user.RegisterPushDevice(request, Ct));
        }

        Assert.Empty(await identity.AliasesAsync($"user/{ownerId}"));
    }

    [Fact]
    public async Task ATokenBelongsToOneInstallationAndTheOldestRegistrationMakesRoomBeyondTheLimit()
    {
        var ownerId = Guid.NewGuid().ToString();
        var user = identity.User(ownerId);
        var token = NewToken();

        // The app was installed again: a new installation ID, the same token. The phone is pushed once.
        await RegisterAsync(ownerId, "old-install", PushPlatform.Fcm, token);
        await RegisterAsync(ownerId, "new-install", PushPlatform.Fcm, token);
        Assert.Equal(["new-install"], (await user.GetPushRegistrations(Ct)).Select(registration => registration.InstallationId));

        for (var index = 1; index <= PushRegistrationLimits.MaxRegistrations; index++)
        {
            identity.Time.Advance(TimeSpan.FromSeconds(1));
            await RegisterAsync(ownerId, $"phone-{index}", PushPlatform.Fcm, NewToken());
        }

        var kept = await user.GetPushRegistrations(Ct);
        Assert.Equal(PushRegistrationLimits.MaxRegistrations, kept.Count);
        Assert.DoesNotContain(kept, registration => registration.InstallationId == "new-install");
        Assert.Equal("phone-1", kept[0].InstallationId);
    }

    [Fact]
    public async Task ASiloRestartKeepsTheRegistrationsAndTheNextAlertIsPushed()
    {
        var (siteId, ownerId) = await SiteAsync();
        var (iphone, android) = await RegisterBothAsync(ownerId);
        var before = await identity.User(ownerId).GetPushRegistrations(Ct);

        // A silo restart sets the fake clock back to its start.
        var now = Now;
        await identity.RestartSiloAsync();
        identity.Time.SetUtcNow(now);

        Assert.Equal(before, await identity.User(ownerId).GetPushRegistrations(Ct));

        GoTo(12, 0);
        await OpenAsync(siteId);
        await identity.WakeUserAsync(ownerId);

        Assert.Single(identity.Push.For(iphone));
        Assert.Single(identity.Push.For(android));
    }

    private static string NewToken() => Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

    private static SimulatedReading Soil(int percent) => new(0, Quantity.SoilMoisture, DryRaw - ((DryRaw - WetRaw) * percent / 100));

    // Moves the clock on to the next hh:mm UTC, today or tomorrow: the clock is shared and only moves forward.
    private void GoTo(int hour, int minute)
    {
        var target = new DateTimeOffset(Now.UtcDateTime.Date, TimeSpan.Zero).AddHours(hour).AddMinutes(minute);
        identity.Time.SetUtcNow(target > Now ? target : target.AddDays(1));
    }

    // An Active Site created by its Owner, whose User grain therefore holds the Site.
    private async Task<(string SiteId, string OwnerId)> SiteAsync(string name = "Home")
    {
        var ownerId = Guid.NewGuid().ToString();
        var created = await identity.User(ownerId).CreateSite("k1", name, Ct);
        Assert.Equal(SiteCreationOutcome.Created, created.Outcome);
        return (created.Site!.Id, ownerId);
    }

    private async Task RegisterAsync(string userId, string installationId, PushPlatform platform, string token) =>
        Assert.Equal(
            PushRegistrationOutcome.Registered,
            await identity.User(userId).RegisterPushDevice(
                new RegisterPushDevice(installationId, platform, token, platform == PushPlatform.Apns ? ApnsEnvironment.Production : null),
                Ct));

    // An iPhone, then an Android phone, each with a token of its own.
    private async Task<(string IPhone, string Android)> RegisterBothAsync(string userId)
    {
        var (iphone, android) = (NewToken(), NewToken());
        await RegisterAsync(userId, "iphone", PushPlatform.Apns, iphone);
        identity.Time.Advance(TimeSpan.FromSeconds(1));
        await RegisterAsync(userId, "android", PushPlatform.Fcm, android);
        return (iphone, android);
    }

    // Opens an Alert the way a Sensor grain does: the Alert grain journals it, reports it to its Site grain and
    // tells every member's User grain before it answers. Its Sensor exists nowhere else, and its Lot only when one is named.
    private async Task<SiteAlert> OpenAsync(string siteId, string? lot = null)
    {
        var sensorId = Guid.NewGuid();
        var lotId = lot ?? Guid.CreateVersion7().ToString();
        var request = new OpenAlert(sensorId, 1, ThresholdSide.Low, siteId, lotId, "5a4b3c2d1e0f7c20", "soil_moisture", Now);
        var alertId = AlertIds.Threshold(sensorId, 1);
        Assert.Equal(new AlertResult(AlertOutcome.Open, Reported: true), await identity.Alert(alertId).Open(request, Ct));
        return new SiteAlert(alertId, AlertKind.Threshold, ThresholdSide.Low, lotId, sensorId, "5a4b3c2d1e0f7c20", "soil_moisture", Now);
    }

    // A Node enrolled on the Lot that declared its four Sensors, its soil Sensor calibrated: the default
    // Thresholds are 30 and 80 percent.
    private async Task<(SimulatedDevice Node, Guid Soil)> CalibratedNodeAsync(string siteId, string lotId, string userId)
    {
        var node = SimulatedDevice.Create(ProtocolKind.Node);
        var enrol = new EnrolDevice(siteId, DeviceKind.Node, identity.Vault.Wrap(node.DeviceId, node.Keys.DeviceKey), userId, Guid.NewGuid().ToString("N"), lotId);
        Assert.Equal(DeviceEnrolmentOutcome.Enrolled, (await identity.Device(node.DeviceId.ToString()).Enrol(enrol, Ct)).Outcome);

        Assert.True((await ReportAsync(node, node.Wake(Now))).SpecificationsUnknown);
        Assert.False((await ReportAsync(node, node.Wake(Now))).SpecificationsUnknown);

        var soil = node.SensorId(0, Quantity.SoilMoisture);
        var dry = await ReadAsync(node, new SimulatedReading(0, Quantity.SoilMoisture, DryRaw));
        var wet = await ReadAsync(node, new SimulatedReading(0, Quantity.SoilMoisture, WetRaw));
        var calibrated = await identity.Sensor(soil).Calibrate(
            new CalibrateSensor(new CalibrationPointRef(SeqOf(dry)), new CalibrationPointRef(SeqOf(wet))),
            Ct);
        Assert.Equal(SensorCalibrationOutcome.Calibrated, calibrated.Outcome);

        return (node, soil);
    }

    // One wake, 15 minutes after the last one, stored and acknowledged.
    private async Task<NodeFrame> ReadAsync(SimulatedDevice node, params SimulatedReading[] readings)
    {
        identity.Time.Advance(Interval);
        var frame = node.Wake(Now, readings: readings);
        await ReportAsync(node, frame);
        return frame;
    }

    private async Task<Downlink> ReportAsync(SimulatedDevice node, NodeFrame frame)
    {
        var envelope = node.SealFrame(frame);
        var result = await identity.Device(node.DeviceId.ToString()).Ingest(
            new DeviceIngest(envelope.ProtocolVersion, envelope.Counter, envelope.Ciphertext.ToByteArray(), HubA),
            Ct);

        Assert.Equal(DeviceIngestStatus.Stored, result.Status);
        return node.OpenDownlink(SealedEnvelope.Parser.ParseFrom(result.Downlink));
    }

    private static ulong SeqOf(NodeFrame frame) => frame.Readings.Single(reading => reading.Slot == 0).ReadingSeq;

    // The User's push and sent events, in order; Memberships, pulls, settings and tracking left out.
    private async Task<List<string>> PushAliasesAsync(string userId) =>
        [.. (await identity.AliasesAsync($"user/{userId}")).Where(alias => alias is Registered or Removed or Sent)];
}
