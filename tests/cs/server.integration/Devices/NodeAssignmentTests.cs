using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Lots;
using Coldframe.Contracts.Sites;
using Coldframe.DeviceSimulator;
using Coldframe.Server.IntegrationTests.Identity;
using ProtocolKind = Coldframe.Protocol.Setup.V1.DeviceKind;

namespace Coldframe.Server.IntegrationTests.Devices;

/// <summary>
/// Node enrolment onto a Lot on a TestCluster (Story 4.2; FR-2, AD-18): the Device grain claims the Lot
/// through <see cref="ILotGrain.Claim"/>, the only occupancy check, and journals <see cref="DeviceEnrolled"/>
/// and <see cref="DeviceAssigned"/> together only after the claim held.
/// </summary>
public sealed class NodeAssignmentTests(IdentityCluster identity) : IClassFixture<IdentityCluster>
{
    private static readonly string[] SeededSite = ["site.created", "site.membership-granted"];

    [Fact]
    public async Task ANodeClaimsAFreeLotAndJournalsItsEnrolmentAndAssignmentTogether()
    {
        var (userId, siteId) = await SeedSiteAsync();
        var lotId = await SeedLotAsync(siteId);
        var node = SimulatedDevice.Create(ProtocolKind.Node);
        var nodeId = node.DeviceId.ToString();
        var now = identity.Time.GetUtcNow();

        var result = await identity.Device(nodeId).Enrol(Request(siteId, node, userId, "k1", lotId), Ct);

        Assert.Equal(
            new DeviceEnrolmentResult(DeviceEnrolmentOutcome.Enrolled, new DeviceSummary(nodeId, DeviceKind.Node, siteId, lotId)),
            result);
        Assert.Equal(["device.enrolled", "device.assigned"], await identity.AliasesAsync($"device/{nodeId}"));
        Assert.Equal(["lot.created", "lot.claimed"], await identity.AliasesAsync($"lot/{lotId}"));
        Assert.Equal([.. SeededSite, "site.device-registered"], await identity.AliasesAsync($"site/{siteId}"));

        var events = await identity.Store.ReadStreamAsync($"device/{nodeId}", Ct);
        var assigned = Assert.IsType<DeviceAssigned>(events[1].Data);
        Assert.Equal(new DeviceAssigned(siteId, lotId, now), assigned);
        Assert.Equal(nodeId, Assert.IsType<LotClaimed>((await identity.Store.ReadStreamAsync($"lot/{lotId}", Ct))[1].Data).NodeId);

        // The claim brought the lots projection up to date before it returned.
        Assert.Equal(nodeId, await ClaimedByAsync(lotId));
    }

    [Fact]
    public async Task AnOccupiedLotRefusesTheNodeAndARetryWithAFreeLotEnrolsIt()
    {
        var (userId, siteId) = await SeedSiteAsync();
        var occupied = await SeedLotAsync(siteId, new LotClaimed("7c20000000000001"));
        var free = await SeedLotAsync(siteId);
        var node = SimulatedDevice.Create(ProtocolKind.Node);
        var nodeId = node.DeviceId.ToString();

        var refused = await identity.Device(nodeId).Enrol(Request(siteId, node, userId, "k1", occupied), Ct);

        Assert.Equal(new DeviceEnrolmentResult(DeviceEnrolmentOutcome.LotOccupied), refused);
        Assert.Empty(await identity.AliasesAsync($"device/{nodeId}"));
        Assert.Equal(["lot.created", "lot.claimed"], await identity.AliasesAsync($"lot/{occupied}"));

        // Only the Site's idempotent registration stays; the same request with a free Lot reuses it.
        Assert.Equal([.. SeededSite, "site.device-registered"], await identity.AliasesAsync($"site/{siteId}"));

        var enrolled = await identity.Device(nodeId).Enrol(Request(siteId, node, userId, "k1", free), Ct);

        Assert.Equal(new DeviceSummary(nodeId, DeviceKind.Node, siteId, free), enrolled.Device);
        Assert.Equal(["device.enrolled", "device.assigned"], await identity.AliasesAsync($"device/{nodeId}"));
        Assert.Equal([.. SeededSite, "site.device-registered"], await identity.AliasesAsync($"site/{siteId}"));
        Assert.Equal("7c20000000000001", await ClaimedByAsync(occupied));
    }

