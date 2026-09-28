namespace Coldframe.Crypto.Tests;

/// <summary>
/// What must fail: a wrong PoP code, tampering, a replayed counter, the wrong enrolment key.
/// </summary>
public sealed class NegativeTests
{
    private static (SetupSession App, SetupSession Hub) Sessions(string appCode, string hubCode)
    {
        var vector = Vectors.First("setup");
        var appPrivateKey = vector.Bytes("appPrivateKey");
        var hubPrivateKey = vector.Bytes("hubPrivateKey");
        return (
            new SetupSession(SetupRole.App, appPrivateKey, X25519.PublicKey(hubPrivateKey), appCode),
            new SetupSession(SetupRole.Device, hubPrivateKey, X25519.PublicKey(appPrivateKey), hubCode));
    }

    private static CryptoFailure FailureOf(Action action) => Assert.Throws<CryptoFailureException>(action).Failure;

    [Fact]
    public void AWrongSetupCodeFailsTheFirstMessageWithADistinctError()
    {
        var vector = Vectors.First("setup");
        var (app, hub) = Sessions(vector.Text("popCode"), vector.Text("wrongPopCode"));
        var (counter, ciphertext) = app.Seal("identity please"u8);
        Assert.Equal(CryptoFailure.WrongSetupCode, FailureOf(() => hub.Open(counter, ciphertext)));
    }

    [Fact]
    public void ATamperedSetupMessageAfterTheFirstIsAnAuthenticationFailure()
    {
        var vector = Vectors.First("setup");
        var (app, hub) = Sessions(vector.Text("popCode"), vector.Text("normalizedPopCode"));
        var (first, firstCiphertext) = app.Seal("one"u8);
        hub.Open(first, firstCiphertext);
        var (second, secondCiphertext) = app.Seal("two"u8);
        secondCiphertext[0] ^= 1;
        Assert.Equal(CryptoFailure.AuthenticationFailed, FailureOf(() => hub.Open(second, secondCiphertext)));
    }

    [Fact]
    public void AReplayedSetupCounterIsRefused()
    {
        var vector = Vectors.First("setup");
        var (app, hub) = Sessions(vector.Text("popCode"), vector.Text("popCode"));
        var (counter, ciphertext) = app.Seal("one"u8);
        hub.Open(counter, ciphertext);
        Assert.Equal(CryptoFailure.Replay, FailureOf(() => hub.Open(counter, ciphertext)));
    }

    [Fact]
    public void AnInvalidSetupCodeIsRefused()
    {
        var vector = Vectors.First("setup");
        Assert.Equal(CryptoFailure.InvalidSetupCode, FailureOf(() => Sessions(vector.Text("popCode"), string.Empty)));
        Assert.Equal(CryptoFailure.InvalidSetupCode, FailureOf(() => Sessions(vector.Text("popCode"), "Ä1B2C3")));
        Assert.Equal(CryptoFailure.InvalidSetupCode, FailureOf(() => Sessions(vector.Text("popCode"), new string('A', CryptoSpec.SetupMaxCodeLength + 1))));
    }

    [Fact]
    public void ACounterThatCannotAdvanceIsAReplayBeforeDecrypting()
    {
        var vector = Vectors.First("setup");
        var (_, hub) = Sessions(vector.Text("popCode"), vector.Text("popCode"));
        Assert.Equal(CryptoFailure.Replay, FailureOf(() => hub.Open(ulong.MaxValue, new byte[32])));
    }

    [Theory]
    [InlineData("92064422C012F481")]
    [InlineData("92064422c012f48")]
    [InlineData("92064422c012f48g")]
    [InlineData("92064422c012f4810")]
    public void AMalformedDeviceIdIsRefused(string text) =>
        Assert.Throws<CryptoFailureException>(() => DeviceId.Parse(text));

