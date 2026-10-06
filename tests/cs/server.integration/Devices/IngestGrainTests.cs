using Coldframe.Contracts.Devices;
using Coldframe.Crypto;
using Coldframe.DeviceSimulator;
using Coldframe.Migrations;
using Coldframe.Protocol.Device.V1;
using Coldframe.Server.IntegrationTests.Identity;
using Google.Protobuf;
using ProtocolKind = Coldframe.Protocol.Setup.V1.DeviceKind;

namespace Coldframe.Server.IntegrationTests.Devices;

/// <summary>
/// Ingestion in the Device grain on a TestCluster with a fake clock (Story 4.5; AD-8, AD-9, AD-11, AD-17):
/// the rows of the story's matrix that need the clock, a Pause, a failing database or a restart. Frames come
/// from the Device simulator only; "restart" is a real silo restart on the same database.
/// </summary>
[Collection(IngestSuites.Name)]
public sealed class IngestGrainTests(IdentityCluster identity) : IClassFixture<IdentityCluster>
{
    private const string HubA = "92064422c012f481";
    private const string HubB = "b485999a177ebbf3";

    private static readonly string SiteId = Guid.CreateVersion7().ToString();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AFrameIsStoredAndAcknowledgedWithARangeForEveryReadingSeq()
    {
        var node = await SeedNodeAsync();
        node.NextReadingSeq = 500;
        var now = identity.Time.GetUtcNow();
        var measuredAt = now.AddMinutes(-15);
        var frame = node.Wake(measuredAt, batteryPercent: 87, ChargeStatus.NotCharging);

        var result = await IngestAsync(node, node.SealFrame(frame));

        Assert.Equal(DeviceIngestStatus.Stored, result.Status);
        var envelope = SealedEnvelope.Parser.ParseFrom(result.Downlink);
        Assert.Equal(0UL, envelope.Counter);
        var downlink = node.OpenDownlink(envelope);
        Assert.Equal(0UL, downlink.AckedCounter);
        Assert.Equal(now.ToUnixTimeMilliseconds(), downlink.ServerTimeMs);
        Assert.Empty(downlink.Commands);
        Assert.Equal([500, 501, 502, 503, 504], SimulatedDevice.AckedReadings(downlink));
        Assert.Equal((500UL, 504UL), (downlink.AckedReadings.Single().First, downlink.AckedReadings.Single().Last));

        var rows = await ReadingsAsync(node);
        Assert.Equal(4, rows.Count);
        foreach (var (reading, index) in SimulatedDevice.DefaultReadings.Select((reading, index) => (reading, index)))
        {
            var row = rows[index];
            Assert.Equal(node.SensorId(reading.Slot, reading.Quantity), row.SensorId);
            Assert.Equal(
                (500m + index, (int)reading.Slot, SimulatedDevice.QuantityToken(reading.Quantity), reading.Value),
                (row.ReadingSeq, row.Slot, row.Quantity, row.RawValue));
            Assert.Equal((measuredAt, now, false), (row.MeasuredAt, row.ReceivedAt, row.TimeUnsynced));
            Assert.True(row.CalibrationIsNull && row.BootIsNull);
        }

        Assert.Equal(
            (504m, measuredAt, (short)87, "not_charging"),
            await ReportAsync(node));
        Assert.Equal(5L, await CountAsync("reading_keys", node));

        // The replay window and the next downlink counter committed with the rows; nothing was journaled
        // per frame, only the relay Hub once.
        Assert.Equal((0m, 1L, 1m), await ReplayRowAsync(node));
        Assert.Equal(["device.enrolled", "device.relay-changed"], await identity.AliasesAsync($"device/{node.DeviceId}"));
    }

