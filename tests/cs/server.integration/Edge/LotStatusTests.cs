using System.Globalization;
using System.Net;
using System.Text.Json;
using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Lots;
using Coldframe.Contracts.Sensors;
using Coldframe.Contracts.Sites;
using Coldframe.DeviceSimulator;
using Coldframe.Server.IntegrationTests.Devices;
using Coldframe.Server.Journal;
using Coldframe.Server.Lots;
using ProtocolKind = Coldframe.Protocol.Setup.V1.DeviceKind;

namespace Coldframe.Server.IntegrationTests.Edge;

/// <summary>
/// The LotStatus projection at the REST surface (Story 4.7; AD-14, AD-21): one test per Server row of the
/// story's matrix, from "No Node" to "List order", and one through the Device simulator.
/// </summary>
/// <remarks>
/// Device and Sensor events are seeded on fresh streams exactly as the grains journal them, because nothing
/// pauses a Device before Epic 8. <c>needsWater</c> comes from Alert events (Story 6.1), seeded the same way.
/// <c>unknown</c> with cause <c>hub</c> has no producer before Epic 7, so the list-order test writes that row
/// into <c>lots</c> itself.
/// </remarks>
[Collection(IngestSuites.Name)]
public sealed class LotStatusTests(EdgeApiFixture edge) : IClassFixture<EdgeApiFixture>
{
    private const string Projector = "lots";

    private static readonly SensorSpecification Soil = new("soil_moisture", SensorUnit.RawCount, 0, 4095, Calibration: true, DefaultLow: 30);

    private static readonly SensorSpecification Temperature = new("temperature", SensorUnit.MilliDegreeCelsius, -40_000, 85_000, Calibration: false);

