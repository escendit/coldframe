using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Notifications;
using Coldframe.Contracts.Sites;
using Coldframe.Server.Alerts;
using Coldframe.Server.Identity;
using Coldframe.Server.IntegrationTests.Devices;
using Coldframe.Server.IntegrationTests.Journal;

namespace Coldframe.Server.IntegrationTests.Identity;

/// <summary>
/// Delivery timing on a TestCluster with a fake clock and a recording channel (Story 6.4; AD-5, AD-6): the User
/// grain learns of its Sites' Alerts from the Alert grain and from the Site grain, and alone decides when the
/// Notifier is handed a notification: at once inside the Notification Window, one summary per Site when the
/// window opens, Reminders at the resolved cadence, nothing for a muted Site. Every deadline is journaled, so a
/// silo restart loses none and sends nothing twice.
/// </summary>
/// <remarks>
/// The fake clock fires no timer and no reminder. A test moves it and then either waits for what the grain's own
/// timer does (<see cref="JournalWait"/>) or wakes the grain the way its reminder does, which returns once the
/// wake has processed everything overdue. The clock only moves forward; every test starts on a new day.
/// </remarks>
[Collection(IngestSuites.Name)]
public sealed class DeliveryTimingGrainTests(IdentityCluster identity) : IClassFixture<IdentityCluster>
{
    private const string Tracked = "user.alert-tracked";
    private const string Dropped = "user.alert-dropped";
    private const string Held = "user.delivery-held";
    private const string Sent = "user.notification-sent";
    private const string Pulled = "user.site-alerts-pulled";
    private const string MembershipChanged = "user.site-membership-changed";

    private static readonly TimeSpan Day = TimeSpan.FromHours(24);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTimeOffset Now => identity.Time.GetUtcNow();

    [Fact]
    public async Task AnAlertThatOpensInsideTheWindowIsOneAlertNotificationSentWithinAMinute()
    {
        var site = await SiteAsync();
        GoTo(12, 0);

        var alert = await OpenAsync(site.SiteId);
        var openedAt = Now;

        // The Alert grain told the User grain; the User grain's own timer does the rest.
        var sent = await SentAsync(site.OwnerId, 1);
        Assert.Equal(
            new Notification(site.OwnerId, site.SiteId, NotificationKind.Alert, openedAt, null, sent.Notification.Entries),
            sent.Notification with { Registrations = null, TimeZone = null, Window = null });
        Assert.Equal(Entry(alert), Assert.Single(sent.Notification.Entries));
        Assert.InRange(sent.SentAt - sent.Notification.DueAt, TimeSpan.Zero, TimeSpan.FromMinutes(1));

        // A report made again tells the User grain again, and nothing is sent twice.
        await identity.User(site.OwnerId).AlertOpened(site.SiteId, alert, Ct);
        await identity.WakeUserAsync(site.OwnerId);
        Assert.Single(identity.Notifications.For(site.OwnerId));
        Assert.Equal([Tracked, Sent], await DeliveryAliasesAsync(site.OwnerId));
        var journaled = Assert.IsType<NotificationSent>((await identity.Store.ReadStreamAsync($"user/{site.OwnerId}", Ct))[^1].Data);
        Assert.Equal((site.SiteId, NotificationKind.Alert, openedAt, sent.SentAt), (journaled.SiteId, journaled.Kind, journaled.DueAt, journaled.SentAt));
        Assert.Equal([alert.AlertId], journaled.AlertIds);
    }

    [Fact]
    public async Task ANotifierThatFailsLeavesTheDeliveryDueForTheNextWake()
    {
        var site = await SiteAsync();
        GoTo(12, 0);
        identity.Notifications.FailNext(site.OwnerId, int.MaxValue);
        var failedBefore = identity.Notifications.Failed;

        await OpenAsync(site.SiteId);
        var openedAt = Now;
        await identity.WakeUserAsync(site.OwnerId);
        await identity.WakeUserAsync(site.OwnerId);

        // Not journaled as sent while the Notifier throws, however often the grain wakes.
        Assert.True(identity.Notifications.Failed >= failedBefore + 2);
        Assert.Equal([Tracked], await DeliveryAliasesAsync(site.OwnerId));

        // The provider is back 20 s later: the next wake sends what stayed due.
        identity.Time.Advance(TimeSpan.FromSeconds(20));
        identity.Notifications.FailNext(site.OwnerId, 0);
        await identity.WakeUserAsync(site.OwnerId);

        var sent = Assert.Single(identity.Notifications.For(site.OwnerId));
        Assert.Equal((NotificationKind.Alert, openedAt, Now), (sent.Notification.Kind, sent.Notification.DueAt, sent.SentAt));
        Assert.Equal([Tracked, Sent], await DeliveryAliasesAsync(site.OwnerId));
    }

    [Fact]
    public async Task ADeliveryThatIsStillDueWhenTheWindowHasClosedIsHeldForTheSummary()
    {
        var site = await SiteAsync();
        GoTo(21, 59);
        identity.Notifications.FailNext(site.OwnerId, int.MaxValue);

        // Due inside the window, but the Notifier fails until the window has closed.
        var alert = await OpenAsync(site.SiteId);
        var dueAt = Now;
        await identity.WakeUserAsync(site.OwnerId);
        GoTo(22, 0);
        identity.Notifications.FailNext(site.OwnerId, 0);
        await identity.WakeUserAsync(site.OwnerId);

        // Nothing is sent at night.
        Assert.Empty(identity.Notifications.For(site.OwnerId));
        Assert.Equal([Tracked, Held], await DeliveryAliasesAsync(site.OwnerId));

        GoTo(7, 0);
        await identity.WakeUserAsync(site.OwnerId);

        var summary = Assert.Single(identity.Notifications.For(site.OwnerId));
        Assert.Equal((NotificationKind.Summary, Now, (DateTimeOffset?)dueAt), (summary.Notification.Kind, summary.Notification.DueAt, summary.Notification.HeldFrom));
        Assert.Equal([Entry(alert)], summary.Notification.Entries);
    }

