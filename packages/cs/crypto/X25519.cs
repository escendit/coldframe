using System.Security.Cryptography;

namespace Coldframe.Crypto;

/// <summary>
/// X25519 (RFC 7748) on raw 32-byte keys, backed by BouncyCastle because .NET has none.
/// </summary>
public static class X25519
{
    /// <summary>
    /// A fresh random private key.
    /// </summary>
    public static byte[] GeneratePrivateKey() => RandomNumberGenerator.GetBytes(CryptoSpec.X25519KeyLength);

    /// <summary>
    /// The public key of a raw private key.
    /// </summary>
    public static byte[] PublicKey(ReadOnlySpan<byte> privateKey)
    {
        CheckLength(privateKey);
        var publicKey = new byte[CryptoSpec.X25519KeyLength];
        Org.BouncyCastle.Math.EC.Rfc7748.X25519.ScalarMultBase(privateKey.ToArray(), 0, publicKey, 0);
        return publicKey;
    }

    /// <summary>
    /// The shared secret; throws <see cref="CryptoFailure.InvalidPublicKey"/> for the all-zero output.
    /// </summary>
    public static byte[] SharedSecret(ReadOnlySpan<byte> privateKey, ReadOnlySpan<byte> publicKey)
    {
        CheckLength(privateKey);
        CheckLength(publicKey);
        var shared = new byte[CryptoSpec.X25519KeyLength];
        if (!Org.BouncyCastle.Math.EC.Rfc7748.X25519.CalculateAgreement(privateKey.ToArray(), 0, publicKey.ToArray(), 0, shared, 0))
        {
            throw new CryptoFailureException(CryptoFailure.InvalidPublicKey);
        }

        return shared;
    }

    private static void CheckLength(ReadOnlySpan<byte> key)
    {
        if (key.Length != CryptoSpec.X25519KeyLength)
        {
            throw new CryptoFailureException(CryptoFailure.InvalidLength);
        }
    }
}
