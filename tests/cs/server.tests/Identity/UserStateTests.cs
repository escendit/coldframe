using Coldframe.Contracts.Sites;
using Coldframe.Server.Identity;

namespace Coldframe.Server.Tests.Identity;

/// <summary>
/// An idempotency key holds its request for 24 h after the request once completed; a pending request
/// holds it until it completes, because its Organization may exist. The Site set follows the Site grains.
/// </summary>
public sealed class UserStateTests
{
    private static readonly DateTimeOffset RequestedAt = new(2026, 5, 1, 6, 30, 0, TimeSpan.Zero);

    [Fact]
    public void ACompletedKeyHoldsForJustUnder24Hours()
    {
        var state = Completed();

        Assert.NotNull(state.FindLive("k1", RequestedAt + TimeSpan.FromHours(24) - TimeSpan.FromTicks(1)));
        Assert.Null(state.FindLive("k1", RequestedAt + TimeSpan.FromHours(24)));
    }

    [Fact]
    public void APendingKeyDoesNotExpire()
    {
        var state = new UserState();
        state.Apply(new SiteCreationRequested("k1", "site-1", "Home", RequestedAt));

        Assert.Equal("site-1", state.FindLive("k1", RequestedAt + TimeSpan.FromDays(3))?.SiteId);
    }

    [Fact]
    public void AKeyUsedAgainAfterItExpiredHoldsTheNewerRequest()
    {
        var state = Completed();
        state.Apply(new SiteCreationRequested("k1", "site-2", "Home", RequestedAt + TimeSpan.FromHours(25)));

        var creation = state.FindLive("k1", RequestedAt + TimeSpan.FromHours(25));
        Assert.Equal("site-2", creation?.SiteId);
        Assert.False(creation?.Completed);
    }

    [Fact]
    public void ACompletionForAnotherSiteChangesNothing()
    {
        var state = new UserState();
        state.Apply(new SiteCreationRequested("k1", "site-2", "Home", RequestedAt));
        state.Apply(new SiteCreationCompleted("k1", "site-1"));

        Assert.False(state.SiteCreations["k1"].Completed);
    }

    [Fact]
    public void TheSiteSetFollowsTheMembershipChanges()
    {
        var state = new UserState();

        state.Apply(new SiteMembershipChanged("site-1", SiteRole.Member));
        state.Apply(new SiteMembershipChanged("site-2", SiteRole.Owner));
        state.Apply(new SiteMembershipChanged("site-1", SiteRole.Administrator));
        state.Apply(new SiteMembershipChanged("site-2", null));

        Assert.Equal(new Dictionary<string, SiteRole> { ["site-1"] = SiteRole.Administrator }, state.Sites);
    }

    [Fact]
    public void WithoutAnyEventTheWindowIs0700To2200AndThereIsNoTimeZone()
    {
        var state = new UserState();

        Assert.Equal(new NotificationWindow(420, 1320), state.NotificationWindow);
        Assert.Equal(NotificationWindow.Default, state.NotificationWindow);
        Assert.Null(state.TimeZone);
        Assert.Null(state.ChosenTimeZone);
        Assert.Empty(state.SiteNotifications);
        Assert.Equal(new UserSiteNotifications(false, null, null), state.SiteNotificationsOf("site-1"));
        Assert.Equal(ReminderCadence.Daily, state.SiteNotificationsOf("site-1").ResolvedReminderCadence);
    }

    [Fact]
    public void TheWindowFollowsItsLastChange()
    {
        var state = new UserState();

        state.Apply(new NotificationWindowChanged(390, 1320, RequestedAt));
        state.Apply(new NotificationWindowChanged(480, 1260, RequestedAt));

        Assert.Equal(new NotificationWindow(480, 1260), state.NotificationWindow);
    }

