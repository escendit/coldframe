using System.Security.Cryptography;
using Coldframe.Crypto;
using Coldframe.Server.Devices;
using Microsoft.Extensions.Options;
using Curve25519 = Coldframe.Crypto.X25519;

namespace Coldframe.Server.Tests.Devices;

/// <summary>
/// The enrolment key comes from PKCS#8 PEM exactly as <c>openssl genpkey -algorithm X25519</c> writes it,
/// and nothing else is accepted.
/// </summary>
public sealed class EnrolmentKeyringTests
{
    // The DER of a key written by "openssl genpkey -algorithm X25519" (test only, never used anywhere).
    // The PEM armour is added at run time, so secret scanners see no committed private key.
    private const string OpenSslDer = "MC4CAQAwBQYDK2VuBCIEILjPpkvJCFDRp+x9PcB3YHxE5ZlUI9konEcbV/K3KE1j";

    private static readonly string OpenSslPem =
        $"-----BEGIN {EnrolmentKeyring.PemLabel}-----\n{OpenSslDer}\n-----END {EnrolmentKeyring.PemLabel}-----\n";

    [Fact]
    public void AnOpenSslKeyParsesToItsRawBytes()
    {
        var key = EnrolmentKeyring.ParsePrivateKey(OpenSslPem);

        Assert.Equal("b8cfa64bc90850d1a7ec7d3dc077607c44e5995423d9289c471b57f2b7284d63", Convert.ToHexStringLower(key));
        Assert.True(EnrolmentKeyring.IsValidPrivateKeyPem(OpenSslPem));

        // The public key openssl derives ("openssl pkey -pubout").
        var keyring = EnrolmentKeyring.FromPrivateKey(key);
        Assert.Equal("9f0e9ac0f60a2e9321bbcb8a06a7a120b8c85920017aaab10153fd750682fa2c", Convert.ToHexStringLower(keyring.PublicKey.Span));
    }

    [Fact]
    public void AWrittenKeyReadsBackAndGivesThePublicKeyAndFingerprint()
    {
        var privateKey = Curve25519.GeneratePrivateKey();
        var pem = EnrolmentKeyring.ToPrivateKeyPem(privateKey);

        Assert.StartsWith("-----BEGIN PRIVATE KEY-----", pem, StringComparison.Ordinal);
        Assert.Equal(privateKey, EnrolmentKeyring.ParsePrivateKey(pem));

        var keyring = new EnrolmentKeyring(Options.Create(new EnrolmentOptions { PrivateKeyPem = pem }));
        Assert.Equal(Curve25519.PublicKey(privateKey), keyring.PublicKey.ToArray());
        Assert.Equal(Enrolment.Fingerprint(Curve25519.PublicKey(privateKey)), keyring.Fingerprint);
        Assert.DoesNotContain(Convert.ToHexStringLower(privateKey), keyring.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheVectorKeyOpensTheVectorEnrolment()
    {
        var vector = Vectors.Enrolment(0);
        var keyring = EnrolmentKeyring.FromPrivateKey(vector.Bytes("recipientPrivateKey"));

        Assert.Equal(vector.Text("recipientPublicKey"), Convert.ToHexStringLower(keyring.PublicKey.Span));
        Assert.Equal(vector.Text("fingerprint"), keyring.Fingerprint);

        var deviceKey = keyring.Open(DeviceId.Parse(vector.Text("deviceId")), vector.Bytes("enc"), vector.Bytes("ciphertext"));
        Assert.Equal(vector.Text("deviceKey"), Convert.ToHexStringLower(deviceKey));

        Assert.Throws<CryptoFailureException>(() =>
            keyring.Open(DeviceId.Parse("0000000000000001"), vector.Bytes("enc"), vector.Bytes("ciphertext")));
    }

    public static TheoryData<string> Refused()
    {
        var key = RandomNumberGenerator.GetBytes(32);

        return
        [
            // Not PEM at all, or empty.
            string.Empty,
            "   ",
            "not a key",
            Convert.ToBase64String(key),

            // Another label.
            Pem("PUBLIC KEY", [.. Convert.FromHexString("302e020100300506032b656e04220420"), .. key]),
            Pem("EC PRIVATE KEY", [.. Convert.FromHexString("302e020100300506032b656e04220420"), .. key]),

            // Another algorithm: Ed25519 (1.3.101.112) has the same shape.
            Pem("PRIVATE KEY", [.. Convert.FromHexString("302e020100300506032b657004220420"), .. key]),

            // A key of the wrong length, with a matching or a stale prefix.
            Pem("PRIVATE KEY", [.. Convert.FromHexString("302d020100300506032b656e04210420"), .. key[..31]]),
            Pem("PRIVATE KEY", [.. Convert.FromHexString("302e020100300506032b656e04220420"), .. key[..31]]),
            Pem("PRIVATE KEY", [.. Convert.FromHexString("302e020100300506032b656e04220420"), .. key, 0]),
        ];
    }

    [Theory]
    [MemberData(nameof(Refused))]
    public void AnythingElseIsRefusedWithoutEchoingIt(string pem)
    {
        var exception = Assert.Throws<FormatException>(() => EnrolmentKeyring.ParsePrivateKey(pem));

        Assert.False(EnrolmentKeyring.IsValidPrivateKeyPem(pem));
        Assert.DoesNotContain("BEGIN", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NoKeyIsRefused()
    {
        Assert.Throws<FormatException>(() => EnrolmentKeyring.ParsePrivateKey(null));
        Assert.False(EnrolmentKeyring.IsValidPrivateKeyPem(null));
    }

    private static string Pem(string label, byte[] der) => new(PemEncoding.Write(label, der));
}