    [Fact]
    public async Task WhatFallsDueOvernightIsOneSummaryWithOneEntryPerAlertWhenTheWindowOpens()
    {
        var site = await SiteAsync();

        // The first Alert opens at 02:00: held, and summarised at 07:00.
        GoTo(2, 0);
        var first = await OpenAsync(site.SiteId);
        var firstDueAt = Now;
        await identity.WakeUserAsync(site.OwnerId);
        Assert.Empty(identity.Notifications.For(site.OwnerId));
        Assert.Equal(firstDueAt.AddHours(5), (await identity.UserStateAsync(site.OwnerId)).WindowOpensAt);

        GoTo(7, 0);
        await identity.WakeUserAsync(site.OwnerId);
        var morning = Assert.Single(identity.Notifications.For(site.OwnerId));
        Assert.Equal((NotificationKind.Summary, Now, (DateTimeOffset?)firstDueAt), (morning.Notification.Kind, morning.Notification.DueAt, morning.Notification.HeldFrom));
        Assert.Equal([Entry(first)], morning.Notification.Entries);

        // Another Alert opens at 23:30, and the first Alert's Reminder falls due at 02:00, a day after its own
        // due-at and not after the summary: nothing until 07:00.
        GoTo(23, 30);
        var second = await OpenAsync(site.SiteId);
        var secondDueAt = Now;
        await identity.WakeUserAsync(site.OwnerId);
        GoTo(2, 0);
        Assert.Equal(firstDueAt + Day, Now);
        await identity.WakeUserAsync(site.OwnerId);
        GoTo(6, 59);
        await identity.WakeUserAsync(site.OwnerId);
        Assert.Single(identity.Notifications.For(site.OwnerId));

        GoTo(7, 0);
        await identity.WakeUserAsync(site.OwnerId);

        var sent = identity.Notifications.For(site.OwnerId);
        Assert.Equal(2, sent.Count);
        var summary = sent[1];
        Assert.Equal(
            new Notification(site.OwnerId, site.SiteId, NotificationKind.Summary, Now, secondDueAt, summary.Notification.Entries),
            summary.Notification with { Registrations = null, TimeZone = null, Window = null });
        Assert.Equal([Entry(first), Entry(second)], summary.Notification.Entries);
        Assert.InRange(summary.SentAt - summary.Notification.DueAt, TimeSpan.Zero, TimeSpan.FromMinutes(1));
        Assert.Equal([Tracked, Held, Sent, Tracked, Held, Held, Sent], await DeliveryAliasesAsync(site.OwnerId));

        // The summary moved no deadline: the next Reminders are due at 02:00 and at 23:30.
        var state = await identity.UserStateAsync(site.OwnerId);
        Assert.Null(state.WindowOpensAt);
        Assert.Equal(firstDueAt + (2 * Day), state.DueAt(state.TrackedAlerts[first.AlertId]));
        Assert.Equal(secondDueAt + Day, state.DueAt(state.TrackedAlerts[second.AlertId]));
    }

    [Fact]
    public async Task AnAlertHeldWithTwoRemindersBeforeTheWindowOpensIsOneEntry()
    {
        var site = await SiteAsync();
        var user = identity.User(site.OwnerId);

        // Opens at 23:30, held for 07:00.
        GoTo(23, 30);
        var alert = await OpenAsync(site.SiteId);
        var dueAt = Now;
        await identity.WakeUserAsync(site.OwnerId);

        // Before the window opens the User moves it to 23:45: held deliveries wait for the window as it is now.
        GoTo(6, 0);
        await user.UpdateNotificationSettings(new UpdateNotificationSettings(new NotificationWindow((23 * 60) + 45, (23 * 60) + 59)), Ct);
        GoTo(7, 0);
        await identity.WakeUserAsync(site.OwnerId);
        Assert.Empty(identity.Notifications.For(site.OwnerId));

        // The first Reminder falls due at 23:30, still outside; the User then moves the window to 23:35.
        GoTo(23, 30);
        await identity.WakeUserAsync(site.OwnerId);
        await user.UpdateNotificationSettings(new UpdateNotificationSettings(new NotificationWindow((23 * 60) + 35, (23 * 60) + 40)), Ct);
        GoTo(23, 45);
        await identity.WakeUserAsync(site.OwnerId);
        Assert.Empty(identity.Notifications.For(site.OwnerId));

        // The second Reminder at 23:30 the day after; the window opens five minutes later.
        GoTo(23, 30);
        await identity.WakeUserAsync(site.OwnerId);
        Assert.Equal([Tracked, Held, Held, Held], await DeliveryAliasesAsync(site.OwnerId));
        GoTo(23, 35);
        await identity.WakeUserAsync(site.OwnerId);

        var summary = Assert.Single(identity.Notifications.For(site.OwnerId));
        Assert.Equal((NotificationKind.Summary, Now, (DateTimeOffset?)dueAt), (summary.Notification.Kind, summary.Notification.DueAt, summary.Notification.HeldFrom));
        Assert.Equal(Entry(alert), Assert.Single(summary.Notification.Entries));
    }