    [Fact]
    public void ADetectedZoneIsTheZoneUntilOneIsChosenAndNeverReplacesTheChosenOne()
    {
        var state = new UserState();

        state.Apply(new TimeZoneDetected("Europe/Zurich", RequestedAt));
        Assert.Equal(("Europe/Zurich", null), (state.TimeZone, state.ChosenTimeZone));

        state.Apply(new TimeZoneChosen("Europe/Vienna", RequestedAt));
        Assert.Equal(("Europe/Vienna", "Europe/Vienna"), (state.TimeZone, state.ChosenTimeZone));

        // A detection journaled by an older Server, or replayed out of intent, still cannot win.
        state.Apply(new TimeZoneDetected("America/New_York", RequestedAt));
        Assert.Equal("Europe/Vienna", state.TimeZone);
    }

    [Fact]
    public void MuteAndCadenceArePerSite()
    {
        var state = new UserState();

        state.Apply(new SiteMuteChanged("site-1", true, RequestedAt));
        state.Apply(new PersonalReminderCadenceChanged("site-2", ReminderCadence.Every2Days, RequestedAt));

        Assert.Equal(new UserSiteNotifications(Muted: true), state.SiteNotificationsOf("site-1"));
        Assert.Equal(new UserSiteNotifications(ReminderCadence: ReminderCadence.Every2Days), state.SiteNotificationsOf("site-2"));
        Assert.Equal(UserSiteNotifications.None, state.SiteNotificationsOf("site-3"));
    }

    [Fact]
    public void TheCadenceResolvesAsMyOwnThenTheSitesThenDaily()
    {
        var state = new UserState();
        Assert.Equal(ReminderCadence.Daily, state.SiteNotificationsOf("site-1").ResolvedReminderCadence);

        state.Apply(new SiteReminderCadenceSynced("site-1", ReminderCadence.Every2Days));
        Assert.Equal(ReminderCadence.Every2Days, state.SiteNotificationsOf("site-1").ResolvedReminderCadence);

        state.Apply(new PersonalReminderCadenceChanged("site-1", ReminderCadence.Daily, RequestedAt));
        Assert.Equal(ReminderCadence.Daily, state.SiteNotificationsOf("site-1").ResolvedReminderCadence);

        state.Apply(new PersonalReminderCadenceChanged("site-1", null, RequestedAt));
        Assert.Equal(ReminderCadence.Every2Days, state.SiteNotificationsOf("site-1").ResolvedReminderCadence);
    }

    [Fact]
    public void ASiteBackAtItsDefaultsKeepsNoEntry()
    {
        var state = new UserState();

        state.Apply(new SiteMuteChanged("site-1", true, RequestedAt));
        state.Apply(new SiteMuteChanged("site-1", false, RequestedAt));

        Assert.Empty(state.SiteNotifications);
    }

    [Fact]
    public void WhenTheMembershipEndsTheMuteTheCadenceAndTheSitesCadenceAreGone()
    {
        var state = new UserState();
        state.Apply(new SiteMembershipChanged("site-1", SiteRole.Member));
        state.Apply(new SiteMembershipChanged("site-2", SiteRole.Member));
        state.Apply(new SiteMuteChanged("site-1", true, RequestedAt));
        state.Apply(new PersonalReminderCadenceChanged("site-1", ReminderCadence.Every2Days, RequestedAt));
        state.Apply(new SiteReminderCadenceSynced("site-1", ReminderCadence.Every2Days));
        state.Apply(new SiteMuteChanged("site-2", true, RequestedAt));

        state.Apply(new SiteMembershipChanged("site-1", null));

        Assert.Equal(UserSiteNotifications.None, state.SiteNotificationsOf("site-1"));
        Assert.Equal(["site-2"], state.SiteNotifications.Keys);

        // A changed Role is no end of the Membership.
        state.Apply(new SiteMembershipChanged("site-2", SiteRole.Administrator));
        Assert.True(state.SiteNotificationsOf("site-2").Muted);
    }

    private static UserState Completed()
    {
        var state = new UserState();
        state.Apply(new SiteCreationRequested("k1", "site-1", "Home", RequestedAt));
        state.Apply(new SiteCreationCompleted("k1", "site-1"));
        return state;
    }
}
