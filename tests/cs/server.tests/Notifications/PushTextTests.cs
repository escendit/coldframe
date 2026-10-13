using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Notifications;
using Coldframe.Contracts.Sites;
using Coldframe.Server.Notifications.Push;
using static Coldframe.Server.Tests.Notifications.PushTestKit;

namespace Coldframe.Server.Tests.Notifications;

/// <summary>
/// The text of a push is written on the Server and is self-contained (Story 6.5): the title is the condition, the
/// body is value and context (UX-DR116), a Reminder is the Alert text with "Still" (UX-DR119), and a summary is one
/// notification with a line per Alert and a footer naming the held hours (UX-DR118).
/// </summary>
public sealed class PushTextTests
{
    private static readonly DateTimeOffset SentAt = AlertDueAt.AddSeconds(20);

    [Fact]
    public async Task UxDr116ALowSoilAlertReadsNeedsWaterWithTheRoundedValueAndTheLow()
    {
        var content = await Builder(TomatoesLookups()).BuildAsync(Alert(IPhone), SentAt);

        Assert.NotNull(content);
        Assert.Equal(("Tomatoes needs water", "~20 % in the soil, your low is 30 %."), (content.Title, content.Body));
        Assert.Equal(("alert", Site, "Home garden", TomatoesLot, (Guid?)TomatoesAlert), (content.Kind, content.SiteId, content.SiteName, content.LotId, content.AlertId));
    }

    [Fact]
    public async Task UxDr116AHighSoilAlertReadsTooWetWithTheHigh()
    {
        var lookups = new FakeLookups();
        var entry = Entry("lot-herbs", side: ThresholdSide.High);
        lookups.Lots["lot-herbs"] = "Herbs";
        lookups.Readings[entry.SensorId!.Value] = new PushReading(new PushValue(65, "%"), AlertDueAt);
        lookups.Thresholds[(entry.SensorId!.Value, ThresholdSide.High)] = new PushValue(60, "%");

        var content = await Builder(lookups).BuildAsync(Alert(IPhone) with { Entries = [entry] }, SentAt);

        Assert.Equal(("Herbs too wet", "~65 % in the soil, your high is 60 %."), (content!.Title, content.Body));
    }

    [Fact]
    public async Task UxDr116AnotherQuantityNamesItsThresholdInTheTitleAndTheReadingWithItsTimeInTheUsersZone()
    {
        var lookups = new FakeLookups();
        var entry = Entry(TomatoesLot, "air_temperature");
        lookups.Lots[TomatoesLot] = "Tomatoes";

        // 03:15 UTC is 05:15 in Zurich in October.
        lookups.Readings[entry.SensorId!.Value] = new PushReading(new PushValue(4, "°C"), new DateTimeOffset(2026, 10, 9, 3, 15, 0, TimeSpan.Zero));
        lookups.Thresholds[(entry.SensorId!.Value, ThresholdSide.Low)] = new PushValue(5, "°C");

        var content = await Builder(lookups).BuildAsync(Alert(IPhone) with { Entries = [entry] }, SentAt);

        Assert.Equal(("Tomatoes temperature below 5 °C", "Reading 4 °C at 05:15."), (content!.Title, content.Body));
    }