    [Fact]
    public async Task AResendIsADuplicateAndAPartlyNewFrameStoresOnlyTheNewRows()
    {
        var node = await SeedNodeAsync();
        var now = identity.Time.GetUtcNow();
        var frame = node.Wake(now);

        var first = await IngestAsync(node, node.SealFrame(frame));
        var resend = await IngestAsync(node, node.SealFrame(frame));

        Assert.Equal(DeviceIngestStatus.Stored, first.Status);
        Assert.Equal(DeviceIngestStatus.Duplicate, resend.Status);
        var acked = node.OpenDownlink(SealedEnvelope.Parser.ParseFrom(resend.Downlink));
        Assert.Equal(1UL, acked.AckedCounter);
        Assert.Equal(SimulatedDevice.ReadingSeqs(frame), SimulatedDevice.AckedReadings(acked));
        Assert.Equal(1UL, SealedEnvelope.Parser.ParseFrom(resend.Downlink).Counter);
        Assert.Equal((4L, 1L, 5L), await CountsAsync(node));

        // The stored Readings again, with one the Server has not seen: only that row is added.
        var partlyNew = frame.Clone();
        partlyNew.Readings.Add(new Reading { Slot = 0, ReadingSeq = 900, Quantity = Quantity.SoilMoisture, Value = 1700 });
        var mixed = await IngestAsync(node, node.SealFrame(partlyNew));

        Assert.Equal(DeviceIngestStatus.Stored, mixed.Status);
        Assert.Equal(
            [0, 1, 2, 3, 4, 900],
            SimulatedDevice.AckedReadings(node.OpenDownlink(SealedEnvelope.Parser.ParseFrom(mixed.Downlink))));
        Assert.Equal((5L, 1L, 6L), await CountsAsync(node));
    }

    [Fact]
    public async Task AReplayedOrTooOldCounterIsRejectedAndAnUnseenSlotBelowTheMarkIsAccepted()
    {
        var node = await SeedNodeAsync();
        var now = identity.Time.GetUtcNow();
        var first = node.SealFrame(100, node.Wake(now));

        Assert.Equal(DeviceIngestStatus.Stored, (await IngestAsync(node, first)).Status);
        var window = await ReplayRowAsync(node);

        // The same sealed bytes again.
        var replayed = await IngestAsync(node, first);
        Assert.Equal(DeviceIngestStatus.RejectedReplay, replayed.Status);
        Assert.Null(replayed.Downlink);

        // Below the 64-entry window.
        var tooOld = await IngestAsync(node, node.SealFrame(36, node.Wake(now)));
        Assert.Equal(DeviceIngestStatus.RejectedReplay, tooOld.Status);
        Assert.Equal(window, await ReplayRowAsync(node));
        Assert.Equal((4L, 1L, 5L), await CountsAsync(node));

        // Out of order: below the high-water mark, in a slot not seen yet. Then that slot is seen.
        var outOfOrder = node.SealFrame(37, node.Wake(now));
        Assert.Equal(DeviceIngestStatus.Stored, (await IngestAsync(node, outOfOrder)).Status);
        Assert.Equal(DeviceIngestStatus.RejectedReplay, (await IngestAsync(node, outOfOrder)).Status);
        Assert.Equal((8L, 2L, 10L), await CountsAsync(node));
        Assert.Equal(100m, (await ReplayRowAsync(node)).HighWater);
    }

    [Fact]
    public async Task ATamperedFrameIsRejectedAuthAndLeavesTheWindowAlone()
    {
        var node = await SeedNodeAsync();
        var now = identity.Time.GetUtcNow();
        Assert.Equal(DeviceIngestStatus.Stored, (await IngestAsync(node, node.SealFrame(node.Wake(now)))).Status);
        var window = await ReplayRowAsync(node);
        var good = node.SealFrame(node.Wake(now));

        var flipped = good.Clone();
        var bytes = flipped.Ciphertext.ToByteArray();
        bytes[5] ^= 0x01;
        flipped.Ciphertext = ByteString.CopyFrom(bytes);
        var otherCounter = good.Clone();
        otherCounter.Counter += 1;

        // A counter the window has seen, on a frame that does not open: still an authentication failure.
        var seenCounter = good.Clone();
        seenCounter.Counter = 0;
        var otherVersion = good.Clone();
        otherVersion.ProtocolVersion = 2;
        var truncated = good.Clone();
        truncated.Ciphertext = ByteString.CopyFrom(bytes.AsSpan(0, 8));

        // Sealed by another Device, claiming to be this Node.
        var impostor = SimulatedDevice.Create(ProtocolKind.Node);
        var forged = impostor.SealFrame(50, impostor.Wake(now));

        foreach (var envelope in new[] { flipped, otherCounter, seenCounter, otherVersion, truncated, forged })
        {
            var result = await IngestAsync(node, envelope);
            Assert.Equal(DeviceIngestStatus.RejectedAuth, result.Status);
            Assert.Null(result.Downlink);
        }

        Assert.Equal(window, await ReplayRowAsync(node));
        Assert.Equal((4L, 1L, 5L), await CountsAsync(node));

        // The untouched frame still goes through afterwards.
        Assert.Equal(DeviceIngestStatus.Stored, (await IngestAsync(node, good)).Status);
    }

