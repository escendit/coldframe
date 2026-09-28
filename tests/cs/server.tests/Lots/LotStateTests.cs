using Coldframe.Contracts.Lots;
using Coldframe.Server.Edge;
using Coldframe.Server.Lots;

namespace Coldframe.Server.Tests.Lots;

/// <summary>
/// The Lot's state and the Edge mapping of its read-model row (AD-18, AD-20).
/// </summary>
public sealed class LotStateTests
{
    [Fact]
    public void ARemovedLotKeepsItsNameAndSite()
    {
        var state = new LotState();
        state.Apply(new LotCreated("site-1", "Tomatoes"));
        state.Apply(new LotRemoved());

        Assert.Equal(LotLifecycle.Removed, state.Lifecycle);
        Assert.Equal(("site-1", "Tomatoes"), (state.SiteId, state.Name));
    }

    [Fact]
    public void AReleaseByAnotherNodeKeepsTheClaim()
    {
        var state = new LotState();
        state.Apply(new LotCreated("site-1", "Tomatoes"));
        state.Apply(new LotClaimed("7C19"));
        state.Apply(new LotReleased("7C20"));

        Assert.Equal("7C19", state.ClaimedBy);

        state.Apply(new LotReleased("7C19"));
        Assert.Null(state.ClaimedBy);
    }

    [Fact]
    public void TheStatusOrderIsTheAd14Order()
    {
        Assert.Equal(["needsWater", "needsCalibration", "unknown", "ok", "paused", "noNode"], LotsReadModel.StatusOrder);
    }

    [Fact]
    public void RemovedIsSentOnlyWhenTrue()
    {
        Assert.Null(EdgeApi.ToLotResponse(new LotView("l", "Beans", "noNode", Removed: false)).Removed);
        Assert.True(EdgeApi.ToLotResponse(new LotView("l", "Beans", "noNode", Removed: true)).Removed);
    }

    [Theory]
    [InlineData("0192F3A4-8A00-7C3D-8E4F-5A6B7C8D9E01", "0192f3a4-8a00-7c3d-8e4f-5a6b7c8d9e01")]
    [InlineData("not-a-lot", null)]
    [InlineData("", null)]
    public void LotIdsAreCanonicalized(string raw, string? expected)
    {
        Assert.Equal(expected, EdgeApi.CanonicalizeLotId(raw));
    }
}
