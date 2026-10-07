using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Lots;
using Coldframe.Contracts.Sites;
using Coldframe.DeviceSimulator;
using Coldframe.Server.IntegrationTests.Identity;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Runtime;
using ProtocolKind = Coldframe.Protocol.Setup.V1.DeviceKind;

namespace Coldframe.Server.IntegrationTests.Devices;

/// <summary>
/// Moving and unassigning a Node on a TestCluster (Story 4.9; FR-2, AD-18): the Device grain claims the new
/// Lot, journals <see cref="DeviceMoved"/> or <see cref="DeviceUnassigned"/>, and only then releases the old
/// Lot from persisted pending state, retried until it succeeds.
/// </summary>
public sealed class NodeMoveTests(IdentityCluster identity) : IClassFixture<IdentityCluster>
{
    [Fact]
    public async Task ANodeMovesToAFreeLotAndTheOldLotIsReleasedAfterTheJournal()
    {
        var (siteId, nodeId, tomatoes) = await EnrolledNodeAsync();
        var peppers = await SeedLotAsync(siteId, "Peppers");
        var now = identity.Time.GetUtcNow();

        var result = await identity.Device(nodeId).Move(siteId, peppers, Ct);

        Assert.Equal(
            new DeviceAssignmentResult(DeviceAssignmentOutcome.Moved, new DeviceSummary(nodeId, DeviceKind.Node, siteId, peppers)),
            result);
        Assert.Equal(["device.enrolled", "device.assigned", "device.moved", "device.lot-released"], await identity.AliasesAsync($"device/{nodeId}"));
        Assert.Equal(new DeviceMoved(siteId, tomatoes, peppers, now), (await identity.Store.ReadStreamAsync($"device/{nodeId}", Ct))[2].Data);
        Assert.Equal(["lot.created", "lot.claimed"], await identity.AliasesAsync($"lot/{peppers}"));
        Assert.Equal(["lot.created", "lot.claimed", "lot.released"], await identity.AliasesAsync($"lot/{tomatoes}"));
        Assert.Equal(nodeId, await ClaimedByAsync(peppers));
        Assert.Null(await ClaimedByAsync(tomatoes));
        Assert.Equal(peppers, await DeviceLotAsync(nodeId));
    }

    [Fact]
    public async Task AnOccupiedTargetIsRefusedAndNothingChanges()
    {
        var (siteId, nodeId, tomatoes) = await EnrolledNodeAsync();
        var occupied = await SeedLotAsync(siteId, "Peppers", new LotClaimed("7c30000000000001"));

        var result = await identity.Device(nodeId).Move(siteId, occupied, Ct);

        Assert.Equal(new DeviceAssignmentResult(DeviceAssignmentOutcome.LotOccupied), result);
        Assert.Equal(["device.enrolled", "device.assigned"], await identity.AliasesAsync($"device/{nodeId}"));
        Assert.Equal(["lot.created", "lot.claimed"], await identity.AliasesAsync($"lot/{occupied}"));
        Assert.Equal(["lot.created", "lot.claimed"], await identity.AliasesAsync($"lot/{tomatoes}"));
        Assert.Equal(nodeId, await ClaimedByAsync(tomatoes));
    }

    [Fact]
    public async Task MovingToTheCurrentLotIsASuccessThatJournalsNothing()
    {
        var (siteId, nodeId, tomatoes) = await EnrolledNodeAsync();

        var result = await identity.Device(nodeId).Move(siteId, tomatoes, Ct);

        Assert.Equal(DeviceAssignmentOutcome.Unchanged, result.Outcome);
        Assert.Equal(tomatoes, result.Device!.LotId);
        Assert.Equal(["device.enrolled", "device.assigned"], await identity.AliasesAsync($"device/{nodeId}"));
        Assert.Equal(["lot.created", "lot.claimed"], await identity.AliasesAsync($"lot/{tomatoes}"));
    }

    [Fact]
    public async Task AnUnknownRemovedOrOtherSitesTargetIsNotFoundAndNothingChanges()
    {
        var (siteId, nodeId, tomatoes) = await EnrolledNodeAsync();
        var (_, otherSiteId) = await SeedSiteAsync();
        var removed = await SeedLotAsync(siteId, "Gone", new LotRemoved());
        var elsewhere = await SeedLotAsync(otherSiteId, "Elsewhere");

        foreach (var lotId in new[] { removed, elsewhere, Guid.CreateVersion7().ToString() })
        {
            Assert.Equal(new DeviceAssignmentResult(DeviceAssignmentOutcome.LotNotFound), await identity.Device(nodeId).Move(siteId, lotId, Ct));
        }

        Assert.Equal(["device.enrolled", "device.assigned"], await identity.AliasesAsync($"device/{nodeId}"));
        Assert.Equal(nodeId, await ClaimedByAsync(tomatoes));
        Assert.Equal(["lot.created"], await identity.AliasesAsync($"lot/{elsewhere}"));
    }