    [Fact]
    public async Task AnAuthenticFrameThatIsNoNodeFrameIsRejectedAuthAndConsumesItsCounter()
    {
        var node = await SeedNodeAsync();
        var garbage = node.SealFrame([0xFF, 0xFF, 0xFF]);
        var noTime = node.Wake(identity.Time.GetUtcNow());
        noTime.ClearMeasured();
        var incomplete = node.SealFrame(noTime);

        foreach (var envelope in new[] { garbage, incomplete })
        {
            var result = await IngestAsync(node, envelope);
            Assert.Equal(DeviceIngestStatus.RejectedAuth, result.Status);
            Assert.Null(result.Downlink);
            Assert.Equal(DeviceIngestStatus.RejectedReplay, (await IngestAsync(node, envelope)).Status);
        }

        Assert.Equal((0L, 0L, 0L), await CountsAsync(node));

        // The counters are consumed; no downlink counter was reserved.
        Assert.Equal((1m, 3L, 0m), await ReplayRowAsync(node));
    }

    [Fact]
    public async Task ADeviceThatIsNotAnEnrolledNodeIsUnknown()
    {
        var stranger = SimulatedDevice.Create(ProtocolKind.Node);
        var hub = await SeedDeviceAsync(DeviceKind.Hub);
        var now = identity.Time.GetUtcNow();

        var unenrolled = await IngestAsync(stranger, stranger.SealFrame(stranger.Wake(now)));
        var asHub = await IngestAsync(hub, hub.SealFrame(hub.Wake(now)));

        Assert.Equal(new DeviceIngestResult(DeviceIngestStatus.UnknownDevice), unenrolled);
        Assert.Equal(DeviceIngestStatus.UnknownDevice, asHub.Status);
        Assert.Null(asHub.Downlink);
        Assert.Empty(await identity.AliasesAsync($"device/{stranger.DeviceId}"));
        Assert.Equal(["device.enrolled"], await identity.AliasesAsync($"device/{hub.DeviceId}"));
        Assert.Equal((0L, 0L, 0L), await CountsAsync(stranger));
        Assert.Equal((0L, 0L, 0L), await CountsAsync(hub));
        Assert.Equal(0L, await CountAsync("device_replay", stranger));
        Assert.Equal(0L, await CountAsync("device_replay", hub));
    }

    [Fact]
    public async Task ATimeMoreThanFiveMinutesAheadIsRejectedTimeAndConsumesTheCounter()
    {
        var node = await SeedNodeAsync();
        var now = identity.Time.GetUtcNow();
        var ahead = node.SealFrame(node.Wake(now.AddMinutes(5).AddMilliseconds(1)));

        var result = await IngestAsync(node, ahead);

        Assert.Equal(DeviceIngestStatus.RejectedTime, result.Status);
        Assert.Null(result.Downlink);
        Assert.Equal((0L, 0L, 0L), await CountsAsync(node));
        Assert.Equal((0m, 1L, 0m), await ReplayRowAsync(node));
        Assert.Equal(DeviceIngestStatus.RejectedReplay, (await IngestAsync(node, ahead)).Status);
        Assert.Equal(["device.enrolled"], await identity.AliasesAsync($"device/{node.DeviceId}"));

        // Exactly five minutes ahead is still accepted.
        var edge = await IngestAsync(node, node.SealFrame(node.Wake(now.AddMinutes(5))));
        Assert.Equal(DeviceIngestStatus.Stored, edge.Status);
        Assert.Equal(now.AddMinutes(5), (await ReadingsAsync(node))[0].MeasuredAt);
    }

