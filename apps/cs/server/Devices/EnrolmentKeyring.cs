using System.Security.Cryptography;
using Coldframe.Crypto;
using Microsoft.Extensions.Options;

namespace Coldframe.Server.Devices;

/// <summary>
/// The Server's X25519 enrolment key (AD-12): the public key and its fingerprint for <c>GET /enrolment-key</c>,
/// and <see cref="Open"/> for a Device's sealed <c>K_dev</c>. The private key never leaves this class.
/// </summary>
public sealed class EnrolmentKeyring
{
    /// <summary>
    /// The PEM label of a PKCS#8 private key.
    /// </summary>
    public const string PemLabel = "PRIVATE KEY";

    // PKCS#8 PrivateKeyInfo of an X25519 key (RFC 8410): version 0, algorithm id-X25519 (1.3.101.110),
    // then the 32-byte key as an OCTET STRING inside the privateKey OCTET STRING.
    private static readonly byte[] Pkcs8Prefix = Convert.FromHexString("302e020100300506032b656e04220420");

    private readonly byte[] _privateKey;

    /// <summary>
    /// Creates the keyring from the configured PEM.
    /// </summary>
    public EnrolmentKeyring(IOptions<EnrolmentOptions> options)
        : this(ParsePrivateKey((options ?? throw new ArgumentNullException(nameof(options))).Value.PrivateKeyPem))
    {
    }

    private EnrolmentKeyring(byte[] privateKey)
    {
        _privateKey = privateKey;
        var publicKey = global::Coldframe.Crypto.X25519.PublicKey(privateKey);
        PublicKey = publicKey;
        Fingerprint = Enrolment.Fingerprint(publicKey);
    }

    /// <summary>
    /// The raw 32-byte X25519 enrolment public key.
    /// </summary>
    public ReadOnlyMemory<byte> PublicKey { get; }

    /// <summary>
    /// Lowercase hex SHA-256 of <see cref="PublicKey"/>.
    /// </summary>
    public string Fingerprint { get; }

    /// <summary>
    /// Creates a keyring from a raw private key (tests).
    /// </summary>
    public static EnrolmentKeyring FromPrivateKey(ReadOnlySpan<byte> privateKey)
    {
        if (privateKey.Length != CryptoSpec.X25519KeyLength)
        {
            throw new ArgumentException("An X25519 private key has 32 bytes.", nameof(privateKey));
        }

        return new EnrolmentKeyring(privateKey.ToArray());
    }

    /// <summary>
    /// Whether <paramref name="pem"/> is a PKCS#8 X25519 private key as <see cref="ParsePrivateKey"/> accepts it.
    /// </summary>
    public static bool IsValidPrivateKeyPem(string? pem)
    {
        try
        {
            CryptographicOperations.ZeroMemory(ParsePrivateKey(pem));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads the raw 32-byte key of a PKCS#8 X25519 private key in PEM (label <c>PRIVATE KEY</c>). Anything
    /// else throws <see cref="FormatException"/>, whose message never contains key material.
    /// </summary>
    public static byte[] ParsePrivateKey(string? pem)
    {
        if (string.IsNullOrWhiteSpace(pem) || !PemEncoding.TryFind(pem, out var fields))
        {
            throw new FormatException("The enrolment key is not PEM.");
        }

        if (!pem.AsSpan()[fields.Label].SequenceEqual(PemLabel))
        {
            throw new FormatException($"The enrolment key is not a PKCS#8 '{PemLabel}'.");
        }

        var der = new byte[fields.DecodedDataLength];

        try
        {
            if (!Convert.TryFromBase64Chars(pem.AsSpan()[fields.Base64Data], der, out var written)
                || written != Pkcs8Prefix.Length + CryptoSpec.X25519KeyLength
                || !der.AsSpan(0, Pkcs8Prefix.Length).SequenceEqual(Pkcs8Prefix))
            {
                throw new FormatException("The enrolment key is not an X25519 private key.");
            }

            return der[Pkcs8Prefix.Length..];
        }
        finally
        {
            CryptographicOperations.ZeroMemory(der);
        }
    }

    /// <summary>
    /// Writes a raw X25519 private key as PKCS#8 PEM, as <c>openssl genpkey -algorithm X25519</c> does.
    /// </summary>
    public static string ToPrivateKeyPem(ReadOnlySpan<byte> privateKey)
    {
        if (privateKey.Length != CryptoSpec.X25519KeyLength)
        {
            throw new ArgumentException("An X25519 private key has 32 bytes.", nameof(privateKey));
        }

        var der = new byte[Pkcs8Prefix.Length + privateKey.Length];

        try
        {
            Pkcs8Prefix.CopyTo(der, 0);
            privateKey.CopyTo(der.AsSpan(Pkcs8Prefix.Length));
            return new string(PemEncoding.Write(PemLabel, der));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(der);
        }
    }

    /// <summary>
    /// Opens a Device's sealed enrolment and returns <c>K_dev</c>; the caller zeroes it after use. Throws
    /// <see cref="CryptoFailureException"/> when it does not open.
    /// </summary>
    /// <param name="deviceId">The claimed Device ID, the HPKE associated data.</param>
    /// <param name="enc">The HPKE encapsulated key.</param>
    /// <param name="ciphertext">The sealed <c>K_dev</c>.</param>
    public byte[] Open(DeviceId deviceId, ReadOnlySpan<byte> enc, ReadOnlySpan<byte> ciphertext) =>
        Enrolment.Open(_privateKey, deviceId, enc, ciphertext);

    /// <summary>
    /// Names the key by its fingerprint only.
    /// </summary>
    public override string ToString() => $"EnrolmentKeyring({Fingerprint})";
}
