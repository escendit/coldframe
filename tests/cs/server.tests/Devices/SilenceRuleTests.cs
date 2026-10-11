using Coldframe.Contracts.Devices;
using Coldframe.Server.Devices;

namespace Coldframe.Server.Tests.Devices;

/// <summary>
/// The Silence Window (Story 7.1): 6 h for a Node and 5 min for a Hub unless the Device has its own, 1 min to
/// 30 days, and expired from the clock start plus the window on, not a second before.
/// </summary>
public sealed class SilenceRuleTests
{
    private static readonly DateTimeOffset T = new(2026, 10, 11, 1, 5, 0, TimeSpan.Zero);

    [Fact]
    public void ANodeHasSixHoursAndAHubFiveMinutes()
    {
        Assert.Equal(TimeSpan.FromHours(6), SilenceRule.DefaultWindow(DeviceKind.Node));
        Assert.Equal(TimeSpan.FromMinutes(5), SilenceRule.DefaultWindow(DeviceKind.Hub));
        Assert.Throws<ArgumentOutOfRangeException>(() => SilenceRule.DefaultWindow((DeviceKind)7));
    }

    [Fact]
    public void AnOverrideReplacesTheDefaultAndWithoutOneTheDefaultIsInForce()
    {
        Assert.Equal(TimeSpan.FromHours(8), SilenceRule.Window(DeviceKind.Node, TimeSpan.FromHours(8)));
        Assert.Equal(TimeSpan.FromMinutes(1), SilenceRule.Window(DeviceKind.Hub, TimeSpan.FromMinutes(1)));
        Assert.Equal(SilenceRule.NodeWindow, SilenceRule.Window(DeviceKind.Node, null));
        Assert.Equal(SilenceRule.HubWindow, SilenceRule.Window(DeviceKind.Hub, null));
    }

    [Theory]
    [InlineData(60, true)]
    [InlineData(61, true)]
    [InlineData(28_800, true)]
    [InlineData(2_592_000, true)]
    [InlineData(59, false)]
    [InlineData(0, false)]
    [InlineData(-60, false)]
    [InlineData(2_592_001, false)]
    public void AWindowIsOneMinuteToThirtyDays(int seconds, bool valid)
    {
        Assert.Equal(valid, SilenceRule.IsValid(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void AWindowIsInWholeSeconds()
    {
        Assert.False(SilenceRule.IsValid(TimeSpan.FromMilliseconds(60_500)));
        Assert.Equal(SilenceRule.WakePeriod, SilenceRule.MinWindow);
    }

    [Fact]
    public void TheWindowExpiresAtTheClockStartPlusTheWindowAndNotASecondBefore()
    {
        var dueAt = SilenceRule.DueAt(T, SilenceRule.NodeWindow);

        Assert.Equal(T.AddHours(6), dueAt);
        Assert.False(SilenceRule.IsOverdue(dueAt, dueAt.AddSeconds(-1)));
        Assert.True(SilenceRule.IsOverdue(dueAt, dueAt));
        Assert.True(SilenceRule.IsOverdue(dueAt, dueAt.AddHours(1)));
    }

    [Fact]
    public void TheClockNeverRunsBackwards()
    {
        Assert.Equal(T, SilenceRule.Latest(null, T));
        Assert.Equal(T.AddHours(1), SilenceRule.Latest(T, T.AddHours(1)));
        Assert.Equal(T, SilenceRule.Latest(T, T.AddHours(-1)));
        Assert.Equal(T, SilenceRule.Latest(T, T));
    }
}
