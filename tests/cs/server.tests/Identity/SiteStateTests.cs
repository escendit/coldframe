using Coldframe.Contracts.Sites;
using Coldframe.Server.Identity;

namespace Coldframe.Server.Tests.Identity;

/// <summary>
/// The Site's Reminder cadence (Story 6.3): daily until <c>site.reminder-cadence-changed</c> says otherwise.
/// </summary>
public sealed class SiteStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ASiteThatNeverChangedItRemindsDaily()
    {
        var state = new SiteState();
        state.Apply(new SiteCreated("Home", "owner"));

        Assert.Equal(ReminderCadence.Daily, state.ReminderCadence);
    }

    [Fact]
    public void TheCadenceFollowsItsLastChange()
    {
        var state = new SiteState();
        state.Apply(new SiteCreated("Home", "owner"));

        state.Apply(new SiteReminderCadenceChanged(ReminderCadence.Every2Days, Now));
        Assert.Equal(ReminderCadence.Every2Days, state.ReminderCadence);

        state.Apply(new SiteReminderCadenceChanged(ReminderCadence.Daily, Now));
        Assert.Equal(ReminderCadence.Daily, state.ReminderCadence);
    }

    [Fact]
    public void ARenameOrAMembershipLeavesTheCadence()
    {
        var state = new SiteState();
        state.Apply(new SiteCreated("Home", "owner"));
        state.Apply(new SiteReminderCadenceChanged(ReminderCadence.Every2Days, Now));

        state.Apply(new SiteRenamed("Allotment"));
        state.Apply(new MembershipGranted("member", SiteRole.Member));
        state.Apply(new MembershipRevoked("member"));

        Assert.Equal(ReminderCadence.Every2Days, state.ReminderCadence);
    }
}