    [Fact]
    public async Task AnUnsyncedReadingIsRebasedWithinItsBootAndGetsTheReceiveTimeAfterAReboot()
    {
        var now = identity.Time.GetUtcNow();

        // Same boot: taken at 10 s of uptime, sealed at 70 s: one minute old.
        var sameBoot = await SeedNodeAsync();
        sameBoot.BootId = 4;
        var wake = sameBoot.WakeUnsynced(uptimeMs: 10_000);
        sameBoot.UptimeMs = 70_000;
        Assert.Equal(DeviceIngestStatus.Stored, (await IngestAsync(sameBoot, sameBoot.SealFrame(wake))).Status);

        var rebased = await ReadingsAsync(sameBoot);
        Assert.All(rebased, row => Assert.Equal((now.AddMinutes(-1), true, 4m, 10_000m), (row.MeasuredAt, row.TimeUnsynced, row.BootId, row.UptimeMs)));
        Assert.Equal(now.AddMinutes(-1), (await ReportAsync(sameBoot)).MeasuredAt);

        // A negative uptime difference counts as 0.
        var negative = await SeedNodeAsync();
        var early = negative.WakeUnsynced(uptimeMs: 90_000);
        negative.UptimeMs = 5_000;
        Assert.Equal(DeviceIngestStatus.Stored, (await IngestAsync(negative, negative.SealFrame(early))).Status);
        Assert.All(await ReadingsAsync(negative), row => Assert.Equal((now, true, 90_000m), (row.MeasuredAt, row.TimeUnsynced, row.UptimeMs)));

        // Earlier boot: the age is unknown, so the receive time; the row keeps the original boot and uptime.
        var rebooted = await SeedNodeAsync();
        rebooted.BootId = 4;
        var before = rebooted.WakeUnsynced(uptimeMs: 10_000);
        rebooted.BootId = 5;
        rebooted.UptimeMs = 70_000;
        Assert.Equal(DeviceIngestStatus.Stored, (await IngestAsync(rebooted, rebooted.SealFrame(before))).Status);
        Assert.All(await ReadingsAsync(rebooted), row => Assert.Equal((now, true, 4m, 10_000m), (row.MeasuredAt, row.TimeUnsynced, row.BootId, row.UptimeMs)));

        // A resend of an unsynced Reading is rebased to another time, and still stored once.
        identity.Time.Advance(TimeSpan.FromMinutes(3));
        var resend = await IngestAsync(rebooted, rebooted.SealFrame(before));
        Assert.Equal(DeviceIngestStatus.Duplicate, resend.Status);
        Assert.Equal((4L, 1L, 5L), await CountsAsync(rebooted));
    }

    [Fact]
    public async Task APausedDevicesReadingsAreAcknowledgedAndDiscarded()
    {
        var now = identity.Time.GetUtcNow();
        var node = await SeedNodeAsync(new DevicePaused(DevicePauseSource.Site, now.AddDays(7), now));
        var frame = node.Wake(now);

        var result = await IngestAsync(node, node.SealFrame(frame));

        Assert.Equal(DeviceIngestStatus.Stored, result.Status);
        var downlink = node.OpenDownlink(SealedEnvelope.Parser.ParseFrom(result.Downlink));
        Assert.Equal(SimulatedDevice.ReadingSeqs(frame), SimulatedDevice.AckedReadings(downlink));
        Assert.Equal(now.ToUnixTimeMilliseconds(), downlink.ServerTimeMs);
        Assert.Empty(downlink.Commands);
        Assert.Equal((0L, 0L, 0L), await CountsAsync(node));
        Assert.Equal((0m, 1L, 1m), await ReplayRowAsync(node));

        // Two sources, one resumed: still paused. Both resumed: stored again.
        var twice = await SeedNodeAsync(
            new DevicePaused(DevicePauseSource.Site, null, now),
            new DevicePaused(DevicePauseSource.Device, null, now),
            new DeviceResumed(DevicePauseSource.Site, now));
        Assert.Equal(DeviceIngestStatus.Stored, (await IngestAsync(twice, twice.SealFrame(twice.Wake(now)))).Status);
        Assert.Equal((0L, 0L, 0L), await CountsAsync(twice));

        var resumed = await SeedNodeAsync(
            new DevicePaused(DevicePauseSource.Device, null, now),
            new DeviceResumed(DevicePauseSource.Device, now));
        Assert.Equal(DeviceIngestStatus.Stored, (await IngestAsync(resumed, resumed.SealFrame(resumed.Wake(now)))).Status);
        Assert.Equal((4L, 1L, 5L), await CountsAsync(resumed));
    }

