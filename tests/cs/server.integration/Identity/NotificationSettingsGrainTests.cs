using Coldframe.Contracts.Sites;
using Coldframe.Server.Notifications;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coldframe.Server.IntegrationTests.Identity;

/// <summary>
/// Notification settings on a TestCluster with a fake clock (Story 6.3): the User grain owns the Notification
/// Window, the time zone and, per Site, the mute and the User's own Reminder cadence; the Site grain owns the
/// Site's cadence, which is handed to every member's User grain. Every accepted change is one event, an
/// unchanged value journals nothing, and a silo restart loses nothing.
/// </summary>
public sealed class NotificationSettingsGrainTests(IdentityCluster identity) : IClassFixture<IdentityCluster>
{
    private const string WindowChanged = "user.notification-window-changed";
    private const string ZoneDetected = "user.time-zone-detected";
    private const string ZoneChosen = "user.time-zone-chosen";
    private const string MuteChanged = "user.site-mute-changed";
    private const string CadenceChanged = "user.site-reminder-cadence-changed";
    private const string CadenceSynced = "user.site-reminder-cadence-synced";
    private const string MembershipChanged = "user.site-membership-changed";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ANewUserHasTheDefaultWindowNoTimeZoneAndNoEvent()
    {
        var userId = NewUserId();

        var settings = await identity.User(userId).GetNotificationSettings(Ct);
        var site = await identity.User(userId).GetSiteNotificationSettings("site-a", Ct);

        Assert.Equal(new UserNotificationSettings(new NotificationWindow(420, 1320), null, false), settings);
        Assert.Equal(new UserSiteNotificationSettings(false, null, ReminderCadence.Daily), site);
        Assert.Empty(await Aliases(userId));
    }

    [Fact]
    public async Task AWindowChangeIsOneEventWithTheClocksTimeAndTheSameWindowAgainJournalsNothing()
    {
        var userId = NewUserId();
        var window = new NotificationWindow(390, 1320);

        var changed = await identity.User(userId).UpdateNotificationSettings(new UpdateNotificationSettings(Window: window), Ct);
        var again = await identity.User(userId).UpdateNotificationSettings(new UpdateNotificationSettings(Window: window), Ct);

        Assert.Equal((NotificationSettingsOutcome.Changed, window), (changed.Outcome, changed.Settings.Window));
        Assert.Equal((NotificationSettingsOutcome.Unchanged, window), (again.Outcome, again.Settings.Window));
        var events = await identity.Store.ReadStreamAsync($"user/{userId}", Ct);
        Assert.Equal(new NotificationWindowChanged(390, 1320, identity.Time.GetUtcNow()), Assert.Single(events).Data);
    }

    [Fact]
    public async Task TheDefaultWindowSentToANewUserJournalsNothing()
    {
        var userId = NewUserId();

        var result = await identity.User(userId).UpdateNotificationSettings(new UpdateNotificationSettings(Window: NotificationWindow.Default), Ct);

        Assert.Equal(NotificationSettingsOutcome.Unchanged, result.Outcome);
        Assert.Empty(await Aliases(userId));
    }

    [Theory]
    [InlineData(420, 420)]
    [InlineData(1320, 420)]
    [InlineData(-1, 420)]
    [InlineData(420, 1440)]
    public async Task AWindowThatDoesNotOpenBeforeItClosesIsRefusedAndNothingIsJournaled(int from, int to)
    {
        var userId = NewUserId();

        // A valid zone in the same request is not journaled either: a refusal changes nothing.
        var result = await identity.User(userId).UpdateNotificationSettings(
            new UpdateNotificationSettings(new NotificationWindow(from, to), TimeZone: "Europe/Zurich"),
            Ct);

        Assert.Equal(NotificationSettingsOutcome.InvalidWindow, result.Outcome);
        Assert.Equal(new UserNotificationSettings(NotificationWindow.Default, null, false), result.Settings);
        Assert.Empty(await Aliases(userId));
    }

