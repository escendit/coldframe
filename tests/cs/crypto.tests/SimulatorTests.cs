using System.Buffers.Text;
using Coldframe.DeviceSimulator;
using Coldframe.Protocol.Device.V1;
using Coldframe.Protocol.Setup.V1;
using Google.Protobuf;

namespace Coldframe.Crypto.Tests;

/// <summary>
/// The Device simulator produces exactly the shared vectors, from the generated Protobuf types and Coldframe.Crypto.
/// </summary>
public sealed class SimulatorTests
{
    [Fact]
    public void ItHasTheIdentityOfItsRootKey()
    {
        var vector = Vectors.First("keyHierarchy");
        var device = SimulatedDevice.Create(vector.Bytes("rootKey"), DeviceKind.Hub);
        Assert.Equal(vector.Text("deviceId"), device.DeviceId.ToString());
        var identity = device.Identity();
        Assert.Equal(vector.Bytes("deviceId"), identity.DeviceId.ToByteArray());
        Assert.Equal(DeviceKind.Hub, identity.Kind);
    }

    [Fact]
    public void ARandomDeviceHasItsOwnIdentity() =>
        Assert.NotEqual(SimulatedDevice.Create().DeviceId, SimulatedDevice.Create().DeviceId);

    [Fact]
    public void ItSealsTheEnrolmentOfTheVectors()
    {
        foreach (var vector in Vectors.List("enrolment"))
        {
            var device = SimulatedDevice.Create(vector.Bytes("rootKey"));
            var siteId = "0192a000-0000-7000-8000-000000000001";
            var enrolment = device.SealEnrolment(vector.Bytes("recipientPublicKey"), siteId, vector.Bytes("ikmE"));
            Assert.Equal(siteId, enrolment.SiteId);
            Assert.Equal(vector.Text("deviceId"), enrolment.DeviceId);
            Assert.Equal("hub", enrolment.Kind);
            Assert.Equal(vector.Bytes("enc"), Base64Url.DecodeFromChars(enrolment.Enc));
            Assert.Equal(vector.Bytes("ciphertext"), Base64Url.DecodeFromChars(enrolment.Ciphertext));
            Assert.Equal(vector.Bytes("enc"), enrolment.Response.Enc.ToByteArray());
            Assert.Equal(vector.Bytes("ciphertext"), enrolment.Response.Ciphertext.ToByteArray());
            Assert.Equal(vector.Bytes("deviceId"), enrolment.Response.DeviceId.ToByteArray());

            var server = SimulatedServer.FromEnrolment(vector.Bytes("recipientPrivateKey"), enrolment);
            Assert.Equal(vector.Text("deviceKey"), Vectors.Hex(server.Keys.DeviceKey.ToArray()));
            Assert.Equal(device.DeviceId, server.DeviceId);
        }
    }

    [Fact]
    public void ARandomEnrolmentOpensOnTheServer()
    {
        var serverPrivateKey = X25519.GeneratePrivateKey();
        var device = SimulatedDevice.Create(DeviceKind.Node);
        var enrolment = device.SealEnrolment(X25519.PublicKey(serverPrivateKey), "site");
        Assert.Equal("node", enrolment.Kind);
        Assert.Equal(device.DeviceId, SimulatedServer.FromEnrolment(serverPrivateKey, enrolment).DeviceId);
    }

    [Fact]
    public void ItSignsHeartbeatsAsTheVectors()
    {
        foreach (var vector in Vectors.List("heartbeat"))
        {
            var device = SimulatedDevice.Create(vector.Bytes("rootKey"));
            var headers = device.SignHeartbeat(vector.Text("method"), vector.Text("path"), vector.Bytes("body"), (long)vector.Number("timestampMs"), vector.Bytes("nonce"));
            foreach (var expected in vector.GetProperty("headers").EnumerateObject())
            {
                Assert.Equal(expected.Value.GetString(), headers[expected.Name]);
            }
        }
    }

    [Fact]
    public void ItSealsUplinkFramesAsTheVectorsAndTheServerVerifiesThem()
    {
        foreach (var vector in Vectors.List("frames").Where(vector => vector.Text("purpose") == "seal"))
        {
            var device = SimulatedDevice.Create(vector.Bytes("rootKey"));
            var envelope = device.SealFrame(vector.Number("counter"), vector.Bytes("plaintext"));
            Assert.Equal(CryptoSpec.ProtocolMajor, envelope.ProtocolVersion);
            Assert.Equal(vector.Bytes("deviceId"), envelope.DeviceId.ToByteArray());
            Assert.Equal(vector.Number("counter"), envelope.Counter);
            Assert.Equal(vector.Text("ciphertext"), Vectors.Hex(envelope.Ciphertext.ToByteArray()));

            var server = new SimulatedServer(device.Keys.DeviceKey);
            var wire = SealedEnvelope.Parser.ParseFrom(envelope.ToByteArray());
            Assert.Equal(vector.Bytes("plaintext"), server.VerifyFrame(wire));
            Assert.Equal(CryptoFailure.Replay, Assert.Throws<CryptoFailureException>(() => server.VerifyFrame(wire)).Failure);
        }
    }

