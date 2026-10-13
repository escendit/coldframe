using Coldframe.Server.Devices;

namespace Coldframe.Server.Tests.Devices;

/// <summary>
/// The low-battery rule (Story 7.3): a report is low when its battery is known and below 20 % while not charging,
/// recovered when it is charging or its battery is known and at 20 % or above, and neither otherwise; the Alert
/// opens with the third consecutive low report and closes with the third consecutive recovered one.
/// </summary>
public sealed class BatteryRuleTests
{
    private const string Charging = "charging";

    private const string NotCharging = "not_charging";

    private const string Unknown = "unknown";

    [Theory]
    [InlineData(19, NotCharging, BatteryReport.Low)]
    [InlineData(14, NotCharging, BatteryReport.Low)]
    [InlineData(0, NotCharging, BatteryReport.Low)]
    [InlineData(20, NotCharging, BatteryReport.Recovered)]
    [InlineData(55, NotCharging, BatteryReport.Recovered)]
    [InlineData(10, Charging, BatteryReport.Recovered)]
    [InlineData(100, Charging, BatteryReport.Recovered)]
    [InlineData(null, Charging, BatteryReport.Recovered)]
    [InlineData(20, Unknown, BatteryReport.Recovered)]
    [InlineData(null, NotCharging, BatteryReport.Neither)]
    [InlineData(19, Unknown, BatteryReport.Neither)]
    [InlineData(null, Unknown, BatteryReport.Neither)]
    [InlineData(19, "something-else", BatteryReport.Neither)]
    [InlineData(19, null, BatteryReport.Neither)]
    public void AReportIsLowRecoveredOrNeither(int? batteryPercent, string? charging, BatteryReport expected) =>
        Assert.Equal(expected, BatteryRule.Classify(batteryPercent, charging));

    [Fact]
    public void TheThresholdIsTwentyAndTheCountIsThree()
    {
        Assert.Equal(20, BatteryRule.LowBelowPercent);
        Assert.Equal(3, BatteryRule.Required);
    }

    [Fact]
    public void ExactlyThreeConsecutiveLowReportsOpen()
    {
        var first = BatteryRule.Advance(default, BatteryReport.Low);
        var second = BatteryRule.Advance(first.Next, BatteryReport.Low);
        var third = BatteryRule.Advance(second.Next, BatteryReport.Low);

        Assert.Equal(new BatteryStep(new BatteryStreaks(false, 1, 0), false, false), first);
        Assert.Equal(new BatteryStep(new BatteryStreaks(false, 2, 0), false, false), second);
        Assert.Equal(new BatteryStep(new BatteryStreaks(true, 0, 0), true, false), third);
    }

    [Fact]
    public void ExactlyThreeConsecutiveRecoveredReportsClose()
    {
        var open = new BatteryStreaks(true, 0, 0);

        var first = BatteryRule.Advance(open, BatteryReport.Recovered);
        var second = BatteryRule.Advance(first.Next, BatteryReport.Recovered);
        var third = BatteryRule.Advance(second.Next, BatteryReport.Recovered);

        Assert.Equal(new BatteryStep(new BatteryStreaks(true, 0, 1), false, false), first);
        Assert.Equal(new BatteryStep(new BatteryStreaks(true, 0, 2), false, false), second);
        Assert.Equal(new BatteryStep(new BatteryStreaks(false, 0, 0), false, true), third);
    }

    [Theory]
    [InlineData(BatteryReport.Recovered)]
    [InlineData(BatteryReport.Neither)]
    public void AReportThatIsNotLowEndsALowStreak(BatteryReport report)
    {
        var step = BatteryRule.Advance(new BatteryStreaks(false, 2, 0), report);

        Assert.Equal(new BatteryStep(default, false, false), step);
    }

    [Theory]
    [InlineData(BatteryReport.Low)]
    [InlineData(BatteryReport.Neither)]
    public void AReportThatIsNotRecoveredEndsARecoveryStreak(BatteryReport report)
    {
        var step = BatteryRule.Advance(new BatteryStreaks(true, 0, 2), report);

        Assert.Equal(new BatteryStep(new BatteryStreaks(true, 0, 0), false, false), step);
    }

    [Fact]
    public void AReportThatChangesNothingLeavesTheStreaksAsTheyAre()
    {
        // A healthy Node, and a Node that stays low while its Alert is open: nothing to journal.
        Assert.Equal(default, BatteryRule.Advance(default, BatteryReport.Recovered).Next);
        Assert.Equal(default, BatteryRule.Advance(default, BatteryReport.Neither).Next);
        Assert.Equal(new BatteryStreaks(true, 0, 0), BatteryRule.Advance(new BatteryStreaks(true, 0, 0), BatteryReport.Low).Next);
    }

    // The matrix rows "flapping around 20 %" and "flapping while open", as sequences of levels while not charging.
    [Theory]
    [InlineData(new[] { 19, 14, 14 }, new[] { 2 })]
    [InlineData(new[] { 19, 19, 20, 19, 19, 21, 19, 19, 19 }, new[] { 8 })]
    [InlineData(new[] { 20, 55, 20, 55 }, new int[0])]
    [InlineData(new[] { 19, 19, -1, 19 }, new int[0])]
    [InlineData(new[] { 19, 19, -1, 19, 19, 19 }, new[] { 5 })]
    public void TheAlertOpensOnlyWithTheThirdConsecutiveLowReport(int[] levels, int[] opensAt)
    {
        var streaks = default(BatteryStreaks);
        var opened = new List<int>();

        for (var index = 0; index < levels.Length; index++)
        {
            // -1 stands for a battery that could not be read.
            var step = BatteryRule.Advance(streaks, BatteryRule.Classify(levels[index] < 0 ? null : levels[index], NotCharging));
            streaks = step.Next;

            if (step.Opens)
            {
                opened.Add(index);
            }
        }

        Assert.Equal(opensAt, opened);
    }

    [Fact]
    public void AnOpenAlertThatFlapsClosesOnlyWithTheThirdConsecutiveRecoveredReport()
    {
        var streaks = new BatteryStreaks(true, 0, 0);
        BatteryReport[] reports =
        [
            BatteryReport.Recovered, BatteryReport.Recovered, BatteryReport.Low,
            BatteryReport.Recovered, BatteryReport.Recovered, BatteryReport.Recovered,
        ];
        var closed = new List<int>();

        for (var index = 0; index < reports.Length; index++)
        {
            var step = BatteryRule.Advance(streaks, reports[index]);
            streaks = step.Next;

            if (step.Closes)
            {
                closed.Add(index);
            }
        }

        Assert.Equal([5], closed);
        Assert.Equal(default, streaks);
    }

    [Fact]
    public void AnyNumberOfLowLevelsWhileChargingOpensNothing()
    {
        var streaks = default(BatteryStreaks);

        for (var index = 0; index < 10; index++)
        {
            var step = BatteryRule.Advance(streaks, BatteryRule.Classify(10, Charging));
            Assert.False(step.Opens);
            streaks = step.Next;
        }

        Assert.Equal(default, streaks);
    }
}