    [Fact]
    public async Task AnUnknownNodeAHubOrANodeOfAnotherSiteIsNotFound()
    {
        var (siteId, nodeId, _) = await EnrolledNodeAsync();
        var (userId, otherSiteId) = await SeedSiteAsync();
        var target = await SeedLotAsync(siteId, "Peppers");
        var hub = SimulatedDevice.Create(ProtocolKind.Hub);
        await identity.Device(hub.DeviceId.ToString()).Enrol(
            new EnrolDevice(siteId, DeviceKind.Hub, identity.Vault.Wrap(hub.DeviceId, hub.Keys.DeviceKey), userId, "k1"),
            Ct);

        var notFound = new DeviceAssignmentResult(DeviceAssignmentOutcome.NotFound);
        Assert.Equal(notFound, await identity.Device("7c30ffffffffffff").Move(siteId, target, Ct));
        Assert.Equal(notFound, await identity.Device(hub.DeviceId.ToString()).Move(siteId, target, Ct));
        Assert.Equal(notFound, await identity.Device(nodeId).Move(otherSiteId, target, Ct));
        Assert.Equal(notFound, await identity.Device(nodeId).Unassign(otherSiteId, Ct));
        Assert.Equal(["device.enrolled", "device.assigned"], await identity.AliasesAsync($"device/{nodeId}"));
        Assert.Equal(["lot.created"], await identity.AliasesAsync($"lot/{target}"));
    }

    [Fact]
    public async Task TwoNodesMovingToOneFreeLotAtOnceLeaveExactlyOneOnItAndTheLoserKeepsItsLot()
    {
        var (siteId, first, firstLot) = await EnrolledNodeAsync();
        var (second, secondLot) = await EnrolNodeAsync(siteId);
        var contested = await SeedLotAsync(siteId, "Peppers");

        var results = await Task.WhenAll(
            identity.Device(first).Move(siteId, contested, Ct),
            identity.Device(second).Move(siteId, contested, Ct));

        var winner = Assert.Single(results, result => result.Outcome == DeviceAssignmentOutcome.Moved).Device!;
        Assert.Single(results, result => result.Outcome == DeviceAssignmentOutcome.LotOccupied);
        Assert.Equal(contested, winner.LotId);

        var (loser, loserLot, winnerOld) = winner.Id == first ? (second, secondLot, firstLot) : (first, firstLot, secondLot);
        Assert.Equal(["device.enrolled", "device.assigned"], await identity.AliasesAsync($"device/{loser}"));
        Assert.Equal(loser, await ClaimedByAsync(loserLot));
        Assert.Equal(winner.Id, await ClaimedByAsync(contested));
        Assert.Null(await ClaimedByAsync(winnerOld));
    }

    [Fact]
    public async Task AFailedReleaseKeepsTheMoveAndIsRetriedFromPersistedStateUntilTheOldLotIsFree()
    {
        var (siteId, nodeId, tomatoes) = await EnrolledNodeAsync();
        var peppers = await SeedLotAsync(siteId, "Peppers");
        identity.LotFaults.FailReleases(tomatoes);

        var result = await identity.Device(nodeId).Move(siteId, peppers, Ct);

        // The move succeeded; the old Lot is still held, and the pending release is journaled state.
        Assert.Equal(DeviceAssignmentOutcome.Moved, result.Outcome);
        Assert.Equal(["device.enrolled", "device.assigned", "device.moved"], await identity.AliasesAsync($"device/{nodeId}"));
        Assert.Equal(nodeId, await ClaimedByAsync(tomatoes));
        Assert.Equal(peppers, await DeviceLotAsync(nodeId));

        // The reminder that survives a deactivation exists while the release is pending.
        Assert.NotNull(await ReminderAsync(nodeId));

        identity.LotFaults.Restore(tomatoes);
        await WaitForAsync(async () => await ClaimedByAsync(tomatoes) is null);
        await WaitForAsync(async () => await ReminderAsync(nodeId) is null);

        Assert.Equal(["lot.created", "lot.claimed", "lot.released"], await identity.AliasesAsync($"lot/{tomatoes}"));
        await WaitForAsync(async () => (await identity.AliasesAsync($"device/{nodeId}")).Contains("device.lot-released"));
        Assert.Equal(nodeId, await ClaimedByAsync(peppers));
    }

