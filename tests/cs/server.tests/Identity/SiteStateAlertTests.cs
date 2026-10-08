using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Sites;
using Coldframe.Server.Identity;

namespace Coldframe.Server.Tests.Identity;

/// <summary>
/// The Site's set of open Alerts (Story 6.1): rebuilt from <c>site.alert-opened</c> and
/// <c>site.alert-closed</c> on the Site's own stream.
/// </summary>
public sealed class SiteStateAlertTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private static readonly SiteAlert Tomatoes = new(
        Guid.Parse("0192f3a4-a000-7000-8000-000000000001"),
        AlertKind.Threshold,
        ThresholdSide.Low,
        "0192f3a4-8a00-7c3d-8e4f-5a6b7c8d9e02",
        Guid.Parse("dac4e7fe-93b1-56fc-a363-51235a586394"),
        "5a4b3c2d1e0f7c20",
        "soil_moisture",
        Now);

    private static readonly SiteAlert Herbs = Tomatoes with { AlertId = Guid.Parse("0192f3a4-a000-7000-8000-000000000002"), Side = ThresholdSide.High };

    [Fact]
    public void ASiteWithoutAlertsHasNoneOpen()
    {
        Assert.Empty(new SiteState().OpenAlerts);
    }

    [Fact]
    public void AnOpenedAlertIsInTheSetUntilItIsClosed()
    {
        var state = new SiteState();

        state.Apply(new SiteAlertOpened(Tomatoes));
        state.Apply(new SiteAlertOpened(Herbs));

        Assert.Equal(Tomatoes, state.OpenAlerts[Tomatoes.AlertId]);
        Assert.Equal(2, state.OpenAlerts.Count);

        state.Apply(new SiteAlertClosed(Tomatoes.AlertId, AlertCloseReason.Recovered, Now.AddHours(1)));

        Assert.Equal([Herbs.AlertId], state.OpenAlerts.Keys);
    }

    [Fact]
    public void AnEventAppliedAgainChangesNothing()
    {
        var state = new SiteState();

        state.Apply(new SiteAlertOpened(Tomatoes));
        state.Apply(new SiteAlertOpened(Tomatoes));
        state.Apply(new SiteAlertClosed(Tomatoes.AlertId, AlertCloseReason.Recovered, Now));
        state.Apply(new SiteAlertClosed(Tomatoes.AlertId, AlertCloseReason.Recovered, Now));

        Assert.Empty(state.OpenAlerts);
    }
}