    [Fact]
    public async Task ALotWithoutANodeIsNoNodeSinceItWasCreatedOrReleased()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, member) = await SeedSiteAsync(cancellationToken);

        var created = await edge.SeedLotAsync(siteId, "Created", cancellationToken);
        var released = await edge.SeedLotAsync(siteId, "Released", cancellationToken, new LotClaimed("7c19"), new LotReleased("7c19"));

        var lots = await ListAsync(member, siteId, cancellationToken);

        Assert.Equal([created, released], lots.Select(lot => lot.GetProperty("id").GetString()));
        Assert.All(lots, lot => Assert.Equal(["id", "name", "status", "statusSince"], Names(lot)));
        Assert.All(lots, lot => Assert.Equal("noNode", lot.GetProperty("status").GetString()));
        Assert.Equal(await RecordedAtAsync($"lot/{created}", 0, cancellationToken), lots[0].GetProperty("statusSince").GetString());
        Assert.Equal(await RecordedAtAsync($"lot/{released}", 2, cancellationToken), lots[1].GetProperty("statusSince").GetString());
    }

    [Fact]
    public async Task ALotWhoseNodeDeclaredNothingIsUnknownByItsNodeWithoutAReadingTime()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, member) = await SeedSiteAsync(cancellationToken);

        // A Node ID without a Device stream, and an enrolled Node that has not reported.
        var stranger = await edge.SeedLotAsync(siteId, "Stranger", cancellationToken, new LotClaimed("7c19"));
        var node = await SeedNodeAsync(siteId, cancellationToken);
        var enrolled = await edge.SeedLotAsync(siteId, "Enrolled", cancellationToken, new LotClaimed(node));

        var lots = await ListAsync(member, siteId, cancellationToken);

        Assert.Equal([stranger, enrolled], lots.Select(lot => lot.GetProperty("id").GetString()));

        foreach (var lot in lots)
        {
            Assert.Equal(["id", "name", "status", "statusSince", "unknownCause"], Names(lot));
            Assert.Equal("unknown", lot.GetProperty("status").GetString());
            Assert.Equal("node", lot.GetProperty("unknownCause").GetString());
            Assert.Equal(
                await RecordedAtAsync($"lot/{lot.GetProperty("id").GetString()}", 1, cancellationToken),
                lot.GetProperty("statusSince").GetString());
        }
    }

    [Fact]
    public async Task ANodeWithAnUncalibratedSoilSensorNeedsCalibrationWithItsNewestReadingTime()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, member) = await SeedSiteAsync(cancellationToken);
        var node = await SeedNodeAsync(siteId, cancellationToken);
        var lotId = await edge.SeedLotAsync(siteId, "Peppers", cancellationToken, new LotClaimed(node));
        var claimedAt = (await edge.ReadStreamAsync($"lot/{lotId}", cancellationToken))[1].RecordedAt;
        var declaredAt = claimedAt.AddMinutes(1);

        var sensors = await DeclareAsync(node, declaredAt, cancellationToken, Soil, Temperature);

        // One Reading from before the Node took the Lot, two after it; the newest of those counts.
        await StoreReadingAsync(node, sensors[0], 1, claimedAt.AddHours(-1), cancellationToken);
        await StoreReadingAsync(node, sensors[0], 2, claimedAt.AddMinutes(2), cancellationToken);
        await StoreReadingAsync(node, sensors[1], 3, claimedAt.AddMinutes(17), cancellationToken);

        var lot = Assert.Single(await ListAsync(member, siteId, cancellationToken));

        Assert.Equal(["id", "lastReadingAt", "name", "status", "statusSince"], Names(lot));
        Assert.Equal("needsCalibration", lot.GetProperty("status").GetString());
        Assert.Equal(Format(declaredAt), lot.GetProperty("statusSince").GetString());
        Assert.Equal(Format(claimedAt.AddMinutes(17)), lot.GetProperty("lastReadingAt").GetString());

        // The single Lot answers the same, plus its Node and Sensors (Story 4.8).
        using var server = edge.CreateServerClient(member.AccessToken);
        using var response = await server.GetAsync(new Uri($"/sites/{siteId}/lots/{lotId}", UriKind.Relative), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var single = await EdgeApiFixture.ReadJsonAsync(response, cancellationToken);
        Assert.Equal(
            Names(lot).Concat(["node", "sensors"]).Order(StringComparer.Ordinal),
            Names(single.RootElement));
        foreach (var property in lot.EnumerateObject())
        {
            Assert.Equal(property.Value.GetRawText(), single.RootElement.GetProperty(property.Name).GetRawText());
        }

        Assert.Equal(node, single.RootElement.GetProperty("node").GetProperty("deviceId").GetString());
    }

    [Fact]
    public async Task ANodeWithoutACalibratingSoilSensorIsOk()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, member) = await SeedSiteAsync(cancellationToken);
        var node = await SeedNodeAsync(siteId, cancellationToken);
        var lotId = await edge.SeedLotAsync(siteId, "Herbs", cancellationToken, new LotClaimed(node));
        var declaredAt = (await edge.ReadStreamAsync($"lot/{lotId}", cancellationToken))[1].RecordedAt.AddMinutes(1);

        await DeclareAsync(node, declaredAt, cancellationToken, Temperature, Soil with { Calibration = false });

        var lot = Assert.Single(await ListAsync(member, siteId, cancellationToken));

        Assert.Equal(["id", "name", "status", "statusSince"], Names(lot));
        Assert.Equal("ok", lot.GetProperty("status").GetString());
        Assert.Equal(Format(declaredAt), lot.GetProperty("statusSince").GetString());
    }

    [Fact]
    public async Task AChangedSpecificationMovesTheLotBetweenNeedsCalibrationAndOkAtItsTime()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, member) = await SeedSiteAsync(cancellationToken);
        var node = await SeedNodeAsync(siteId, cancellationToken);
        var lotId = await edge.SeedLotAsync(siteId, "Peppers", cancellationToken, new LotClaimed(node));
        var declaredAt = (await edge.ReadStreamAsync($"lot/{lotId}", cancellationToken))[1].RecordedAt.AddMinutes(1);
        var soil = (await DeclareAsync(node, declaredAt, cancellationToken, Soil))[0];

        async Task<JsonElement> AfterAsync(int version, SensorSpecification specification, DateTimeOffset changedAt)
        {
            var last = await edge.AppendAsync($"sensor/{soil}", version, [new SensorSpecificationChanged(specification, changedAt)], cancellationToken);
            await edge.WaitForProjectionCheckpointAsync(Projector, last);
            return Assert.Single(await ListAsync(member, siteId, cancellationToken));
        }

        var lot = Assert.Single(await ListAsync(member, siteId, cancellationToken));
        Assert.Equal("needsCalibration", lot.GetProperty("status").GetString());

        // The Sensor no longer calibrates: nothing is uncalibrated.
        lot = await AfterAsync(1, Soil with { Calibration = false }, declaredAt.AddMinutes(1));
        Assert.Equal("ok", lot.GetProperty("status").GetString());
        Assert.Equal(Format(declaredAt.AddMinutes(1)), lot.GetProperty("statusSince").GetString());

        // And back.
        lot = await AfterAsync(2, Soil, declaredAt.AddMinutes(2));
        Assert.Equal("needsCalibration", lot.GetProperty("status").GetString());
        Assert.Equal(Format(declaredAt.AddMinutes(2)), lot.GetProperty("statusSince").GetString());
    }

    [Fact]
    public async Task ALaterSpecificationSetReplacesTheFirst()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, member) = await SeedSiteAsync(cancellationToken);
        var node = await SeedNodeAsync(siteId, cancellationToken);
        var lotId = await edge.SeedLotAsync(siteId, "Herbs", cancellationToken, new LotClaimed(node));
        var declaredAt = (await edge.ReadStreamAsync($"lot/{lotId}", cancellationToken))[1].RecordedAt.AddMinutes(1);

        await DeclareAsync(node, declaredAt, cancellationToken, Temperature);
        var lot = Assert.Single(await ListAsync(member, siteId, cancellationToken));
        Assert.Equal("ok", lot.GetProperty("status").GetString());

        // The Node gets a soil probe: the new set counts, not the first.
        await DeclareAsync(node, declaredAt.AddMinutes(5), cancellationToken, Temperature, Soil);
        lot = Assert.Single(await ListAsync(member, siteId, cancellationToken));
        Assert.Equal("needsCalibration", lot.GetProperty("status").GetString());
        Assert.Equal(Format(declaredAt.AddMinutes(5)), lot.GetProperty("statusSince").GetString());
    }

    [Fact]
    public async Task ANodePausedBeforeItsFirstDeclarationNeedsCalibrationOnceItDeclaredAndResumed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, member) = await SeedSiteAsync(cancellationToken);
        var node = await SeedNodeAsync(siteId, cancellationToken);
        var lotId = await edge.SeedLotAsync(siteId, "Strawberries", cancellationToken, new LotClaimed(node));
        var start = (await edge.ReadStreamAsync($"lot/{lotId}", cancellationToken))[1].RecordedAt.AddMinutes(1);

        // The Pause writes the Device's row before any declaration does.
        var last = await edge.AppendAsync($"device/{node}", 1, [new DevicePaused(DevicePauseSource.Site, null, start)], cancellationToken);
        await edge.WaitForProjectionCheckpointAsync(Projector, last);
        var lot = Assert.Single(await ListAsync(member, siteId, cancellationToken));
        Assert.Equal("paused", lot.GetProperty("status").GetString());

        await DeclareAsync(node, start.AddMinutes(1), cancellationToken, Soil);
        lot = Assert.Single(await ListAsync(member, siteId, cancellationToken));
        Assert.Equal("paused", lot.GetProperty("status").GetString());
        Assert.Equal(Format(start), lot.GetProperty("statusSince").GetString());

        last = await edge.AppendAsync($"device/{node}", 3, [new DeviceResumed(DevicePauseSource.Site, start.AddMinutes(2))], cancellationToken);
        await edge.WaitForProjectionCheckpointAsync(Projector, last);
        lot = Assert.Single(await ListAsync(member, siteId, cancellationToken));
        Assert.Equal(["id", "name", "status", "statusSince"], Names(lot));
        Assert.Equal("needsCalibration", lot.GetProperty("status").GetString());
        Assert.Equal(Format(start.AddMinutes(2)), lot.GetProperty("statusSince").GetString());
    }

    [Fact]
    public async Task APausedLotNamesItsSourcesAndItsEndAndGetsItsStatusBackAfterTheLastResume()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, member) = await SeedSiteAsync(cancellationToken);
        var node = await SeedNodeAsync(siteId, cancellationToken);
        var lotId = await edge.SeedLotAsync(siteId, "Strawberries", cancellationToken, new LotClaimed(node));
        var start = (await edge.ReadStreamAsync($"lot/{lotId}", cancellationToken))[1].RecordedAt.AddMinutes(1);
        await DeclareAsync(node, start, cancellationToken, Soil);
        var stream = $"device/{node}";
        var november = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var march = new DateTimeOffset(2027, 3, 1, 0, 0, 0, TimeSpan.Zero);

        async Task<JsonElement> AfterAsync(int version, object @event)
        {
            var last = await edge.AppendAsync(stream, version, [@event], cancellationToken);
            await edge.WaitForProjectionCheckpointAsync(Projector, last);
            return Assert.Single(await ListAsync(member, siteId, cancellationToken));
        }

        // The Device itself, with an end.
        var lot = await AfterAsync(2, new DevicePaused(DevicePauseSource.Device, november, start.AddMinutes(1)));
        Assert.Equal(["id", "name", "pausedBy", "pausedUntil", "status", "statusSince"], Names(lot));
        Assert.Equal("paused", lot.GetProperty("status").GetString());
        Assert.Equal(["device"], Strings(lot.GetProperty("pausedBy")));
        Assert.Equal(Format(november), lot.GetProperty("pausedUntil").GetString());
        Assert.Equal(Format(start.AddMinutes(1)), lot.GetProperty("statusSince").GetString());

        // The Site as well, with a later end: both sources, the latest end, and the same since.
        lot = await AfterAsync(3, new DevicePaused(DevicePauseSource.Site, march, start.AddMinutes(2)));
        Assert.Equal(["device", "site"], Strings(lot.GetProperty("pausedBy")));
        Assert.Equal(Format(march), lot.GetProperty("pausedUntil").GetString());
        Assert.Equal(Format(start.AddMinutes(1)), lot.GetProperty("statusSince").GetString());

        // The Site's Pause loses its end: no end at all.
        lot = await AfterAsync(4, new DevicePaused(DevicePauseSource.Site, null, start.AddMinutes(3)));
        Assert.Equal(["id", "name", "pausedBy", "status", "statusSince"], Names(lot));
        Assert.Equal(["device", "site"], Strings(lot.GetProperty("pausedBy")));

        // The Device resumes: still paused, by the Site alone.
        lot = await AfterAsync(5, new DeviceResumed(DevicePauseSource.Device, start.AddMinutes(4)));
        Assert.Equal("paused", lot.GetProperty("status").GetString());
        Assert.Equal(["site"], Strings(lot.GetProperty("pausedBy")));
        Assert.Equal(Format(start.AddMinutes(1)), lot.GetProperty("statusSince").GetString());

        // The last source clears: the status from before the Pause, since the resume.
        lot = await AfterAsync(6, new DeviceResumed(DevicePauseSource.Site, start.AddMinutes(5)));
        Assert.Equal(["id", "name", "status", "statusSince"], Names(lot));
        Assert.Equal("needsCalibration", lot.GetProperty("status").GetString());
        Assert.Equal(Format(start.AddMinutes(5)), lot.GetProperty("statusSince").GetString());
    }

    [Fact]
    public async Task ASensorDeclaredBeforeItsDeviceEventAndEventsAppliedAgainLeaveTheRowsAsTheyAre()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, member) = await SeedSiteAsync(cancellationToken);
        var node = await SeedNodeAsync(siteId, cancellationToken);
        var lotId = await edge.SeedLotAsync(siteId, "Beans", cancellationToken, new LotClaimed(node));
        var claimedAt = (await edge.ReadStreamAsync($"lot/{lotId}", cancellationToken))[1].RecordedAt;
        var sensorId = Guid.NewGuid();

        // The Sensor grain journals first; the Device's set does not hold the Sensor yet, so nothing changes.
        var last = await edge.AppendAsync($"sensor/{sensorId}", [new SensorDeclared(node, 0, Soil, claimedAt.AddMinutes(1))], cancellationToken);
        await edge.WaitForProjectionCheckpointAsync(Projector, last);

        var before = Assert.Single(await ListAsync(member, siteId, cancellationToken));
        Assert.Equal("unknown", before.GetProperty("status").GetString());
        Assert.Equal(Format(claimedAt), before.GetProperty("statusSince").GetString());

        var declaredAt = claimedAt.AddMinutes(2);
        last = await edge.AppendAsync(
            $"device/{node}",
            1,
            [new DeviceSpecificationsDeclared([1, 2, 3], [new DeclaredSensor(0, Soil.Quantity, sensorId)], declaredAt)],
            cancellationToken);
        await edge.WaitForProjectionCheckpointAsync(Projector, last);

        var declared = Assert.Single(await ListAsync(member, siteId, cancellationToken));
        Assert.Equal("needsCalibration", declared.GetProperty("status").GetString());
        Assert.Equal(Format(declaredAt), declared.GetProperty("statusSince").GetString());

        // Every event of the three streams once more, in any order, through the projector itself.
        var again = new List<JournalEvent>();
        again.AddRange(await edge.ReadStreamAsync($"device/{node}", cancellationToken));
        again.AddRange(await edge.ReadStreamAsync($"sensor/{sensorId}", cancellationToken));
        again.AddRange(await edge.ReadStreamAsync($"lot/{lotId}", cancellationToken));

        await using (var connection = await edge.Database.OpenConnectionAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            var projector = new LotsProjector();

            foreach (var journalEvent in again)
            {
                await projector.ApplyAsync(journalEvent, transaction, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        Assert.Equal(declared.GetRawText(), Assert.Single(await ListAsync(member, siteId, cancellationToken)).GetRawText());

        // The same set declared again later keeps the status, so its time does not move.
        last = await edge.AppendAsync(
            $"device/{node}",
            2,
            [new DeviceSpecificationsDeclared([1, 2, 3], [new DeclaredSensor(0, Soil.Quantity, sensorId)], declaredAt.AddHours(1))],
            cancellationToken);
        await edge.WaitForProjectionCheckpointAsync(Projector, last);

        Assert.Equal(declared.GetRawText(), Assert.Single(await ListAsync(member, siteId, cancellationToken)).GetRawText());
    }

    [Fact]
    public async Task OneLotPerStatusIsListedInTheServersOrderWithOnlyTheFieldsThatApply()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, member) = await SeedSiteAsync(cancellationToken);
        var at = TimeProvider.System.GetUtcNow().AddMinutes(10);

        // Created in the reverse of the Server's order, so the order is the status's and not the creation's.
        var noNode = await edge.SeedLotAsync(siteId, "Potatoes", cancellationToken);

        var pausedNode = await SeedNodeAsync(siteId, cancellationToken);
        var paused = await edge.SeedLotAsync(siteId, "Strawberries", cancellationToken, new LotClaimed(pausedNode));
        var last = await edge.AppendAsync($"device/{pausedNode}", 1, [new DevicePaused(DevicePauseSource.Site, null, at)], cancellationToken);
        await edge.WaitForProjectionCheckpointAsync(Projector, last);

        var okNode = await SeedNodeAsync(siteId, cancellationToken);
        var ok = await edge.SeedLotAsync(siteId, "Herbs", cancellationToken, new LotClaimed(okNode));
        await DeclareAsync(okNode, at, cancellationToken, Temperature);

        var hubSilent = await edge.SeedLotAsync(siteId, "Lettuce", cancellationToken, new LotClaimed("aa01"));
        var nodeSilent = await edge.SeedLotAsync(siteId, "Beans", cancellationToken, new LotClaimed("aa02"));

        var calibrationNode = await SeedNodeAsync(siteId, cancellationToken);
        var needsCalibration = await edge.SeedLotAsync(siteId, "Peppers", cancellationToken, new LotClaimed(calibrationNode));
        await DeclareAsync(calibrationNode, at, cancellationToken, Soil);

        // A calibrated soil Sensor with an open low-side Threshold Alert: the Lot needs water.
        var dryNode = await SeedNodeAsync(siteId, cancellationToken);
        var needsWater = await edge.SeedLotAsync(siteId, "Tomatoes", cancellationToken, new LotClaimed(dryNode));
        var drySoil = (await DeclareAsync(dryNode, at, cancellationToken, Soil))[0];
        await CalibrateAsync(drySoil, at, cancellationToken);
        await OpenAlertAsync(siteId, needsWater, dryNode, drySoil, ThresholdSide.Low, Soil.Quantity, at, cancellationToken);

        // The one status without a producer, written as the projector will store it.
        await using (var seed = edge.Database.CreateCommand(
            "UPDATE lots SET status = 'unknown', status_since = @at, unknown_cause = 'hub' WHERE lot_id = @hub_silent"))
        {
            seed.Parameters.AddWithValue("at", at);
            seed.Parameters.AddWithValue("hub_silent", hubSilent);
            Assert.Equal(1, await seed.ExecuteNonQueryAsync(cancellationToken));
        }

        var lots = await ListAsync(member, siteId, cancellationToken);

        string[] core = ["id", "name", "status", "statusSince"];
        (string Id, string Name, string Status, string[] Names)[] expected =
        [
            (needsWater, "Tomatoes", "needsWater", core),
            (needsCalibration, "Peppers", "needsCalibration", core),
            (hubSilent, "Lettuce", "unknown", [.. core, "unknownCause"]),
            (nodeSilent, "Beans", "unknown", [.. core, "unknownCause"]),
            (ok, "Herbs", "ok", core),
            (paused, "Strawberries", "paused", ["id", "name", "pausedBy", "status", "statusSince"]),
            (noNode, "Potatoes", "noNode", core),
        ];

        Assert.Equal(
            expected.Select(lot => (lot.Id, lot.Name, lot.Status)),
            lots.Select(lot => (lot.GetProperty("id").GetString()!, lot.GetProperty("name").GetString()!, lot.GetProperty("status").GetString()!)));
        Assert.Equal(expected.Select(lot => lot.Names.ToList()), lots.Select(Names));
        Assert.Equal(["hub", "node"], lots.Skip(2).Take(2).Select(lot => lot.GetProperty("unknownCause").GetString()));
        Assert.Equal(["site"], Strings(lots[5].GetProperty("pausedBy")));
        Assert.All(lots, lot => Assert.EndsWith("Z", lot.GetProperty("statusSince").GetString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnOpenLowSideAlertOnASoilSensorIsNeedsWaterUntilItClosesAndOtherAlertsChangeNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (siteId, member) = await SeedSiteAsync(cancellationToken);
        var node = await SeedNodeAsync(siteId, cancellationToken);
        var lotId = await edge.SeedLotAsync(siteId, "Tomatoes", cancellationToken, new LotClaimed(node));
        var start = Now().AddMinutes(10);
        var sensors = await DeclareAsync(node, start, cancellationToken, Soil, Temperature);
        await CalibrateAsync(sensors[0], start.AddMinutes(1), cancellationToken);

        async Task<(string? Status, string? Since)> StatusAsync()
        {
            var lot = Assert.Single(await ListAsync(member, siteId, cancellationToken));
            return (lot.GetProperty("status").GetString(), lot.GetProperty("statusSince").GetString());
        }

        var ok = ("ok", Format(start.AddMinutes(1)));
        Assert.Equal(ok, await StatusAsync());

        // Too wet, and an Alert of another quantity: neither is "needs water".
        await OpenAlertAsync(siteId, lotId, node, sensors[0], ThresholdSide.High, Soil.Quantity, start.AddMinutes(2), cancellationToken);
        await OpenAlertAsync(siteId, lotId, node, sensors[1], ThresholdSide.Low, Temperature.Quantity, start.AddMinutes(3), cancellationToken);
        Assert.Equal(ok, await StatusAsync());

        // A low-side Alert of a soil Sensor of another Node is not this Lot's.
        var otherNode = await SeedNodeAsync(siteId, cancellationToken);
        await OpenAlertAsync(siteId, lotId, otherNode, Guid.NewGuid(), ThresholdSide.Low, Soil.Quantity, start.AddMinutes(4), cancellationToken);
        Assert.Equal(ok, await StatusAsync());

        var alertId = await OpenAlertAsync(siteId, lotId, node, sensors[0], ThresholdSide.Low, Soil.Quantity, start.AddMinutes(5), cancellationToken);
        var needsWater = ("needsWater", Format(start.AddMinutes(5)));
        Assert.Equal(needsWater, await StatusAsync());

        // The Alert's events once more through the projector itself: nothing moves.
        var again = await edge.ReadStreamAsync($"alert/{alertId}", cancellationToken);
        await using (var connection = await edge.Database.OpenConnectionAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            await new LotsProjector().ApplyAsync(again[0], transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        Assert.Equal(needsWater, await StatusAsync());

        // Closed: back to ok at the time of the close, whatever else is still open.
        var last = await edge.AppendAsync(
            $"alert/{alertId}",
            1,
            [new AlertClosed(AlertCloseReason.Recovered, start.AddMinutes(50))],
            cancellationToken);
        await edge.WaitForProjectionCheckpointAsync(Projector, last);

        Assert.Equal(("ok", Format(start.AddMinutes(50))), await StatusAsync());
    }

    [Fact]
    public async Task ANodeEnrolledOnALotThatDeclaresAndReportsNeedsCalibrationWithItsReadingTime()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var administrator = await edge.CreateUserAsync("lot-status-admin", cancellationToken);
        var siteId = Guid.CreateVersion7().ToString();
        var seeded = await edge.AppendAsync(
            $"site/{siteId}",
            [new SiteCreated("Lot status", administrator.UserId), new MembershipGranted(administrator.UserId, SiteRole.Owner)],
            cancellationToken);
        await edge.WaitForIdentityCheckpointAsync(seeded);
        var lotId = await edge.SeedLotAsync(siteId, "Tomatoes", cancellationToken);

        var hub = await EnrolAsync(siteId, administrator, ProtocolKind.Hub, null, cancellationToken);
        var node = await EnrolAsync(siteId, administrator, ProtocolKind.Node, lotId, cancellationToken);

        // Assigned and silent so far: the claim brought the projection up to date before the enrolment answered.
        var assigned = Assert.Single(await ListAsync(administrator, siteId, cancellationToken));
        Assert.Equal(("unknown", "node"), (assigned.GetProperty("status").GetString(), assigned.GetProperty("unknownCause").GetString()));
        Assert.False(assigned.TryGetProperty("lastReadingAt", out _));

        // The first report is asked for the Specification set; the second declares it.
        var asked = Assert.Single(await IngestAsync(hub, node.SealFrame(node.Wake(Now())), cancellationToken));
        Assert.True(node.OpenDownlink(asked.Downlink!).SpecificationsUnknown);
        var measuredAt = Now();
        var declared = Assert.Single(await IngestAsync(hub, node.SealFrame(node.Wake(measuredAt)), cancellationToken));
        Assert.Equal("stored", declared.Status);

        var events = await edge.ReadStreamAsync($"device/{node.DeviceId}", cancellationToken);
        var accepted = Assert.IsType<DeviceSpecificationsDeclared>(events[^1].Data);
        await edge.WaitForProjectionCheckpointAsync(Projector, events[^1].Position);

        var lot = Assert.Single(await ListAsync(administrator, siteId, cancellationToken));

        Assert.Equal(["id", "lastReadingAt", "name", "status", "statusSince"], Names(lot));
        Assert.Equal("needsCalibration", lot.GetProperty("status").GetString());
        Assert.Equal(Format(accepted.DeclaredAt), lot.GetProperty("statusSince").GetString());
        Assert.Equal(Format(measuredAt), lot.GetProperty("lastReadingAt").GetString());
    }

    private static DateTimeOffset Now() => TimeProvider.System.GetUtcNow();

    // The contract's times have milliseconds; PostgreSQL keeps microseconds.
    private static string Format(DateTimeOffset time) =>
        time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static List<string> Names(JsonElement lot) =>
        [.. lot.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)];

    private static List<string?> Strings(JsonElement array) => [.. array.EnumerateArray().Select(item => item.GetString())];

    private static string NewDeviceId() => Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8));

    private async Task<string> RecordedAtAsync(string streamId, int index, CancellationToken cancellationToken) =>
        Format((await edge.ReadStreamAsync(streamId, cancellationToken))[index].RecordedAt);

    private async Task<List<JsonElement>> ListAsync(TestUser user, string siteId, CancellationToken cancellationToken)
    {
        using var server = edge.CreateServerClient(user.AccessToken);
        using var response = await server.GetAsync(new Uri($"/sites/{siteId}/lots", UriKind.Relative), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await EdgeApiFixture.ReadJsonAsync(response, cancellationToken);

        return [.. body.RootElement.GetProperty("lots").EnumerateArray().Select(lot => lot.Clone())];
    }

    // An enrolled Node, as its Device stream starts.
    private async Task<string> SeedNodeAsync(string siteId, CancellationToken cancellationToken)
    {
        var deviceId = NewDeviceId();
        var wrapped = new WrappedDeviceKey("0000000000000000", new byte[12], new byte[48]);
        await edge.AppendAsync($"device/{deviceId}", [new DeviceEnrolled(siteId, DeviceKind.Node, wrapped, Now())], cancellationToken);
        return deviceId;
    }

    // Declares a Specification set as the grains journal it: every Sensor first, then the Device's set.
    private async Task<List<Guid>> DeclareAsync(
        string deviceId,
        DateTimeOffset declaredAt,
        CancellationToken cancellationToken,
        params SensorSpecification[] specifications)
    {
        var sensors = new List<DeclaredSensor>();

        foreach (var (specification, slot) in specifications.Select((specification, slot) => (specification, slot)))
        {
            var sensorId = Guid.NewGuid();
            await edge.AppendAsync($"sensor/{sensorId}", [new SensorDeclared(deviceId, slot, specification, declaredAt)], cancellationToken);
            sensors.Add(new DeclaredSensor(slot, specification.Quantity, sensorId));
        }

        var version = (await edge.ReadStreamAsync($"device/{deviceId}", cancellationToken)).Count;
        var last = await edge.AppendAsync(
            $"device/{deviceId}",
            version,
            [new DeviceSpecificationsDeclared([1, 2, 3], sensors, declaredAt)],
            cancellationToken);
        await edge.WaitForProjectionCheckpointAsync(Projector, last);

        return [.. sensors.Select(sensor => sensor.SensorId)];
    }

    // A Calibration, as the Sensor grain journals it after its declaration.
    private async Task CalibrateAsync(Guid sensorId, DateTimeOffset calibratedAt, CancellationToken cancellationToken)
    {
        var last = await edge.AppendAsync(
            $"sensor/{sensorId}",
            1,
            [new SensorCalibrated(Guid.CreateVersion7(), 3000, 1000, calibratedAt)],
            cancellationToken);
        await edge.WaitForProjectionCheckpointAsync(Projector, last);
    }

    // An open Threshold Alert, as the Alert grain journals it.
    private async Task<Guid> OpenAlertAsync(
        string siteId,
        string lotId,
        string deviceId,
        Guid sensorId,
        ThresholdSide side,
        string quantity,
        DateTimeOffset openedAt,
        CancellationToken cancellationToken)
    {
        var alertId = Guid.NewGuid();
        var last = await edge.AppendAsync(
            $"alert/{alertId}",
            [new AlertOpened(AlertKind.Threshold, side, siteId, lotId, sensorId, deviceId, quantity, 1, openedAt)],
            cancellationToken);
        await edge.WaitForProjectionCheckpointAsync(Projector, last);

        return alertId;
    }

    // A stored Reading, as the Device grain writes it.
    private async Task StoreReadingAsync(string deviceId, Guid sensorId, int seq, DateTimeOffset measuredAt, CancellationToken cancellationToken)
    {
        await using var command = edge.Database.CreateCommand(
            """
            INSERT INTO readings (device_id, sensor_id, reading_seq, measured_at, slot, quantity, raw_value, time_unsynced, received_at)
            VALUES (@device_id, @sensor_id, @seq, @measured_at, 0, 'soil_moisture', 612, false, @measured_at)
            """);
        command.Parameters.AddWithValue("device_id", deviceId);
        command.Parameters.AddWithValue("sensor_id", sensorId);
        command.Parameters.AddWithValue("seq", (decimal)seq);
        command.Parameters.AddWithValue("measured_at", measuredAt.ToUniversalTime());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SimulatedDevice> EnrolAsync(string siteId, TestUser administrator, ProtocolKind kind, string? lotId, CancellationToken cancellationToken)
    {
        var device = SimulatedDevice.Create(kind);
        using var server = edge.CreateServerClient(administrator.AccessToken);
        var sealedEnrolment = device.SealEnrolment(await edge.GetEnrolmentPublicKeyAsync(cancellationToken), siteId);
        using var enrolled = await EnrolmentTests.PostAsync(
            server,
            siteId,
            Guid.NewGuid().ToString(),
            new EnrolBody(sealedEnrolment.DeviceId, sealedEnrolment.Kind, sealedEnrolment.Enc, sealedEnrolment.Ciphertext, lotId),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, enrolled.StatusCode);

        return device;
    }

    private async Task<IReadOnlyList<SimulatedIngestResult>> IngestAsync(
        SimulatedDevice hub,
        Coldframe.Protocol.Device.V1.SealedEnvelope frame,
        CancellationToken cancellationToken)
    {
        using var server = edge.CreateServerClient();
        using var request = hub.IngestRequest(Now(), SimulatedDevice.IngestBody(frame));
        using var response = await server.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return SimulatedDevice.ReadIngestResponse(await response.Content.ReadAsByteArrayAsync(cancellationToken));
    }

    // A Site with a Member, seeded as events.
    private async Task<(string SiteId, TestUser Member)> SeedSiteAsync(CancellationToken cancellationToken)
    {
        var owner = await edge.CreateUserAsync("lot-status-owner", cancellationToken);
        var member = await edge.CreateUserAsync("lot-status-member", cancellationToken);
        var siteId = Guid.CreateVersion7().ToString();

        var last = await edge.AppendAsync(
            $"site/{siteId}",
            [
                new SiteCreated("Lot status", owner.UserId),
                new MembershipGranted(owner.UserId, SiteRole.Owner),
                new MembershipGranted(member.UserId, SiteRole.Member),
            ],
            cancellationToken);
        await edge.WaitForIdentityCheckpointAsync(last);

        return (siteId, member);
    }
}