    [Fact]
    public async Task AnEmptyOrOverlongZoneIsRefusedAndNothingIsJournaled()
    {
        var userId = NewUserId();

        var empty = await identity.User(userId).UpdateNotificationSettings(new UpdateNotificationSettings(TimeZone: string.Empty), Ct);
        var tooLong = await identity.User(userId).UpdateNotificationSettings(new UpdateNotificationSettings(DetectedTimeZone: new string('Z', 65)), Ct);

        Assert.Equal(NotificationSettingsOutcome.InvalidTimeZone, empty.Outcome);
        Assert.Equal(NotificationSettingsOutcome.InvalidTimeZone, tooLong.Outcome);
        Assert.Empty(await Aliases(userId));
    }

    [Fact]
    public async Task ADetectedZoneIsStoredUnconfirmedWhileNoneIsChosen()
    {
        var userId = NewUserId();

        var detected = await identity.User(userId).UpdateNotificationSettings(new UpdateNotificationSettings(DetectedTimeZone: "Europe/Zurich"), Ct);
        var again = await identity.User(userId).UpdateNotificationSettings(new UpdateNotificationSettings(DetectedTimeZone: "Europe/Zurich"), Ct);

        Assert.Equal(("Europe/Zurich", false), (detected.Settings.TimeZone, detected.Settings.TimeZoneConfirmed));
        Assert.Equal(NotificationSettingsOutcome.Unchanged, again.Outcome);
        Assert.Equal([ZoneDetected], await Aliases(userId));

        // Another device in another zone: still only a proposal, the newest one.
        var moved = await identity.User(userId).UpdateNotificationSettings(new UpdateNotificationSettings(DetectedTimeZone: "Europe/Lisbon"), Ct);
        Assert.Equal(("Europe/Lisbon", false), (moved.Settings.TimeZone, moved.Settings.TimeZoneConfirmed));
        Assert.Equal([ZoneDetected, ZoneDetected], await Aliases(userId));
    }

    [Fact]
    public async Task AChosenZoneWinsIsConfirmedAndIsNeverReplacedByADetectedOne()
    {
        var userId = NewUserId();
        await identity.User(userId).UpdateNotificationSettings(new UpdateNotificationSettings(DetectedTimeZone: "Europe/Zurich"), Ct);

        // Confirming the detected zone is a choice, and an event of its own.
        var confirmed = await identity.User(userId).UpdateNotificationSettings(new UpdateNotificationSettings(TimeZone: "Europe/Zurich"), Ct);
        Assert.Equal(("Europe/Zurich", true), (confirmed.Settings.TimeZone, confirmed.Settings.TimeZoneConfirmed));

        var detectedLater = await identity.User(userId).UpdateNotificationSettings(new UpdateNotificationSettings(DetectedTimeZone: "America/New_York"), Ct);
        Assert.Equal(NotificationSettingsOutcome.Unchanged, detectedLater.Outcome);
        Assert.Equal(("Europe/Zurich", true), (detectedLater.Settings.TimeZone, detectedLater.Settings.TimeZoneConfirmed));
        Assert.Equal([ZoneDetected, ZoneChosen], await Aliases(userId));

        // The User changes their mind; a detection sent along is ignored.
        var changed = await identity.User(userId).UpdateNotificationSettings(
            new UpdateNotificationSettings(TimeZone: "Europe/Vienna", DetectedTimeZone: "America/New_York"),
            Ct);
        Assert.Equal(("Europe/Vienna", true), (changed.Settings.TimeZone, changed.Settings.TimeZoneConfirmed));
        Assert.Equal([ZoneDetected, ZoneChosen, ZoneChosen], await Aliases(userId));
        var events = await identity.Store.ReadStreamAsync($"user/{userId}", Ct);
        Assert.Equal(new TimeZoneChosen("Europe/Vienna", identity.Time.GetUtcNow()), events[^1].Data);
    }

