using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Coldframe.Crypto;

/// <summary>
/// The result of a base-mode <see cref="Hpke.SealBase"/>.
/// </summary>
/// <param name="Enc">The encapsulated key, 32 bytes.</param>
/// <param name="Ciphertext">The ciphertext followed by the tag.</param>
public sealed record HpkeSealed(byte[] Enc, byte[] Ciphertext);

/// <summary>
/// HPKE (RFC 9180) base mode with DHKEM(X25519, HKDF-SHA256), HKDF-SHA256 and ChaCha20Poly1305, composed
/// from the standard primitives and checked against RFC 9180 Appendix A.2.1. Single-shot: sequence 0.
/// </summary>
public static class Hpke
{
    private const int HashLength = 32;

    private static readonly byte[] KemSuite = Suite("KEM", CryptoSpec.HpkeKemId);

    private static readonly byte[] HpkeSuite = Suite("HPKE", CryptoSpec.HpkeKemId, CryptoSpec.HpkeKdfId, CryptoSpec.HpkeAeadId);

    /// <summary>
    /// <c>DeriveKeyPair(ikm)</c> for DHKEM(X25519): the private and the public key.
    /// </summary>
    public static (byte[] PrivateKey, byte[] PublicKey) DeriveKeyPair(ReadOnlySpan<byte> ikm)
    {
        var dkpPrk = LabeledExtract(KemSuite, [], CryptoSpec.HpkeLabelDkpPrk, ikm);
        var privateKey = LabeledExpand(KemSuite, dkpPrk, CryptoSpec.HpkeLabelSk, [], CryptoSpec.X25519KeyLength);
        return (privateKey, X25519.PublicKey(privateKey));
    }

    /// <summary>
    /// Seals to <paramref name="recipientPublicKey"/> with an ephemeral key derived from <paramref name="ikmE"/>.
    /// </summary>
    public static HpkeSealed SealBase(
        ReadOnlySpan<byte> recipientPublicKey,
        ReadOnlySpan<byte> ikmE,
        ReadOnlySpan<byte> info,
        ReadOnlySpan<byte> aad,
        ReadOnlySpan<byte> plaintext)
    {
        var (ephemeral, enc) = DeriveKeyPair(ikmE);
        var dh = X25519.SharedSecret(ephemeral, recipientPublicKey);
        var (key, baseNonce) = KeySchedule(SharedSecret(dh, enc, recipientPublicKey), info);
        return new HpkeSealed(enc, Aead.Seal(key, baseNonce, aad, plaintext));
    }

    /// <summary>
    /// Opens a base-mode ciphertext with the recipient's private key.
    /// </summary>
    public static byte[] OpenBase(
        ReadOnlySpan<byte> recipientPrivateKey,
        ReadOnlySpan<byte> enc,
        ReadOnlySpan<byte> info,
        ReadOnlySpan<byte> aad,
        ReadOnlySpan<byte> ciphertext)
    {
        var dh = X25519.SharedSecret(recipientPrivateKey, enc);
        var recipientPublicKey = X25519.PublicKey(recipientPrivateKey);
        var (key, baseNonce) = KeySchedule(SharedSecret(dh, enc, recipientPublicKey), info);
        return Aead.Open(key, baseNonce, aad, ciphertext);
    }

    /// <summary>
    /// <c>ExtractAndExpand(dh, enc ‖ pkR)</c>, exposed for the vectors.
    /// </summary>
    public static byte[] SharedSecret(ReadOnlySpan<byte> dh, ReadOnlySpan<byte> enc, ReadOnlySpan<byte> recipientPublicKey)
    {
        var eaePrk = LabeledExtract(KemSuite, [], CryptoSpec.HpkeLabelEaePrk, dh);
        return LabeledExpand(KemSuite, eaePrk, CryptoSpec.HpkeLabelSharedSecret, [.. enc, .. recipientPublicKey], HashLength);
    }

    /// <summary>
    /// The base-mode key schedule: the AEAD key and base nonce, exposed for the vectors.
    /// </summary>
    public static (byte[] Key, byte[] BaseNonce) KeySchedule(ReadOnlySpan<byte> sharedSecret, ReadOnlySpan<byte> info)
    {
        var pskIdHash = LabeledExtract(HpkeSuite, [], CryptoSpec.HpkeLabelPskIdHash, []);
        var infoHash = LabeledExtract(HpkeSuite, [], CryptoSpec.HpkeLabelInfoHash, info);
        byte[] context = [CryptoSpec.HpkeMode, .. pskIdHash, .. infoHash];
        var secret = LabeledExtract(HpkeSuite, sharedSecret, CryptoSpec.HpkeLabelSecret, []);
        return (
            LabeledExpand(HpkeSuite, secret, CryptoSpec.HpkeLabelKey, context, CryptoSpec.AeadKeyLength),
            LabeledExpand(HpkeSuite, secret, CryptoSpec.HpkeLabelBaseNonce, context, CryptoSpec.AeadNonceLength));
    }