    [Fact]
    public async Task AnUnknownRemovedOrOtherSitesLotIsNotFoundAndTheDeviceJournalsNothing()
    {
        var (userId, siteId) = await SeedSiteAsync();
        var (_, otherSiteId) = await SeedSiteAsync();
        var removed = await SeedLotAsync(siteId, new LotRemoved());
        var elsewhere = await SeedLotAsync(otherSiteId);
        var uncreated = Guid.CreateVersion7().ToString();
        var node = SimulatedDevice.Create(ProtocolKind.Node);
        var nodeId = node.DeviceId.ToString();

        foreach (var lotId in new[] { removed, elsewhere, uncreated })
        {
            var result = await identity.Device(nodeId).Enrol(Request(siteId, node, userId, "k1", lotId), Ct);
            Assert.Equal(new DeviceEnrolmentResult(DeviceEnrolmentOutcome.LotNotFound), result);
        }

        Assert.Empty(await identity.AliasesAsync($"device/{nodeId}"));
        Assert.Equal(["lot.created", "lot.removed"], await identity.AliasesAsync($"lot/{removed}"));
        Assert.Equal(["lot.created"], await identity.AliasesAsync($"lot/{elsewhere}"));
        Assert.Empty(await identity.AliasesAsync($"lot/{uncreated}"));
    }

    [Fact]
    public async Task ARetryOnTheSameLotAnswersTheSameAndAnotherLotIsRefused()
    {
        var (userId, siteId) = await SeedSiteAsync();
        var lotA = await SeedLotAsync(siteId);
        var lotB = await SeedLotAsync(siteId);
        var node = SimulatedDevice.Create(ProtocolKind.Node);
        var nodeId = node.DeviceId.ToString();

        var first = await identity.Device(nodeId).Enrol(Request(siteId, node, userId, "k1", lotA), Ct);
        var retry = await identity.Device(nodeId).Enrol(Request(siteId, node, userId, "k1", lotA), Ct);
        var newKey = await identity.Device(nodeId).Enrol(Request(siteId, node, userId, "k2", lotA), Ct);
        var other = await identity.Device(nodeId).Enrol(Request(siteId, node, userId, "k3", lotB), Ct);

        Assert.Equal(DeviceEnrolmentOutcome.Enrolled, first.Outcome);
        Assert.Equal(first, retry);
        Assert.Equal(first, newKey);
        Assert.Equal(new DeviceEnrolmentResult(DeviceEnrolmentOutcome.AlreadyAssigned), other);

        Assert.Equal(["device.enrolled", "device.assigned"], await identity.AliasesAsync($"device/{nodeId}"));
        Assert.Equal(["lot.created", "lot.claimed"], await identity.AliasesAsync($"lot/{lotA}"));
        Assert.Equal(["lot.created"], await identity.AliasesAsync($"lot/{lotB}"));
        Assert.Null(await ClaimedByAsync(lotB));
    }

    [Fact]
    public async Task ANodeEnrolledWithoutALotIsAssignedByALaterEnrolment()
    {
        var (userId, siteId) = await SeedSiteAsync();
        var lotId = await SeedLotAsync(siteId);
        var node = SimulatedDevice.Create(ProtocolKind.Node);
        var nodeId = node.DeviceId.ToString();

        var unassigned = await identity.Device(nodeId).Enrol(Request(siteId, node, userId, "k1", lotId: null), Ct);
        var assigned = await identity.Device(nodeId).Enrol(Request(siteId, node, userId, "k2", lotId), Ct);

        Assert.Equal(new DeviceSummary(nodeId, DeviceKind.Node, siteId), unassigned.Device);
        Assert.Equal(new DeviceSummary(nodeId, DeviceKind.Node, siteId, lotId), assigned.Device);
        Assert.Equal(["device.enrolled", "device.assigned"], await identity.AliasesAsync($"device/{nodeId}"));
    }