    [Fact]
    public async Task AFailedTransactionIsRetryAndChangesNeitherTheStoredNorTheInMemoryReplayState()
    {
        var node = await SeedNodeAsync();
        var now = identity.Time.GetUtcNow();
        Assert.Equal(DeviceIngestStatus.Stored, (await IngestAsync(node, node.SealFrame(node.Wake(now)))).Status);
        var before = await ReplayRowAsync(node);
        var frame = node.SealFrame(node.Wake(now));

        // Through another Hub than the recorded one: a frame that is not committed records no relay.
        identity.Ingestion.FailNextCommits(1);
        var failed = await IngestAsync(node, frame, HubB);

        Assert.Equal(DeviceIngestStatus.Retry, failed.Status);
        Assert.Null(failed.Downlink);
        Assert.Equal(before, await ReplayRowAsync(node));
        Assert.Equal((4L, 1L, 5L), await CountsAsync(node));
        Assert.Equal(["device.enrolled", "device.relay-changed"], await identity.AliasesAsync($"device/{node.DeviceId}"));

        // The very same sealed frame is accepted afterwards: its counter was not consumed in memory either.
        var again = await IngestAsync(node, frame);
        Assert.Equal(DeviceIngestStatus.Stored, again.Status);
        Assert.Equal(1UL, SealedEnvelope.Parser.ParseFrom(again.Downlink).Counter);
        Assert.Equal((8L, 2L, 10L), await CountsAsync(node));

        // The same holds for a frame that stores no row.
        var ahead = node.SealFrame(node.Wake(now.AddHours(1)));
        identity.Ingestion.FailNextCommits(1);
        Assert.Equal(DeviceIngestStatus.Retry, (await IngestAsync(node, ahead)).Status);
        Assert.Equal(DeviceIngestStatus.RejectedTime, (await IngestAsync(node, ahead)).Status);
    }

    [Fact]
    public async Task WhenTheReplayWindowCannotBeReadTheFrameIsRetryAndNothingChanges()
    {
        var node = await SeedNodeAsync();
        var now = identity.Time.GetUtcNow();
        Assert.Equal(DeviceIngestStatus.Stored, (await IngestAsync(node, node.SealFrame(node.Wake(now)))).Status);
        var before = await ReplayRowAsync(node);

        // Only a fresh activation reads the window: the silo restarts, then the read fails once.
        await identity.RestartSiloAsync();
        now = identity.Time.GetUtcNow();
        var frame = node.SealFrame(node.Wake(now));
        identity.Ingestion.FailNextLoads(1);

        var failed = await IngestAsync(node, frame);

        Assert.Equal(DeviceIngestStatus.Retry, failed.Status);
        Assert.Null(failed.Downlink);
        Assert.Equal(before, await ReplayRowAsync(node));
        Assert.Equal((4L, 1L, 5L), await CountsAsync(node));

        var again = await IngestAsync(node, frame);
        Assert.Equal(DeviceIngestStatus.Stored, again.Status);
        Assert.Equal(1UL, node.OpenDownlink(SealedEnvelope.Parser.ParseFrom(again.Downlink)).AckedCounter);
        Assert.Equal((8L, 2L, 10L), await CountsAsync(node));
    }

    [Fact]
    public async Task AWindowAdvancedUnderneathALiveGrainIsNotOverwritten()
    {
        var node = await SeedNodeAsync();
        var now = identity.Time.GetUtcNow();
        Assert.Equal(DeviceIngestStatus.Stored, (await IngestAsync(node, node.SealFrame(node.Wake(now)))).Status);

        // advance-replay while the grain still holds the old window in memory (the apps were not stopped).
        await ReadingsMaintenance.AdvanceReplayAsync(identity.Database.ConnectionString, cancellationToken: Ct);
        var advanced = await ReplayRowAsync(node);
        Assert.Equal((64m, -1L), (advanced.HighWater, advanced.Seen));
        var next = node.SealFrame(node.Wake(now));

        // The stale window would accept counter 1; the commit refuses to move the stored mark back.
        var stale = await IngestAsync(node, next);
        Assert.Equal(DeviceIngestStatus.Retry, stale.Status);
        Assert.Null(stale.Downlink);
        Assert.Equal(advanced, await ReplayRowAsync(node));
        Assert.Equal((4L, 1L, 5L), await CountsAsync(node));

        // The grain read the window again: the same frame is now a replay, and one above the mark is stored.
        Assert.Equal(DeviceIngestStatus.RejectedReplay, (await IngestAsync(node, next)).Status);
        Assert.Equal(DeviceIngestStatus.Stored, (await IngestAsync(node, node.SealFrame(65, node.Wake(now)))).Status);
        Assert.Equal(65m, (await ReplayRowAsync(node)).HighWater);
    }

