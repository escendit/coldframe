using System.Text;

namespace Coldframe.Crypto.Tests;

/// <summary>
/// Coldframe.Crypto reproduces every shared vector of <c>packages/crypto-spec/vectors.json</c>.
/// </summary>
public sealed class VectorTests
{
    [Fact]
    public void TheVectorsUseTheGeneratedProtocolMajor() =>
        Assert.Equal(CryptoSpec.ProtocolMajor, Vectors.Root.GetProperty("protocolMajor").GetInt32());

    [Fact]
    public void KeyHierarchy()
    {
        Assert.NotEmpty(Vectors.List("keyHierarchy"));
        foreach (var vector in Vectors.List("keyHierarchy"))
        {
            var deviceKey = Crypto.KeyHierarchy.DeriveDeviceKey(vector.Bytes("rootKey"));
            Assert.Equal(vector.Text("deviceKey"), Vectors.Hex(deviceKey));
            var keys = DeviceKeys.FromDeviceKey(deviceKey);
            Assert.Equal(vector.Text("sealKey"), Vectors.Hex(keys.SealKey.ToArray()));
            Assert.Equal(vector.Text("ackKey"), Vectors.Hex(keys.AckKey.ToArray()));
            Assert.Equal(vector.Text("hubAuthKey"), Vectors.Hex(keys.HubAuthKey.ToArray()));
            Assert.Equal(vector.Text("deviceId"), keys.DeviceId.ToString());
            Assert.Equal(keys.DeviceId, DeviceId.Parse(vector.Text("deviceId")));
        }
    }

    [Fact]
    public void FramesAndDownlinks()
    {
        Assert.Contains(Vectors.List("frames"), vector => vector.Text("purpose") == "seal");
        Assert.Contains(Vectors.List("frames"), vector => vector.Text("purpose") == "ack");
        foreach (var vector in Vectors.List("frames"))
        {
            var keys = DeviceKeys.FromRootKey(vector.Bytes("rootKey"));
            var key = vector.Text("purpose") == "seal" ? keys.SealKey.ToArray() : keys.AckKey.ToArray();
            Assert.Equal(vector.Text("key"), Vectors.Hex(key));
            var deviceId = DeviceId.Parse(vector.Text("deviceId"));
            Assert.Equal(keys.DeviceId, deviceId);
            var counter = vector.Number("counter");
            Assert.Equal(vector.Text("nonce"), Vectors.Hex(Frames.Nonce(deviceId, counter)));
            Assert.Equal(vector.Text("aad"), Vectors.Hex(Frames.Aad(deviceId, counter)));
            Assert.Equal(vector.Text("ciphertext"), Vectors.Hex(Frames.Seal(key, deviceId, counter, vector.Bytes("plaintext"))));
            Assert.Equal(vector.Bytes("plaintext"), Frames.Open(key, deviceId, counter, vector.Bytes("ciphertext")));
        }
    }

    [Fact]
    public void ReplayWindow()
    {
        var replay = Vectors.Root.GetProperty("replay");
        Assert.Equal(CryptoSpec.ReplayWindow, replay.GetProperty("window").GetInt32());
        var window = new ReplayWindow();
        var steps = replay.GetProperty("steps").EnumerateArray().ToList();
        Assert.Contains(steps, step => !step.GetProperty("accepted").GetBoolean());
        foreach (var step in steps)
        {
            var counter = step.Number("counter");
            var accepted = window.WouldAccept(counter);
            if (accepted)
            {
                window.Accept(counter);
            }

            Assert.True(step.GetProperty("accepted").GetBoolean() == accepted, $"counter {counter}");
        }
    }

    [Fact]
    public void ReplayWindowStateSurvivesAnExportAfterEveryStep()
    {
        // The same sequence, but the window is stored and created again before every step (AD-17).
        var window = new ReplayWindow();
        Assert.Null(window.Highest);
        Assert.Equal(0UL, window.Seen);

        foreach (var step in Vectors.Root.GetProperty("replay").GetProperty("steps").EnumerateArray())
        {
            var restored = window.Highest is { } highest ? Crypto.ReplayWindow.FromState(highest, window.Seen) : new ReplayWindow();
            var counter = step.Number("counter");
            Assert.Equal(window.WouldAccept(counter), restored.WouldAccept(counter));
            Assert.True(step.GetProperty("accepted").GetBoolean() == restored.WouldAccept(counter), $"counter {counter}");

            if (restored.WouldAccept(counter))
            {
                restored.Accept(counter);
                Assert.Equal(1UL, restored.Seen & 1);
            }

            window = restored;
        }

        Assert.Equal(201UL, window.Highest);
    }

    [Fact]
    public void ACloneOfAReplayWindowChangesOnItsOwn()
    {
        var window = new ReplayWindow();
        window.Accept(10);
        var clone = window.Clone();
        clone.Accept(11);

        Assert.Equal(10UL, window.Highest);
        Assert.True(window.WouldAccept(11));
        Assert.False(clone.WouldAccept(11));
        Assert.True(new ReplayWindow().Clone().WouldAccept(0));
    }