    [Fact]
    public async Task OneRequestMayChangeTheWindowAndTheZoneEachAsItsOwnEvent()
    {
        var userId = NewUserId();

        var result = await identity.User(userId).UpdateNotificationSettings(
            new UpdateNotificationSettings(new NotificationWindow(480, 1200), TimeZone: "Europe/Zurich"),
            Ct);

        Assert.Equal(new UserNotificationSettings(new NotificationWindow(480, 1200), "Europe/Zurich", true), result.Settings);
        Assert.Equal([WindowChanged, ZoneChosen], await Aliases(userId));
    }

    [Fact]
    public async Task MutingASiteAffectsOnlyMeAndOnlyThatSite()
    {
        var (a, b) = (NewUserId(), NewUserId());

        var muted = await identity.User(a).SetSiteNotificationSettings("site-s", muted: true, reminderCadence: null, Ct);
        var again = await identity.User(a).SetSiteNotificationSettings("site-s", muted: true, reminderCadence: null, Ct);

        Assert.True(muted.Muted);
        Assert.True(again.Muted);
        Assert.False((await identity.User(b).GetSiteNotificationSettings("site-s", Ct)).Muted);
        Assert.False((await identity.User(a).GetSiteNotificationSettings("site-t", Ct)).Muted);
        Assert.Equal([MuteChanged], await Aliases(a));
        Assert.Empty(await Aliases(b));
        var events = await identity.Store.ReadStreamAsync($"user/{a}", Ct);
        Assert.Equal(new SiteMuteChanged("site-s", true, identity.Time.GetUtcNow()), events[0].Data);

        var unmuted = await identity.User(a).SetSiteNotificationSettings("site-s", muted: false, reminderCadence: null, Ct);
        Assert.False(unmuted.Muted);
        Assert.Equal([MuteChanged, MuteChanged], await Aliases(a));
    }

    [Fact]
    public async Task MyCadenceIsSetAndTakenBackEachAsOneEvent()
    {
        var userId = NewUserId();

        var own = await identity.User(userId).SetSiteNotificationSettings("site-s", false, ReminderCadence.Every2Days, Ct);
        var same = await identity.User(userId).SetSiteNotificationSettings("site-s", false, ReminderCadence.Every2Days, Ct);
        var back = await identity.User(userId).SetSiteNotificationSettings("site-s", false, null, Ct);

        Assert.Equal(new UserSiteNotificationSettings(false, ReminderCadence.Every2Days, ReminderCadence.Every2Days), own);
        Assert.Equal(own, same);
        Assert.Equal(new UserSiteNotificationSettings(false, null, ReminderCadence.Daily), back);
        Assert.Equal([CadenceChanged, CadenceChanged], await Aliases(userId));
    }

    [Fact]
    public async Task AMuteAndACadenceInOneRequestAreTwoEvents()
    {
        var userId = NewUserId();

        var both = await identity.User(userId).SetSiteNotificationSettings("site-s", true, ReminderCadence.Every2Days, Ct);

        Assert.Equal(new UserSiteNotificationSettings(true, ReminderCadence.Every2Days, ReminderCadence.Every2Days), both);
        Assert.Equal([MuteChanged, CadenceChanged], await Aliases(userId));
    }

    [Fact]
    public async Task TheCadenceResolvesAsMyOwnElseTheSitesElseDaily()
    {
        var userId = NewUserId();
        var user = identity.User(userId);
        Assert.Equal(ReminderCadence.Daily, (await user.GetSiteNotificationSettings("site-s", Ct)).ResolvedReminderCadence);

        // Daily is what a Site reminds at anyway: handing it to a User who never heard of one journals nothing.
        await user.SyncSiteReminderCadence("site-s", ReminderCadence.Daily, Ct);
        Assert.Empty(await Aliases(userId));

        await user.SyncSiteReminderCadence("site-s", ReminderCadence.Every2Days, Ct);
        await user.SyncSiteReminderCadence("site-s", ReminderCadence.Every2Days, Ct);
        Assert.Equal(new UserSiteNotificationSettings(false, null, ReminderCadence.Every2Days), await user.GetSiteNotificationSettings("site-s", Ct));
        Assert.Equal([CadenceSynced], await Aliases(userId));

        await user.SetSiteNotificationSettings("site-s", false, ReminderCadence.Daily, Ct);
        Assert.Equal(new UserSiteNotificationSettings(false, ReminderCadence.Daily, ReminderCadence.Daily), await user.GetSiteNotificationSettings("site-s", Ct));

        // Back to the Site setting, and the Site goes back to daily.
        await user.SetSiteNotificationSettings("site-s", false, null, Ct);
        Assert.Equal(ReminderCadence.Every2Days, (await user.GetSiteNotificationSettings("site-s", Ct)).ResolvedReminderCadence);
        await user.SyncSiteReminderCadence("site-s", ReminderCadence.Daily, Ct);
        Assert.Equal(ReminderCadence.Daily, (await user.GetSiteNotificationSettings("site-s", Ct)).ResolvedReminderCadence);
        Assert.Equal([CadenceSynced, CadenceChanged, CadenceChanged, CadenceSynced], await Aliases(userId));
    }