    [Fact]
    public async Task UxDr119AReminderIsTheAlertTextWithStill()
    {
        var content = await Builder(TomatoesLookups()).BuildAsync(Reminder(IPhone), SentAt.AddDays(1));

        Assert.Equal(("reminder", "Tomatoes needs water", "Still ~20 % in the soil, your low is 30 %."), (content!.Kind, content.Title, content.Body));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task WithoutTheValueOrTheThresholdAnAlertIsItsTitleOnly(bool noReading, bool noThreshold)
    {
        var lookups = TomatoesLookups();

        if (noReading)
        {
            lookups.Readings.Clear();
        }

        if (noThreshold)
        {
            lookups.Thresholds.Clear();
        }

        var content = await Builder(lookups).BuildAsync(Alert(IPhone), SentAt);

        Assert.Equal(("Tomatoes needs water", null), (content!.Title, content.Body));
    }

    [Fact]
    public async Task AReadingStoredWithoutACalibrationIsNoValue()
    {
        var lookups = TomatoesLookups();
        lookups.Readings[TomatoesSoil] = new PushReading(new PushValue(1830, "raw"), AlertDueAt);

        var content = await Builder(lookups).BuildAsync(Alert(IPhone), SentAt);

        Assert.Null(content!.Body);
    }

    [Fact]
    public async Task ALookupThatFailsLeavesItsPartOutAndNeverFailsThePush()
    {
        var content = await Builder(new FakeLookups { Fail = true }).BuildAsync(Alert(IPhone), SentAt);

        Assert.Equal(("A Lot needs water", null, null), (content!.Title, content.Body, content.SiteName));
    }

    [Fact]
    public async Task UxDr118ASummaryIsOneNotificationWithNeedsWaterFirstPluralCountsAndTheHeldHours()
    {
        var lookups = new FakeLookups();

        var content = await Builder(lookups).BuildAsync(Summary(lookups, IPhone), SentAt);

        Assert.Equal("Home garden: 2 need water, 3 to check", content!.Title);
        Assert.Equal(
            [
                "Tomatoes needs water",
                "Peppers needs water",
                "Herbs too wet",
                "Beans temperature below 5 °C",
                "Lettuce humidity above 90 %",
                "Held overnight, 22:00–07:00",
            ],
            content.Body!.Split('\n'));
        Assert.Equal(("summary", null, null), (content.Kind, content.LotId, content.AlertId));
    }

    [Fact]
    public async Task ASummaryLeavesOutTheLineOfALotThatIsGoneAndCountsWithoutIt()
    {
        var lookups = new FakeLookups();
        var summary = Summary(lookups, IPhone);
        lookups.Lots.Remove("lot-peppers");

        var content = await Builder(lookups).BuildAsync(summary, SentAt);

        Assert.Equal("Home garden: 1 needs water, 3 to check", content!.Title);
        Assert.DoesNotContain("Peppers", content.Body, StringComparison.Ordinal);
        Assert.Equal(5, content.Body!.Split('\n').Length);
    }

    [Fact]
    public async Task ASummaryWhoseLotNamesCouldNotBeReadKeepsEveryLineUnnamedAndIsStillPushed()
    {
        // A lookup that failed is no removed Lot: a stall of the database must not turn the summary into
        // nothing, which would be journaled as sent without ever reaching a phone.
        var lookups = new FakeLookups();
        var summary = Summary(lookups, IPhone);
        lookups.FailLots = true;

        var content = await Builder(lookups).BuildAsync(summary, SentAt);

        Assert.NotNull(content);
        Assert.Equal("Home garden: 2 need water, 3 to check", content.Title);
        Assert.Equal(
            [
                "A Lot needs water",
                "A Lot needs water",
                "A Lot too wet",
                "A Lot temperature below 5 °C",
                "A Lot humidity above 90 %",
                "Held overnight, 22:00–07:00",
            ],
            content.Body!.Split('\n'));
    }

    [Fact]
    public async Task ALookupThatNeverAnswersIsLeftOutOnceTheLookupBudgetIsOver()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(AlertDueAt);
        var lookups = TomatoesLookups();
        lookups.HangThresholds = true;

        var build = Builder(lookups, clock: clock).BuildAsync(Alert(IPhone), SentAt);
        Assert.False(build.IsCompleted);
        await AdvanceUntilDoneAsync(clock, build);

        // Built within the budget (2 s), without the part that did not arrive: the title stands alone.
        var content = await build;
        Assert.Equal(("Tomatoes needs water", null), (content!.Title, content.Body));
        Assert.InRange(clock.GetUtcNow() - AlertDueAt, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4));
    }

    [Fact]
    public void ANameThatLooksLikeAPlaceholderIsShownAsItIs()
    {
        var text = PushText.English;
        var temperature = Entry("lot-1", "air_temperature");

        Assert.Equal(
            "{unit} {threshold} temperature below 5 °C",
            text.Title(new PushEntryFacts(temperature, "{unit} {threshold}", Threshold: new PushValue(5, "°C")), SentAt));
        Assert.Equal("{counts}: 1 needs water", text.SummaryTitle("{counts}", 1, 0));
        Assert.Equal("{lot} needs water", text.Title(new PushEntryFacts(Entry("lot-1"), "{lot}"), SentAt));
    }

    [Fact]
    public async Task ASummaryWhoseLotsAreAllGoneSaysNothing()
    {
        var lookups = new FakeLookups();
        var summary = Summary(lookups, IPhone);
        lookups.Lots.Clear();

        Assert.Null(await Builder(lookups).BuildAsync(summary, SentAt));
    }

    // Story 7.1: a Health Alert has no Sensor, so no Reading and no Threshold is looked up for it. An Alert
    // tracked before Story 7.2 says neither what kind of Device it is nor since when it is silent: its text stays
    // the neutral one.
    [Fact]
    public async Task ASilentAlertTrackedBeforeTheFinalCopyIsTitledNeutrallyWithItsLotAndLooksUpNoReadingOrThreshold()
    {
        var lookups = TomatoesLookups();
        var silent = Silent(TomatoesLot);

        var alert = await Builder(lookups).BuildAsync(Alert(IPhone) with { Entries = [silent] }, SentAt);

        Assert.Equal(("Tomatoes needs attention", null, TomatoesLot, (Guid?)silent.AlertId), (alert!.Title, alert.Body, alert.LotId, alert.AlertId));

        // The Site's name and the Lot's name, nothing of a Sensor.
        Assert.Equal(2, lookups.Calls);

        var reminder = await Builder(lookups).BuildAsync(Reminder(IPhone) with { Entries = [silent] }, SentAt.AddDays(1));

        Assert.Equal(("reminder", "Tomatoes needs attention", "Still open."), (reminder!.Kind, reminder.Title, reminder.Body));
    }

    [Fact]
    public async Task ASilentAlertWithoutALotIsSentAloneWithNeutralText()
    {
        var lookups = new FakeLookups();
        var silent = Silent(lotId: null);

        var content = await Builder(lookups).BuildAsync(Alert(IPhone) with { Entries = [silent] }, SentAt);

        Assert.NotNull(content);
        Assert.Equal(("alert", "A device needs attention", null, null), (content.Kind, content.Title, content.Body, content.LotId));
        Assert.Equal((Guid?)silent.AlertId, content.AlertId);

        // Only the Site's name: an Alert without a Lot has no Lot to look up.
        Assert.Equal(1, lookups.Calls);
    }

    [Fact]
    public async Task ASilentAlertWithoutALotKeepsItsLineInASummaryAlsoWhenEveryLotIsGone()
    {
        var lookups = new FakeLookups();
        var summary = Summary(lookups, IPhone);
        var withSilent = summary with { Entries = [.. summary.Entries, Silent(lotId: null)] };

        var content = await Builder(lookups).BuildAsync(withSilent, SentAt);

        Assert.Equal("Home garden: 2 need water, 4 to check", content!.Title);
        Assert.Equal("A device needs attention", content.Body!.Split('\n')[^2]);

        // No Lot is no Lot that is gone: the summary of that Alert alone is still pushed.
        lookups.Lots.Clear();
        var alone = await Builder(lookups).BuildAsync(withSilent, SentAt);

        Assert.NotNull(alone);
        Assert.Equal("Home garden: 1 to check", alone.Title);
        Assert.Equal(["A device needs attention", "Held overnight, 22:00–07:00"], alone.Body!.Split('\n'));
    }

    [Fact]
    public async Task ASilentAlertNeverFailsThePushWhenEveryLookupFails()
    {
        var failing = new FakeLookups { Fail = true };

        var withLot = await Builder(failing).BuildAsync(Alert(IPhone) with { Entries = [Silent(TomatoesLot)] }, SentAt);
        var withoutLot = await Builder(failing).BuildAsync(Alert(IPhone) with { Entries = [Silent(lotId: null)] }, SentAt);

        Assert.Equal(("A Lot needs attention", null), (withLot!.Title, withLot.Body));
        Assert.Equal(("A device needs attention", null), (withoutLot!.Title, withoutLot.Body));
    }

    [Theory]
    [InlineData(1, 0, "Home garden: 1 needs water")]
    [InlineData(2, 0, "Home garden: 2 need water")]
    [InlineData(0, 1, "Home garden: 1 to check")]
    [InlineData(1, 1, "Home garden: 1 needs water, 1 to check")]
    [InlineData(2, 3, "Home garden: 2 need water, 3 to check")]
    public void TheSummaryTitleCountsWithPluralRulesAndLeavesAnEmptyGroupOut(int needWater, int toCheck, string expected) =>
        Assert.Equal(expected, PushText.English.SummaryTitle("Home garden", needWater, toCheck));

    [Fact]
    public void TheSummaryFooterNamesTheUsersOwnWindow()
    {
        var facts = new[] { new PushEntryFacts(Entry("lot-1"), "Tomatoes") };

        var body = PushText.English.SummaryBody(facts, new NotificationWindow(6 * 60 + 30, 21 * 60), SentAt);

        Assert.EndsWith("\nHeld overnight, 21:00–06:30", body, StringComparison.Ordinal);
    }

    [Fact]
    public void ALongSummaryListsTwelveAlertsAndSaysHowManyMoreSoThatThePayloadStaysSmall()
    {
        var facts = Enumerable.Range(1, 15).Select(index => new PushEntryFacts(Entry($"lot-{index}"), $"Lot {index}")).ToList();

        var lines = PushText.English.SummaryBody(facts, NotificationWindow.Default, SentAt).Split('\n');

        Assert.Equal(PushText.MaxSummaryLines + 2, lines.Length);
        Assert.Equal("and 3 more", lines[^2]);
    }

    [Fact]
    public void ValuesAreWholeWhereTheyAreWholeAndOtherwiseShort()
    {
        var text = PushText.English;
        var temperature = Entry("lot-1", "air_temperature");
        var gas = Entry("lot-1", "gas_resistance", ThresholdSide.High);

        Assert.Equal("Tomatoes temperature below 5.5 °C", text.Title(new PushEntryFacts(temperature, "Tomatoes", Threshold: new PushValue(5.5, "°C")), SentAt));
        Assert.Equal("Tomatoes temperature below 0 °C", text.Title(new PushEntryFacts(temperature, "Tomatoes", Threshold: new PushValue(-0.04, "°C")), SentAt));
        Assert.Equal("Tomatoes gas resistance above 12.3 kΩ", text.Title(new PushEntryFacts(gas, "Tomatoes", Threshold: new PushValue(12.34, "kΩ")), SentAt));
        Assert.Equal("Tomatoes temperature too low", text.Title(new PushEntryFacts(temperature, "Tomatoes"), SentAt));
    }

    [Fact]
    public void TheCollapseIdentityIsStableForTheSameSendAndDiffersByAlertKindAndDueAt()
    {
        var alert = Alert(IPhone);

        Assert.Equal("01d6721c3dfeb91c7bb422ee379e08ec", PushContentBuilder.CollapseIdOf(alert));
        Assert.Equal(PushContentBuilder.CollapseIdOf(alert), PushContentBuilder.CollapseIdOf(Alert(Android)));
        Assert.NotEqual(PushContentBuilder.CollapseIdOf(alert), PushContentBuilder.CollapseIdOf(Reminder(IPhone)));
        Assert.NotEqual(PushContentBuilder.CollapseIdOf(alert), PushContentBuilder.CollapseIdOf(alert with { DueAt = AlertDueAt.AddMinutes(1) }));
        Assert.NotEqual(
            PushContentBuilder.CollapseIdOf(alert),
            PushContentBuilder.CollapseIdOf(alert with { Entries = [Entry(TomatoesLot)] }));

        // A summary is identified by its Site, not by the Alerts it lists.
        var lookups = new FakeLookups();
        Assert.Equal(PushContentBuilder.CollapseIdOf(Summary(lookups)), PushContentBuilder.CollapseIdOf(Summary(lookups)));
    }

    [Fact]
    public async Task BothChannelsShareOneLookupOfTheSameNotification()
    {
        var lookups = TomatoesLookups();
        var builder = Builder(lookups);
        var notification = Alert(IPhone, Android);

        var first = await builder.BuildAsync(notification, SentAt);
        var calls = lookups.Calls;
        var second = await builder.BuildAsync(notification, SentAt);

        Assert.Same(first, second);
        Assert.Equal(calls, lookups.Calls);
    }

    // Story 7.2, the copy matrix. Zurich is UTC+2 on these days: 01:05 there is 23:05 UTC the day before.
    [Fact]
    public async Task ASilentNodeOnALotSaysForHowLongAndSinceWhenInTheUsersZone()
    {
        var lookups = BeansLookups();
        var silent = SilentNode(BeansLot);

        var alert = await Builder(lookups).BuildAsync(Alert(IPhone) with { Entries = [silent] }, DeliveredAt);

        Assert.Equal(
            ("Node on Lot 'Beans' silent for 6 h", "No Reading since 01:05. Check power or range.", BeansLot, (Guid?)silent.AlertId),
            (alert!.Title, alert.Body, alert.LotId, alert.AlertId));

        // The Site's name, the Lot's name and whether the Node ever reported: nothing of a Sensor.
        Assert.Equal(3, lookups.Calls);
    }

    [Fact]
    public async Task ASilentNodeThatNeverReportedNamesNoTime()
    {
        var lookups = BeansLookups();
        lookups.Reported.Clear();

        var alert = await Builder(lookups).BuildAsync(Alert(IPhone) with { Entries = [SilentNode(BeansLot)] }, DeliveredAt);
        var reminder = await Builder(lookups).BuildAsync(Reminder(IPhone) with { Entries = [SilentNode(BeansLot)] }, DeliveredAt.AddDays(1));

        Assert.Equal(("Node on Lot 'Beans' silent for 6 h", "No Reading yet. Check power or range."), (alert!.Title, alert.Body));
        Assert.Equal(("Node on Lot 'Beans' silent for 1 d", "Still no Reading yet. Check power or range."), (reminder!.Title, reminder.Body));
    }

    [Fact]
    public async Task ASilentHubIsNamedByTheFirstFourDigitsOfItsIdAndSaysWhatCannotBeRead()
    {
        var lookups = new FakeLookups();
        var silent = SilentHub(LastHeard);

        var alert = await Builder(lookups).BuildAsync(Alert(IPhone) with { Entries = [silent] }, LastHeard.AddMinutes(5).AddSeconds(59));

        Assert.Equal(("Hub 3F2A silent for 5 min", "Lots behind it can't be read.", null), (alert!.Title, alert.Body, alert.LotId));

        // Only the Site's name: a Hub has no Lot, and its body names no time.
        Assert.Equal(1, lookups.Calls);
    }

    [Fact]
    public async Task ASilentNodeOnNoLotIsNamedByItsId()
    {
        var alert = await Builder(BeansLookups()).BuildAsync(Alert(IPhone) with { Entries = [SilentNode(lotId: null)] }, DeliveredAt);

        Assert.Equal(("Node 9C0D silent for 6 h", "No Reading since 01:05. Check power or range."), (alert!.Title, alert.Body));
    }

    [Fact]
    public async Task AReminderOfASilentDeviceCountsTheDurationAtSendTimeAndSaysStill()
    {
        var lookups = BeansLookups();
        var dayLater = DeliveredAt.AddDays(1);

        var node = await Builder(lookups).BuildAsync(Reminder(IPhone) with { Entries = [SilentNode(BeansLot)] }, dayLater);
        var hub = await Builder(lookups).BuildAsync(Reminder(IPhone) with { Entries = [SilentHub(LastHeard)] }, dayLater);

        Assert.Equal(("Node on Lot 'Beans' silent for 1 d", "Still no Reading since 01:05. Check power or range."), (node!.Title, node.Body));
        Assert.Equal(("Hub 3F2A silent for 1 d", "Lots behind it still can't be read."), (hub!.Title, hub.Body));
    }

    [Fact]
    public async Task ASummaryListsSilentDevicesUnderToCheckWithTheLotFirst()
    {
        var lookups = BeansLookups();
        var summary = Summary(new FakeLookups(), IPhone) with
        {
            Entries = [SilentNode(BeansLot), SilentHub(DeliveredAt.AddMinutes(-5)), SilentNode(lotId: null)],
        };

        var content = await Builder(lookups).BuildAsync(summary, DeliveredAt);

        Assert.Equal("Home garden: 3 to check", content!.Title);
        Assert.Equal(
            ["Beans: Node silent for 6 h", "Hub 3F2A silent for 5 min", "Node 9C0D silent for 6 h", "Held overnight, 22:00–07:00"],
            content.Body!.Split('\n'));

        // A summary has no body per Alert: the Node's Readings are not looked up. The Site and the one Lot are.
        Assert.Equal(2, lookups.Calls);
    }

    [Fact]
    public async Task ASilentNodeWhoseLotIsGoneKeepsItsLineUnderItsOwnName()
    {
        var lookups = new FakeLookups();
        var summary = Summary(lookups, IPhone) with { Entries = [SilentNode(BeansLot)] };
        lookups.Lots.Clear();

        var content = await Builder(lookups).BuildAsync(summary, DeliveredAt);

        Assert.Equal(["Node 9C0D silent for 6 h", "Held overnight, 22:00–07:00"], content!.Body!.Split('\n'));
    }

    [Fact]
    public async Task ASilentDeviceNeverFailsThePushWhenEveryLookupFails()
    {
        var failing = new FakeLookups { Fail = true };

        var node = await Builder(failing).BuildAsync(Alert(IPhone) with { Entries = [SilentNode(BeansLot)] }, DeliveredAt);
        var hub = await Builder(failing).BuildAsync(Alert(IPhone) with { Entries = [SilentHub(DeliveredAt.AddMinutes(-5))] }, DeliveredAt);

        Assert.Equal(("Node 9C0D silent for 6 h", "No Reading since 01:05. Check power or range."), (node!.Title, node.Body));
        Assert.Equal(("Hub 3F2A silent for 5 min", "Lots behind it can't be read."), (hub!.Title, hub.Body));
    }

    // Story 7.3, the copy matrix: a low battery says its level, and the Lot in the body.
    [Fact]
    public async Task ALowBatteryOnALotSaysTheLevelAndNamesTheLotInTheBody()
    {
        var lookups = LettuceLookups();
        var battery = Battery(LettuceLot);

        var alert = await Builder(lookups).BuildAsync(Alert(IPhone) with { Entries = [battery] }, SentAt);

        Assert.Equal(
            ("Node battery 14 %, not charging", "Lot 'Lettuce'.", LettuceLot, (Guid?)battery.AlertId),
            (alert!.Title, alert.Body, alert.LotId, alert.AlertId));

        // The Site's name and the Lot's name: nothing of a Sensor, and no Device lookup.
        Assert.Equal(2, lookups.Calls);
    }

    [Fact]
    public async Task ALowBatteryOnNoLotIsNamedByItsIdAndHasNoBody()
    {
        var lookups = new FakeLookups();

        var alert = await Builder(lookups).BuildAsync(Alert(IPhone) with { Entries = [Battery(lotId: null)] }, SentAt);

        Assert.Equal(("Node 9C0D battery 14 %, not charging", null, null), (alert!.Title, alert.Body, alert.LotId));
        Assert.Equal(1, lookups.Calls);
    }

    [Fact]
    public async Task AReminderOfALowBatteryKeepsTheTitleAndSaysStillLow()
    {
        var dayLater = SentAt.AddDays(1);

        var onLot = await Builder(LettuceLookups()).BuildAsync(Reminder(IPhone) with { Entries = [Battery(LettuceLot)] }, dayLater);
        var onNone = await Builder(LettuceLookups()).BuildAsync(Reminder(IPhone) with { Entries = [Battery(lotId: null)] }, dayLater);

        Assert.Equal(("reminder", "Node battery 14 %, not charging", "Still low. Lot 'Lettuce'."), (onLot!.Kind, onLot.Title, onLot.Body));
        Assert.Equal(("Node 9C0D battery 14 %, not charging", "Still low."), (onNone!.Title, onNone.Body));
    }

    [Fact]
    public async Task ASummaryListsALowBatteryUnderToCheckWithTheLotFirst()
    {
        var lookups = LettuceLookups();
        var summary = Summary(new FakeLookups(), IPhone) with { Entries = [Battery(LettuceLot), Battery(lotId: null)] };

        var content = await Builder(lookups).BuildAsync(summary, SentAt);

        Assert.Equal("Home garden: 2 to check", content!.Title);
        Assert.Equal(
            ["Lettuce: Node battery 14 %, not charging", "Node 9C0D battery 14 %, not charging", "Held overnight, 22:00–07:00"],
            content.Body!.Split('\n'));
    }

    [Fact]
    public async Task ALowBatteryWithoutALevelKeepsTheNeutralText()
    {
        var lookups = LettuceLookups();
        var onLot = Battery(LettuceLot) with { BatteryPercent = null };
        var onNone = Battery(lotId: null) with { BatteryPercent = null };

        var alert = await Builder(lookups).BuildAsync(Alert(IPhone) with { Entries = [onLot] }, SentAt);
        var alone = await Builder(lookups).BuildAsync(Alert(IPhone) with { Entries = [onNone] }, SentAt);
        var reminder = await Builder(lookups).BuildAsync(Reminder(IPhone) with { Entries = [onLot] }, SentAt.AddDays(1));

        Assert.Equal(("Lettuce needs attention", null), (alert!.Title, alert.Body));
        Assert.Equal(("A device needs attention", null), (alone!.Title, alone.Body));
        Assert.Equal(("Lettuce needs attention", "Still open."), (reminder!.Title, reminder.Body));
    }

    [Fact]
    public async Task ALowBatteryWhoseLotIsGoneOrNotFoundKeepsItsLineUnderItsOwnName()
    {
        var gone = new FakeLookups();
        var summary = Summary(gone, IPhone) with { Entries = [Battery(LettuceLot)] };
        gone.Lots.Clear();
        var failing = new FakeLookups { Fail = true };

        var listed = await Builder(gone).BuildAsync(summary, SentAt);
        var alert = await Builder(failing).BuildAsync(Alert(IPhone) with { Entries = [Battery(LettuceLot)] }, SentAt);
        var reminder = await Builder(failing).BuildAsync(Reminder(IPhone) with { Entries = [Battery(LettuceLot)] }, SentAt.AddDays(1));

        Assert.Equal(["Node 9C0D battery 14 %, not charging", "Held overnight, 22:00–07:00"], listed!.Body!.Split('\n'));
        Assert.Equal(("Node 9C0D battery 14 %, not charging", null), (alert!.Title, alert.Body));
        Assert.Equal(("Node 9C0D battery 14 %, not charging", "Still low."), (reminder!.Title, reminder.Body));
    }

    // Story 7.4, the copy matrix: an uncalibrated Sensor names its Lot and says who can calibrate it.
    [Fact]
    public async Task AnUncalibratedSensorNamesItsLotAndSaysWhoCanCalibrateItAndRoutesToTheLot()
    {
        var lookups = PeppersLookups();
        var uncalibrated = Uncalibrated(PeppersLot);

        var alert = await Builder(lookups).BuildAsync(Alert(IPhone) with { Entries = [uncalibrated] }, SentAt);

        Assert.Equal(
            ("Soil sensor on Lot 'Peppers' needs calibration", "An Owner or Administrator can calibrate it.", PeppersLot, (Guid?)uncalibrated.AlertId),
            (alert!.Title, alert.Body, alert.LotId, alert.AlertId));

        // The Site's name and the Lot's name: the Alert has a Sensor, but no Reading and no Threshold is read.
        Assert.Equal(2, lookups.Calls);
    }

    [Fact]
    public async Task AReminderOfAnUncalibratedSensorKeepsTheTitleAndSaysStillNotCalibrated()
    {
        var reminder = await Builder(PeppersLookups()).BuildAsync(Reminder(IPhone) with { Entries = [Uncalibrated(PeppersLot)] }, SentAt.AddDays(1));

        Assert.Equal(
            ("reminder", "Soil sensor on Lot 'Peppers' needs calibration", "Still not calibrated. An Owner or Administrator can calibrate it."),
            (reminder!.Kind, reminder.Title, reminder.Body));
    }

    [Fact]
    public async Task ASummaryListsAnUncalibratedSensorUnderToCheckWithTheLotFirst()
    {
        var lookups = PeppersLookups();
        var summary = Summary(new FakeLookups(), IPhone) with { Entries = [Uncalibrated(PeppersLot)] };

        var content = await Builder(lookups).BuildAsync(summary, SentAt);

        Assert.Equal("Home garden: 1 to check", content!.Title);
        Assert.Equal(["Peppers: soil sensor needs calibration", "Held overnight, 22:00–07:00"], content.Body!.Split('\n'));
    }

    [Fact]
    public async Task AnUncalibratedSensorWhoseLotNameWasNotFoundKeepsTheNeutralTextAndOneWhoseLotIsGoneHasNoSummaryLine()
    {
        var failing = new FakeLookups { Fail = true };
        var gone = new FakeLookups();
        var summary = Summary(gone, IPhone) with { Entries = [Uncalibrated(PeppersLot)] };
        gone.Lots.Clear();

        var alert = await Builder(failing).BuildAsync(Alert(IPhone) with { Entries = [Uncalibrated(PeppersLot)] }, SentAt);
        var reminder = await Builder(failing).BuildAsync(Reminder(IPhone) with { Entries = [Uncalibrated(PeppersLot)] }, SentAt.AddDays(1));

        // Today's neutral text, with the route to the Lot all the same.
        Assert.Equal(("A Lot needs attention", null, PeppersLot), (alert!.Title, alert.Body, alert.LotId));
        Assert.Equal(("A Lot needs attention", "Still open."), (reminder!.Title, reminder.Body));

        // The Alert is about its Lot, like a Threshold Alert: a Lot that is gone has no line in a summary. Sent
        // alone, the Alert and its Reminder keep the neutral text, as for a name that was not found.
        Assert.Null(await Builder(gone).BuildAsync(summary, SentAt));
        var alone = await Builder(gone).BuildAsync(Alert(IPhone) with { Entries = [Uncalibrated(PeppersLot)] }, SentAt);
        var again = await Builder(gone).BuildAsync(Reminder(IPhone) with { Entries = [Uncalibrated(PeppersLot)] }, SentAt.AddDays(1));
        Assert.Equal(("A Lot needs attention", null), (alone!.Title, alone.Body));
        Assert.Equal(("A Lot needs attention", "Still open."), (again!.Title, again.Body));
    }

    [Theory]
    [InlineData(0, "0 min")]
    [InlineData(59, "0 min")]
    [InlineData(12 * 60 + 59, "12 min")]
    [InlineData(59 * 60 + 59, "59 min")]
    [InlineData(60 * 60, "1 h")]
    [InlineData(6 * 60 * 60 + 5 * 60, "6 h")]
    [InlineData(24 * 60 * 60 - 1, "23 h")]
    [InlineData(24 * 60 * 60, "1 d")]
    [InlineData(3 * 24 * 60 * 60 + 23 * 60 * 60, "3 d")]
    [InlineData(-30, "0 min")]
    public void ASilenceDurationIsRoundedDownInMinutesThenHoursThenDays(int seconds, string expected) =>
        Assert.Equal(expected, PushText.English.Duration(LastHeard, LastHeard.AddSeconds(seconds)));

    [Fact]
    public void ADeviceIsNamedShortByItsFirstFourHexDigitsInUpperCase()
    {
        Assert.Equal("3F2A", PushText.ShortName("3f2a9c0d1e4b5a67"));
        Assert.Equal("9C0D", PushText.ShortName(NodeId));
    }

    private const string BeansLot = "0199c1f0-5a00-7000-8000-0000000000b4";

    private const string NodeId = "9c0d1e4b5a673f2a";

    private const string HubId = "3f2a9c0d1e4b5a67";

    // 01:05 in Zurich, when the Node was last heard, and 07:05, when its Alert is delivered.
    private static readonly DateTimeOffset LastHeard = new(2026, 10, 8, 23, 5, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset DeliveredAt = LastHeard.AddHours(6);

    // The Lot "Beans", and its Node, which the Server stored Readings of.
    private static FakeLookups BeansLookups()
    {
        var lookups = new FakeLookups();
        lookups.Lots[BeansLot] = "Beans";
        lookups.Reported.Add(NodeId);

        return lookups;
    }

    private const string LettuceLot = "0199c1f0-5a00-7000-8000-0000000000b5";

    private const string PeppersLot = "0192f3a4-8a00-7c3d-8e4f-5a6b7c8d9e08";

    // The Lot "Lettuce".
    private static FakeLookups LettuceLookups()
    {
        var lookups = new FakeLookups();
        lookups.Lots[LettuceLot] = "Lettuce";

        return lookups;
    }

    // A low-battery Alert of a Node (Story 7.3): the level of the report that opened it, and no silent-since.
    private static NotificationEntry Battery(string? lotId) =>
        new(Guid.NewGuid(), AlertKind.Battery, null, lotId, null, NodeId, null, AlertDueAt.AddMinutes(-30), DeviceKind.Node, BatteryPercent: 14);

    private static FakeLookups PeppersLookups()
    {
        var lookups = new FakeLookups();
        lookups.Lots[PeppersLot] = "Peppers";

        return lookups;
    }

    // An uncalibrated Sensor's Alert (Story 7.4): its Sensor, quantity and Lot, the Device kind of its Node, and
    // no side.
    private static NotificationEntry Uncalibrated(string lotId) =>
        new(Guid.NewGuid(), AlertKind.Uncalibrated, null, lotId, Guid.NewGuid(), NodeId, "soil_moisture", AlertDueAt.AddMinutes(-30), DeviceKind.Node);

    // A Silent Device Alert as the User grain held it before Story 7.2: no side, no Sensor, no quantity, a Lot
    // only for a Node that is on one, and neither the Device kind nor when it was last heard.
    private static NotificationEntry Silent(string? lotId) =>
        new(Guid.NewGuid(), AlertKind.Silent, null, lotId, null, "92064422c012f481", null, AlertDueAt.AddMinutes(-30));

    private static NotificationEntry SilentNode(string? lotId) =>
        new(Guid.NewGuid(), AlertKind.Silent, null, lotId, null, NodeId, null, DeliveredAt, DeviceKind.Node, LastHeard);

    private static NotificationEntry SilentHub(DateTimeOffset silentSince) =>
        new(Guid.NewGuid(), AlertKind.Silent, null, null, null, HubId, null, silentSince.AddMinutes(5), DeviceKind.Hub, silentSince);
}
