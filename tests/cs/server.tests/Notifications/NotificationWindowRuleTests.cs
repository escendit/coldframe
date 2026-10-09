using Coldframe.Contracts.Sites;
using Coldframe.Server.Notifications;

namespace Coldframe.Server.Tests.Notifications;

/// <summary>
/// The Notification Window (Story 6.4) is wall-clock time in the User's zone: its instants follow daylight
/// saving, a start the clocks skip opens at the first valid instant after it, a start that occurs twice opens
/// at its first occurrence, and a User without a zone has a window in UTC.
/// </summary>
public sealed class NotificationWindowRuleTests
{
    private static readonly NotificationWindow Default = NotificationWindow.Default;

    private static readonly TimeZoneInfo Zurich = NotificationWindowRule.ZoneOf("Europe/Zurich");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Mars/Olympus")]
    public void WithoutAKnownZoneTheWindowIsEvaluatedInUtc(string? timeZone)
    {
        var zone = NotificationWindowRule.ZoneOf(timeZone);

        Assert.Equal(TimeSpan.Zero, zone.GetUtcOffset(Utc(2026, 7, 1, 12, 0)));
        Assert.True(NotificationWindowRule.Contains(Default, zone, Utc(2026, 7, 1, 7, 0)));
        Assert.False(NotificationWindowRule.Contains(Default, zone, Utc(2026, 7, 1, 6, 59)));
        Assert.Equal(Utc(2026, 7, 2, 7, 0), NotificationWindowRule.NextOpening(Default, zone, Utc(2026, 7, 1, 23, 30)));
    }

    [Theory]
    [InlineData(6, 59, false)]
    [InlineData(7, 0, true)]
    [InlineData(12, 0, true)]
    [InlineData(21, 59, true)]
    [InlineData(22, 0, false)]
    [InlineData(23, 30, false)]
    [InlineData(0, 0, false)]
    public void TheWindowHoldsItsStartAndNotItsEnd(int hour, int minute, bool inside)
    {
        Assert.Equal(inside, NotificationWindowRule.Contains(Default, TimeZoneInfo.Utc, Utc(2026, 7, 1, hour, minute)));
    }

    [Fact]
    public void TheWindowIsEvaluatedInTheUsersZone()
    {
        // 05:30 UTC is 07:30 in Zurich in summer and 06:30 in winter.
        Assert.True(NotificationWindowRule.Contains(Default, Zurich, Utc(2026, 7, 1, 5, 30)));
        Assert.False(NotificationWindowRule.Contains(Default, Zurich, Utc(2026, 12, 1, 5, 30)));

        // 12:00 UTC is 21:00 in Tokyo and 05:00 in Los Angeles.
        Assert.True(NotificationWindowRule.Contains(Default, NotificationWindowRule.ZoneOf("Asia/Tokyo"), Utc(2026, 7, 1, 12, 0)));
        Assert.False(NotificationWindowRule.Contains(Default, NotificationWindowRule.ZoneOf("America/Los_Angeles"), Utc(2026, 7, 1, 12, 0)));
    }

    [Fact]
    public void TheNextOpeningIsTodayBeforeTheWindowAndTomorrowAfterIt()
    {
        // 02:00 and 23:30 in Zurich in summer (UTC+2).
        Assert.Equal(Utc(2026, 7, 1, 5, 0), NotificationWindowRule.NextOpening(Default, Zurich, Utc(2026, 7, 1, 0, 0)));
        Assert.Equal(Utc(2026, 7, 2, 5, 0), NotificationWindowRule.NextOpening(Default, Zurich, Utc(2026, 7, 1, 21, 30)));

        // The opening itself is an opening.
        Assert.Equal(Utc(2026, 7, 1, 5, 0), NotificationWindowRule.NextOpening(Default, Zurich, Utc(2026, 7, 1, 5, 0)));
        Assert.Equal(TimeSpan.Zero, NotificationWindowRule.NextOpening(Default, Zurich, Utc(2026, 7, 1, 0, 0)).Offset);
    }

