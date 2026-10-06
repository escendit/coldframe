using System.Net;
using System.Text;
using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Sites;
using Coldframe.Crypto;
using Coldframe.DeviceSimulator;
using Coldframe.Protocol.Device.V1;
using Coldframe.Server.IntegrationTests.Edge;
using Google.Protobuf;
using ProtocolKind = Coldframe.Protocol.Setup.V1.DeviceKind;

namespace Coldframe.Server.IntegrationTests.Devices;

/// <summary>
/// <c>POST /device/ingest</c> on the AppHost (Story 4.5; FR-4, AD-9, AD-12, AD-17): a simulated Hub relays
/// the sealed frames of simulated Nodes, every Device enrolled through the Edge API. The rows that need a
/// fake clock, a Pause, a failing database or a restart are in <see cref="IngestGrainTests"/>.
/// </summary>
[Collection(IngestSuites.Name)]
public sealed class IngestTests(EdgeApiFixture edge) : IClassFixture<EdgeApiFixture>
{
    private const string Unauthorized = "urn:coldframe:problem:device-unauthorized";
    private const string Validation = "urn:coldframe:problem:validation";

    // How far the Server clock and the test clock may be apart.
    private static readonly TimeSpan ClockTolerance = TimeSpan.FromSeconds(30);

    private static readonly SemaphoreSlim SiteLock = new(1, 1);
    private static Stage? _stage;

    [Fact]
    public async Task ThreeFramesWithATamperedSecondAreStoredRejectedAuthStored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var stage = await StageAsync(cancellationToken);
        var node = await EnrolAsync(stage, ProtocolKind.Node, cancellationToken);
        var first = node.SealFrame(node.Wake(Now()));
        var second = node.SealFrame(node.Wake(Now()));
        var third = node.SealFrame(node.Wake(Now()));
        var tampered = second.Ciphertext.ToByteArray();
        tampered[^1] ^= 0x80;
        second.Ciphertext = ByteString.CopyFrom(tampered);

        var results = await IngestAsync(stage.Hub, SimulatedDevice.IngestBody(first, second, third), cancellationToken);

