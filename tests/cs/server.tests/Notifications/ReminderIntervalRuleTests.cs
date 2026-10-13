using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Sites;
using Coldframe.Server.Notifications;

namespace Coldframe.Server.Tests.Notifications;

/// <summary>
/// Reminders (Story 6.4): the interval is the User's own cadence, else the Site's, else daily; a Health Alert
/// never reminds more often than every 24 h; and a Reminder is due one interval after the previous due-at.
/// </summary>
public sealed class ReminderIntervalRuleTests
{
    private static readonly DateTimeOffset OpenedAt = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Day = TimeSpan.FromHours(24);

    [Fact]
    public void DailyIs24HoursAndEvery2DaysIs48()
    {
        Assert.Equal(TimeSpan.FromHours(24), ReminderIntervalRule.Of(ReminderCadence.Daily));
        Assert.Equal(TimeSpan.FromHours(48), ReminderIntervalRule.Of(ReminderCadence.Every2Days));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReminderIntervalRule.Of((ReminderCadence)7));
    }

    [Fact]
    public void TheCadenceIsMyOwnElseTheSitesElseDaily()
    {
        // Own every 2 days over a daily Site; then own unset; then neither.
        Assert.Equal(TimeSpan.FromHours(48), ReminderIntervalRule.Interval(AlertKind.Threshold, ReminderCadence.Every2Days, ReminderCadence.Daily));
        Assert.Equal(TimeSpan.FromHours(24), ReminderIntervalRule.Interval(AlertKind.Threshold, null, ReminderCadence.Daily));
        Assert.Equal(TimeSpan.FromHours(24), ReminderIntervalRule.Interval(AlertKind.Threshold, null, null));

        // And the other way round: my daily over the Site's every 2 days, and the Site's when I set none.
        Assert.Equal(TimeSpan.FromHours(24), ReminderIntervalRule.Interval(AlertKind.Threshold, ReminderCadence.Daily, ReminderCadence.Every2Days));
        Assert.Equal(TimeSpan.FromHours(48), ReminderIntervalRule.Interval(AlertKind.Threshold, null, ReminderCadence.Every2Days));
    }

    // Any kind that is not a Threshold Alert is a Health Alert: a silent Device (Story 7.1), a low battery
    // (Story 7.3), an uncalibrated Sensor (Story 7.4).
    [Theory]
    [InlineData(AlertKind.Silent)]
    [InlineData(AlertKind.Battery)]
    [InlineData(AlertKind.Uncalibrated)]
    public void AHealthAlertNeverRemindsMoreOftenThanEvery24Hours(AlertKind health)
    {
        Assert.True(ReminderIntervalRule.IsHealth(health));
        Assert.False(ReminderIntervalRule.IsHealth(AlertKind.Threshold));

        // A resolved interval below the floor is raised to it; one at or above it is kept.
        Assert.Equal(TimeSpan.FromHours(24), ReminderIntervalRule.Interval(health, TimeSpan.FromHours(6)));
        Assert.Equal(TimeSpan.FromHours(24), ReminderIntervalRule.Interval(health, TimeSpan.FromHours(24)));
        Assert.Equal(TimeSpan.FromHours(48), ReminderIntervalRule.Interval(health, TimeSpan.FromHours(48)));
        Assert.Equal(TimeSpan.FromHours(48), ReminderIntervalRule.Interval(health, ReminderCadence.Every2Days, null));

        // A Threshold Alert has no floor.
        Assert.Equal(TimeSpan.FromHours(6), ReminderIntervalRule.Interval(AlertKind.Threshold, TimeSpan.FromHours(6)));
    }

    [Fact]
    public void AnAlertLearnedByPullIsFirstRemindedAtTheFirstMultipleOfTheIntervalAfterNow()
    {
        Assert.Equal(OpenedAt + Day, ReminderIntervalRule.FirstAfter(OpenedAt, Day, OpenedAt));
        Assert.Equal(OpenedAt + Day, ReminderIntervalRule.FirstAfter(OpenedAt, Day, OpenedAt.AddHours(5)));
        Assert.Equal(OpenedAt + (2 * Day), ReminderIntervalRule.FirstAfter(OpenedAt, Day, OpenedAt + Day));
        Assert.Equal(OpenedAt + (4 * Day), ReminderIntervalRule.FirstAfter(OpenedAt, Day, OpenedAt.AddDays(3).AddMinutes(1)));

        // An Alert stamped ahead of the clock is reminded one interval after it opened.
        Assert.Equal(OpenedAt + Day, ReminderIntervalRule.FirstAfter(OpenedAt, Day, OpenedAt.AddMinutes(-2)));
    }

    [Fact]
    public void OverdueRemindersCollapseIntoTheLastOneSoTheNextIsDueAfterNow()
    {
        var dueAt = OpenedAt + Day;

        Assert.Equal(dueAt, ReminderIntervalRule.LastAtOrBefore(dueAt, Day, dueAt));
        Assert.Equal(dueAt, ReminderIntervalRule.LastAtOrBefore(dueAt, Day, dueAt.AddHours(23)));
        Assert.Equal(dueAt + (3 * Day), ReminderIntervalRule.LastAtOrBefore(dueAt, Day, dueAt.AddDays(3).AddHours(1)));

        // Not overdue: nothing collapses.
        Assert.Equal(dueAt, ReminderIntervalRule.LastAtOrBefore(dueAt, Day, dueAt.AddHours(-1)));
    }
}
