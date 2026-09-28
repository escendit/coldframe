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

    private static UserState Completed()
    {
        var state = new UserState();
        state.Apply(new SiteCreationRequested("k1", "site-1", "Home", RequestedAt));
        state.Apply(new SiteCreationCompleted("k1", "site-1"));
        return state;
    }
}