        Assert.Equal(["stored", "rejected_auth", "stored"], results.Select(result => result.Status));
        Assert.NotNull(results[0].Downlink);
        Assert.Null(results[1].Downlink);
        Assert.NotNull(results[2].Downlink);
        Assert.Equal(first.Counter, node.OpenDownlink(results[0].Downlink!).AckedCounter);
        Assert.Equal(third.Counter, node.OpenDownlink(results[2].Downlink!).AckedCounter);
        Assert.Equal((8L, 2L), await CountsAsync(node, cancellationToken));
    }

    [Fact]
    public async Task AStoredFrameIsAcknowledgedWithTheServerTimeItsReadingSeqsAndNoCommands()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var stage = await StageAsync(cancellationToken);
        var node = await EnrolAsync(stage, ProtocolKind.Node, cancellationToken);
        node.NextReadingSeq = 7_000;
        // Now, to the millisecond: the row then belongs to the current month's partition whenever the test runs.
        var measuredAt = DateTimeOffset.FromUnixTimeMilliseconds(Now().ToUnixTimeMilliseconds());
        var frame = node.Wake(measuredAt, batteryPercent: 64, ChargeStatus.Charging);
        var sent = Now();

        using var server = edge.CreateServerClient();
        using var request = stage.Hub.IngestRequest(sent, SimulatedDevice.IngestBody(node.SealFrame(frame)));
        using var response = await server.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var json = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        using (var body = System.Text.Json.JsonDocument.Parse(json))
        {
            Assert.Equal(["results"], body.RootElement.EnumerateObject().Select(property => property.Name));
            var only = Assert.Single(body.RootElement.GetProperty("results").EnumerateArray());
            Assert.Equal(["downlink", "status"], only.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        }

        var result = Assert.Single(SimulatedDevice.ReadIngestResponse(json));
        Assert.Equal("stored", result.Status);
        Assert.Equal(node.DeviceId.ToBytes(), result.Downlink!.DeviceId.ToByteArray());

        // Only the Node's ack/v1 key opens it; the Hub's does not.
        Assert.Throws<CryptoFailureException>(() => stage.Hub.OpenDownlink(result.Downlink));
        var downlink = node.OpenDownlink(result.Downlink);
        Assert.Equal(CryptoSpec.ProtocolMajor, downlink.ProtocolVersion);
        Assert.Equal(0UL, downlink.AckedCounter);
        Assert.InRange(
            DateTimeOffset.FromUnixTimeMilliseconds(downlink.ServerTimeMs),
            sent - ClockTolerance,
            Now() + ClockTolerance);
        Assert.Empty(downlink.Commands);
        Assert.Equal([7_000, 7_001, 7_002, 7_003, 7_004], SimulatedDevice.AckedReadings(downlink));
        Assert.Equal(SimulatedDevice.ReadingSeqs(frame), SimulatedDevice.AckedReadings(downlink));

        // The Readings and the device report are in this month's partitions, under the AD-19 Sensor IDs.
        var month = ReadingsPartition("readings", measuredAt);
        await using var readings = edge.Database.CreateCommand(
            """
            SELECT sensor_id, reading_seq, raw_value, quantity, measured_at, calibration_id IS NULL, time_unsynced, tableoid::regclass::text
            FROM readings WHERE device_id = @id ORDER BY reading_seq
            """);
        readings.Parameters.AddWithValue("id", node.DeviceId.ToString());
        await using (var reader = await readings.ExecuteReaderAsync(cancellationToken))
        {
            foreach (var (expected, index) in SimulatedDevice.DefaultReadings.Select((reading, index) => (reading, index)))
            {
                Assert.True(await reader.ReadAsync(cancellationToken));
                Assert.Equal(node.SensorId(expected.Slot, expected.Quantity), reader.GetGuid(0));
                Assert.Equal(7_000m + index, reader.GetDecimal(1));
                Assert.Equal(expected.Value, reader.GetInt64(2));
                Assert.Equal(SimulatedDevice.QuantityToken(expected.Quantity), reader.GetString(3));
                Assert.Equal(measuredAt, reader.GetFieldValue<DateTimeOffset>(4));
                Assert.True(reader.GetBoolean(5));
                Assert.False(reader.GetBoolean(6));
                Assert.Equal(month, reader.GetString(7));
            }

            Assert.False(await reader.ReadAsync(cancellationToken));
        }

        await using var report = edge.Database.CreateCommand(
            "SELECT reading_seq, battery_percent, charging, measured_at, tableoid::regclass::text FROM device_reports WHERE device_id = @id");
        report.Parameters.AddWithValue("id", node.DeviceId.ToString());
        await using (var reader = await report.ExecuteReaderAsync(cancellationToken))
        {
            Assert.True(await reader.ReadAsync(cancellationToken));
            Assert.Equal(
                (7_004m, (short)64, "charging", measuredAt, ReadingsPartition("device_reports", measuredAt)),
                (reader.GetDecimal(0), reader.GetInt16(1), reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3), reader.GetString(4)));
            Assert.False(await reader.ReadAsync(cancellationToken));
        }

        // Nothing is journaled per frame: the enrolment, and the relay Hub once.
        Assert.Equal(["device.enrolled", "device.relay-changed"], await edge.AliasesAsync($"device/{node.DeviceId}", cancellationToken));
    }

    [Fact]
    public async Task AResendIsADuplicateThatIsAcknowledgedAndAddsNoRow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var stage = await StageAsync(cancellationToken);
        var node = await EnrolAsync(stage, ProtocolKind.Node, cancellationToken);
        var frame = node.Wake(Now());

        var first = Assert.Single(await IngestAsync(stage.Hub, SimulatedDevice.IngestBody(node.SealFrame(frame)), cancellationToken));
        var resend = Assert.Single(await IngestAsync(stage.Hub, SimulatedDevice.IngestBody(node.SealFrame(frame)), cancellationToken));

        Assert.Equal(("stored", "duplicate"), (first.Status, resend.Status));
        Assert.True(resend.Downlink!.Counter > first.Downlink!.Counter);
        var downlink = node.OpenDownlink(resend.Downlink);
        Assert.Equal(1UL, downlink.AckedCounter);
        Assert.Equal(SimulatedDevice.ReadingSeqs(frame), SimulatedDevice.AckedReadings(downlink));
        Assert.Equal((4L, 1L), await CountsAsync(node, cancellationToken));

        // A frame that mixes the stored Readings with a new one adds only the new row and acknowledges all.
        var partlyNew = frame.Clone();
        partlyNew.Readings.Add(new Reading { Slot = 1, ReadingSeq = 40, Quantity = Quantity.AirTemperature, Value = -1_250 });
        var mixed = Assert.Single(await IngestAsync(stage.Hub, SimulatedDevice.IngestBody(node.SealFrame(partlyNew)), cancellationToken));

        Assert.Equal("stored", mixed.Status);
        Assert.Equal([0, 1, 2, 3, 4, 40], SimulatedDevice.AckedReadings(node.OpenDownlink(mixed.Downlink!)));
        Assert.Equal((5L, 1L), await CountsAsync(node, cancellationToken));
    }

    [Fact]
    public async Task TheSameSealedBytesTwiceAreAReplayAndAnOlderUnseenCounterIsAccepted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var stage = await StageAsync(cancellationToken);
        var node = await EnrolAsync(stage, ProtocolKind.Node, cancellationToken);
        var later = node.SealFrame(20, node.Wake(Now()));
        var earlier = node.SealFrame(12, node.Wake(Now()));

        // In one envelope: frames of one Node are processed in request order.
        var results = await IngestAsync(stage.Hub, SimulatedDevice.IngestBody(later, later, earlier, earlier), cancellationToken);

        Assert.Equal(["stored", "rejected_replay", "stored", "rejected_replay"], results.Select(result => result.Status));
        Assert.Null(results[1].Downlink);
        Assert.Null(results[3].Downlink);
        Assert.Equal(12UL, node.OpenDownlink(results[2].Downlink!).AckedCounter);
        Assert.Equal((8L, 2L), await CountsAsync(node, cancellationToken));

        // And across requests.
        var again = Assert.Single(await IngestAsync(stage.Hub, SimulatedDevice.IngestBody(later), cancellationToken));
        Assert.Equal("rejected_replay", again.Status);
    }

    [Fact]
    public async Task WhatDoesNotAuthenticateIsRejectedAuthAndDoesNotTouchTheOtherFrames()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var stage = await StageAsync(cancellationToken);
        var node = await EnrolAsync(stage, ProtocolKind.Node, cancellationToken);
        var other = await EnrolAsync(stage, ProtocolKind.Node, cancellationToken);
        var good = node.SealFrame(node.Wake(Now()));
        var template = node.SealFrame(node.Wake(Now()));

        var otherCounter = template.Clone();
        otherCounter.Counter += 100;
        var otherDevice = template.Clone();
        otherDevice.DeviceId = ByteString.CopyFrom(other.DeviceId.ToBytes());
        var otherVersion = template.Clone();
        otherVersion.ProtocolVersion = 2;
        var undecodable = node.SealFrame([0x0A, 0xFF]);

        string[] frames =
        [
            "not base64 at all!",
            Convert.ToBase64String(Encoding.UTF8.GetBytes("not a SealedEnvelope")),
            SimulatedDevice.EncodeFrame(otherCounter),
            SimulatedDevice.EncodeFrame(otherDevice),
            SimulatedDevice.EncodeFrame(otherVersion),
            SimulatedDevice.EncodeFrame(undecodable),
            SimulatedDevice.EncodeFrame(good),
        ];

        var results = await IngestAsync(stage.Hub, SimulatedDevice.IngestBody(frames), cancellationToken);

        Assert.Equal(
            ["rejected_auth", "rejected_auth", "rejected_auth", "rejected_auth", "rejected_auth", "rejected_auth", "stored"],
            results.Select(result => result.Status));
        Assert.All(results.Take(6), result => Assert.Null(result.Downlink));
        Assert.Equal(good.Counter, node.OpenDownlink(results[6].Downlink!).AckedCounter);
        Assert.Equal((4L, 1L), await CountsAsync(node, cancellationToken));
        Assert.Equal((0L, 0L), await CountsAsync(other, cancellationToken));
    }

    [Fact]
    public async Task ADeviceThatIsNotAnEnrolledNodeIsUnknownAndLeavesNoTrace()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var stage = await StageAsync(cancellationToken);
        var stranger = SimulatedDevice.Create(ProtocolKind.Node);
        var hubAsNode = stage.OtherHub.SealFrame(stage.OtherHub.Wake(Now()));
        var hubEvents = await edge.AliasesAsync($"device/{stage.OtherHub.DeviceId}", cancellationToken);

        var results = await IngestAsync(
            stage.Hub,
            SimulatedDevice.IngestBody(stranger.SealFrame(stranger.Wake(Now())), hubAsNode),
            cancellationToken);

        Assert.Equal(["unknown_device", "unknown_device"], results.Select(result => result.Status));
        Assert.All(results, result => Assert.Null(result.Downlink));
        Assert.Empty(await edge.AliasesAsync($"device/{stranger.DeviceId}", cancellationToken));
        Assert.Equal(hubEvents, await edge.AliasesAsync($"device/{stage.OtherHub.DeviceId}", cancellationToken));
        Assert.Equal((0L, 0L), await CountsAsync(stranger, cancellationToken));
        Assert.Equal((0L, 0L), await CountsAsync(stage.OtherHub, cancellationToken));
    }

    [Fact]
    public async Task ANodeWithALotAndOneWithoutAreStoredAlike()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var stage = await StageAsync(cancellationToken);
        var unassigned = await EnrolAsync(stage, ProtocolKind.Node, cancellationToken);
        var lotId = await edge.SeedLotAsync(stage.SiteId, "Tomatoes", cancellationToken);
        var assigned = await EnrolAsync(stage, ProtocolKind.Node, cancellationToken, lotId);
        Assert.Equal(["device.enrolled"], await edge.AliasesAsync($"device/{unassigned.DeviceId}", cancellationToken));
        Assert.Equal(["device.enrolled", "device.assigned"], await edge.AliasesAsync($"device/{assigned.DeviceId}", cancellationToken));

        var results = await IngestAsync(
            stage.Hub,
            SimulatedDevice.IngestBody(unassigned.SealFrame(unassigned.Wake(Now())), assigned.SealFrame(assigned.Wake(Now()))),
            cancellationToken);

        Assert.Equal(["stored", "stored"], results.Select(result => result.Status));
        Assert.Equal(0UL, unassigned.OpenDownlink(results[0].Downlink!).AckedCounter);
        Assert.Equal(0UL, assigned.OpenDownlink(results[1].Downlink!).AckedCounter);
        Assert.Equal((4L, 1L), await CountsAsync(unassigned, cancellationToken));
        Assert.Equal((4L, 1L), await CountsAsync(assigned, cancellationToken));
    }

    [Fact]
    public async Task AReadingOfAMonthWithoutAPartitionLandsInTheDefaultPartition()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var stage = await StageAsync(cancellationToken);
        var node = await EnrolAsync(stage, ProtocolKind.Node, cancellationToken);

        // A Node whose buffered Reading is from long before the first partition.
        var longAgo = new DateTimeOffset(2019, 3, 14, 6, 30, 0, TimeSpan.Zero);
        var result = Assert.Single(await IngestAsync(stage.Hub, SimulatedDevice.IngestBody(node.SealFrame(node.Wake(longAgo))), cancellationToken));

        Assert.Equal("stored", result.Status);
        await using var command = edge.Database.CreateCommand(
            """
            SELECT (SELECT string_agg(DISTINCT tableoid::regclass::text, ',') FROM readings WHERE device_id = @id),
                (SELECT string_agg(DISTINCT tableoid::regclass::text, ',') FROM device_reports WHERE device_id = @id)
            """);
        command.Parameters.AddWithValue("id", node.DeviceId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        Assert.True(await reader.ReadAsync(cancellationToken));
        Assert.Equal(("readings_default", "device_reports_default"), (reader.GetString(0), reader.GetString(1)));
    }

    [Fact]
    public async Task ATimeTenMinutesAheadIsRejectedTimeAndTheNextValidFrameIsStored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var stage = await StageAsync(cancellationToken);
        var node = await EnrolAsync(stage, ProtocolKind.Node, cancellationToken);

        var ahead = Assert.Single(await IngestAsync(
            stage.Hub,
            SimulatedDevice.IngestBody(node.SealFrame(node.Wake(Now().AddMinutes(10)))),
            cancellationToken));

        Assert.Equal("rejected_time", ahead.Status);
        Assert.Null(ahead.Downlink);
        Assert.Equal((0L, 0L), await CountsAsync(node, cancellationToken));

        var valid = node.SealFrame(node.Wake(Now()));
        var stored = Assert.Single(await IngestAsync(stage.Hub, SimulatedDevice.IngestBody(valid), cancellationToken));

        Assert.Equal("stored", stored.Status);
        Assert.Equal(valid.Counter, node.OpenDownlink(stored.Downlink!).AckedCounter);
        Assert.Equal((4L, 1L), await CountsAsync(node, cancellationToken));
    }

    [Fact]
    public async Task AFrameThroughAnotherHubJournalsOneRelayChange()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var stage = await StageAsync(cancellationToken);
        var node = await EnrolAsync(stage, ProtocolKind.Node, cancellationToken);
        var stream = $"device/{node.DeviceId}";

        foreach (var hub in new[] { stage.Hub, stage.Hub, stage.OtherHub, stage.OtherHub })
        {
            var result = Assert.Single(await IngestAsync(hub, SimulatedDevice.IngestBody(node.SealFrame(node.Wake(Now()))), cancellationToken));
            Assert.Equal("stored", result.Status);
        }

        Assert.Equal(["device.enrolled", "device.relay-changed", "device.relay-changed"], await edge.AliasesAsync(stream, cancellationToken));
        var events = await edge.ReadStreamAsync(stream, cancellationToken);
        Assert.Equal(stage.Hub.DeviceId.ToString(), Assert.IsType<DeviceRelayChanged>(events[1].Data).HubId);
        Assert.Equal(stage.OtherHub.DeviceId.ToString(), Assert.IsType<DeviceRelayChanged>(events[2].Data).HubId);

        // Relaying journals nothing on the Hub's own stream.
        Assert.DoesNotContain("device.seen", await edge.AliasesAsync($"device/{stage.Hub.DeviceId}", cancellationToken));
    }

    [Fact]
    public async Task ABadHubSignatureIsUnauthorizedAndNoFrameIsProcessed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var stage = await StageAsync(cancellationToken);
        var node = await EnrolAsync(stage, ProtocolKind.Node, cancellationToken);
        var signer = await EnrolAsync(stage, ProtocolKind.Node, cancellationToken);
        var frame = node.SealFrame(node.Wake(Now()));
        var body = SimulatedDevice.IngestBody(frame);
        using var server = edge.CreateServerClient();

        // Signed over one envelope, sent with another.
        using (var tampered = stage.Hub.IngestRequest(Now(), SimulatedDevice.IngestBody()))
        {
            tampered.Content = new ByteArrayContent(body);
            tampered.Content.Headers.ContentType = new("application/json");
            await AssertRefusedAsync(server, tampered, cancellationToken);
        }

        // More than five minutes off the Server clock.
        foreach (var offset in new[] { -301, 301 })
        {
            using var stale = stage.Hub.IngestRequest(Now().AddSeconds(offset), body);
            await AssertRefusedAsync(server, stale, cancellationToken);
        }

        // Signed by an enrolled Node, by a Device the Server does not know, and by another Device under the Hub's ID.
        using (var byNode = signer.IngestRequest(Now(), body))
        {
            await AssertRefusedAsync(server, byNode, cancellationToken);
        }

        var stranger = SimulatedDevice.Create();
        using (var byStranger = stranger.IngestRequest(Now(), body))
        {
            await AssertRefusedAsync(server, byStranger, cancellationToken);
        }

        using (var impostor = stranger.IngestRequest(Now(), body))
        {
            impostor.Headers.Remove(CryptoSpec.HeartbeatDeviceHeader);
            impostor.Headers.Add(CryptoSpec.HeartbeatDeviceHeader, stage.Hub.DeviceId.ToString());
            await AssertRefusedAsync(server, impostor, cancellationToken);
        }

        // A heartbeat signature does not sign an ingest: the path is part of what is signed.
        using (var otherPath = stage.Hub.HeartbeatRequest(Now(), body))
        {
            otherPath.RequestUri = new Uri(SimulatedDevice.IngestPath, UriKind.Relative);
            await AssertRefusedAsync(server, otherPath, cancellationToken);
        }

        // None of them processed the frame: nothing is stored, and its counter is still unused.
        Assert.Equal((0L, 0L), await CountsAsync(node, cancellationToken));
        Assert.Equal(["device.enrolled"], await edge.AliasesAsync($"device/{node.DeviceId}", cancellationToken));

        // An authentic request stores it; the same request again reuses its nonce and is refused.
        var nonce = System.Security.Cryptography.RandomNumberGenerator.GetBytes(CryptoSpec.HeartbeatNonceLength);
        var timestamp = Now().ToUnixTimeMilliseconds();
        using (var accepted = stage.Hub.IngestRequest(timestamp, nonce, body))
        using (var response = await server.SendAsync(accepted, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("stored", Assert.Single(SimulatedDevice.ReadIngestResponse(await response.Content.ReadAsByteArrayAsync(cancellationToken))).Status);
        }

        using var reused = stage.Hub.IngestRequest(timestamp, nonce, body);
        await AssertRefusedAsync(server, reused, cancellationToken);
    }

    [Theory]
    [InlineData("X-Coldframe-Device")]
    [InlineData("X-Coldframe-Timestamp")]
    [InlineData("X-Coldframe-Nonce")]
    [InlineData("X-Coldframe-Signature")]
    public async Task AMissingHeaderIsUnauthorized(string header)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var stage = await StageAsync(cancellationToken);
        using var server = edge.CreateServerClient();

        using var request = stage.Hub.IngestRequest(Now(), SimulatedDevice.IngestBody());
        request.Headers.Remove(header);

        await AssertRefusedAsync(server, request, cancellationToken);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"frames\":\"AAAA\"}")]
    [InlineData("{\"frames\":[1]}")]
    [InlineData("[]")]
    public async Task ABodyThatIsNotAnEnvelopeIsValidation(string body)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var stage = await StageAsync(cancellationToken);
        using var server = edge.CreateServerClient();

        // Authentic headers over the body as sent: only the envelope is wrong.
        using var request = stage.Hub.IngestRequest(Now(), Encoding.UTF8.GetBytes(body));
        using var response = await server.SendAsync(request, cancellationToken);

        await EdgeApiTests.AssertProblemAsync(response, HttpStatusCode.BadRequest, Validation, cancellationToken);
    }

    [Fact]
    public async Task MoreThan32FramesOrABodyOver16KiBIsValidationAndStoresNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var stage = await StageAsync(cancellationToken);
        var node = await EnrolAsync(stage, ProtocolKind.Node, cancellationToken);
        using var server = edge.CreateServerClient();

        var tooMany = Enumerable.Range(0, 33).Select(_ => node.SealFrame(node.Wake(Now()))).ToArray();
        using (var request = stage.Hub.IngestRequest(Now(), SimulatedDevice.IngestBody(tooMany)))
        using (var response = await server.SendAsync(request, cancellationToken))
        {
            await EdgeApiTests.AssertProblemAsync(response, HttpStatusCode.BadRequest, Validation, cancellationToken);
        }

        var padded = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(SimulatedDevice.IngestBody(tooMany[0])) + new string(' ', 16 * 1024));
        using (var request = stage.Hub.IngestRequest(Now(), padded))
        using (var response = await server.SendAsync(request, cancellationToken))
        {
            await EdgeApiTests.AssertProblemAsync(response, HttpStatusCode.BadRequest, Validation, cancellationToken);
        }

        Assert.Equal((0L, 0L), await CountsAsync(node, cancellationToken));

        // Exactly 32 frames are processed, each on its own.
        var results = await IngestAsync(stage.Hub, SimulatedDevice.IngestBody(tooMany[..32]), cancellationToken);
        Assert.Equal(32, results.Count);
        Assert.All(results, result => Assert.Equal("stored", result.Status));
        Assert.Equal(Enumerable.Range(0, 32).Select(index => (ulong)index), results.Select(result => node.OpenDownlink(result.Downlink!).AckedCounter));
        Assert.Equal((128L, 32L), await CountsAsync(node, cancellationToken));
    }

    [Fact]
    public async Task AnEmptyEnvelopeAnswersAnEmptyResultList()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var stage = await StageAsync(cancellationToken);
        using var server = edge.CreateServerClient();

        using var request = stage.Hub.IngestRequest(Now(), SimulatedDevice.IngestBody());
        using var response = await server.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"results\":[]}", await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task AUserTokenDoesNotAuthenticateAHub()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var stage = await StageAsync(cancellationToken);
        using var server = edge.CreateServerClient(stage.AdministratorToken);

        using var unsigned = await PostUnsignedAsync(server, cancellationToken);
        await EdgeApiTests.AssertProblemAsync(unsigned, HttpStatusCode.Unauthorized, Unauthorized, cancellationToken);

        // A signed envelope with a user token alongside still works: the token is ignored.
        using var signed = stage.Hub.IngestRequest(Now(), SimulatedDevice.IngestBody());
        using var accepted = await server.SendAsync(signed, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    /// <summary>
    /// An ingest request without any Device header, for the authorization matrix.
    /// </summary>
    internal static async Task<HttpResponseMessage> PostUnsignedAsync(HttpClient server, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(SimulatedDevice.IngestPath, UriKind.Relative))
        {
            Content = new ByteArrayContent(SimulatedDevice.IngestBody()),
        };
        request.Content.Headers.ContentType = new("application/json");

        // Awaited here: the request must outlive the send.
        return await server.SendAsync(request, cancellationToken);
    }

    private static DateTimeOffset Now() => TimeProvider.System.GetUtcNow();

    private static string ReadingsPartition(string table, DateTimeOffset measuredAt) =>
        Coldframe.Migrations.ReadingsMaintenance.PartitionName(table, measuredAt.UtcDateTime.Year, measuredAt.UtcDateTime.Month);

    private static async Task AssertRefusedAsync(HttpClient server, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await server.SendAsync(request, cancellationToken);
        await EdgeApiTests.AssertProblemAsync(response, HttpStatusCode.Unauthorized, Unauthorized, cancellationToken);
    }

    // Posts a signed envelope as `hub` and reads the 200 answer.
    private async Task<IReadOnlyList<SimulatedIngestResult>> IngestAsync(SimulatedDevice hub, byte[] body, CancellationToken cancellationToken)
    {
        using var server = edge.CreateServerClient();
        using var request = hub.IngestRequest(Now(), body);
        using var response = await server.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return SimulatedDevice.ReadIngestResponse(await response.Content.ReadAsByteArrayAsync(cancellationToken));
    }

    private async Task<(long Readings, long Reports)> CountsAsync(SimulatedDevice device, CancellationToken cancellationToken)
    {
        await using var command = edge.Database.CreateCommand(
            """
            SELECT (SELECT COUNT(*) FROM readings WHERE device_id = @id), (SELECT COUNT(*) FROM device_reports WHERE device_id = @id),
                (SELECT COUNT(*) FROM reading_keys WHERE device_id = @id)
            """);
        command.Parameters.AddWithValue("id", device.DeviceId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        Assert.True(await reader.ReadAsync(cancellationToken));

        // Every row has its key, and nothing else has one.
        Assert.Equal(reader.GetInt64(0) + reader.GetInt64(1), reader.GetInt64(2));
        return (reader.GetInt64(0), reader.GetInt64(1));
    }

    // Enrols a fresh simulated Device on the stage's Site through the Edge API.
    private async Task<SimulatedDevice> EnrolAsync(Stage stage, ProtocolKind kind, CancellationToken cancellationToken, string? lotId = null)
    {
        var device = SimulatedDevice.Create(kind);
        using var server = edge.CreateServerClient(stage.AdministratorToken);
        var sealedEnrolment = device.SealEnrolment(await edge.GetEnrolmentPublicKeyAsync(cancellationToken), stage.SiteId);
        using var enrolled = await EnrolmentTests.PostAsync(
            server,
            stage.SiteId,
            Guid.NewGuid().ToString(),
            new EnrolBody(sealedEnrolment.DeviceId, sealedEnrolment.Kind, sealedEnrolment.Enc, sealedEnrolment.Ciphertext, lotId),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, enrolled.StatusCode);

        return device;
    }

    // One Site with its Administrator and two enrolled Hubs, created once for the whole run.
    private async Task<Stage> StageAsync(CancellationToken cancellationToken)
    {
        await SiteLock.WaitAsync(cancellationToken);

        try
        {
            if (_stage is not null)
            {
                return _stage;
            }

            var administrator = await edge.CreateUserAsync("ingest-admin", cancellationToken);
            var siteId = Guid.CreateVersion7().ToString();
            var last = await edge.AppendAsync(
                $"site/{siteId}",
                [
                    new SiteCreated("Ingest", administrator.UserId),
                    new MembershipGranted(administrator.UserId, SiteRole.Owner),
                ],
                cancellationToken);
            await edge.WaitForIdentityCheckpointAsync(last);

            var stage = new Stage(siteId, administrator.AccessToken, null!, null!);
            _stage = stage with
            {
                Hub = await EnrolAsync(stage, ProtocolKind.Hub, cancellationToken),
                OtherHub = await EnrolAsync(stage, ProtocolKind.Hub, cancellationToken),
            };
            return _stage;
        }
        finally
        {
            SiteLock.Release();
        }
    }

    private sealed record Stage(string SiteId, string AdministratorToken, SimulatedDevice Hub, SimulatedDevice OtherHub);
}
