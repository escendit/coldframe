using Coldframe.Server.Lots;

namespace Coldframe.Server.Tests.Lots;

/// <summary>
/// The one LotStatus rule (AD-14): precedence <c>noNode &gt; paused &gt; unknown &gt; needsCalibration &gt;
/// needsWater &gt; ok</c> over every combination of its five inputs, and the supporting fields that go with
/// the winning status only.
/// </summary>
public sealed class LotStatusRuleTests
{
    public static TheoryData<bool, bool, bool, bool, bool> EveryCombination()
    {
        var data = new TheoryData<bool, bool, bool, bool, bool>();

        for (var bits = 0; bits < 32; bits++)
        {
            data.Add((bits & 1) != 0, (bits & 2) != 0, (bits & 4) != 0, (bits & 8) != 0, (bits & 16) != 0);
        }

        return data;
    }

    [Fact]
    public void ThereAreThirtyTwoCombinations()
    {
        Assert.Equal(32, EveryCombination().Count);
    }

    [Theory]
    [MemberData(nameof(EveryCombination))]
    public void EveryCombinationHasExactlyOneStatusByPrecedence(bool hasNode, bool paused, bool silent, bool uncalibrated, bool lowAlert)
    {
        var inputs = new LotStatusInputs(
            hasNode,
            paused ? LotPauseSources.Device : LotPauseSources.None,
            silent ? LotSilence.Node : LotSilence.None,
            uncalibrated,
            lowAlert);

        // Written out independently of the rule: the first condition that holds wins.
        var expected =
            !hasNode ? "noNode"
            : paused ? "paused"
            : silent ? "unknown"
            : uncalibrated ? "needsCalibration"
            : lowAlert ? "needsWater"
            : "ok";

        var result = LotStatusRule.Evaluate(inputs);

        Assert.Equal(expected, result.Status);
        Assert.Contains(result.Status, LotsReadModel.StatusOrder);
        Assert.Equal(expected == "unknown" ? "node" : null, result.UnknownCause);
        Assert.Equal(expected == "paused" ? ["device"] : [], result.PausedBy);
    }

    [Theory]
    [InlineData(LotSilence.Node, "node")]
    [InlineData(LotSilence.Hub, "hub")]
    public void AnUnknownLotNamesWhatIsSilent(LotSilence silence, string cause)
    {
        var result = LotStatusRule.Evaluate(new LotStatusInputs(true, LotPauseSources.None, silence, true, true));

        Assert.Equal(("unknown", cause), (result.Status, result.UnknownCause));
        Assert.Empty(result.PausedBy);
    }

    [Theory]
    [InlineData(LotPauseSources.Device, new[] { "device" })]
    [InlineData(LotPauseSources.Site, new[] { "site" })]
    [InlineData(LotPauseSources.Device | LotPauseSources.Site, new[] { "device", "site" })]
    public void APausedLotNamesItsSourcesDeviceFirst(LotPauseSources sources, string[] pausedBy)
    {
        var result = LotStatusRule.Evaluate(new LotStatusInputs(true, sources, LotSilence.Hub, true, true));

        Assert.Equal("paused", result.Status);
        Assert.Equal(pausedBy, result.PausedBy);
        Assert.Null(result.UnknownCause);
    }

    [Theory]
    [InlineData(LotPauseSources.Device | LotPauseSources.Site, LotSilence.Hub)]
    [InlineData(LotPauseSources.Site, LotSilence.None)]
    [InlineData(LotPauseSources.None, LotSilence.Node)]
    public void ALotWithoutANodeCarriesNoCauseAndNoPauseSource(LotPauseSources sources, LotSilence silence)
    {
        var result = LotStatusRule.Evaluate(new LotStatusInputs(false, sources, silence, true, true));

        Assert.Equal("noNode", result.Status);
        Assert.Null(result.UnknownCause);
        Assert.Empty(result.PausedBy);
    }

    [Fact]
    public void ThePauseEndsAtTheLatestEndAndIsOpenWhenAnySourceIs()
    {
        var early = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var late = early.AddDays(30);

        Assert.Equal(late, LotStatusRule.PausedUntil([early, late]));
        Assert.Equal(early, LotStatusRule.PausedUntil([early]));
        Assert.Null(LotStatusRule.PausedUntil([early, null]));
        Assert.Null(LotStatusRule.PausedUntil([null]));
        Assert.Null(LotStatusRule.PausedUntil([]));
    }
}
