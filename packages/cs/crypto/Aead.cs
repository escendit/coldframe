using System.Security.Cryptography;

namespace Coldframe.Crypto;

/// <summary>
/// ChaCha20-Poly1305 (RFC 8439). A sealed message is the ciphertext followed by the 16-byte tag.
/// </summary>
public static class Aead
{
    /// <summary>
    /// Seals <paramref name="plaintext"/>.
    /// </summary>
    public static byte[] Seal(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> aad, ReadOnlySpan<byte> plaintext)
    {
        CheckSizes(key, nonce);
        var sealedBytes = new byte[plaintext.Length + CryptoSpec.AeadTagLength];
        using var cipher = new ChaCha20Poly1305(key);
        cipher.Encrypt(nonce, plaintext, sealedBytes.AsSpan(0, plaintext.Length), sealedBytes.AsSpan(plaintext.Length), aad);
        return sealedBytes;
    }

    /// <summary>
    /// Opens <paramref name="sealedBytes"/>, or throws <see cref="CryptoFailure.AuthenticationFailed"/>.
    /// </summary>
    public static byte[] Open(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> aad, ReadOnlySpan<byte> sealedBytes)
    {
        CheckSizes(key, nonce);
        if (sealedBytes.Length < CryptoSpec.AeadTagLength)
        {
            throw new CryptoFailureException(CryptoFailure.AuthenticationFailed);
        }

        var length = sealedBytes.Length - CryptoSpec.AeadTagLength;
        var plaintext = new byte[length];
        using var cipher = new ChaCha20Poly1305(key);
        try
        {
            cipher.Decrypt(nonce, sealedBytes[..length], sealedBytes[length..], plaintext, aad);
        }
        catch (AuthenticationTagMismatchException exception)
        {
            throw new CryptoFailureException("The message did not authenticate.", exception);
        }

        return plaintext;
    }

    private static void CheckSizes(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce)
    {
        if (key.Length != CryptoSpec.AeadKeyLength || nonce.Length != CryptoSpec.AeadNonceLength)
        {
            throw new CryptoFailureException(CryptoFailure.InvalidLength);
        }
    }
}
