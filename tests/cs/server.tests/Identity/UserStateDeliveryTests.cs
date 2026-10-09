using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Notifications;
using Coldframe.Contracts.Sites;
using Coldframe.Server.Identity;

namespace Coldframe.Server.Tests.Identity;

/// <summary>
/// What the User grain has to deliver, as its events leave it (Story 6.4): the open Alerts of its Sites with one
/// Reminder due-at each, what is held for a summary, the window-opening due-at and the Sites still to pull.
/// </summary>
public sealed class UserStateDeliveryTests
{
    private const string Site = "site-1";

    private const string Other = "site-2";

    private static readonly DateTimeOffset Noon = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Day = TimeSpan.FromHours(24);

    private static readonly SiteAlert Tomatoes = new(
        Guid.Parse("0192f3a4-a000-7000-8000-000000000001"),
        AlertKind.Threshold,
        ThresholdSide.Low,
        "lot-1",
        Guid.Parse("dac4e7fe-93b1-56fc-a363-51235a586394"),
        "5a4b3c2d1e0f7c20",
        "soil_moisture",
        Noon);

    private static readonly SiteAlert Herbs = Tomatoes with { AlertId = Guid.Parse("0192f3a4-a000-7000-8000-000000000002"), LotId = "lot-2" };

    [Fact]
    public void WithoutAnyEventNothingIsTrackedHeldOrPending()
    {
        var state = new UserState();

        Assert.Empty(state.TrackedAlerts);
        Assert.Empty(state.PendingPulls);
        Assert.Null(state.WindowOpensAt);
        Assert.False(state.NeedsWaking);
        Assert.Empty(state.DueAlerts(Noon.AddYears(1)));
        Assert.Empty(state.HeldAlertsBySite());
    }

    [Fact]
    public void AMembershipForASiteTheUserDidNotHoldIsAPullToMakeAndARoleChangeIsNot()
    {
        var state = new UserState();

        state.Apply(new SiteMembershipChanged(Site, SiteRole.Member));
        Assert.Equal([Site], state.PendingPulls);
        Assert.True(state.NeedsWaking);

        state.Apply(new SiteAlertsPulled(Site, Noon));
        Assert.Empty(state.PendingPulls);
        Assert.False(state.NeedsWaking);

        state.Apply(new SiteMembershipChanged(Site, SiteRole.Administrator));
        Assert.Empty(state.PendingPulls);
    }

    [Fact]
    public void AnAlertTheUserWasToldOfIsDueAtOnceAndThenRemindedOneIntervalLater()
    {
        var state = Member();

        state.Apply(new AlertTracked(Site, Tomatoes, Told: true, RemindFrom: Noon, TrackedAt: Noon));

        var tracked = state.TrackedAlerts[Tomatoes.AlertId];
        Assert.Equal(new TrackedAlert(Site, Tomatoes, Noon, OpeningPending: true), tracked);
        Assert.Equal(Noon, state.DueAt(tracked));
        Assert.Equal([tracked], state.DueAlerts(Noon));
        Assert.Empty(state.DueAlerts(Noon.AddTicks(-1)));
        Assert.True(state.NeedsWaking);

        state.Apply(new NotificationSent(Site, NotificationKind.Alert, [Tomatoes.AlertId], Noon, Noon.AddSeconds(3)));

        // Anchored to the due-at, not to the time it was sent.
        tracked = state.TrackedAlerts[Tomatoes.AlertId];
        Assert.False(tracked.OpeningPending);
        Assert.Equal(Noon + Day, state.DueAt(tracked));
        Assert.Empty(state.DueAlerts(Noon + Day - TimeSpan.FromTicks(1)));

        state.Apply(new NotificationSent(Site, NotificationKind.Reminder, [Tomatoes.AlertId], Noon + Day, Noon + Day + TimeSpan.FromSeconds(9)));
        Assert.Equal(Noon + (2 * Day), state.DueAt(state.TrackedAlerts[Tomatoes.AlertId]));
    }

    [Fact]
    public void AnAlertLearnedByPullHasNoOpeningNotificationAndIsTrackedOnce()
    {
        var state = Member();

        // Opened 30 h ago: the last multiple of the interval is 24 h after it opened, the first Reminder 48 h.
        state.Apply(new AlertTracked(Site, Tomatoes, Told: false, RemindFrom: Noon + Day, TrackedAt: Noon.AddHours(30)));
        state.Apply(new AlertTracked(Site, Tomatoes, Told: true, RemindFrom: Noon.AddHours(31), TrackedAt: Noon.AddHours(31)));

        var tracked = state.TrackedAlerts[Tomatoes.AlertId];
        Assert.False(tracked.OpeningPending);
        Assert.Equal(Noon + (2 * Day), state.DueAt(tracked));
        Assert.Empty(state.DueAlerts(Noon.AddHours(47)));
    }

