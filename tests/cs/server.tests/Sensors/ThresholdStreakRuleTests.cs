using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Sensors;
using Coldframe.Server.Sensors;

namespace Coldframe.Server.Tests.Sensors;

/// <summary>
/// The streak rule of Threshold evaluation (Story 6.1): three consecutive Readings beyond the same side open an
/// Alert, three consecutive within close it, and any Reading off the running side resets the streak.
/// </summary>
public sealed class ThresholdStreakRuleTests
{
    private const long Low = 30;

    private static readonly StreakState Idle = new(null, ThresholdPosition.Within, 0);

    private static readonly StreakState OpenLow = new(ThresholdSide.Low, ThresholdPosition.Within, 0);

    [Fact]
    public void ExactlyThreeReadingsBelowTheLowOpenALowAlert()
    {
        var steps = Run(Idle, Low, null, 20, 20, 20);

        Assert.Equal([null, null, ThresholdSide.Low], steps.Select(step => step.Opens));
        Assert.All(steps, step => Assert.False(step.Closes));
        Assert.Equal(
            [new StreakState(null, ThresholdPosition.BelowLow, 1), new StreakState(null, ThresholdPosition.BelowLow, 2), OpenLow],
            steps.Select(step => step.Next));
    }

    [Fact]
    public void TwoOfThreeOpensNothing()
    {
        var steps = Run(Idle, Low, null, 20, 20, 40, 20, 20);

        Assert.All(steps, step => Assert.Null(step.Opens));
        Assert.Equal([1, 2, 0, 1, 2], steps.Select(step => step.Next.Count));
        Assert.All(steps, step => Assert.Null(step.Next.Open));
    }

    [Fact]
    public void ExactlyThreeReadingsWithinCloseTheAlert()
    {
        var steps = Run(OpenLow, Low, null, 40, 40, 40);

        Assert.Equal([false, false, true], steps.Select(step => step.Closes));
        Assert.All(steps, step => Assert.Null(step.Opens));
        Assert.Equal(Idle, steps[^1].Next);
    }

    [Fact]
    public void AFlappingLotKeepsItsAlertOpen()
    {
        var steps = Run(OpenLow, Low, null, 40, 40, 20, 40, 40);

        Assert.All(steps, step => Assert.False(step.Closes));
        Assert.All(steps, step => Assert.Equal(ThresholdSide.Low, step.Next.Open));
        Assert.Equal([1, 2, 0, 1, 2], steps.Select(step => step.Next.Count));
    }

    [Fact]
    public void ThreeReadingsOnTheOtherSideCloseTheAlertAndOpenTheNextOnThatSide()
    {
        var steps = Run(OpenLow, Low, 60, 70, 70, 70);

        Assert.Equal([false, false, true], steps.Select(step => step.Closes));
        Assert.Equal([null, null, ThresholdSide.High], steps.Select(step => step.Opens));
        Assert.Equal(new StreakState(ThresholdSide.High, ThresholdPosition.Within, 0), steps[^1].Next);
    }

    [Fact]
    public void AReadingOffTheRunningSideStartsItsOwnStreak()
    {
        // Within, within, then above the high: the closing streak is gone, and the high one is at one.
        var steps = Run(OpenLow, Low, 60, 40, 40, 70, 70);

        Assert.All(steps, step => Assert.False(step.Closes));
        Assert.Equal(new StreakState(ThresholdSide.Low, ThresholdPosition.AboveHigh, 2), steps[^1].Next);

        // Below, below, above, above, above without an Alert: only the last three count, and they open high.
        var switched = Run(Idle, Low, 60, 20, 20, 70, 70, 70);
        Assert.Equal([null, null, null, null, ThresholdSide.High], switched.Select(step => step.Opens));
    }

    [Fact]
    public void ASteadyReadingChangesNothing()
    {
        Assert.Equal(new StreakStep(Idle, false, null), ThresholdStreakRule.Advance(Idle, ThresholdPosition.Within));
        Assert.Equal(new StreakStep(OpenLow, false, null), ThresholdStreakRule.Advance(OpenLow, ThresholdPosition.BelowLow));
    }

    [Theory]
    [InlineData(29, 30L, 60L, ThresholdPosition.BelowLow)]
    [InlineData(30, 30L, 60L, ThresholdPosition.Within)]
    [InlineData(60, 30L, 60L, ThresholdPosition.Within)]
    [InlineData(61, 30L, 60L, ThresholdPosition.AboveHigh)]
    [InlineData(100, 30L, null, ThresholdPosition.Within)]
    [InlineData(0, 30L, null, ThresholdPosition.BelowLow)]
    public void AValueIsBeyondOnlyStrictlyAndAnEmptyHighNeverAlerts(long value, long low, long? high, ThresholdPosition expected)
    {
        Assert.Equal(expected, ThresholdStreakRule.Classify(value, low, high));
    }

    [Fact]
    public void AResetStreakNeedsThreeMore()
    {
        var two = Run(Idle, Low, null, 20, 20)[^1].Next;

        var steps = Run(ThresholdStreakRule.Reset(two), Low, null, 20, 20, 20);

        Assert.Equal(Idle, ThresholdStreakRule.Reset(two));
        Assert.Equal([null, null, ThresholdSide.Low], steps.Select(step => step.Opens));

        // An open Alert stays open through a reset.
        Assert.Equal(OpenLow, ThresholdStreakRule.Reset(new StreakState(ThresholdSide.Low, ThresholdPosition.Within, 2)));
    }

    private static List<StreakStep> Run(StreakState start, long low, long? high, params long[] values)
    {
        var steps = new List<StreakStep>();
        var state = start;

        foreach (var value in values)
        {
            var step = ThresholdStreakRule.Advance(state, ThresholdStreakRule.Classify(value, low, high));
            steps.Add(step);
            state = step.Next;
        }

        return steps;
    }
}