    [Fact]
    public void ANegativeHeartbeatTimestampIsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Heartbeat.Canonical("POST", "/device/heartbeat", [], -1, new byte[CryptoSpec.HeartbeatNonceLength]));

    [Fact]
    public void ATamperedFrameOrChangedAadFails()
    {
        var vector = Vectors.First("frames");
        var key = vector.Bytes("key");
        var deviceId = DeviceId.Parse(vector.Text("deviceId"));
        var counter = vector.Number("counter");
        var sealedBytes = vector.Bytes("ciphertext");

        var flipped = sealedBytes.ToArray();
        flipped[0] ^= 0x80;
        Assert.Equal(CryptoFailure.AuthenticationFailed, FailureOf(() => Frames.Open(key, deviceId, counter, flipped)));
        var flippedTag = sealedBytes.ToArray();
        flippedTag[^1] ^= 1;
        Assert.Equal(CryptoFailure.AuthenticationFailed, FailureOf(() => Frames.Open(key, deviceId, counter, flippedTag)));
        Assert.Equal(CryptoFailure.AuthenticationFailed, FailureOf(() => Frames.Open(key, deviceId, counter + 1, sealedBytes)));
        Assert.Equal(CryptoFailure.AuthenticationFailed, FailureOf(() => Frames.Open(key, new DeviceId(deviceId.Value ^ 1), counter, sealedBytes)));
        Assert.Equal(CryptoFailure.AuthenticationFailed, FailureOf(() => Frames.Open(key, deviceId, counter, sealedBytes.AsSpan(0, 15))));
    }

    [Fact]
    public void AReplayedFrameIsRefusedAndAForgedOneDoesNotMoveTheWindow()
    {
        var vector = Vectors.First("frames");
        var key = vector.Bytes("key");
        var deviceId = DeviceId.Parse(vector.Text("deviceId"));
        var counter = vector.Number("counter");
        var sealedBytes = vector.Bytes("ciphertext");
        var window = new ReplayWindow();

        var forged = sealedBytes.ToArray();
        forged[1] ^= 1;
        Assert.Equal(CryptoFailure.AuthenticationFailed, FailureOf(() => Frames.Open(key, deviceId, counter, forged, window)));
        Assert.Null(window.Highest);
        Frames.Open(key, deviceId, counter, sealedBytes, window);
        Assert.Equal(CryptoFailure.Replay, FailureOf(() => Frames.Open(key, deviceId, counter, sealedBytes, window)));
    }

    [Fact]
    public void EnrolmentOpensOnlyWithTheRightKeyAndDeviceId()
    {
        var vector = Vectors.First("enrolment");
        var recipientPrivateKey = vector.Bytes("recipientPrivateKey");
        var deviceId = DeviceId.Parse(vector.Text("deviceId"));
        var enc = vector.Bytes("enc");
        var ciphertext = vector.Bytes("ciphertext");

        var wrongKey = recipientPrivateKey.ToArray();
        wrongKey[0] ^= 0x40;
        Assert.Equal(CryptoFailure.AuthenticationFailed, FailureOf(() => Enrolment.Open(wrongKey, deviceId, enc, ciphertext)));
        Assert.Equal(CryptoFailure.AuthenticationFailed, FailureOf(() => Enrolment.Open(recipientPrivateKey, new DeviceId(deviceId.Value ^ 1), enc, ciphertext)));
    }

    [Fact]
    public void ASmallOrderPublicKeyIsRefused()
    {
        var keys = DeviceKeys.FromRootKey(Vectors.First("enrolment").Bytes("rootKey"));
        Assert.Equal(CryptoFailure.InvalidPublicKey, FailureOf(() => Enrolment.Seal(new byte[32], new byte[32], keys)));
    }

    [Fact]
    public void AChangedBodyOrPathBreaksTheHeartbeatSignature()
    {
        var vector = Vectors.First("heartbeat");
        var key = vector.Bytes("hubAuthKey");
        var method = vector.Text("method");
        var path = vector.Text("path");
        var body = vector.Bytes("body");
        var timestamp = (long)vector.Number("timestampMs");
        var nonce = vector.Bytes("nonce");
        var signature = vector.Bytes("signature");

        Assert.True(Heartbeat.Verify(key, method, path, body, timestamp, nonce, signature));
        var changedBody = body.ToArray();
        changedBody[0] ^= 1;
        Assert.False(Heartbeat.Verify(key, method, path, changedBody, timestamp, nonce, signature));
        Assert.False(Heartbeat.Verify(key, method, "/device/ingest", body, timestamp, nonce, signature));
        Assert.False(Heartbeat.Verify(key, method, path, body, timestamp + 1, nonce, signature));
    }

    [Fact]
    public void KeysNeverAppearInText()
    {
        var keys = DeviceKeys.FromRootKey(Vectors.First("keyHierarchy").Bytes("rootKey"));
        var text = keys.ToString();
        Assert.DoesNotContain(Vectors.Hex(keys.DeviceKey.ToArray()), text, StringComparison.Ordinal);
        Assert.DoesNotContain(Vectors.Hex(keys.HubAuthKey.ToArray()), text, StringComparison.Ordinal);
    }
}