    private static byte[] Suite(string prefix, params ushort[] ids)
    {
        var suite = new byte[prefix.Length + (2 * ids.Length)];
        Encoding.ASCII.GetBytes(prefix, suite);
        for (var index = 0; index < ids.Length; index++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(suite.AsSpan(prefix.Length + (2 * index)), ids[index]);
        }

        return suite;
    }

    private static byte[] LabeledExtract(byte[] suite, ReadOnlySpan<byte> salt, string label, ReadOnlySpan<byte> ikm)
    {
        byte[] labeledIkm = [.. Encoding.ASCII.GetBytes(CryptoSpec.HpkeVersionLabel), .. suite, .. Encoding.ASCII.GetBytes(label), .. ikm];
        var prk = new byte[HashLength];
        HKDF.Extract(HashAlgorithmName.SHA256, labeledIkm, salt, prk);
        return prk;
    }

    private static byte[] LabeledExpand(byte[] suite, ReadOnlySpan<byte> prk, string label, ReadOnlySpan<byte> info, int length)
    {
        var lengthBytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(lengthBytes, checked((ushort)length));
        byte[] labeledInfo = [.. lengthBytes, .. Encoding.ASCII.GetBytes(CryptoSpec.HpkeVersionLabel), .. suite, .. Encoding.ASCII.GetBytes(label), .. info];
        var output = new byte[length];
        HKDF.Expand(HashAlgorithmName.SHA256, prk, output, labeledInfo);
        return output;
    }
}

/// <summary>
/// A Device's sealed enrolment: <c>K_dev</c> sealed with HPKE to the Server's enrolment key (AD-12).
/// </summary>
/// <param name="DeviceId">The Device ID, also the HPKE associated data.</param>
/// <param name="Enc">The HPKE encapsulated key, 32 bytes.</param>
/// <param name="Ciphertext">The sealed <c>K_dev</c>, 48 bytes.</param>
public sealed record SealedEnrolment(DeviceId DeviceId, byte[] Enc, byte[] Ciphertext);

/// <summary>
/// Enrolment sealing: <c>info = "coldframe/enrolment/v1"</c>, <c>aad = device_id</c>, <c>pt = K_dev</c>.
/// </summary>
public static class Enrolment
{
    private static readonly byte[] Info = Encoding.ASCII.GetBytes(CryptoSpec.EnrolmentInfo);

    /// <summary>
    /// Seals <c>K_dev</c> to <paramref name="serverPublicKey"/>. <paramref name="ikmE"/> is 32 fresh random bytes;
    /// only vectors pass fixed ones.
    /// </summary>
    public static SealedEnrolment Seal(ReadOnlySpan<byte> serverPublicKey, ReadOnlySpan<byte> ikmE, DeviceKeys keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var sealedKey = Hpke.SealBase(serverPublicKey, ikmE, Info, keys.DeviceId.ToBytes(), keys.DeviceKey);
        return new SealedEnrolment(keys.DeviceId, sealedKey.Enc, sealedKey.Ciphertext);
    }

    /// <summary>
    /// Opens a sealed enrolment with the Server's private key and returns <c>K_dev</c>.
    /// </summary>
    public static byte[] Open(ReadOnlySpan<byte> serverPrivateKey, DeviceId deviceId, ReadOnlySpan<byte> enc, ReadOnlySpan<byte> ciphertext)
    {
        var deviceKey = Hpke.OpenBase(serverPrivateKey, enc, Info, deviceId.ToBytes(), ciphertext);
        if (deviceKey.Length != CryptoSpec.DeviceKeyLength)
        {
            throw new CryptoFailureException(CryptoFailure.InvalidLength);
        }

        return deviceKey;
    }

    /// <summary>
    /// The fingerprint of an enrolment public key: lowercase hex SHA-256 of its 32 raw bytes.
    /// </summary>
    public static string Fingerprint(ReadOnlySpan<byte> publicKey) => Convert.ToHexStringLower(SHA256.HashData(publicKey));
}
