using System.Security.Cryptography;

namespace Coldframe.Crypto.Tests;

/// <summary>
/// The primitives reproduce the RFC vectors in <c>vectors.json</c> (copied verbatim from RFC 5869, RFC 7748,
/// RFC 8439 and RFC 9180).
/// </summary>
public sealed class AnchorTests
{
    [Fact]
    public void Rfc5869HkdfSha256TestCase1()
    {
        var anchor = Vectors.Anchor("rfc5869TestCase1");
        var prk = HKDF.Extract(HashAlgorithmName.SHA256, anchor.Bytes("ikm"), anchor.Bytes("salt"));
        Assert.Equal(anchor.Text("prk"), Vectors.Hex(prk));
        var okm = HKDF.Expand(HashAlgorithmName.SHA256, prk, anchor.GetProperty("length").GetInt32(), anchor.Bytes("info"));
        Assert.Equal(anchor.Text("okm"), Vectors.Hex(okm));
    }

    [Fact]
    public void Rfc7748X25519()
    {
        var anchor = Vectors.Anchor("rfc7748Section61");
        Assert.Equal(anchor.Text("alicePublic"), Vectors.Hex(X25519.PublicKey(anchor.Bytes("alicePrivate"))));
        Assert.Equal(anchor.Text("bobPublic"), Vectors.Hex(X25519.PublicKey(anchor.Bytes("bobPrivate"))));
        Assert.Equal(anchor.Text("sharedSecret"), Vectors.Hex(X25519.SharedSecret(anchor.Bytes("alicePrivate"), anchor.Bytes("bobPublic"))));
        Assert.Equal(anchor.Text("sharedSecret"), Vectors.Hex(X25519.SharedSecret(anchor.Bytes("bobPrivate"), anchor.Bytes("alicePublic"))));
    }

    [Fact]
    public void Rfc8439ChaCha20Poly1305()
    {
        var anchor = Vectors.Anchor("rfc8439Section282");
        var sealedBytes = Aead.Seal(anchor.Bytes("key"), anchor.Bytes("nonce"), anchor.Bytes("aad"), anchor.Bytes("plaintext"));
        Assert.Equal(anchor.Text("ciphertext") + anchor.Text("tag"), Vectors.Hex(sealedBytes));
        Assert.Equal(anchor.Bytes("plaintext"), Aead.Open(anchor.Bytes("key"), anchor.Bytes("nonce"), anchor.Bytes("aad"), sealedBytes));
    }

    [Fact]
    public void Rfc9180A21BaseModeFirstEncryption()
    {
        var anchor = Vectors.Anchor("rfc9180A21");
        Assert.Equal(CryptoSpec.HpkeKemId, anchor.GetProperty("kemId").GetUInt16());
        Assert.Equal(CryptoSpec.HpkeKdfId, anchor.GetProperty("kdfId").GetUInt16());
        Assert.Equal(CryptoSpec.HpkeAeadId, anchor.GetProperty("aeadId").GetUInt16());
        var (skE, pkE) = Hpke.DeriveKeyPair(anchor.Bytes("ikmE"));
        Assert.Equal(anchor.Text("skEm"), Vectors.Hex(skE));
        Assert.Equal(anchor.Text("pkEm"), Vectors.Hex(pkE));

        var sealedBytes = Hpke.SealBase(anchor.Bytes("pkRm"), anchor.Bytes("ikmE"), anchor.Bytes("info"), anchor.Bytes("aad"), anchor.Bytes("pt"));
        Assert.Equal(anchor.Text("enc"), Vectors.Hex(sealedBytes.Enc));
        Assert.Equal(anchor.Text("ct"), Vectors.Hex(sealedBytes.Ciphertext));

        var sharedSecret = Hpke.SharedSecret(X25519.SharedSecret(skE, anchor.Bytes("pkRm")), pkE, anchor.Bytes("pkRm"));
        Assert.Equal(anchor.Text("sharedSecret"), Vectors.Hex(sharedSecret));
        var (key, baseNonce) = Hpke.KeySchedule(sharedSecret, anchor.Bytes("info"));
        Assert.Equal(anchor.Text("key"), Vectors.Hex(key));
        Assert.Equal(anchor.Text("baseNonce"), Vectors.Hex(baseNonce));

        Assert.Equal(anchor.Bytes("pt"), Hpke.OpenBase(anchor.Bytes("skRm"), sealedBytes.Enc, anchor.Bytes("info"), anchor.Bytes("aad"), sealedBytes.Ciphertext));
    }
}