    [Fact]
    public async Task AFrameThroughAnotherHubJournalsOneRelayChange()
    {
        var node = await SeedNodeAsync();
        var now = identity.Time.GetUtcNow();
        var stream = $"device/{node.DeviceId}";

        await IngestAsync(node, node.SealFrame(node.Wake(now)), HubA);
        await IngestAsync(node, node.SealFrame(node.Wake(now)), HubA);
        Assert.Equal(["device.enrolled", "device.relay-changed"], await identity.AliasesAsync(stream));

        await IngestAsync(node, node.SealFrame(node.Wake(now)), HubB);
        await IngestAsync(node, node.SealFrame(node.Wake(now)), HubB);
        Assert.Equal(["device.enrolled", "device.relay-changed", "device.relay-changed"], await identity.AliasesAsync(stream));

        var events = await identity.Store.ReadStreamAsync(stream, Ct);
        Assert.Equal(new DeviceRelayChanged(HubA, now), events[1].Data);
        Assert.Equal(new DeviceRelayChanged(HubB, now), events[2].Data);

        // A frame that is not accepted records no relay.
        var other = await SeedNodeAsync();
        var replayed = other.SealFrame(other.Wake(now));
        await IngestAsync(other, replayed, HubA);
        await IngestAsync(other, replayed, HubB);
        Assert.Equal(["device.enrolled", "device.relay-changed"], await identity.AliasesAsync($"device/{other.DeviceId}"));
    }

    [Fact]
    public async Task OnlyAnEnrolledHubWithAFreshAuthenticRequestMayRelay()
    {
        var hub = await SeedDeviceAsync(DeviceKind.Hub);
        var node = await SeedNodeAsync();
        var stranger = SimulatedDevice.Create();
        var now = identity.Time.GetUtcNow();
        var body = SimulatedDevice.IngestBody(node.SealFrame(node.Wake(now)));

        Assert.True((await AuthenticateAsync(hub, hub, body, now)).Authenticated);
        Assert.True((await AuthenticateAsync(hub, hub, body, now.AddMinutes(5))).Authenticated);
        Assert.True((await AuthenticateAsync(hub, hub, body, now.AddMinutes(-5))).Authenticated);

        // A Node's key signs no relay; nor does a Device the Server never enrolled.
        Assert.False((await AuthenticateAsync(node, node, body, now)).Authenticated);
        Assert.False((await AuthenticateAsync(stranger, stranger, body, now)).Authenticated);

        // Another Device's signature under the Hub's ID, a stale time, a body other than the signed one.
        Assert.False((await AuthenticateAsync(hub, stranger, body, now)).Authenticated);
        Assert.False((await AuthenticateAsync(hub, hub, body, now.AddMinutes(5).AddMilliseconds(1))).Authenticated);
        Assert.False((await AuthenticateAsync(hub, hub, body, now.AddMinutes(-5).AddMilliseconds(-1))).Authenticated);
        Assert.False((await AuthenticateAsync(hub, hub, body, now, sentBody: SimulatedDevice.IngestBody())).Authenticated);
        Assert.False((await AuthenticateAsync(hub, hub, body, now, path: SimulatedDevice.HeartbeatPath)).Authenticated);

        // A nonce is good once, even with a newer timestamp.
        var nonce = System.Security.Cryptography.RandomNumberGenerator.GetBytes(CryptoSpec.HeartbeatNonceLength);
        Assert.True((await AuthenticateAsync(hub, hub, body, now, nonce)).Authenticated);
        Assert.False((await AuthenticateAsync(hub, hub, body, now, nonce)).Authenticated);
        Assert.False((await AuthenticateAsync(hub, hub, body, now.AddSeconds(1), nonce)).Authenticated);

        // Authenticating a relay journals nothing.
        Assert.Equal(["device.enrolled"], await identity.AliasesAsync($"device/{hub.DeviceId}"));
    }

    [Fact]
    public async Task AfterACrashBetweenCommitAndAnswerTheResendIsADuplicateUnderANewDownlinkCounter()
    {
        var node = await SeedNodeAsync();
        var frame = node.Wake(identity.Time.GetUtcNow());
        var sent = node.SealFrame(frame);

        // The commit succeeds; the answer never reaches the Node, and the silo dies with all it held in memory.
        var lost = await IngestAsync(node, sent);
        Assert.Equal(DeviceIngestStatus.Stored, lost.Status);
        var lostCounter = SealedEnvelope.Parser.ParseFrom(lost.Downlink).Counter;
        await identity.RestartSiloAsync();

        // The window came back from the database: the sealed bytes the Server already took are a replay.
        Assert.Equal(DeviceIngestStatus.RejectedReplay, (await IngestAsync(node, sent)).Status);

        var resend = await IngestAsync(node, node.SealFrame(frame));

        Assert.Equal(DeviceIngestStatus.Duplicate, resend.Status);
        var envelope = SealedEnvelope.Parser.ParseFrom(resend.Downlink);
        Assert.True(envelope.Counter > lostCounter, $"The downlink counter {envelope.Counter} is not above the lost {lostCounter}.");
        Assert.Equal(SimulatedDevice.ReadingSeqs(frame), SimulatedDevice.AckedReadings(node.OpenDownlink(envelope)));
        Assert.Equal((4L, 1L, 5L), await CountsAsync(node));
    }