    [Fact]
    public void TheServerSealsTheDownlinkOfTheVectorsAndTheDeviceOpensIt()
    {
        var vectors = Vectors.List("frames").Where(vector => vector.Text("purpose") == "ack").ToList();
        Assert.Equal(2, vectors.Count);
        foreach (var vector in vectors)
        {
            var device = SimulatedDevice.Create(vector.Bytes("rootKey"));
            var server = new SimulatedServer(device.Keys.DeviceKey);
            var expected = Downlink.Parser.ParseFrom(vector.Bytes("plaintext"));
            Assert.Equal(CryptoSpec.ProtocolMajor, expected.ProtocolVersion);
            Assert.Empty(expected.Commands);

            var envelope = server.SealDownlink(vector.Number("counter"), expected);
            Assert.Equal(vector.Text("ciphertext"), Vectors.Hex(envelope.Ciphertext.ToByteArray()));

            var opened = device.OpenDownlink(SealedEnvelope.Parser.ParseFrom(envelope.ToByteArray()));
            Assert.Equal(expected, opened);
            Assert.Equal(CryptoFailure.Replay, Assert.Throws<CryptoFailureException>(() => device.OpenDownlink(envelope)).Failure);
        }
    }

    [Fact]
    public void ANodeWakeIsTheNodeFrameOfTheVectors()
    {
        var vector = Vectors.List("frames").Single(vector => Message(vector) == "NodeFrame");
        var message = vector.GetProperty("message");
        var node = SimulatedDevice.Create(vector.Bytes("rootKey"), DeviceKind.Node);
        node.NextReadingSeq = message.GetProperty("readings")[0].Number("readingSeq");
        node.BootId = message.Number("bootId");
        node.UptimeMs = message.Number("uptimeMs");

        var readings = message.GetProperty("readings").EnumerateArray()
            .Select(reading => new SimulatedReading(
                reading.GetProperty("slot").GetUInt32(),
                Enum.GetValues<Quantity>().Single(quantity =>
                    quantity != Quantity.Unspecified && SimulatedDevice.QuantityToken(quantity) == reading.Text("quantity")),
                long.Parse(reading.Text("value"), System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();
        Assert.Equal("charging", message.Text("charging"));
        var frame = node.Wake(
            DateTimeOffset.FromUnixTimeMilliseconds((long)message.Number("measuredAtMs")),
            message.GetProperty("batteryPercent").GetUInt32(),
            ChargeStatus.Charging,
            readings);

        Assert.Equal(message.Number("reportSeq"), frame.ReportSeq);
        Assert.Equal(message.Number("reportSeq") + 1, node.NextReadingSeq);
        Assert.Equal([100, 101, 102, 103, 104], SimulatedDevice.ReadingSeqs(frame));

        // The generated Protobuf types write exactly the bytes the contract's reference encoder wrote.
        var envelope = node.SealFrame(vector.Number("counter"), frame);
        Assert.Equal(vector.Text("ciphertext"), Vectors.Hex(envelope.Ciphertext.ToByteArray()));

        var decoded = NodeFrame.Parser.ParseFrom(vector.Bytes("plaintext"));
        Assert.Equal(NodeFrame.MeasuredOneofCase.MeasuredAtMs, decoded.MeasuredCase);
        Assert.Equal(-2500, decoded.Readings[1].Value);
        Assert.True(decoded.HasBatteryPercent);

        // A resend seals the same report under a new counter.
        var resend = node.SealFrame(frame);
        Assert.Equal(vector.Number("counter") + 1, resend.Counter);
        var server = new SimulatedServer(node.Keys.DeviceKey);
        Assert.Equal(server.VerifyFrame(envelope), server.VerifyFrame(resend));
    }

    [Fact]
    public void TheDownlinkOfTheVectorsAcknowledgesTheReadingsOfTheNodeFrame()
    {
        var frame = NodeFrame.Parser.ParseFrom(Vectors.List("frames").Single(vector => Message(vector) == "NodeFrame").Bytes("plaintext"));
        var vector = Vectors.List("frames").Single(vector => Message(vector) == "Downlink");
        var message = vector.GetProperty("message");
        var downlink = Downlink.Parser.ParseFrom(vector.Bytes("plaintext"));

        Assert.Equal(message.Number("ackedCounter"), downlink.AckedCounter);
        Assert.Equal((long)message.Number("serverTimeMs"), downlink.ServerTimeMs);
        var range = Assert.Single(downlink.AckedReadings);
        Assert.Equal(message.GetProperty("ackedReadings")[0].Number("first"), range.First);
        Assert.Equal(message.GetProperty("ackedReadings")[0].Number("last"), range.Last);
        Assert.Equal(SimulatedDevice.ReadingSeqs(frame), SimulatedDevice.AckedReadings(downlink));
    }

    [Fact]
    public void AnUnsyncedWakeCarriesItsBootAndUptime()
    {
        var node = SimulatedDevice.Create(DeviceKind.Node);
        node.BootId = 7;
        var frame = node.WakeUnsynced(uptimeMs: 1_500, batteryPercent: null, charging: ChargeStatus.Unspecified);

        Assert.Equal(NodeFrame.MeasuredOneofCase.Unsynced, frame.MeasuredCase);
        Assert.Equal((7UL, 1_500UL), (frame.Unsynced.BootId, frame.Unsynced.UptimeMs));
        Assert.False(frame.HasBatteryPercent);

        // After a reboot the same report is sealed by another boot.
        node.BootId = 8;
        node.UptimeMs = 900;
        var resent = NodeFrame.Parser.ParseFrom(new SimulatedServer(node.Keys.DeviceKey).VerifyFrame(node.SealFrame(frame)));
        Assert.Equal((8UL, 900UL), (resent.BootId, resent.UptimeMs));
        Assert.Equal(7UL, resent.Unsynced.BootId);
    }

    [Fact]
    public void TheIngestEnvelopeAndItsAnswerRoundTrip()
    {
        var node = SimulatedDevice.Create(DeviceKind.Node);
        var hub = SimulatedDevice.Create();
        var frame = node.SealFrame(node.Wake(DateTimeOffset.FromUnixTimeMilliseconds(1_790_000_000_000)));
        var body = SimulatedDevice.IngestBody(frame);

        using var document = System.Text.Json.JsonDocument.Parse(body);
        var encoded = Assert.Single(document.RootElement.GetProperty("frames").EnumerateArray()).GetString();
        Assert.Equal(frame, SealedEnvelope.Parser.ParseFrom(Convert.FromBase64String(encoded!)));

        using var request = hub.IngestRequest(1_790_000_000_000, new byte[CryptoSpec.HeartbeatNonceLength], body);
        Assert.Equal(SimulatedDevice.IngestPath, request.RequestUri!.OriginalString);
        Assert.True(Heartbeat.Verify(
            hub.Keys.HubAuthKey,
            "POST",
            SimulatedDevice.IngestPath,
            body,
            1_790_000_000_000,
            new byte[CryptoSpec.HeartbeatNonceLength],
            Convert.FromHexString(request.Headers.GetValues(CryptoSpec.HeartbeatSignatureHeader).Single())));

        var downlink = new SimulatedServer(node.Keys.DeviceKey).SealDownlink(new Downlink { ProtocolVersion = CryptoSpec.ProtocolMajor });
        var answer = System.Text.Encoding.UTF8.GetBytes(
            $"{{\"results\":[{{\"status\":\"stored\",\"downlink\":\"{SimulatedDevice.EncodeFrame(downlink)}\"}},{{\"status\":\"rejected_auth\"}}]}}");
        var results = SimulatedDevice.ReadIngestResponse(answer);
        Assert.Equal(["stored", "rejected_auth"], results.Select(result => result.Status));
        Assert.Equal(downlink, results[0].Downlink);
        Assert.Null(results[1].Downlink);
    }

    private static string? Message(System.Text.Json.JsonElement vector) =>
        vector.TryGetProperty("message", out var message) ? message.Text("type") : null;

    [Fact]
    public void ADownlinkForAnotherDeviceOrTamperedIsRefused()
    {
        var device = SimulatedDevice.Create();
        var other = new SimulatedServer(SimulatedDevice.Create().Keys.DeviceKey);
        var downlink = new Downlink { ProtocolVersion = CryptoSpec.ProtocolMajor, AckedCounter = 1, ServerTimeMs = 1 };
        Assert.Throws<CryptoFailureException>(() => device.OpenDownlink(other.SealDownlink(downlink)));

        var server = new SimulatedServer(device.Keys.DeviceKey);
        var envelope = server.SealDownlink(downlink);
        var tampered = envelope.Ciphertext.ToByteArray();
        tampered[0] ^= 1;
        envelope.Ciphertext = ByteString.CopyFrom(tampered);
        Assert.Equal(CryptoFailure.AuthenticationFailed, Assert.Throws<CryptoFailureException>(() => device.OpenDownlink(envelope)).Failure);
    }
}
