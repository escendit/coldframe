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
        var vector = Vectors.List("frames").Single(vector => vector.Text("purpose") == "ack");
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