    [Fact]
    public void AdvancingAReplayWindowRefusesEverythingUpToTheMargin()
    {
        var replay = Vectors.Root.GetProperty("replay");
        var window = new ReplayWindow();
        foreach (var step in replay.GetProperty("steps").EnumerateArray().Where(step => step.GetProperty("accepted").GetBoolean()))
        {
            window.Accept(step.Number("counter"));
        }

        // 137 and 150 were seen below 201; 180 was still open before the restore.
        Assert.True(window.WouldAccept(180));
        var margin = (ulong)CryptoSpec.ReplayWindow;
        window.Advance(margin);

        Assert.Equal(201 + margin, window.Highest);
        Assert.Equal(ulong.MaxValue, window.Seen);
        for (var counter = 0UL; counter <= 201 + margin; counter++)
        {
            Assert.False(window.WouldAccept(counter), $"counter {counter}");
        }

        Assert.True(window.WouldAccept(201 + margin + 1));
        window.Accept(201 + margin + 1);
        Assert.False(window.WouldAccept(201 + margin));

        // A window that accepted nothing refuses its first `margin` counters, and the mark never wraps.
        var empty = new ReplayWindow();
        empty.Advance(margin);
        Assert.Equal(margin - 1, empty.Highest);
        Assert.False(empty.WouldAccept(0));
        Assert.False(empty.WouldAccept(margin - 1));
        Assert.True(empty.WouldAccept(margin));

        var last = Crypto.ReplayWindow.FromState(ulong.MaxValue - 3, 1);
        last.Advance(margin);
        Assert.Equal(ulong.MaxValue, last.Highest);
        Assert.Throws<ArgumentOutOfRangeException>(() => last.Advance(0));
    }

    [Fact]
    public void SensorIds()
    {
        Assert.NotEmpty(Vectors.List("sensorId"));
        foreach (var vector in Vectors.List("sensorId"))
        {
            var deviceId = DeviceKeys.FromRootKey(vector.Bytes("rootKey")).DeviceId;
            var slot = vector.GetProperty("slot").GetUInt32();
            Assert.Equal(vector.Text("deviceId"), deviceId.ToString());
            Assert.Equal(vector.Text("namespace"), Crypto.SensorIds.Namespace.ToString("D"));
            Assert.Equal(vector.Text("uuidName"), Crypto.SensorIds.Name(deviceId, slot, vector.Text("quantity")));
            Assert.Equal(vector.Text("sensorId"), Crypto.SensorIds.Derive(deviceId, slot, vector.Text("quantity")).ToString("D"));
        }

        // The namespace is itself the UUIDv5 of its name in the RFC's URL namespace.
        Assert.Equal(
            Crypto.SensorIds.Namespace,
            Crypto.SensorIds.UuidV5(Guid.Parse("6ba7b811-9dad-11d1-80b4-00c04fd430c8"), "https://github.com/escendit/coldframe/sensor"));
        Assert.Equal(Guid.Empty, Crypto.SensorIds.DeviceReport);
        string[] tokens =
        [
            CryptoSpec.SensorQuantitySoilMoisture,
            CryptoSpec.SensorQuantityAirTemperature,
            CryptoSpec.SensorQuantityRelativeHumidity,
            CryptoSpec.SensorQuantityGasResistance,
        ];
        Assert.Equal(["soil_moisture", "air_temperature", "relative_humidity", "gas_resistance"], tokens);
    }

    [Fact]
    public void Enrolment()
    {
        Assert.NotEmpty(Vectors.List("enrolment"));
        foreach (var vector in Vectors.List("enrolment"))
        {
            var keys = DeviceKeys.FromRootKey(vector.Bytes("rootKey"));
            var recipientPrivateKey = vector.Bytes("recipientPrivateKey");
            var recipientPublicKey = X25519.PublicKey(recipientPrivateKey);
            Assert.Equal(vector.Text("recipientPublicKey"), Vectors.Hex(recipientPublicKey));
            Assert.Equal(vector.Text("fingerprint"), Crypto.Enrolment.Fingerprint(recipientPublicKey));
            Assert.Equal(vector.Bytes("info"), Encoding.ASCII.GetBytes(CryptoSpec.EnrolmentInfo));

            var (ephemeral, enc) = Hpke.DeriveKeyPair(vector.Bytes("ikmE"));
            Assert.Equal(vector.Text("ephemeralPrivateKey"), Vectors.Hex(ephemeral));
            Assert.Equal(vector.Text("enc"), Vectors.Hex(enc));
            var sharedSecret = Hpke.SharedSecret(X25519.SharedSecret(ephemeral, recipientPublicKey), enc, recipientPublicKey);
            Assert.Equal(vector.Text("sharedSecret"), Vectors.Hex(sharedSecret));
            var (key, baseNonce) = Hpke.KeySchedule(sharedSecret, vector.Bytes("info"));
            Assert.Equal(vector.Text("key"), Vectors.Hex(key));
            Assert.Equal(vector.Text("baseNonce"), Vectors.Hex(baseNonce));

            var sealedEnrolment = Crypto.Enrolment.Seal(recipientPublicKey, vector.Bytes("ikmE"), keys);
            Assert.Equal(vector.Text("deviceId"), sealedEnrolment.DeviceId.ToString());
            Assert.Equal(vector.Text("enc"), Vectors.Hex(sealedEnrolment.Enc));
            Assert.Equal(vector.Text("ciphertext"), Vectors.Hex(sealedEnrolment.Ciphertext));

            var opened = Crypto.Enrolment.Open(recipientPrivateKey, DeviceId.Parse(vector.Text("deviceId")), vector.Bytes("enc"), vector.Bytes("ciphertext"));
            Assert.Equal(vector.Text("deviceKey"), Vectors.Hex(opened));
        }
    }

