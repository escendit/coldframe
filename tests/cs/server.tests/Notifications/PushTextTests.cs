using Coldframe.Contracts.Alerts;
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
        lookups.Readings[entry.SensorId] = new PushReading(new PushValue(65, "%"), AlertDueAt);
        lookups.Thresholds[(entry.SensorId, ThresholdSide.High)] = new PushValue(60, "%");

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
        lookups.Readings[entry.SensorId] = new PushReading(new PushValue(4, "°C"), new DateTimeOffset(2026, 10, 9, 3, 15, 0, TimeSpan.Zero));
        lookups.Thresholds[(entry.SensorId, ThresholdSide.Low)] = new PushValue(5, "°C");

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
            text.Title(new PushEntryFacts(temperature, "{unit} {threshold}", Threshold: new PushValue(5, "°C"))));
        Assert.Equal("{counts}: 1 needs water", text.SummaryTitle("{counts}", 1, 0));
        Assert.Equal("{lot} needs water", text.Title(new PushEntryFacts(Entry("lot-1"), "{lot}")));
    }

    [Fact]
    public async Task ASummaryWhoseLotsAreAllGoneSaysNothing()
    {
        var lookups = new FakeLookups();
        var summary = Summary(lookups, IPhone);
        lookups.Lots.Clear();

        Assert.Null(await Builder(lookups).BuildAsync(summary, SentAt));
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

        var body = PushText.English.SummaryBody(facts, new NotificationWindow(6 * 60 + 30, 21 * 60));

        Assert.EndsWith("\nHeld overnight, 21:00–06:30", body, StringComparison.Ordinal);
    }

    [Fact]
    public void ALongSummaryListsTwelveAlertsAndSaysHowManyMoreSoThatThePayloadStaysSmall()
    {
        var facts = Enumerable.Range(1, 15).Select(index => new PushEntryFacts(Entry($"lot-{index}"), $"Lot {index}")).ToList();

        var lines = PushText.English.SummaryBody(facts, NotificationWindow.Default).Split('\n');

        Assert.Equal(PushText.MaxSummaryLines + 2, lines.Length);
        Assert.Equal("and 3 more", lines[^2]);
    }

    [Fact]
    public void ValuesAreWholeWhereTheyAreWholeAndOtherwiseShort()
    {
        var text = PushText.English;
        var temperature = Entry("lot-1", "air_temperature");
        var gas = Entry("lot-1", "gas_resistance", ThresholdSide.High);

        Assert.Equal("Tomatoes temperature below 5.5 °C", text.Title(new PushEntryFacts(temperature, "Tomatoes", Threshold: new PushValue(5.5, "°C"))));
        Assert.Equal("Tomatoes temperature below 0 °C", text.Title(new PushEntryFacts(temperature, "Tomatoes", Threshold: new PushValue(-0.04, "°C"))));
        Assert.Equal("Tomatoes gas resistance above 12.3 kΩ", text.Title(new PushEntryFacts(gas, "Tomatoes", Threshold: new PushValue(12.34, "kΩ"))));
        Assert.Equal("Tomatoes temperature too low", text.Title(new PushEntryFacts(temperature, "Tomatoes")));
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
}