    [Fact]
    public async Task ADeliveryThatFellDueAtNightAndIsFirstSeenInsideTheWindowIsASummaryNotAReminder()
    {
        var site = await SiteAsync();
        GoTo(2, 0);
        var alert = await OpenAsync(site.SiteId);
        var openedAt = Now;
        await identity.WakeUserAsync(site.OwnerId);
        GoTo(7, 0);
        await identity.WakeUserAsync(site.OwnerId);
        Assert.Equal([Tracked, Held, Sent], await DeliveryAliasesAsync(site.OwnerId));

        // The Reminder falls due at 02:00 and nothing wakes the grain before 07:05.
        identity.Time.Advance(Day + TimeSpan.FromMinutes(5));
        await identity.WakeUserAsync(site.OwnerId);

        var sent = identity.Notifications.For(site.OwnerId);
        Assert.Equal([NotificationKind.Summary, NotificationKind.Summary], sent.Select(notification => notification.Notification.Kind));
        Assert.Equal(
            (openedAt + Day + TimeSpan.FromHours(5), (DateTimeOffset?)(openedAt + Day)),
            (sent[1].Notification.DueAt, sent[1].Notification.HeldFrom));
        Assert.Equal([Entry(alert)], sent[1].Notification.Entries);
        Assert.Equal([Tracked, Held, Sent, Held, Sent], await DeliveryAliasesAsync(site.OwnerId));
    }

    [Fact]
    public async Task HeldDeliveriesFollowAWindowOrAZoneThatNowOpensEarlier()
    {
        var site = await SiteAsync();
        var user = identity.User(site.OwnerId);

        // Held at 23:30 for 07:00; at 01:00 the User moves the window so that it is open already.
        GoTo(23, 30);
        var first = await OpenAsync(site.SiteId);
        var firstDueAt = Now;
        await identity.WakeUserAsync(site.OwnerId);
        GoTo(1, 0);
        await user.UpdateNotificationSettings(new UpdateNotificationSettings(new NotificationWindow(30, 5 * 60)), Ct);
        await identity.WakeUserAsync(site.OwnerId);

        var summary = Assert.Single(identity.Notifications.For(site.OwnerId));
        Assert.Equal((NotificationKind.Summary, Now, (DateTimeOffset?)firstDueAt), (summary.Notification.Kind, summary.Notification.DueAt, summary.Notification.HeldFrom));
        Assert.Equal([Entry(first)], summary.Notification.Entries);
        await user.AlertClosed(site.SiteId, first.AlertId, Ct);

        // Held again at 06:00 UTC, outside 00:30 to 05:00. In Sao Paulo it is 03:00 then: the window is open.
        GoTo(6, 0);
        var second = await OpenAsync(site.SiteId);
        var secondDueAt = Now;
        await identity.WakeUserAsync(site.OwnerId);
        Assert.Single(identity.Notifications.For(site.OwnerId));
        await user.UpdateNotificationSettings(new UpdateNotificationSettings(TimeZone: "America/Sao_Paulo"), Ct);
        await identity.WakeUserAsync(site.OwnerId);

        var sent = identity.Notifications.For(site.OwnerId);
        Assert.Equal(2, sent.Count);
        Assert.Equal((NotificationKind.Summary, (DateTimeOffset?)secondDueAt), (sent[1].Notification.Kind, sent[1].Notification.HeldFrom));
        Assert.InRange(sent[1].SentAt - sent[1].Notification.DueAt, TimeSpan.Zero, TimeSpan.FromHours(3));
        Assert.Equal([Entry(second)], sent[1].Notification.Entries);
    }

    [Fact]
    public async Task ALaterHeldDeliveryJournalsTheWindowOpeningOfTheWindowAsItIsNow()
    {
        var site = await SiteAsync();
        GoTo(23, 30);
        await OpenAsync(site.SiteId);
        var dueAt = Now;
        await identity.WakeUserAsync(site.OwnerId);
        Assert.Equal(dueAt.AddHours(7.5), (await identity.UserStateAsync(site.OwnerId)).WindowOpensAt);

        // The window moves to 23:45; the Reminder a day later is held for that opening, not for 07:00.
        await identity.User(site.OwnerId).UpdateNotificationSettings(new UpdateNotificationSettings(new NotificationWindow((23 * 60) + 45, (23 * 60) + 59)), Ct);
        identity.Time.Advance(Day);
        await identity.WakeUserAsync(site.OwnerId);

        Assert.Equal([Tracked, Held, Held], await DeliveryAliasesAsync(site.OwnerId));
        Assert.Equal(dueAt + Day + TimeSpan.FromMinutes(15), (await identity.UserStateAsync(site.OwnerId)).WindowOpensAt);
        Assert.Empty(identity.Notifications.For(site.OwnerId));
    }

    [Fact]
    public async Task AUserWhoTracksNothingStillReconcilesOnActivation()
    {
        var site = await SiteAsync();
        GoTo(12, 0);

        // The Site lists an Alert the User grain was never told of.
        var missed = NewAlert(site.SiteId).Alert;
        await identity.Site(site.SiteId).AlertOpened(missed, Ct);
        Assert.Empty(await DeliveryAliasesAsync(site.OwnerId));
        var now = Now;
        await identity.RestartSiloAsync();
        identity.Time.SetUtcNow(now);

        await identity.User(site.OwnerId).GetNotificationSettings(Ct);
        await JournalWait.UntilAsync(
            async () => (await DeliveryAliasesAsync(site.OwnerId)).Count == 1,
            "the User grain reconciled its Site on activation");

        Assert.Equal([missed.AlertId], (await identity.UserStateAsync(site.OwnerId)).TrackedAlerts.Keys);
        Assert.Empty(identity.Notifications.For(site.OwnerId));
    }

    [Fact]
    public async Task AnAlertClosedWhileHeldIsNotDeliveredAndNoSummaryIsSent()
    {
        var site = await SiteAsync();
        GoTo(23, 30);
        var alert = await OpenAsync(site.SiteId);
        await identity.WakeUserAsync(site.OwnerId);
        Assert.Equal([Tracked, Held], await DeliveryAliasesAsync(site.OwnerId));
        Assert.True(await identity.HasWakeReminderAsync(site.OwnerId));

        // Closes at 05:00, as the Alert grain tells every member.
        GoTo(5, 0);
        await identity.User(site.OwnerId).AlertClosed(site.SiteId, alert.AlertId, Ct);
        await identity.User(site.OwnerId).AlertClosed(site.SiteId, alert.AlertId, Ct);

        GoTo(7, 0);
        await identity.WakeUserAsync(site.OwnerId);

        Assert.Empty(identity.Notifications.For(site.OwnerId));
        Assert.Equal([Tracked, Held, Dropped], await DeliveryAliasesAsync(site.OwnerId));
        var state = await identity.UserStateAsync(site.OwnerId);
        Assert.Empty(state.TrackedAlerts);
        Assert.Null(state.WindowOpensAt);

        // Nothing is left to deliver: the wake-up reminder is gone.
        Assert.False(await identity.HasWakeReminderAsync(site.OwnerId));
    }