    [Fact]
    public async Task APendingReleaseSurvivesASiloRestart()
    {
        var (siteId, nodeId, tomatoes) = await EnrolledNodeAsync();
        var peppers = await SeedLotAsync(siteId, "Peppers");
        identity.LotFaults.FailReleases(tomatoes);
        await identity.Device(nodeId).Move(siteId, peppers, Ct);
        Assert.Equal(nodeId, await ClaimedByAsync(tomatoes));

        await identity.RestartSiloAsync();
        identity.LotFaults.Restore(tomatoes);

        // Any call activates the grain, which finds the pending release in its journal and keeps releasing.
        Assert.Equal(DeviceAssignmentOutcome.Unchanged, (await identity.Device(nodeId).Move(siteId, peppers, Ct)).Outcome);
        await WaitForAsync(async () => await ClaimedByAsync(tomatoes) is null);

        Assert.Equal(nodeId, await ClaimedByAsync(peppers));
    }

    [Fact]
    public async Task AMovedNodeCanReturnToALotThatIsStillPendingReleaseWithoutLosingIt()
    {
        var (siteId, nodeId, tomatoes) = await EnrolledNodeAsync();
        var peppers = await SeedLotAsync(siteId, "Peppers");
        identity.LotFaults.FailReleases(tomatoes);
        await identity.Device(nodeId).Move(siteId, peppers, Ct);

        // Back to Tomatoes, which this Node still holds: the claim is held, and Tomatoes is no longer pending.
        var back = await identity.Device(nodeId).Move(siteId, tomatoes, Ct);
        identity.LotFaults.Restore(tomatoes);
        await WaitForAsync(async () => await ClaimedByAsync(peppers) is null);

        Assert.Equal(tomatoes, back.Device!.LotId);
        Assert.Equal(nodeId, await ClaimedByAsync(tomatoes));
        Assert.Equal(tomatoes, await DeviceLotAsync(nodeId));
    }

    [Fact]
    public async Task UnassigningANodeJournalsItReleasesTheLotAndListsItUnassigned()
    {
        var (siteId, nodeId, tomatoes) = await EnrolledNodeAsync();
        var now = identity.Time.GetUtcNow();

        var result = await identity.Device(nodeId).Unassign(siteId, Ct);

        Assert.Equal(
            new DeviceAssignmentResult(DeviceAssignmentOutcome.Unassigned, new DeviceSummary(nodeId, DeviceKind.Node, siteId)),
            result);
        Assert.Equal(["device.enrolled", "device.assigned", "device.unassigned", "device.lot-released"], await identity.AliasesAsync($"device/{nodeId}"));
        Assert.Equal(new DeviceUnassigned(siteId, tomatoes, now), (await identity.Store.ReadStreamAsync($"device/{nodeId}", Ct))[2].Data);
        Assert.Equal(["lot.created", "lot.claimed", "lot.released"], await identity.AliasesAsync($"lot/{tomatoes}"));
        Assert.Null(await ClaimedByAsync(tomatoes));
        Assert.Null(await DeviceLotAsync(nodeId));

        // Unassigning again changes nothing.
        Assert.Equal(DeviceAssignmentOutcome.Unchanged, (await identity.Device(nodeId).Unassign(siteId, Ct)).Outcome);
        Assert.Equal(4, (await identity.AliasesAsync($"device/{nodeId}")).Count);
    }

    [Fact]
    public async Task AFailedReleaseAfterUnassignIsRetriedUntilTheLotIsFree()
    {
        var (siteId, nodeId, tomatoes) = await EnrolledNodeAsync();
        identity.LotFaults.FailReleases(tomatoes);

        var result = await identity.Device(nodeId).Unassign(siteId, Ct);

        Assert.Equal(DeviceAssignmentOutcome.Unassigned, result.Outcome);
        Assert.Equal(nodeId, await ClaimedByAsync(tomatoes));

        identity.LotFaults.Restore(tomatoes);
        await WaitForAsync(async () => await ClaimedByAsync(tomatoes) is null);
    }

    [Fact]
    public async Task AnUnassignedNodeCanBeMovedOntoALotAndAssignedAgain()
    {
        var (siteId, nodeId, _) = await EnrolledNodeAsync();
        var peppers = await SeedLotAsync(siteId, "Peppers");
        await identity.Device(nodeId).Unassign(siteId, Ct);

        var result = await identity.Device(nodeId).Move(siteId, peppers, Ct);

        Assert.Equal(DeviceAssignmentOutcome.Moved, result.Outcome);
        Assert.Equal(peppers, result.Device!.LotId);
        Assert.Equal(nodeId, await ClaimedByAsync(peppers));
    }