    [Fact]
    public async Task AHubEnrolsAsBeforeAndAHubWithALotIsAnArgumentError()
    {
        var (userId, siteId) = await SeedSiteAsync();
        var lotId = await SeedLotAsync(siteId);
        var hub = SimulatedDevice.Create(ProtocolKind.Hub);
        var hubId = hub.DeviceId.ToString();
        var wrapped = identity.Vault.Wrap(hub.DeviceId, hub.Keys.DeviceKey);

        await Assert.ThrowsAsync<ArgumentException>(
            () => identity.Device(hubId).Enrol(new EnrolDevice(siteId, DeviceKind.Hub, wrapped, userId, "k1", lotId), Ct));

        Assert.Empty(await identity.AliasesAsync($"device/{hubId}"));
        Assert.Equal(SeededSite, await identity.AliasesAsync($"site/{siteId}"));
        Assert.Equal(["lot.created"], await identity.AliasesAsync($"lot/{lotId}"));

        var enrolled = await identity.Device(hubId).Enrol(new EnrolDevice(siteId, DeviceKind.Hub, wrapped, userId, "k1"), Ct);

        Assert.Equal(new DeviceEnrolmentResult(DeviceEnrolmentOutcome.Enrolled, new DeviceSummary(hubId, DeviceKind.Hub, siteId)), enrolled);
        Assert.Equal(["device.enrolled"], await identity.AliasesAsync($"device/{hubId}"));
    }

    [Fact]
    public async Task TwoNodesClaimingOneLotAtOnceLeaveExactlyOneOnIt()
    {
        var (userId, siteId) = await SeedSiteAsync();
        var lotId = await SeedLotAsync(siteId);
        var first = SimulatedDevice.Create(ProtocolKind.Node);
        var second = SimulatedDevice.Create(ProtocolKind.Node);

        var results = await Task.WhenAll(
            identity.Device(first.DeviceId.ToString()).Enrol(Request(siteId, first, userId, "k1", lotId), Ct),
            identity.Device(second.DeviceId.ToString()).Enrol(Request(siteId, second, userId, "k2", lotId), Ct));

        var winner = Assert.Single(results, result => result.Outcome == DeviceEnrolmentOutcome.Enrolled);
        Assert.Single(results, result => result.Outcome == DeviceEnrolmentOutcome.LotOccupied);
        Assert.Equal(lotId, winner.Device!.LotId);

        var loser = winner.Device.Id == first.DeviceId.ToString() ? second : first;
        Assert.Equal(["device.enrolled", "device.assigned"], await identity.AliasesAsync($"device/{winner.Device.Id}"));
        Assert.Empty(await identity.AliasesAsync($"device/{loser.DeviceId}"));
        Assert.Equal(["lot.created", "lot.claimed"], await identity.AliasesAsync($"lot/{lotId}"));
        Assert.Equal(winner.Device.Id, await ClaimedByAsync(lotId));
    }