    [Fact]
    public async Task AfterAdvanceReplayOldCountersAreRejectedAndTheDownlinkCounterJumps()
    {
        var node = await SeedNodeAsync();
        var silent = await SeedNodeAsync();
        var now = identity.Time.GetUtcNow();
        Assert.Equal(DeviceIngestStatus.Stored, (await IngestAsync(node, node.SealFrame(9, node.Wake(now)))).Status);
        var last = await IngestAsync(node, node.SealFrame(10, node.Wake(now)));
        var lastDownlink = SealedEnvelope.Parser.ParseFrom(last.Downlink).Counter;

        // The restore step, with the apps stopped; then the Server starts.
        var advanced = await ReadingsMaintenance.AdvanceReplayAsync(identity.Database.ConnectionString, cancellationToken: Ct);
        Assert.True(advanced >= 2, $"advance-replay changed {advanced} Devices.");
        await identity.RestartSiloAsync();
        now = identity.Time.GetUtcNow();

        // At or below the old high-water mark + 64: a replay, also in a slot that was never seen.
        foreach (var counter in new ulong[] { 5, 10, 11, 73, 74 })
        {
            Assert.Equal(DeviceIngestStatus.RejectedReplay, (await IngestAsync(node, node.SealFrame(counter, node.Wake(now)))).Status);
        }

        var accepted = await IngestAsync(node, node.SealFrame(75, node.Wake(now)));
        Assert.Equal(DeviceIngestStatus.Stored, accepted.Status);
        var envelope = SealedEnvelope.Parser.ParseFrom(accepted.Downlink);
        Assert.True(
            envelope.Counter >= lastDownlink + ReadingsMaintenance.DefaultDownlinkMargin,
            $"The downlink counter {envelope.Counter} is less than {ReadingsMaintenance.DefaultDownlinkMargin} above {lastDownlink}.");
        Assert.Equal(75UL, node.OpenDownlink(envelope).AckedCounter);

        // A Node that had sent nothing before the backup loses its first 64 counters too.
        Assert.Equal(DeviceIngestStatus.RejectedReplay, (await IngestAsync(silent, silent.SealFrame(63, silent.Wake(now)))).Status);
        var first = await IngestAsync(silent, silent.SealFrame(64, silent.Wake(now)));
        Assert.Equal(DeviceIngestStatus.Stored, first.Status);
        Assert.Equal(ReadingsMaintenance.DefaultDownlinkMargin, SealedEnvelope.Parser.ParseFrom(first.Downlink).Counter);
    }

    private Task<SimulatedDevice> SeedNodeAsync(params object[] more) => SeedDeviceAsync(DeviceKind.Node, more);

    // An enrolled Device, seeded as its journal events: enrolment itself is Story 3.3's and 4.2's.
    private async Task<SimulatedDevice> SeedDeviceAsync(DeviceKind kind, params object[] more)
    {
        var device = SimulatedDevice.Create(kind == DeviceKind.Node ? ProtocolKind.Node : ProtocolKind.Hub);
        var enrolled = new DeviceEnrolled(SiteId, kind, identity.Vault.Wrap(device.DeviceId, device.Keys.DeviceKey), identity.Time.GetUtcNow());

        Assert.True(await identity.Store.AppendAsync($"device/{device.DeviceId}", 0, [enrolled, .. more], Ct));
        return device;
    }

    private Task<DeviceIngestResult> IngestAsync(SimulatedDevice node, SealedEnvelope envelope, string hubId = HubA) =>
        identity.Device(node.DeviceId.ToString()).Ingest(
            new DeviceIngest(envelope.ProtocolVersion, envelope.Counter, envelope.Ciphertext.ToByteArray(), hubId),
            Ct);