    [Fact]
    public async Task WhenTheMembershipEndsTheSitesSettingsAreGone()
    {
        var userId = NewUserId();
        var user = identity.User(userId);
        await user.SyncSiteMembership("site-s", SiteRole.Member, Ct);
        await user.SyncSiteMembership("site-t", SiteRole.Member, Ct);
        await user.SetSiteNotificationSettings("site-s", true, ReminderCadence.Every2Days, Ct);
        await user.SyncSiteReminderCadence("site-s", ReminderCadence.Every2Days, Ct);
        await user.SetSiteNotificationSettings("site-t", true, null, Ct);

        await user.SyncSiteMembership("site-s", null, Ct);

        Assert.Equal(new UserSiteNotificationSettings(false, null, ReminderCadence.Daily), await user.GetSiteNotificationSettings("site-s", Ct));
        Assert.True((await user.GetSiteNotificationSettings("site-t", Ct)).Muted);

        // And after the grain replays its journal.
        await identity.RestartSiloAsync();
        Assert.Equal(new UserSiteNotificationSettings(false, null, ReminderCadence.Daily), await identity.User(userId).GetSiteNotificationSettings("site-s", Ct));
        Assert.True((await identity.User(userId).GetSiteNotificationSettings("site-t", Ct)).Muted);
    }

    [Fact]
    public async Task SettingsForASiteTheGrainHoldsNoMembershipOfAreAcceptedAndDroppedWhenItIsToldTheMembershipEnded()
    {
        // The Edge policy reads the Membership from the projection; the User grain may not have heard of it.
        var userId = NewUserId();
        var user = identity.User(userId);

        var muted = await user.SetSiteNotificationSettings("site-s", true, null, Ct);
        Assert.True(muted.Muted);

        await user.SyncSiteMembership("site-s", null, Ct);

        Assert.False((await user.GetSiteNotificationSettings("site-s", Ct)).Muted);
        Assert.Equal([MuteChanged, MembershipChanged], await Aliases(userId));

        // Nothing is left to drop: the same call again journals nothing.
        await user.SyncSiteMembership("site-s", null, Ct);
        Assert.Equal(2, (await Aliases(userId)).Count);
    }

    [Fact]
    public async Task EverySettingSurvivesASiloRestart()
    {
        var userId = NewUserId();
        var (siteId, ownerId) = await CreateSiteAsync();
        await identity.User(userId).UpdateNotificationSettings(
            new UpdateNotificationSettings(new NotificationWindow(390, 1260), TimeZone: "Europe/Vienna"),
            Ct);
        await identity.User(userId).SetSiteNotificationSettings(siteId, true, ReminderCadence.Every2Days, Ct);
        await identity.User(ownerId).SyncSiteReminderCadence(siteId, ReminderCadence.Every2Days, Ct);
        await identity.Site(siteId).SetReminderCadence(ReminderCadence.Every2Days, Ct);

        await identity.RestartSiloAsync();

        Assert.Equal(
            new UserNotificationSettings(new NotificationWindow(390, 1260), "Europe/Vienna", true),
            await identity.User(userId).GetNotificationSettings(Ct));
        Assert.Equal(
            new UserSiteNotificationSettings(true, ReminderCadence.Every2Days, ReminderCadence.Every2Days),
            await identity.User(userId).GetSiteNotificationSettings(siteId, Ct));
        Assert.Equal(ReminderCadence.Every2Days, (await identity.User(ownerId).GetSiteNotificationSettings(siteId, Ct)).ResolvedReminderCadence);
        Assert.Equal(ReminderCadence.Every2Days, await identity.Site(siteId).GetReminderCadence(Ct));
    }