    [Fact]
    public async Task ClaimHoldsAFreeLotOnceAndRefusesEverythingElse()
    {
        var siteId = Guid.CreateVersion7().ToString();
        var lotId = await SeedLotAsync(siteId);
        var removed = await SeedLotAsync(siteId, new LotRemoved());
        var lot = identity.Lot(lotId);

        var held = await lot.Claim(siteId, "7c21000000000001", Ct);
        var again = await lot.Claim(siteId, "7c21000000000001", Ct);
        var other = await lot.Claim(siteId, "7c21000000000002", Ct);

        var summary = new LotSummary(lotId, siteId, "Tomatoes", "7c21000000000001", false);
        Assert.Equal(new LotResult(LotOutcome.Held, summary), held);
        Assert.Equal(held, again);
        Assert.Equal(new LotResult(LotOutcome.Claimed, summary), other);
        Assert.Equal(["lot.created", "lot.claimed"], await identity.AliasesAsync($"lot/{lotId}"));

        Assert.Equal(LotOutcome.NotFound, (await lot.Claim(Guid.CreateVersion7().ToString(), "7c21000000000001", Ct)).Outcome);
        Assert.Equal(LotOutcome.NotFound, (await identity.Lot(Guid.CreateVersion7().ToString()).Claim(siteId, "7c21000000000001", Ct)).Outcome);
        Assert.Equal(LotOutcome.AlreadyRemoved, (await identity.Lot(removed).Claim(siteId, "7c21000000000001", Ct)).Outcome);
        Assert.Equal(["lot.created", "lot.removed"], await identity.AliasesAsync($"lot/{removed}"));

        // A claimed Lot still refuses removal.
        Assert.Equal(LotOutcome.Claimed, (await lot.Remove(siteId, Ct)).Outcome);
    }

    [Fact]
    public async Task ReleaseFreesTheLotOnlyForItsHolderAndOnlyOnce()
    {
        var siteId = Guid.CreateVersion7().ToString();
        var lotId = await SeedLotAsync(siteId);
        var lot = identity.Lot(lotId);
        await lot.Claim(siteId, "7c22000000000001", Ct);

        var other = await lot.Release(siteId, "7c22000000000002", Ct);
        var released = await lot.Release(siteId, "7c22000000000001", Ct);
        var again = await lot.Release(siteId, "7c22000000000001", Ct);

        var free = new LotSummary(lotId, siteId, "Tomatoes", null, false);
        Assert.Equal(LotOutcome.Unchanged, other.Outcome);
        Assert.Equal(new LotResult(LotOutcome.Released, free), released);
        Assert.Equal(new LotResult(LotOutcome.Unchanged, free), again);
        Assert.Equal(["lot.created", "lot.claimed", "lot.released"], await identity.AliasesAsync($"lot/{lotId}"));
        Assert.Null(await ClaimedByAsync(lotId));

        Assert.Equal(LotOutcome.NotFound, (await lot.Release(Guid.CreateVersion7().ToString(), "7c22000000000001", Ct)).Outcome);
        Assert.Equal(
            LotOutcome.NotFound,
            (await identity.Lot(Guid.CreateVersion7().ToString()).Release(siteId, "7c22000000000001", Ct)).Outcome);

        // A released Lot can be removed again.
        Assert.Equal(LotOutcome.Removed, (await lot.Remove(siteId, Ct)).Outcome);
    }

    [Fact]
    public async Task AClaimLeftBehindByACrashBeforeTheDeviceWriteHealsOnARetryWithTheSameLot()
    {
        var (userId, siteId) = await SeedSiteAsync();
        var node = SimulatedDevice.Create(ProtocolKind.Node);
        var nodeId = node.DeviceId.ToString();

        // The Lot journaled the claim, then the silo died before the Device's write: no Device events.
        var lotId = await SeedLotAsync(siteId, new LotClaimed(nodeId));

        var result = await identity.Device(nodeId).Enrol(Request(siteId, node, userId, "k1", lotId), Ct);

        Assert.Equal(
            new DeviceEnrolmentResult(DeviceEnrolmentOutcome.Enrolled, new DeviceSummary(nodeId, DeviceKind.Node, siteId, lotId)),
            result);
        Assert.Equal(["device.enrolled", "device.assigned"], await identity.AliasesAsync($"device/{nodeId}"));
        Assert.Equal(["lot.created", "lot.claimed"], await identity.AliasesAsync($"lot/{lotId}"));
    }