    // The request as the Edge API reads it from the simulator's signed POST, handed to the grain of `claimed`.
    private Task<DeviceRelayAuthenticationResult> AuthenticateAsync(
        SimulatedDevice claimed,
        SimulatedDevice signer,
        byte[] body,
        DateTimeOffset time,
        byte[]? nonce = null,
        byte[]? sentBody = null,
        string path = SimulatedDevice.IngestPath)
    {
        nonce ??= System.Security.Cryptography.RandomNumberGenerator.GetBytes(CryptoSpec.HeartbeatNonceLength);
        var headers = signer.SignHeartbeat("POST", path, body, time.ToUnixTimeMilliseconds(), nonce);

        return identity.Device(claimed.DeviceId.ToString()).AuthenticateRelay(
            new DeviceRelayAuthentication(
                "POST",
                SimulatedDevice.IngestPath,
                sentBody ?? body,
                time.ToUnixTimeMilliseconds(),
                nonce,
                Convert.FromHexString(headers[CryptoSpec.HeartbeatSignatureHeader])),
            Ct);
    }

    private async Task<(long Readings, long Reports, long Keys)> CountsAsync(SimulatedDevice device) =>
        (await CountAsync("readings", device), await CountAsync("device_reports", device), await CountAsync("reading_keys", device));

    private async Task<long> CountAsync(string table, SimulatedDevice device) =>
        await identity.Database.ScalarAsync<long>($"SELECT COUNT(*) FROM {table} WHERE device_id = @id", ("id", device.DeviceId.ToString()));

    private async Task<(decimal HighWater, long Seen, decimal DownlinkCounter)> ReplayRowAsync(SimulatedDevice device)
    {
        await using var command = identity.Database.DataSource.CreateCommand(
            "SELECT high_water, seen, downlink_counter FROM device_replay WHERE device_id = @id");
        command.Parameters.AddWithValue("id", device.DeviceId.ToString());
        await using var reader = await command.ExecuteReaderAsync(Ct);
        Assert.True(await reader.ReadAsync(Ct), "The Device has no replay row.");
        return (reader.GetDecimal(0), reader.GetInt64(1), reader.GetDecimal(2));
    }

    private async Task<(decimal ReportSeq, DateTimeOffset MeasuredAt, short? Battery, string Charging)> ReportAsync(SimulatedDevice device)
    {
        await using var command = identity.Database.DataSource.CreateCommand(
            "SELECT reading_seq, measured_at, battery_percent, charging FROM device_reports WHERE device_id = @id");
        command.Parameters.AddWithValue("id", device.DeviceId.ToString());
        await using var reader = await command.ExecuteReaderAsync(Ct);
        Assert.True(await reader.ReadAsync(Ct), "The Device has no report.");
        var report = (
            reader.GetDecimal(0),
            reader.GetFieldValue<DateTimeOffset>(1),
            await reader.IsDBNullAsync(2, Ct) ? (short?)null : reader.GetInt16(2),
            reader.GetString(3));
        Assert.False(await reader.ReadAsync(Ct), "The Device has more than one report.");
        return report;
    }

    private async Task<List<ReadingRow>> ReadingsAsync(SimulatedDevice device)
    {
        var rows = new List<ReadingRow>();
        await using var command = identity.Database.DataSource.CreateCommand(
            """
            SELECT sensor_id, reading_seq, measured_at, slot, quantity, raw_value, calibration_id IS NULL, time_unsynced,
                boot_id, uptime_ms, received_at
            FROM readings WHERE device_id = @id ORDER BY reading_seq
            """);
        command.Parameters.AddWithValue("id", device.DeviceId.ToString());
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            rows.Add(new ReadingRow(
                reader.GetGuid(0),
                reader.GetDecimal(1),
                reader.GetFieldValue<DateTimeOffset>(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.GetInt64(5),
                reader.GetBoolean(6),
                reader.GetBoolean(7),
                await reader.IsDBNullAsync(8, Ct) ? null : reader.GetDecimal(8),
                await reader.IsDBNullAsync(9, Ct) ? null : reader.GetDecimal(9),
                reader.GetFieldValue<DateTimeOffset>(10)));
        }

        return rows;
    }

    private sealed record ReadingRow(
        Guid SensorId,
        decimal ReadingSeq,
        DateTimeOffset MeasuredAt,
        int Slot,
        string Quantity,
        long RawValue,
        bool CalibrationIsNull,
        bool TimeUnsynced,
        decimal? BootId,
        decimal? UptimeMs,
        DateTimeOffset ReceivedAt)
    {
        public bool BootIsNull => BootId is null && UptimeMs is null;
    }
}