    [Fact]
    public async Task ASiteRemindsDailyUntilItsCadenceIsChangedOnceAndTheSameCadenceJournalsNothing()
    {
        var (siteId, ownerId) = await CreateSiteAsync();
        Assert.Equal(ReminderCadence.Daily, await identity.Site(siteId).GetReminderCadence(Ct));

        var daily = await identity.Site(siteId).SetReminderCadence(ReminderCadence.Daily, Ct);
        var changed = await identity.Site(siteId).SetReminderCadence(ReminderCadence.Every2Days, Ct);
        var again = await identity.Site(siteId).SetReminderCadence(ReminderCadence.Every2Days, Ct);

        Assert.Equal(SiteReminderCadenceOutcome.Unchanged, daily.Outcome);
        Assert.Equal((SiteReminderCadenceOutcome.Changed, ReminderCadence.Every2Days), (changed.Outcome, changed.Cadence));
        Assert.Equal((SiteReminderCadenceOutcome.Unchanged, ReminderCadence.Every2Days), (again.Outcome, again.Cadence));

        // The members are named every time, also when nothing changed, so the caller can finish a fan-out.
        Assert.Equal([ownerId], daily.Members);
        Assert.Equal([ownerId], again.Members);
        Assert.Equal(["site.created", "site.membership-granted", "site.reminder-cadence-changed"], await identity.AliasesAsync($"site/{siteId}"));
        var events = await identity.Store.ReadStreamAsync($"site/{siteId}", Ct);
        Assert.Equal(new SiteReminderCadenceChanged(ReminderCadence.Every2Days, identity.Time.GetUtcNow()), events[^1].Data);
    }

    [Fact]
    public async Task ASiteThatWasNeverCreatedHasNoCadenceAndTakesNone()
    {
        var siteId = Guid.CreateVersion7().ToString();

        var result = await identity.Site(siteId).SetReminderCadence(ReminderCadence.Every2Days, Ct);
        var delivery = await SiteReminderCadenceFanOut.SetAsync(identity.Cluster.GrainFactory, siteId, ReminderCadence.Every2Days, NullLogger.Instance, Ct);

        Assert.Equal(SiteReminderCadenceOutcome.NotFound, result.Outcome);
        Assert.Empty(result.Members);
        Assert.Equal(SiteReminderCadenceDelivery.SiteNotFound, delivery.Delivery);
        Assert.Null(await identity.Site(siteId).GetReminderCadence(Ct));
        Assert.Empty(await identity.AliasesAsync($"site/{siteId}"));
    }

    [Fact]
    public async Task TheSitesCadenceReachesEveryMemberAndAMemberWithTheirOwnKeepsIt()
    {
        var (siteId, ownerId) = await CreateSiteAsync();
        var (member, own) = (NewUserId(), NewUserId());
        await AddMemberAsync(siteId, member);
        await AddMemberAsync(siteId, own);
        await identity.User(own).SetSiteNotificationSettings(siteId, false, ReminderCadence.Daily, Ct);
        var bystander = NewUserId();

        var result = await SiteReminderCadenceFanOut.SetAsync(identity.Cluster.GrainFactory, siteId, ReminderCadence.Every2Days, NullLogger.Instance, Ct);

        Assert.Equal((SiteReminderCadenceDelivery.Delivered, ReminderCadence.Every2Days), result);
        Assert.Equal(ReminderCadence.Every2Days, (await identity.User(ownerId).GetSiteNotificationSettings(siteId, Ct)).ResolvedReminderCadence);
        Assert.Equal(ReminderCadence.Every2Days, (await identity.User(member).GetSiteNotificationSettings(siteId, Ct)).ResolvedReminderCadence);
        Assert.Equal(
            new UserSiteNotificationSettings(false, ReminderCadence.Daily, ReminderCadence.Daily),
            await identity.User(own).GetSiteNotificationSettings(siteId, Ct));
        Assert.Equal(CadenceSynced, (await Aliases(member))[^1]);
        Assert.Equal(CadenceSynced, (await Aliases(own))[^1]);
        Assert.Empty(await Aliases(bystander));

        // Once the member takes their own back, the Site's cadence is already there.
        await identity.User(own).SetSiteNotificationSettings(siteId, false, null, Ct);
        Assert.Equal(ReminderCadence.Every2Days, (await identity.User(own).GetSiteNotificationSettings(siteId, Ct)).ResolvedReminderCadence);
    }

