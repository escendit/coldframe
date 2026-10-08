using System.Text.Json;
using Coldframe.Contracts.Lots;
using Coldframe.Server.Edge;
using Coldframe.Server.Lots;
using Coldframe.Server.Tests.Edge;
using Microsoft.AspNetCore.Http;

namespace Coldframe.Server.Tests.Lots;

/// <summary>
/// The Lot's state and the Edge mapping of its read-model row (AD-18, AD-20).
/// </summary>
public sealed class LotStateTests
{
    private static readonly DateTimeOffset Since = new(2026, 10, 6, 7, 2, 0, TimeSpan.Zero);

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
        Assert.Null(EdgeApi.ToLotResponse(new LotView("l", "Beans", "noNode", Since, Removed: false)).Removed);
        Assert.True(EdgeApi.ToLotResponse(new LotView("l", "Beans", "noNode", Since, Removed: true)).Removed);
    }

    [Fact]
    public async Task ALotWithoutSupportingFieldsSendsOnlyItsStatusAndSinceInUtc()
    {
        var since = new DateTimeOffset(2026, 10, 6, 9, 2, 0, 120, TimeSpan.FromHours(2));

        using var body = await SerializeAsync(EdgeApi.ToLotResponse(new LotView("l", "Potatoes", "noNode", since, Removed: false)));

        Assert.Equal(["id", "name", "status", "statusSince"], Names(body.RootElement));
        Assert.Equal("2026-10-06T07:02:00.120Z", body.RootElement.GetProperty("statusSince").GetString());
    }

    [Fact]
    public async Task EveryFieldOfTheReadModelIsMapped()
    {
        var view = new LotView(
            "l",
            "Strawberries",
            "paused",
            Since,
            Removed: false,
            LastReadingAt: Since.AddMinutes(15),
            UnknownCause: "hub",
            PausedBy: ["device", "site"],
            PausedUntil: new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));

        using var body = await SerializeAsync(EdgeApi.ToLotResponse(view));
        var lot = body.RootElement;

        Assert.Equal(["id", "lastReadingAt", "name", "pausedBy", "pausedUntil", "status", "statusSince", "unknownCause"], Names(lot));
        Assert.Equal("2026-10-06T07:02:00.000Z", lot.GetProperty("statusSince").GetString());
        Assert.Equal("2026-10-06T07:17:00.000Z", lot.GetProperty("lastReadingAt").GetString());
        Assert.Equal("hub", lot.GetProperty("unknownCause").GetString());
        Assert.Equal(["device", "site"], lot.GetProperty("pausedBy").EnumerateArray().Select(source => source.GetString()));
        Assert.Equal("2026-11-01T00:00:00.000Z", lot.GetProperty("pausedUntil").GetString());
    }

    [Fact]
    public void AnEmptyPauseSourceListIsNotSent()
    {
        Assert.Null(EdgeApi.ToLotResponse(new LotView("l", "Beans", "ok", Since, Removed: false, PausedBy: [])).PausedBy);
    }

    [Fact]
    public void TheServerSendsAPercentageAndALowThresholdOnlyForACalibratedLot()
    {
        var sent = typeof(LotResponse).GetProperties().Select(property => property.Name);

        Assert.Contains("MoisturePercent", sent);
        Assert.Contains("LowThresholdPercent", sent);

        // A Lot whose newest soil-moisture Reading has no Calibration carries neither (Story 5.4).
        var uncalibrated = new LotResponse("lot", "Tomatoes", "ok", "2026-10-06T07:00:00.000Z");
        Assert.Null(uncalibrated.MoisturePercent);
        Assert.Null(uncalibrated.LowThresholdPercent);
    }

    [Theory]
    [InlineData(false, false, LotSilence.None)]
    [InlineData(false, true, LotSilence.None)]
    [InlineData(true, false, LotSilence.Node)]
    [InlineData(true, true, LotSilence.None)]
    public void ANodeThatDeclaredNothingIsTheOnlySilenceTheProjectorKnows(bool hasNode, bool hasDeclared, LotSilence silence)
    {
        var inputs = LotsProjector.InputsOf(hasNode, hasDeclared, LotPauseSources.Site, uncalibratedSoilSensor: true, openLowAlert: false);

        Assert.Equal(new LotStatusInputs(hasNode, LotPauseSources.Site, silence, true, OpenLowAlert: false), inputs);
    }

    [Theory]
    [InlineData(true, "needsWater")]
    [InlineData(false, "ok")]
    public void AnOpenLowSideAlertOnASoilSensorIsTheProjectorsNeedsWater(bool openLowAlert, string status)
    {
        var inputs = LotsProjector.InputsOf(hasNode: true, hasDeclared: true, LotPauseSources.None, uncalibratedSoilSensor: false, openLowAlert);

        Assert.Equal(openLowAlert, inputs.OpenLowAlert);
        Assert.Equal(status, LotStatusRule.Evaluate(inputs).Status);
    }

    [Theory]
    [InlineData("alert/0760cb39-dfed-5779-9344-f44689933ee4", "0760cb39-dfed-5779-9344-f44689933ee4")]
    [InlineData("alert/not-a-uuid", null)]
    [InlineData("sensor/0760cb39-dfed-5779-9344-f44689933ee4", null)]
    public void AlertStreamsAreProjected(string streamId, string? alertId)
    {
        Assert.Equal(alertId, LotsProjector.AlertIdOf(streamId)?.ToString());
    }

    [Theory]
    [InlineData("lot/0192f3a4-8a00-7c3d-8e4f-5a6b7c8d9e02", "0192f3a4-8a00-7c3d-8e4f-5a6b7c8d9e02", null, null)]
    [InlineData("device/92064422c012f481", null, "92064422c012f481", null)]
    [InlineData("sensor/5b1f6c1e-3a52-5c0e-9d1b-0a4a8f6f2c11", null, null, "5b1f6c1e-3a52-5c0e-9d1b-0a4a8f6f2c11")]
    [InlineData("sensor/not-a-uuid", null, null, null)]
    [InlineData("site/0192f3a4-7c1e-7d2b-9a51-3f7e2c9b1d00", null, null, null)]
    public void LotDeviceAndSensorStreamsAreProjected(string streamId, string? lotId, string? deviceId, string? sensorId)
    {
        Assert.Equal(lotId, LotsProjector.LotIdOf(streamId));
        Assert.Equal(deviceId, LotsProjector.DeviceIdOf(streamId));
        Assert.Equal(sensorId, LotsProjector.SensorIdOf(streamId)?.ToString());
    }

    [Theory]
    [InlineData("0192F3A4-8A00-7C3D-8E4F-5A6B7C8D9E01", "0192f3a4-8a00-7c3d-8e4f-5a6b7c8d9e01")]
    [InlineData("not-a-lot", null)]
    [InlineData("", null)]
    public void LotIdsAreCanonicalized(string raw, string? expected)
    {
        Assert.Equal(expected, EdgeApi.CanonicalizeLotId(raw));
    }

    private static List<string> Names(JsonElement lot) =>
        [.. lot.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)];

    // Through the Server's own JSON options, exactly as the endpoint writes it.
    private static async Task<JsonDocument> SerializeAsync(LotResponse response)
    {
        await using var app = EdgeTestHost.Build();
        using var stream = new MemoryStream();
        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Response.Body = stream;

        await TypedResults.Ok(response).ExecuteAsync(context);

        stream.Position = 0;
        return await JsonDocument.ParseAsync(stream, cancellationToken: TestContext.Current.CancellationToken);
    }
}
