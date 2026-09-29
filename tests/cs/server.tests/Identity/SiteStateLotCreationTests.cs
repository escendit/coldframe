using Coldframe.Contracts.Sites;
using Coldframe.Server.Identity;

namespace Coldframe.Server.Tests.Identity;

/// <summary>
/// The Site grain keeps Lot-creation keys like the User grain keeps Site-creation keys: 24 h after the
/// request once completed, and until completion while pending.
/// </summary>
public sealed class SiteStateLotCreationTests
{
    private static readonly DateTimeOffset RequestedAt = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ACompletedKeyHoldsForJustUnder24Hours()
    {
        var state = new SiteState();
        state.Apply(new LotCreationRequested("u:k1", "lot-1", "Tomatoes", RequestedAt));
        state.Apply(new LotCreationCompleted("u:k1"));

        Assert.Equal("lot-1", state.FindLiveLotCreation("u:k1", RequestedAt + TimeSpan.FromHours(24) - TimeSpan.FromTicks(1))?.LotId);
        Assert.Null(state.FindLiveLotCreation("u:k1", RequestedAt + TimeSpan.FromHours(24)));
    }

    [Fact]
    public void APendingKeyDoesNotExpire()
    {
        var state = new SiteState();
        state.Apply(new LotCreationRequested("u:k1", "lot-1", "Tomatoes", RequestedAt));

        var creation = state.FindLiveLotCreation("u:k1", RequestedAt + TimeSpan.FromDays(3));
        Assert.Equal("lot-1", creation?.LotId);
        Assert.False(creation?.Completed);
    }

    [Fact]
    public void KeysAreScopedToTheirCaller()
    {
        var state = new SiteState();
        state.Apply(new LotCreationRequested("alice:k1", "lot-1", "Tomatoes", RequestedAt));

        Assert.Null(state.FindLiveLotCreation("bob:k1", RequestedAt));
    }
}
