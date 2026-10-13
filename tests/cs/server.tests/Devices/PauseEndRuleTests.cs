using Coldframe.Server.Devices;

namespace Coldframe.Server.Tests.Devices;

/// <summary>
/// The end of a Pause (Story 8.1): a calendar date resolves to 00:00 on that date in the time zone of the User
/// who set the Pause, UTC without one, the first instant of the date when its midnight does not exist, and never
/// to an instant that is not after now.
/// </summary>
public sealed class PauseEndRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ADateIsMidnightInTheUsersZone()
    {
        // Zurich is UTC+1 in November and in March before the clocks change.
        Assert.Equal(new DateTimeOffset(2026, 10, 31, 23, 0, 0, TimeSpan.Zero), PauseEndRule.Resolve(new DateOnly(2026, 11, 1), "Europe/Zurich"));
        Assert.Equal(new DateTimeOffset(2027, 2, 28, 23, 0, 0, TimeSpan.Zero), PauseEndRule.Resolve(new DateOnly(2027, 3, 1), "Europe/Zurich"));

        // And UTC+2 in summer.
        Assert.Equal(new DateTimeOffset(2027, 6, 30, 22, 0, 0, TimeSpan.Zero), PauseEndRule.Resolve(new DateOnly(2027, 7, 1), "Europe/Zurich"));

        // West of Greenwich the date starts later.
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 7, 0, 0, TimeSpan.Zero), PauseEndRule.Resolve(new DateOnly(2026, 11, 1), "America/Los_Angeles"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Mars/Olympus_Mons")]
    public void AUserWithoutAKnownZoneGetsUtc(string? timeZone)
    {
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), PauseEndRule.Resolve(new DateOnly(2026, 11, 1), timeZone));
    }

    [Fact]
    public void AMidnightTheClocksSkipIsTheFirstInstantOfThatDate()
    {
        // Cairo went forward at 00:00 on 24 April 2026: 00:00 to 00:59 did not exist, the date began at 01:00 (UTC+3).
        Assert.Equal(new DateTimeOffset(2026, 4, 23, 22, 0, 0, TimeSpan.Zero), PauseEndRule.Resolve(new DateOnly(2026, 4, 24), "Africa/Cairo"));
    }

    [Fact]
    public void AMidnightThatOccursTwiceIsItsFirstOccurrence()
    {
        // Havana goes back at 01:00 on the first Sunday of November to 00:00: midnight occurs twice.
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 4, 0, 0, TimeSpan.Zero), PauseEndRule.Resolve(new DateOnly(2026, 11, 1), "America/Havana"));
    }

    [Fact]
    public void AnEndMustBeAfterNow()
    {
        Assert.True(PauseEndRule.TryResolve(new DateOnly(2026, 10, 14), null, Now, out var tomorrow));
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero), tomorrow);

        // Today's midnight has passed, and so has yesterday's.
        Assert.False(PauseEndRule.TryResolve(new DateOnly(2026, 10, 13), null, Now, out _));
        Assert.False(PauseEndRule.TryResolve(new DateOnly(2026, 10, 12), null, Now, out _));

        // Exactly now is not after now.
        Assert.False(PauseEndRule.TryResolve(new DateOnly(2026, 10, 14), null, new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero), out _));

        // The zone decides: in Auckland 14 October began at 11:00 UTC on the 13th, which has passed.
        Assert.False(PauseEndRule.TryResolve(new DateOnly(2026, 10, 14), "Pacific/Auckland", Now, out _));
        Assert.True(PauseEndRule.TryResolve(new DateOnly(2026, 10, 14), "America/Los_Angeles", Now, out _));
    }

    [Fact]
    public void ADateWhoseMidnightNoInstantHoldsIsRefused()
    {
        Assert.False(PauseEndRule.TryResolve(DateOnly.MinValue, "Pacific/Auckland", Now, out _));
        Assert.False(PauseEndRule.TryResolve(DateOnly.MaxValue, "America/Los_Angeles", DateTimeOffset.MaxValue, out _));
    }

    [Theory]
    [InlineData("2026-11-01", 2026, 11, 1)]
    [InlineData("2027-03-01", 2027, 3, 1)]
    public void ADateIsWrittenAsFourTwoTwoDigits(string text, int year, int month, int day)
    {
        Assert.True(PauseEndRule.TryParseDate(text, out var date));
        Assert.Equal(new DateOnly(year, month, day), date);
    }

    [Theory]
    [InlineData("")]
    [InlineData("2026-11-1")]
    [InlineData("2026-13-01")]
    [InlineData("2026-02-30")]
    [InlineData("01.11.2026")]
    [InlineData("2026-11-01T00:00:00Z")]
    [InlineData(" 2026-11-01")]
    [InlineData("tomorrow")]
    public void AnythingElseIsNoDate(string text)
    {
        Assert.False(PauseEndRule.TryParseDate(text, out _));
    }
}