    [Fact]
    public void ACadenceChangeReschedulesFromThePreviousDueAt()
    {
        var state = Member();
        state.Apply(new AlertTracked(Site, Tomatoes, Told: true, RemindFrom: Noon, TrackedAt: Noon));
        state.Apply(new NotificationSent(Site, NotificationKind.Alert, [Tomatoes.AlertId], Noon, Noon));
        Assert.Equal(Noon + Day, state.DueAt(state.TrackedAlerts[Tomatoes.AlertId]));

        // The Site's cadence, then my own over it, then my own taken back.
        state.Apply(new SiteReminderCadenceSynced(Site, ReminderCadence.Every2Days));
        Assert.Equal(Noon + (2 * Day), state.DueAt(state.TrackedAlerts[Tomatoes.AlertId]));

        state.Apply(new PersonalReminderCadenceChanged(Site, ReminderCadence.Daily, Noon.AddHours(1)));
        Assert.Equal(Noon + Day, state.DueAt(state.TrackedAlerts[Tomatoes.AlertId]));

        state.Apply(new PersonalReminderCadenceChanged(Site, null, Noon.AddHours(2)));
        Assert.Equal(Noon + (2 * Day), state.DueAt(state.TrackedAlerts[Tomatoes.AlertId]));
    }

    [Fact]
    public void HeldDeliveriesOfOneAlertAreOneEntryAndTheSummaryMovesNoDeadline()
    {
        var state = Member();
        var night = Noon.AddHours(11.5);
        var opens = Noon.AddHours(19);
        state.Apply(new AlertTracked(Site, Tomatoes, Told: true, RemindFrom: night, TrackedAt: night));

        state.Apply(new DeliveryHeld(Site, Tomatoes.AlertId, night, opens));
        state.Apply(new DeliveryHeld(Site, Tomatoes.AlertId, night + Day, opens + Day));

        var held = Assert.Single(Assert.Single(state.HeldAlertsBySite()));
        Assert.Equal((night, false), (held.HeldFrom, held.OpeningPending));
        Assert.Equal(opens + Day, state.WindowOpensAt);
        Assert.Equal(night + (2 * Day), state.DueAt(held));

        state.Apply(new NotificationSent(Site, NotificationKind.Summary, [Tomatoes.AlertId], opens + Day, opens + Day));

        Assert.Empty(state.HeldAlertsBySite());
        Assert.Null(state.WindowOpensAt);
        Assert.Equal(night + (2 * Day), state.DueAt(state.TrackedAlerts[Tomatoes.AlertId]));
    }

    [Fact]
    public void ASummaryIsPerSiteWithItsOldestAlertFirst()
    {
        var state = Member();
        state.Apply(new SiteMembershipChanged(Other, SiteRole.Member));
        var basil = Herbs with { AlertId = Guid.Parse("0192f3a4-a000-7000-8000-000000000003"), OpenedAt = Noon.AddHours(-1) };
        var night = Noon.AddHours(11);
        var opens = Noon.AddHours(19);

        foreach (var (site, alert) in new[] { (Site, Tomatoes), (Other, Herbs), (Site, basil) })
        {
            state.Apply(new AlertTracked(site, alert, Told: true, RemindFrom: night, TrackedAt: night));
            state.Apply(new DeliveryHeld(site, alert.AlertId, night, opens));
        }

        var sites = state.HeldAlertsBySite();
        Assert.Equal(2, sites.Count);
        Assert.Equal([basil.AlertId, Tomatoes.AlertId], sites[0].Select(alert => alert.Alert.AlertId));
        Assert.Equal([Herbs.AlertId], sites[1].Select(alert => alert.Alert.AlertId));

        // One Site's summary leaves the other Site's held delivery and the window opening in place.
        state.Apply(new NotificationSent(Site, NotificationKind.Summary, [basil.AlertId, Tomatoes.AlertId], opens, opens));
        Assert.Equal([Herbs.AlertId], Assert.Single(state.HeldAlertsBySite()).Select(alert => alert.Alert.AlertId));
        Assert.Equal(opens, state.WindowOpensAt);
    }