    [Fact]
    public async Task AnOpenAlertIsRemindedAtTheResolvedCadenceAndNeverAfterItClosed()
    {
        var site = await SiteAsync();
        GoTo(12, 0);
        var alert = await OpenAsync(site.SiteId);
        var openedAt = Now;
        await identity.WakeUserAsync(site.OwnerId);

        // Daily: nothing a minute before, one Reminder at 12:00 the next day.
        identity.Time.Advance(Day - TimeSpan.FromMinutes(1));
        await identity.WakeUserAsync(site.OwnerId);
        Assert.Single(identity.Notifications.For(site.OwnerId));
        identity.Time.Advance(TimeSpan.FromMinutes(1));
        await identity.WakeUserAsync(site.OwnerId);

        var sent = identity.Notifications.For(site.OwnerId);
        Assert.Equal([NotificationKind.Alert, NotificationKind.Reminder], sent.Select(notification => notification.Notification.Kind));
        Assert.Equal((openedAt + Day, (DateTimeOffset?)null), (sent[1].Notification.DueAt, sent[1].Notification.HeldFrom));
        Assert.Equal([Entry(alert)], sent[1].Notification.Entries);

        // My own cadence, every 2 days over the Site's daily, re-schedules from the previous due-at.
        await identity.User(site.OwnerId).SetSiteNotificationSettings(site.SiteId, false, ReminderCadence.Every2Days, Ct);
        identity.Time.Advance(Day);
        await identity.WakeUserAsync(site.OwnerId);
        Assert.Equal(2, identity.Notifications.For(site.OwnerId).Count);
        identity.Time.Advance(Day);
        await identity.WakeUserAsync(site.OwnerId);
        sent = identity.Notifications.For(site.OwnerId);
        Assert.Equal((3, NotificationKind.Reminder, openedAt + (3 * Day)), (sent.Count, sent[2].Notification.Kind, sent[2].Notification.DueAt));

        // Back to the Site setting, which the Site then sets to every 2 days; then neither: daily.
        await identity.User(site.OwnerId).SetSiteNotificationSettings(site.SiteId, false, null, Ct);
        await identity.User(site.OwnerId).SyncSiteReminderCadence(site.SiteId, ReminderCadence.Every2Days, Ct);
        var state = await identity.UserStateAsync(site.OwnerId);
        Assert.Equal(openedAt + (5 * Day), state.DueAt(state.TrackedAlerts[alert.AlertId]));
        await identity.User(site.OwnerId).SyncSiteReminderCadence(site.SiteId, ReminderCadence.Daily, Ct);
        state = await identity.UserStateAsync(site.OwnerId);
        Assert.Equal(openedAt + (4 * Day), state.DueAt(state.TrackedAlerts[alert.AlertId]));

        // Closed: no notification, and no Reminder ever after.
        await identity.User(site.OwnerId).AlertClosed(site.SiteId, alert.AlertId, Ct);
        identity.Time.Advance(3 * Day);
        await identity.WakeUserAsync(site.OwnerId);
        Assert.Equal(3, identity.Notifications.For(site.OwnerId).Count);
        Assert.False(await identity.HasWakeReminderAsync(site.OwnerId));
    }

    [Fact]
    public async Task OverdueRemindersCollapseIntoOneAndTheNextIsDueAfterNow()
    {
        var site = await SiteAsync();
        GoTo(12, 0);
        var alert = await OpenAsync(site.SiteId);
        var openedAt = Now;
        await identity.WakeUserAsync(site.OwnerId);

        // Nothing wakes the grain for three days and an hour.
        identity.Time.Advance((3 * Day) + TimeSpan.FromHours(1));
        await identity.WakeUserAsync(site.OwnerId);
        await identity.WakeUserAsync(site.OwnerId);

        var sent = identity.Notifications.For(site.OwnerId);
        Assert.Equal(2, sent.Count);
        Assert.Equal((NotificationKind.Reminder, openedAt + (3 * Day)), (sent[1].Notification.Kind, sent[1].Notification.DueAt));
        var state = await identity.UserStateAsync(site.OwnerId);
        Assert.Equal(openedAt + (4 * Day), state.DueAt(state.TrackedAlerts[alert.AlertId]));
    }

    [Fact]
    public async Task AMutedSiteNotifiesOnlyTheOthersAndUnmutingStartsNoCatchUp()
    {
        var site = await SiteAsync();
        var muted = site.OwnerId;
        var other = await AddMemberAsync(site.SiteId);
        await identity.User(muted).SetSiteNotificationSettings(site.SiteId, muted: true, null, Ct);
        GoTo(12, 0);

        var alert = await OpenAsync(site.SiteId);
        var openedAt = Now;
        await identity.WakeUserAsync(muted);
        await identity.WakeUserAsync(other);

        // The Alert is open for everyone; only the User who muted the Site hears nothing.
        Assert.Equal(NotificationKind.Alert, Assert.Single(identity.Notifications.For(other)).Notification.Kind);
        Assert.Empty(identity.Notifications.For(muted));
        Assert.Contains(alert.AlertId, (await identity.UserStateAsync(muted)).TrackedAlerts.Keys);

        // Muted over the first Reminder too: discarded, not held and not sent later.
        identity.Time.Advance(Day + TimeSpan.FromHours(1));
        await identity.WakeUserAsync(muted);
        await identity.WakeUserAsync(other);
        Assert.Equal(2, identity.Notifications.For(other).Count);
        Assert.Empty(identity.Notifications.For(muted));

        await identity.User(muted).SetSiteNotificationSettings(site.SiteId, muted: false, null, Ct);
        await identity.WakeUserAsync(muted);
        Assert.Empty(identity.Notifications.For(muted));
        Assert.Equal([Tracked], await DeliveryAliasesAsync(muted));

        // The Reminder advanced all the while: the next one is due two days after the Alert opened.
        identity.Time.Advance(TimeSpan.FromHours(23));
        await identity.WakeUserAsync(muted);
        var reminder = Assert.Single(identity.Notifications.For(muted));
        Assert.Equal((NotificationKind.Reminder, openedAt + (2 * Day)), (reminder.Notification.Kind, reminder.Notification.DueAt));
    }