    [Fact]
    public async Task ReEnrollingAnAssignedNodeWithoutALotKeepsItsLotAndJournalsNothing()
    {
        var (userId, siteId) = await SeedSiteAsync();
        var lotId = await SeedLotAsync(siteId);
        var node = SimulatedDevice.Create(ProtocolKind.Node);
        var nodeId = node.DeviceId.ToString();

        await identity.Device(nodeId).Enrol(Request(siteId, node, userId, "k1", lotId), Ct);
        var again = await identity.Device(nodeId).Enrol(Request(siteId, node, userId, "k2", lotId: null), Ct);

        // Leaving out lotId never unassigns a Node.
        Assert.Equal(
            new DeviceEnrolmentResult(DeviceEnrolmentOutcome.Enrolled, new DeviceSummary(nodeId, DeviceKind.Node, siteId, lotId)),
            again);
        Assert.Equal(["device.enrolled", "device.assigned"], await identity.AliasesAsync($"device/{nodeId}"));
        Assert.Equal(["lot.created", "lot.claimed"], await identity.AliasesAsync($"lot/{lotId}"));
        Assert.Equal(nodeId, await ClaimedByAsync(lotId));
    }

    [Fact]
    public async Task AConflictingDeviceWriteAfterAGrantedClaimIsRetriedSoTheLotIsNeverStranded()
    {
        var (userId, siteId) = await SeedSiteAsync();
        var lotId = await SeedLotAsync(siteId);
        var node = SimulatedDevice.Create(ProtocolKind.Node);
        var nodeId = node.DeviceId.ToString();

        // Activate the Device grain, then move its stream on behind its back: its next write conflicts.
        await identity.Device(nodeId).Enrol(Request(siteId, node, userId, "k1", lotId: null), Ct);
        Assert.True(await identity.Store.AppendAsync(
            $"device/{nodeId}",
            1,
            [new DeviceSeen(identity.Time.GetUtcNow(), 1, null)],
            Ct));

        // Orleans' log-consistency adaptor re-reads the stream and retries the write instead of failing
        // ConfirmEvents, so the granted claim reaches the Device's journal: the Lot is held by a Node whose
        // journal says so, and nothing has to be released.
        var result = await identity.Device(nodeId).Enrol(Request(siteId, node, userId, "k2", lotId), Ct);

        Assert.Equal(new DeviceSummary(nodeId, DeviceKind.Node, siteId, lotId), result.Device);
        Assert.Equal(["device.enrolled", "device.seen", "device.assigned"], await identity.AliasesAsync($"device/{nodeId}"));
        Assert.Equal(["lot.created", "lot.claimed"], await identity.AliasesAsync($"lot/{lotId}"));
        Assert.Equal(nodeId, await ClaimedByAsync(lotId));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private EnrolDevice Request(string siteId, SimulatedDevice device, string callerId, string key, string? lotId) =>
        new(siteId, DeviceKind.Node, identity.Vault.Wrap(device.DeviceId, device.Keys.DeviceKey), callerId, key, lotId);

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

    private async Task<string> SeedLotAsync(string siteId, params object[] more)
    {
        var lotId = Guid.CreateVersion7().ToString();
        Assert.True(await identity.Store.AppendAsync($"lot/{lotId}", 0, [new LotCreated(siteId, "Tomatoes"), .. more], Ct));
        return lotId;
    }

    // The lots projection's occupant of a Lot.
    private async Task<string?> ClaimedByAsync(string lotId)
    {
        await using var command = identity.Database.DataSource.CreateCommand("SELECT claimed_by FROM lots WHERE lot_id = @lot_id");
        command.Parameters.AddWithValue("lot_id", lotId);
        return await command.ExecuteScalarAsync(Ct) switch
        {
            string nodeId => nodeId,
            _ => null,
        };
    }
}