    [Fact]
    public void TheOpeningStays0700LocalAcrossTheMarchChange()
    {
        // Held from 23:30 on 27 March 2027 (CET, UTC+1); the clocks go forward at 02:00 on the 28th.
        var opening = NotificationWindowRule.NextOpening(Default, Zurich, Utc(2027, 3, 27, 22, 30));

        Assert.Equal(Utc(2027, 3, 28, 5, 0), opening);
        Assert.Equal(new TimeSpan(7, 0, 0), TimeZoneInfo.ConvertTime(opening, Zurich).TimeOfDay);
        Assert.False(NotificationWindowRule.Contains(Default, Zurich, opening.AddMinutes(-1)));
        Assert.True(NotificationWindowRule.Contains(Default, Zurich, opening));
    }

    [Fact]
    public void TheOpeningStays0700LocalAcrossTheOctoberChange()
    {
        // Held from 23:30 on 24 October 2026 (CEST, UTC+2); the clocks go back at 03:00 on the 25th.
        var opening = NotificationWindowRule.NextOpening(Default, Zurich, Utc(2026, 10, 24, 21, 30));

        Assert.Equal(Utc(2026, 10, 25, 6, 0), opening);
        Assert.Equal(new TimeSpan(7, 0, 0), TimeZoneInfo.ConvertTime(opening, Zurich).TimeOfDay);
        Assert.False(NotificationWindowRule.Contains(Default, Zurich, opening.AddMinutes(-1)));
        Assert.True(NotificationWindowRule.Contains(Default, Zurich, opening));
    }

    [Fact]
    public void AStartTheClocksSkipOpensAtTheFirstValidInstantAfterIt()
    {
        // 02:30 does not exist in Zurich on 28 March 2027: 02:00 CET is followed by 03:00 CEST (01:00 UTC).
        var window = new NotificationWindow((2 * 60) + 30, 5 * 60);

        var opening = NotificationWindowRule.NextOpening(window, Zurich, Utc(2027, 3, 27, 22, 0));

        Assert.Equal(Utc(2027, 3, 28, 1, 0), opening);
        Assert.False(NotificationWindowRule.Contains(window, Zurich, opening.AddMinutes(-1)));
        Assert.True(NotificationWindowRule.Contains(window, Zurich, opening));

        // The day after, 02:30 exists again.
        Assert.Equal(Utc(2027, 3, 29, 0, 30), NotificationWindowRule.NextOpening(window, Zurich, opening.AddHours(12)));
    }

    [Fact]
    public void AStartThatOccursTwiceOpensAtItsFirstOccurrence()
    {
        // 02:30 occurs twice in Zurich on 25 October 2026: at 00:30 UTC (CEST) and at 01:30 UTC (CET).
        var window = new NotificationWindow((2 * 60) + 30, 5 * 60);

        var opening = NotificationWindowRule.NextOpening(window, Zurich, Utc(2026, 10, 24, 22, 0));

        Assert.Equal(Utc(2026, 10, 25, 0, 30), opening);
        Assert.True(NotificationWindowRule.Contains(window, Zurich, opening));

        // Between the two occurrences the wall clock reads 02:00 to 02:30 again: the window opens a second time.
        Assert.False(NotificationWindowRule.Contains(window, Zurich, Utc(2026, 10, 25, 1, 15)));
        Assert.Equal(Utc(2026, 10, 25, 1, 30), NotificationWindowRule.NextOpening(window, Zurich, Utc(2026, 10, 25, 1, 15)));
    }

    [Fact]
    public void TheLastOpeningIsTheOneOfTheWindowThatIsOpenOrWasOpenLast()
    {
        Assert.Equal(Utc(2026, 7, 1, 5, 0), NotificationWindowRule.LastOpening(Default, Zurich, Utc(2026, 7, 1, 5, 0)));
        Assert.Equal(Utc(2026, 7, 1, 5, 0), NotificationWindowRule.LastOpening(Default, Zurich, Utc(2026, 7, 1, 12, 0)));
        Assert.Equal(Utc(2026, 7, 1, 5, 0), NotificationWindowRule.LastOpening(Default, Zurich, Utc(2026, 7, 2, 4, 59)));
        Assert.Equal(Utc(2026, 10, 25, 6, 0), NotificationWindowRule.LastOpening(Default, Zurich, Utc(2026, 10, 25, 9, 0)));
    }

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);
}