    [Fact]
    public async Task ReadingsStayWithTheNodeAndLaterOnesAreStoredWhileItIsMovedAndUnassigned()
    {
        var (userId, siteId) = await SeedSiteAsync();
        var tomatoes = await SeedLotAsync(siteId, "Tomatoes");
        var peppers = await SeedLotAsync(siteId, "Peppers");
        var node = SimulatedDevice.Create(ProtocolKind.Node);
        var nodeId = node.DeviceId.ToString();
        await identity.Device(nodeId).Enrol(
            new EnrolDevice(siteId, DeviceKind.Node, identity.Vault.Wrap(node.DeviceId, node.Keys.DeviceKey), userId, "k1", tomatoes),
            Ct);
        var now = identity.Time.GetUtcNow();

        Assert.Equal(DeviceIngestStatus.Stored, (await IngestAsync(node, node.Wake(now.AddMinutes(-30)))).Status);
        await identity.Device(nodeId).Move(siteId, peppers, Ct);
        Assert.Equal(DeviceIngestStatus.Stored, (await IngestAsync(node, node.Wake(now.AddMinutes(-15)))).Status);
        await identity.Device(nodeId).Unassign(siteId, Ct);
        Assert.Equal(DeviceIngestStatus.Stored, (await IngestAsync(node, node.Wake(now))).Status);

        // Nothing deletes or rewrites a Reading: they are keyed by the Node, not by a Lot.
        await using var count = identity.Database.DataSource.CreateCommand("SELECT count(*) FROM readings WHERE device_id = @device_id");
        count.Parameters.AddWithValue("device_id", nodeId);
        Assert.Equal(12L, await count.ExecuteScalarAsync(Ct));
        Assert.Null(await ClaimedByAsync(tomatoes));
        Assert.Null(await ClaimedByAsync(peppers));
    }

    private Task<DeviceIngestResult> IngestAsync(SimulatedDevice node, Coldframe.Protocol.Device.V1.NodeFrame frame)
    {
        var envelope = node.SealFrame(frame);
        return identity.Device(node.DeviceId.ToString()).Ingest(
            new DeviceIngest(envelope.ProtocolVersion, envelope.Counter, envelope.Ciphertext.ToByteArray(), "92064422c012f481"),
            Ct);
    }

    // The grain's reminder, read from the silo's reminder service; the 1-minute period is never waited for.
    private async Task<IGrainReminder?> ReminderAsync(string nodeId)
    {
        var reminders = identity.SiloServices.GetRequiredService<IReminderService>();
        return await reminders.GetReminder(
            GrainId.Create("device", nodeId),
            "release-pending-lots");
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        // The retry timer and reminder run on the real clock; the fake one only stamps events.
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition did not hold in time.");
            await Task.Delay(250, Ct);
        }
    }

    private async Task<(string SiteId, string NodeId, string LotId)> EnrolledNodeAsync()
    {
        var (userId, siteId) = await SeedSiteAsync();
        var (nodeId, lotId) = await EnrolNodeAsync(siteId, userId);
        return (siteId, nodeId, lotId);
    }

    private async Task<(string NodeId, string LotId)> EnrolNodeAsync(string siteId, string? userId = null)
    {
        var lotId = await SeedLotAsync(siteId, "Tomatoes");
        var node = SimulatedDevice.Create(ProtocolKind.Node);
        var request = new EnrolDevice(
            siteId,
            DeviceKind.Node,
            identity.Vault.Wrap(node.DeviceId, node.Keys.DeviceKey),
            userId ?? Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString("N"),
            lotId);

        Assert.Equal(DeviceEnrolmentOutcome.Enrolled, (await identity.Device(node.DeviceId.ToString()).Enrol(request, Ct)).Outcome);
        return (node.DeviceId.ToString(), lotId);
    }

    private async Task<(string UserId, string SiteId)> SeedSiteAsync()
    {
        var userId = Guid.NewGuid().ToString();
        var siteId = Guid.CreateVersion7().ToString();

        Assert.True(await identity.Store.AppendAsync(
            $"site/{siteId}",
            0,
            [new SiteCreated("Home", userId), new MembershipGranted(userId, SiteRole.Owner)],
            Ct));

        return (userId, siteId);
    }

    private async Task<string> SeedLotAsync(string siteId, string name, params object[] more)
    {
        var lotId = Guid.CreateVersion7().ToString();
        Assert.True(await identity.Store.AppendAsync($"lot/{lotId}", 0, [new LotCreated(siteId, name), .. more], Ct));
        return lotId;
    }

    private async Task<string?> ClaimedByAsync(string lotId)
    {
        await using var command = identity.Database.DataSource.CreateCommand("SELECT claimed_by FROM lots WHERE lot_id = @lot_id");
        command.Parameters.AddWithValue("lot_id", lotId);
        return await command.ExecuteScalarAsync(Ct) as string;
    }

    // The devices projection's Lot of a Node: the Move and Unassign calls brought it up to date.
    private async Task<string?> DeviceLotAsync(string deviceId)
    {
        await using var command = identity.Database.DataSource.CreateCommand("SELECT lot_id FROM devices WHERE device_id = @device_id");
        command.Parameters.AddWithValue("device_id", deviceId);
        return await command.ExecuteScalarAsync(Ct) as string;
    }
}