    [Fact]
    public async Task AFanOutThatFailedHalfwayIsRepairedByTheSameRequestAgain()
    {
        var (siteId, ownerId) = await CreateSiteAsync();

        // User IDs sort after the Owner's or before; whoever fails, the Site holds the cadence.
        var member = NewUserId();
        await AddMemberAsync(siteId, member);

        identity.UserFaults.FailCadenceSyncs(member);
        (SiteReminderCadenceDelivery Delivery, ReminderCadence Cadence) failed;
        try
        {
            failed = await SiteReminderCadenceFanOut.SetAsync(identity.Cluster.GrainFactory, siteId, ReminderCadence.Every2Days, NullLogger.Instance, Ct);
        }
        finally
        {
            identity.UserFaults.Restore(member);
        }

        Assert.Equal((SiteReminderCadenceDelivery.NotDelivered, ReminderCadence.Every2Days), failed);
        Assert.Equal(ReminderCadence.Every2Days, await identity.Site(siteId).GetReminderCadence(Ct));
        Assert.Equal(ReminderCadence.Daily, (await identity.User(member).GetSiteNotificationSettings(siteId, Ct)).ResolvedReminderCadence);

        // The same request finds the Site unchanged and still hands the cadence to everyone.
        var retried = await SiteReminderCadenceFanOut.SetAsync(identity.Cluster.GrainFactory, siteId, ReminderCadence.Every2Days, NullLogger.Instance, Ct);

        Assert.Equal((SiteReminderCadenceDelivery.Delivered, ReminderCadence.Every2Days), retried);
        Assert.Equal(ReminderCadence.Every2Days, (await identity.User(member).GetSiteNotificationSettings(siteId, Ct)).ResolvedReminderCadence);
        Assert.Equal(ReminderCadence.Every2Days, (await identity.User(ownerId).GetSiteNotificationSettings(siteId, Ct)).ResolvedReminderCadence);
        Assert.Equal(1, (await identity.AliasesAsync($"site/{siteId}")).Count(alias => alias == "site.reminder-cadence-changed"));
        Assert.Equal(1, (await Aliases(member)).Count(alias => alias == CadenceSynced));
        Assert.Equal(1, (await Aliases(ownerId)).Count(alias => alias == CadenceSynced));
    }

    private static string NewUserId() => Guid.NewGuid().ToString();

    private Task<List<string>> Aliases(string userId) => identity.AliasesAsync($"user/{userId}");

    private async Task<(string SiteId, string OwnerId)> CreateSiteAsync()
    {
        var ownerId = NewUserId();
        var created = await identity.User(ownerId).CreateSite("k1", "Home", Ct);
        Assert.Equal(SiteCreationOutcome.Created, created.Outcome);
        return (created.Site!.Id, ownerId);
    }

    // As a reconciliation does it: Keycloak shows the member, the Site grain journals the Membership.
    private async Task AddMemberAsync(string siteId, string userId)
    {
        identity.PhaseTwo.ConsoleAddMember(siteId, userId, "member");
        var result = await identity.Site(siteId).Reconcile(null, acceptUnconfirmed: false, Ct);
        Assert.Equal(SiteRole.Member, result.Members[userId]);
    }
}