    [Fact]
    public void SetupSessionKeysAndMessages()
    {
        Assert.NotEmpty(Vectors.List("setup"));
        foreach (var vector in Vectors.List("setup"))
        {
            var appPrivateKey = vector.Bytes("appPrivateKey");
            var hubPrivateKey = vector.Bytes("hubPrivateKey");
            var appPublicKey = X25519.PublicKey(appPrivateKey);
            var hubPublicKey = X25519.PublicKey(hubPrivateKey);
            Assert.Equal(vector.Text("appPublicKey"), Vectors.Hex(appPublicKey));
            Assert.Equal(vector.Text("hubPublicKey"), Vectors.Hex(hubPublicKey));
            Assert.Equal(vector.Text("sharedSecret"), Vectors.Hex(X25519.SharedSecret(appPrivateKey, hubPublicKey)));
            Assert.Equal([CryptoSpec.ProtocolMajor], vector.Bytes("aad"));

            var code = vector.Text("popCode");
            foreach (var keys in new[]
            {
                SetupSession.DeriveKeys(appPrivateKey, hubPublicKey, appPublicKey, hubPublicKey, code),
                SetupSession.DeriveKeys(hubPrivateKey, appPublicKey, appPublicKey, hubPublicKey, code),
            })
            {
                Assert.Equal(vector.Text("appToHubKey"), Vectors.Hex(keys.AppToDevice));
                Assert.Equal(vector.Text("hubToAppKey"), Vectors.Hex(keys.DeviceToApp));
            }

            var app = new SetupSession(SetupRole.App, appPrivateKey, hubPublicKey, code);
            var hub = new SetupSession(SetupRole.Device, hubPrivateKey, appPublicKey, vector.Text("normalizedPopCode"));
            foreach (var message in vector.GetProperty("messages").EnumerateArray())
            {
                var (sender, receiver) = message.Text("direction") == "appToHub" ? (app, hub) : (hub, app);
                Assert.Equal(message.Text("nonce"), Vectors.Hex(SetupSession.Nonce(message.Number("counter"))));
                var (counter, ciphertext) = sender.Seal(message.Bytes("plaintext"));
                Assert.Equal(message.Number("counter"), counter);
                Assert.Equal(message.Text("ciphertext"), Vectors.Hex(ciphertext));
                Assert.Equal(message.Bytes("plaintext"), receiver.Open(counter, ciphertext));
            }
        }
    }

    [Fact]
    public void HeartbeatSignature()
    {
        Assert.NotEmpty(Vectors.List("heartbeat"));
        foreach (var vector in Vectors.List("heartbeat"))
        {
            var keys = DeviceKeys.FromRootKey(vector.Bytes("rootKey"));
            Assert.Equal(vector.Text("hubAuthKey"), Vectors.Hex(keys.HubAuthKey.ToArray()));
            var method = vector.Text("method");
            var path = vector.Text("path");
            var body = vector.Bytes("body");
            var timestamp = (long)vector.Number("timestampMs");
            var nonce = vector.Bytes("nonce");
            var canonical = Heartbeat.Canonical(method, path, body, timestamp, nonce);
            Assert.Equal(vector.Text("canonical"), canonical);
            Assert.Contains(vector.Text("bodyHash"), canonical, StringComparison.Ordinal);
            Assert.Equal(vector.Text("signature"), Vectors.Hex(Heartbeat.Sign(keys.HubAuthKey, method, path, body, timestamp, nonce)));

            var headers = Heartbeat.Headers(keys, method, path, body, timestamp, nonce);
            var expected = vector.GetProperty("headers").EnumerateObject().ToDictionary(header => header.Name, header => header.Value.GetString());
            Assert.Equal(4, expected.Count);
            Assert.Equal(expected[CryptoSpec.HeartbeatDeviceHeader], headers[CryptoSpec.HeartbeatDeviceHeader]);
            Assert.Equal(expected[CryptoSpec.HeartbeatTimestampHeader], headers[CryptoSpec.HeartbeatTimestampHeader]);
            Assert.Equal(expected[CryptoSpec.HeartbeatNonceHeader], headers[CryptoSpec.HeartbeatNonceHeader]);
            Assert.Equal(expected[CryptoSpec.HeartbeatSignatureHeader], headers[CryptoSpec.HeartbeatSignatureHeader]);
        }
    }
}