    [Fact]
    public void AnAlertClosedWhileHeldIsGoneWithItsHeldDeliveryAndTheWindowOpening()
    {
        var state = Member();
        var night = Noon.AddHours(11.5);
        state.Apply(new AlertTracked(Site, Tomatoes, Told: true, RemindFrom: night, TrackedAt: night));
        state.Apply(new DeliveryHeld(Site, Tomatoes.AlertId, night, Noon.AddHours(19)));

        state.Apply(new AlertDropped(Tomatoes.AlertId, Noon.AddHours(17)));

        Assert.Empty(state.TrackedAlerts);
        Assert.Empty(state.HeldAlertsBySite());
        Assert.Null(state.WindowOpensAt);
        Assert.False(state.NeedsWaking);

        // Events of an Alert that is gone change nothing.
        state.Apply(new DeliveryHeld(Site, Tomatoes.AlertId, night, Noon.AddHours(19)));
        state.Apply(new NotificationSent(Site, NotificationKind.Reminder, [Tomatoes.AlertId], night, night));
        state.Apply(new AlertDropped(Tomatoes.AlertId, Noon.AddHours(18)));
        Assert.Empty(state.TrackedAlerts);
        Assert.Null(state.WindowOpensAt);
    }

    [Fact]
    public void WhenTheMembershipEndsTheSitesAlertsHeldDeliveriesDeadlinesAndPullAreGone()
    {
        var state = Member();
        state.Apply(new SiteMembershipChanged(Other, SiteRole.Member));
        var night = Noon.AddHours(11.5);
        state.Apply(new AlertTracked(Site, Tomatoes, Told: true, RemindFrom: night, TrackedAt: night));
        state.Apply(new DeliveryHeld(Site, Tomatoes.AlertId, night, Noon.AddHours(19)));
        state.Apply(new AlertTracked(Other, Herbs, Told: true, RemindFrom: Noon, TrackedAt: Noon));

        state.Apply(new SiteMembershipChanged(Site, null));

        Assert.Equal([Herbs.AlertId], state.TrackedAlerts.Keys);
        Assert.Null(state.WindowOpensAt);
        Assert.Equal([Other], state.PendingPulls);

        state.Apply(new SiteMembershipChanged(Other, null));
        Assert.Empty(state.TrackedAlerts);
        Assert.Empty(state.PendingPulls);
        Assert.False(state.NeedsWaking);
    }

    [Fact]
    public void AMutedSiteHasNothingDueAndNothingHeldAndUnmutingStartsNoCatchUp()
    {
        var state = Member();
        state.Apply(new SiteMembershipChanged(Other, SiteRole.Member));
        state.Apply(new AlertTracked(Site, Tomatoes, Told: true, RemindFrom: Noon, TrackedAt: Noon));
        state.Apply(new NotificationSent(Site, NotificationKind.Alert, [Tomatoes.AlertId], Noon, Noon));
        state.Apply(new AlertTracked(Other, Herbs, Told: true, RemindFrom: Noon, TrackedAt: Noon));
        state.Apply(new DeliveryHeld(Site, Tomatoes.AlertId, Noon + Day, Noon.AddHours(43)));

        state.Apply(new SiteMuteChanged(Site, true, Noon.AddHours(30)));

        // Discarded, not held; the other Site is not affected.
        Assert.Empty(state.HeldAlertsBySite());
        Assert.Null(state.WindowOpensAt);
        Assert.Equal([Herbs.AlertId], state.DueAlerts(Noon.AddDays(10)).Select(alert => alert.Alert.AlertId));

        // Muted over the Reminders of day 2 and 3; unmuted half a day after the last of them.
        state.Apply(new SiteMuteChanged(Site, false, Noon.AddDays(3).AddHours(12)));

        var tracked = state.TrackedAlerts[Tomatoes.AlertId];
        Assert.Equal(Noon.AddDays(4), state.DueAt(tracked));
        Assert.DoesNotContain(tracked, state.DueAlerts(Noon.AddDays(4).AddTicks(-1)));
        Assert.Contains(tracked, state.DueAlerts(Noon.AddDays(4)));
    }

    [Fact]
    public void AnAlertThatOpenedOnAMutedSiteGetsNoOpeningNotificationAfterTheUnmute()
    {
        var state = Member();
        state.Apply(new SiteMuteChanged(Site, true, Noon.AddHours(-1)));
        state.Apply(new AlertTracked(Site, Tomatoes, Told: true, RemindFrom: Noon, TrackedAt: Noon));
        Assert.Empty(state.DueAlerts(Noon.AddHours(1)));

        state.Apply(new SiteMuteChanged(Site, false, Noon.AddHours(2)));

        var tracked = state.TrackedAlerts[Tomatoes.AlertId];
        Assert.False(tracked.OpeningPending);
        Assert.Empty(state.DueAlerts(Noon.AddHours(3)));
        Assert.Equal(Noon + Day, state.DueAt(tracked));
    }

    private static UserState Member()
    {
        var state = new UserState();
        state.Apply(new SiteMembershipChanged(Site, SiteRole.Member));
        state.Apply(new SiteAlertsPulled(Site, Noon.AddDays(-1)));
        return state;
    }
}