    [Fact]
    public async Task EachMemberIsNotifiedOrHeldByTheirOwnWindowAndZone()
    {
        var site = await SiteAsync();
        var (tokyo, losAngeles) = (site.OwnerId, await AddMemberAsync(site.SiteId));
        await identity.User(tokyo).UpdateNotificationSettings(new UpdateNotificationSettings(TimeZone: "Asia/Tokyo"), Ct);
        await identity.User(losAngeles).UpdateNotificationSettings(
            new UpdateNotificationSettings(new NotificationWindow(8 * 60, 20 * 60), DetectedTimeZone: "America/Los_Angeles"),
            Ct);

        // 04:00 UTC is 13:00 in Tokyo and, the evening before, 20:00 or 21:00 in Los Angeles: after the window.
        GoTo(4, 0);
        var alert = await OpenAsync(site.SiteId);
        var openedAt = Now;
        await identity.WakeUserAsync(tokyo);
        await identity.WakeUserAsync(losAngeles);

        Assert.Equal(NotificationKind.Alert, Assert.Single(identity.Notifications.For(tokyo)).Notification.Kind);
        Assert.Empty(identity.Notifications.For(losAngeles));
        var opensAt = (await identity.UserStateAsync(losAngeles)).WindowOpensAt;
        Assert.NotNull(opensAt);
        Assert.Equal(new TimeSpan(8, 0, 0), TimeZoneInfo.ConvertTime(opensAt.Value, TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles")).TimeOfDay);
        Assert.InRange(opensAt.Value - openedAt, TimeSpan.FromHours(10), TimeSpan.FromHours(13));

        identity.Time.Advance(opensAt.Value - Now - TimeSpan.FromMinutes(1));
        await identity.WakeUserAsync(losAngeles);
        Assert.Empty(identity.Notifications.For(losAngeles));
        identity.Time.Advance(TimeSpan.FromMinutes(1));
        await identity.WakeUserAsync(losAngeles);

        var summary = Assert.Single(identity.Notifications.For(losAngeles));
        Assert.Equal((NotificationKind.Summary, opensAt.Value, (DateTimeOffset?)openedAt), (summary.Notification.Kind, summary.Notification.DueAt, summary.Notification.HeldFrom));
        Assert.Equal([Entry(alert)], summary.Notification.Entries);
        Assert.Single(identity.Notifications.For(tokyo));
    }

    [Fact]
    public async Task TheSummaryIsSentAt0700LocalOverBothDaylightSavingChanges()
    {
        var zurich = TimeZoneInfo.FindSystemTimeZoneById("Europe/Zurich");
        var site = await SiteAsync();
        await identity.User(site.OwnerId).UpdateNotificationSettings(new UpdateNotificationSettings(TimeZone: "Europe/Zurich"), Ct);

        // The night the clocks go back (last Sunday of October), then the night they go forward (of March).
        foreach (var (month, heldAtUtc, opensAtUtc) in new[] { (10, new TimeSpan(21, 30, 0), new TimeSpan(6, 0, 0)), (3, new TimeSpan(22, 30, 0), new TimeSpan(5, 0, 0)) })
        {
            var change = NextLastSunday(month);
            identity.Time.SetUtcNow(new DateTimeOffset(change.AddDays(-1), TimeOnly.FromTimeSpan(heldAtUtc), TimeSpan.Zero));
            Assert.Equal(new TimeSpan(23, 30, 0), TimeZoneInfo.ConvertTime(Now, zurich).TimeOfDay);
            var before = identity.Notifications.For(site.OwnerId).Count;

            var alert = await OpenAsync(site.SiteId);
            await identity.WakeUserAsync(site.OwnerId);
            var opensAt = new DateTimeOffset(change, TimeOnly.FromTimeSpan(opensAtUtc), TimeSpan.Zero);
            Assert.Equal(opensAt, (await identity.UserStateAsync(site.OwnerId)).WindowOpensAt);

            identity.Time.SetUtcNow(opensAt.AddMinutes(-1));
            await identity.WakeUserAsync(site.OwnerId);
            Assert.Equal(before, identity.Notifications.For(site.OwnerId).Count);

            identity.Time.SetUtcNow(opensAt);
            await identity.WakeUserAsync(site.OwnerId);

            var sent = identity.Notifications.For(site.OwnerId);
            Assert.Equal(before + 1, sent.Count);
            Assert.Equal((NotificationKind.Summary, opensAt), (sent[^1].Notification.Kind, sent[^1].Notification.DueAt));
            Assert.Equal(new TimeSpan(7, 0, 0), TimeZoneInfo.ConvertTime(sent[^1].SentAt, zurich).TimeOfDay);
            Assert.Contains(Entry(alert), sent[^1].Notification.Entries);

            // Closed, so that the next night starts with nothing held and no Reminder falls due in between.
            await identity.User(site.OwnerId).AlertClosed(site.SiteId, alert.AlertId, Ct);
        }
    }

    [Fact]
    public async Task ASummaryPendingAcrossASiloRestartIsSentOnceAndNoDeadlineMoves()
    {
        var site = await SiteAsync();
        GoTo(12, 0);
        var reminded = await OpenAsync(site.SiteId);
        await identity.WakeUserAsync(site.OwnerId);
        GoTo(23, 30);
        var held = await OpenAsync(site.SiteId);
        var heldAt = Now;
        await identity.WakeUserAsync(site.OwnerId);
        Assert.Equal([Tracked, Sent, Tracked, Held], await DeliveryAliasesAsync(site.OwnerId));

        // The silo goes down at 06:50 and is back at 07:05: nobody was there at 07:00.
        GoTo(6, 50);
        var opensAt = Now.AddMinutes(10);
        var before = await identity.UserStateAsync(site.OwnerId);
        Assert.Equal(opensAt, before.WindowOpensAt);
        await identity.RestartSiloAsync();
        identity.Time.SetUtcNow(opensAt.AddMinutes(5));
        Assert.Empty(identity.Notifications.For(site.OwnerId));

        // The first call activates the grain, which processes everything overdue by itself.
        await identity.User(site.OwnerId).GetNotificationSettings(Ct);
        var summary = await SentAsync(site.OwnerId, 1);

        Assert.Equal(
            (NotificationKind.Summary, opensAt, (DateTimeOffset?)heldAt, opensAt.AddMinutes(5)),
            (summary.Notification.Kind, summary.Notification.DueAt, summary.Notification.HeldFrom, summary.SentAt));
        Assert.Equal([Entry(held)], summary.Notification.Entries);

        // Once: further wakes and another restart send nothing, and both Reminders are due when they were.
        await identity.WakeUserAsync(site.OwnerId);
        await identity.RestartSiloAsync();
        identity.Time.SetUtcNow(opensAt.AddMinutes(6));
        await identity.WakeUserAsync(site.OwnerId);
        Assert.Empty(identity.Notifications.For(site.OwnerId));
        Assert.Equal([Tracked, Sent, Tracked, Held, Sent], await DeliveryAliasesAsync(site.OwnerId));
        var after = await identity.UserStateAsync(site.OwnerId);
        Assert.Null(after.WindowOpensAt);
        Assert.Equal(before.DueAt(before.TrackedAlerts[reminded.AlertId]), after.DueAt(after.TrackedAlerts[reminded.AlertId]));
        Assert.Equal(heldAt + Day, after.DueAt(after.TrackedAlerts[held.AlertId]));
        Assert.True(await identity.HasWakeReminderAsync(site.OwnerId));

        // The Reminder that was due at noon all along is sent at noon, by the restarted silo.
        GoTo(12, 0);
        await identity.WakeUserAsync(site.OwnerId);
        var reminder = Assert.Single(identity.Notifications.For(site.OwnerId));
        Assert.Equal((NotificationKind.Reminder, Now, reminded.AlertId), (reminder.Notification.Kind, reminder.Notification.DueAt, reminder.Notification.Entries[0].AlertId));
    }

    [Fact]
    public async Task AUserWhoJoinsTracksTheOpenAlertsWithoutAnOpeningNotificationAndIsRemindedOnTheirSchedule()
    {
        var site = await SiteAsync();
        GoTo(9, 0);
        var first = await OpenAsync(site.SiteId);
        var firstOpenedAt = Now;
        GoTo(11, 0);
        var second = await OpenAsync(site.SiteId);

        // Granted at 12:00 the day after, while both Alerts are open.
        GoTo(12, 0);
        identity.Time.Advance(Day);
        Assert.Equal(firstOpenedAt.AddHours(27), Now);
        var joiner = await AddMemberAsync(site.SiteId);

        Assert.Equal([MembershipChanged, Tracked, Tracked, Pulled], await identity.AliasesAsync($"user/{joiner}"));
        await identity.WakeUserAsync(joiner);
        Assert.Empty(identity.Notifications.For(joiner));
        var state = await identity.UserStateAsync(joiner);
        Assert.Equal([first.AlertId, second.AlertId], state.TrackedAlerts.Values.OrderBy(alert => alert.Alert.OpenedAt).Select(alert => alert.Alert.AlertId));
        Assert.All(state.TrackedAlerts.Values, alert => Assert.False(alert.OpeningPending));
        Assert.Empty(state.PendingPulls);

        // The first Reminders follow at the first openedAt + n × 24 h after the join: 09:00 and 11:00 tomorrow.
        Assert.Equal(firstOpenedAt + (2 * Day), state.DueAt(state.TrackedAlerts[first.AlertId]));
        GoTo(9, 0);
        await identity.WakeUserAsync(joiner);
        GoTo(11, 0);
        await identity.WakeUserAsync(joiner);

        var sent = identity.Notifications.For(joiner);
        Assert.Equal([NotificationKind.Reminder, NotificationKind.Reminder], sent.Select(notification => notification.Notification.Kind));
        Assert.Equal([first.AlertId, second.AlertId], sent.Select(notification => notification.Notification.Entries[0].AlertId));
        Assert.Equal(firstOpenedAt + (2 * Day), sent[0].Notification.DueAt);

        // Told of an Alert it already pulled: nothing changes.
        await identity.User(joiner).AlertOpened(site.SiteId, first, Ct);
        Assert.Equal(2, (await identity.AliasesAsync($"user/{joiner}")).Count(alias => alias == Tracked));
    }

    [Fact]
    public async Task APullThatFailsNeverFailsTheMembershipAndIsRepeatedOnTheNextWake()
    {
        var site = await SiteAsync();
        GoTo(12, 0);
        var alert = await OpenAsync(site.SiteId);
        var joiner = Guid.NewGuid().ToString();
        identity.SiteFaults.FailOpenAlerts(site.SiteId);

        try
        {
            await identity.User(joiner).SyncSiteMembership(site.SiteId, SiteRole.Member, Ct);

            // The Membership stands; the pull is pending, so the grain keeps waking.
            Assert.Equal([MembershipChanged], await identity.AliasesAsync($"user/{joiner}"));
            Assert.Equal(SiteRole.Member, (await identity.UserSitesAsync(joiner))[site.SiteId]);
            await identity.WakeUserAsync(joiner);
            Assert.Equal([site.SiteId], (await identity.UserStateAsync(joiner)).PendingPulls);
            Assert.True(await identity.HasWakeReminderAsync(joiner));
        }
        finally
        {
            identity.SiteFaults.RestoreOpenAlerts(site.SiteId);
        }

        // With nobody calling: the grain's own timer repeats the pull.
        await JournalWait.UntilAsync(
            async () => (await identity.AliasesAsync($"user/{joiner}")).Contains(Pulled),
            "the User grain pulled the Site's open Alerts");
        Assert.Equal([MembershipChanged, Tracked, Pulled], await identity.AliasesAsync($"user/{joiner}"));
        Assert.Equal([alert.AlertId], (await identity.UserStateAsync(joiner)).TrackedAlerts.Keys);
        Assert.Empty(identity.Notifications.For(joiner));
    }

    [Fact]
    public async Task WhenTheMembershipEndsTheSitesAlertsHeldDeliveriesAndDeadlinesAreGone()
    {
        var site = await SiteAsync();
        var member = await AddMemberAsync(site.SiteId);
        GoTo(12, 0);
        await OpenAsync(site.SiteId);
        await identity.WakeUserAsync(member);
        GoTo(23, 30);
        await OpenAsync(site.SiteId);
        await identity.WakeUserAsync(member);
        var before = await identity.UserStateAsync(member);
        Assert.Equal((2, 1), (before.TrackedAlerts.Count, before.HeldAlertsBySite().Count));
        Assert.True(await identity.HasWakeReminderAsync(member));

        await identity.User(member).SyncSiteMembership(site.SiteId, null, Ct);

        var after = await identity.UserStateAsync(member);
        Assert.Empty(after.TrackedAlerts);
        Assert.Empty(after.PendingPulls);
        Assert.Null(after.WindowOpensAt);
        Assert.False(await identity.HasWakeReminderAsync(member));

        // Neither the summary at 07:00 nor a Reminder at noon.
        GoTo(7, 0);
        await identity.WakeUserAsync(member);
        GoTo(12, 0);
        await identity.WakeUserAsync(member);
        Assert.Single(identity.Notifications.For(member));
        Assert.Equal(MembershipChanged, (await identity.AliasesAsync($"user/{member}"))[^1]);
    }

    [Fact]
    public async Task AnAlertOfASiteTheUserDoesNotHoldIsAcknowledgedAndNothingIsStored()
    {
        var site = await SiteAsync();
        var stranger = Guid.NewGuid().ToString();
        GoTo(12, 0);
        var alert = await OpenAsync(site.SiteId);

        await identity.User(stranger).AlertOpened(site.SiteId, alert, Ct);
        await identity.User(stranger).AlertClosed(site.SiteId, alert.AlertId, Ct);

        Assert.Empty(await identity.AliasesAsync($"user/{stranger}"));
        Assert.False(await identity.HasWakeReminderAsync(stranger));

        // An Alert the User tracks is not closed by a report for another Site.
        await identity.User(site.OwnerId).AlertClosed("another-site", alert.AlertId, Ct);
        Assert.Contains(alert.AlertId, (await identity.UserStateAsync(site.OwnerId)).TrackedAlerts.Keys);
    }

    [Fact]
    public async Task AMemberWhoCannotBeReachedKeepsTheReportPendingAndTheOthersAreNotNotifiedTwice()
    {
        var site = await SiteAsync();
        var unreachable = await AddMemberAsync(site.SiteId);
        GoTo(12, 0);
        identity.UserFaults.FailAlertCalls(unreachable);
        var failedBefore = identity.UserFaults.FailedAlertCalls;
        var (request, alert) = NewAlert(site.SiteId);

        try
        {
            var opened = await identity.Alert(alert.AlertId).Open(request, Ct);

            // The Site knows the Alert, but not every member answered: the report is not acknowledged.
            Assert.Equal(new AlertResult(AlertOutcome.Open, Reported: false), opened);
            Assert.Equal(["alert.opened"], await identity.AliasesAsync($"alert/{alert.AlertId}"));
            Assert.Equal(alert.AlertId, Assert.Single(await identity.Site(site.SiteId).OpenAlerts(Ct)).AlertId);

            // The member who can be reached was told at once, and the report is repeated with nobody calling.
            await SentAsync(site.OwnerId, 1);
            await JournalWait.UntilAsync(
                () => Task.FromResult(identity.UserFaults.FailedAlertCalls >= failedBefore + 3),
                "the Alert grain repeated its report");
        }
        finally
        {
            identity.UserFaults.RestoreAlertCalls(unreachable);
        }

        await JournalWait.UntilAsync(
            async () => (await identity.AliasesAsync($"alert/{alert.AlertId}")).Contains("alert.site-notified"),
            "the Alert grain's report was acknowledged by the Site and every member");

        await SentAsync(unreachable, 1);
        await identity.WakeUserAsync(site.OwnerId);
        Assert.Single(identity.Notifications.For(site.OwnerId));
        Assert.Single(await identity.AliasesAsync($"user/{site.OwnerId}"), alias => alias == Tracked);
        Assert.Single(await identity.AliasesAsync($"site/{site.SiteId}"), alias => alias == "site.alert-opened");
    }

    [Fact]
    public async Task OnActivationTheGrainReconcilesItsSitesAddingWhatItMissedAndDroppingWhatIsGone()
    {
        var site = await SiteAsync();
        GoTo(12, 0);
        var gone = await OpenAsync(site.SiteId);
        await identity.WakeUserAsync(site.OwnerId);

        // While the User grain cannot be told, one Alert closes on the Site and another opens.
        var missed = NewAlert(site.SiteId).Alert;
        await identity.Site(site.SiteId).AlertClosed(gone.AlertId, AlertCloseReason.Recovered, Now, Ct);
        await identity.Site(site.SiteId).AlertOpened(missed, Ct);
        var now = Now;
        await identity.RestartSiloAsync();
        identity.Time.SetUtcNow(now);

        await identity.User(site.OwnerId).GetNotificationSettings(Ct);
        await JournalWait.UntilAsync(
            async () => (await DeliveryAliasesAsync(site.OwnerId)).Count == 4,
            "the User grain reconciled its Site on activation");

        // No opening notification for what it found by itself, and none for what closed.
        Assert.Equal([Tracked, Sent, Tracked, Dropped], await DeliveryAliasesAsync(site.OwnerId));
        Assert.Equal([missed.AlertId], (await identity.UserStateAsync(site.OwnerId)).TrackedAlerts.Keys);
        await identity.WakeUserAsync(site.OwnerId);
        Assert.Empty(identity.Notifications.For(site.OwnerId));
    }

    [Fact]
    public async Task AUserWithNothingOpenAndNothingToPullHasNoWakeUpReminder()
    {
        var site = await SiteAsync();
        Assert.False(await identity.HasWakeReminderAsync(site.OwnerId));
        GoTo(12, 0);

        var alert = await OpenAsync(site.SiteId);
        Assert.True(await identity.HasWakeReminderAsync(site.OwnerId));

        await identity.User(site.OwnerId).AlertClosed(site.SiteId, alert.AlertId, Ct);
        Assert.False(await identity.HasWakeReminderAsync(site.OwnerId));

        // A wake that finds nothing registers nothing.
        await identity.WakeUserAsync(site.OwnerId);
        Assert.False(await identity.HasWakeReminderAsync(site.OwnerId));
    }

    private static NotificationEntry Entry(SiteAlert alert) =>
        new(alert.AlertId, alert.Kind, alert.Side, alert.LotId, alert.SensorId, alert.DeviceId, alert.Quantity, alert.OpenedAt);

    // Moves the clock on to the next hh:mm UTC, today or tomorrow: the clock is shared and only moves forward.
    private void GoTo(int hour, int minute)
    {
        var target = new DateTimeOffset(Now.UtcDateTime.Date, TimeSpan.Zero).AddHours(hour).AddMinutes(minute);
        identity.Time.SetUtcNow(target > Now ? target : target.AddDays(1));
    }

    // The next last Sunday of the month that is more than a day ahead: when Europe changes its clocks.
    private DateOnly NextLastSunday(int month)
    {
        for (var year = Now.Year; ; year++)
        {
            var day = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
            while (day.DayOfWeek != DayOfWeek.Sunday)
            {
                day = day.AddDays(-1);
            }

            if (day.AddDays(-2).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) > Now.UtcDateTime)
            {
                return day;
            }
        }
    }

    // An Active Site created by its Owner, whose User grain therefore holds the Site.
    private async Task<(string SiteId, string OwnerId)> SiteAsync()
    {
        var ownerId = Guid.NewGuid().ToString();
        var created = await identity.User(ownerId).CreateSite("k1", "Home", Ct);
        Assert.Equal(SiteCreationOutcome.Created, created.Outcome);
        return (created.Site!.Id, ownerId);
    }

    // As a reconciliation does it: Keycloak shows the member, the Site grain journals the Membership, and the
    // activity hands it to the member's User grain.
    private async Task<string> AddMemberAsync(string siteId)
    {
        var userId = Guid.NewGuid().ToString();
        identity.PhaseTwo.ConsoleAddMember(siteId, userId, "member");
        var result = await identity.Site(siteId).Reconcile(null, acceptUnconfirmed: false, Ct);
        await identity.User(userId).SyncSiteMembership(siteId, result.Members[userId], Ct);
        return userId;
    }

    private (OpenAlert Request, SiteAlert Alert) NewAlert(string siteId)
    {
        var sensorId = Guid.NewGuid();
        var lotId = Guid.CreateVersion7().ToString();
        var request = new OpenAlert(sensorId, 1, ThresholdSide.Low, siteId, lotId, "5a4b3c2d1e0f7c20", "soil_moisture", Now);
        return (request, new SiteAlert(AlertIds.Threshold(sensorId, 1), AlertKind.Threshold, ThresholdSide.Low, lotId, sensorId, "5a4b3c2d1e0f7c20", "soil_moisture", Now));
    }

    // Opens an Alert the way a Sensor grain does: the Alert grain journals it, reports it to its Site grain and
    // tells every member's User grain before it answers.
    private async Task<SiteAlert> OpenAsync(string siteId)
    {
        var (request, alert) = NewAlert(siteId);
        Assert.Equal(new AlertResult(AlertOutcome.Open, Reported: true), await identity.Alert(alert.AlertId).Open(request, Ct));
        return alert;
    }

    // Waits for what the grain's own timer sends, with nobody calling the User grain.
    private async Task<RecordedNotification> SentAsync(string userId, int count)
    {
        await JournalWait.UntilAsync(
            () => Task.FromResult(identity.Notifications.For(userId).Count >= count),
            $"User {userId} was sent {count} notification(s)");
        var sent = identity.Notifications.For(userId);
        Assert.Equal(count, sent.Count);
        return sent[^1];
    }

    // The User's delivery events, in order; Memberships, pulls and settings left out.
    private async Task<List<string>> DeliveryAliasesAsync(string userId) =>
        [.. (await identity.AliasesAsync($"user/{userId}")).Where(alias => alias is Tracked or Dropped or Held or Sent)];
}
