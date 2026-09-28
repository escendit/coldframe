using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Coldframe.Crypto;

/// <summary>
/// Hub request authentication (AD-12): HMAC-SHA256 with the <c>hub-auth/v1</c> key over
/// <c>METHOD\nPATH\nhex(SHA-256(body))\nTIMESTAMP_MS\nNONCE_HEX</c>, sent as lowercase hex.
/// </summary>
public static class Heartbeat
{
    /// <summary>
    /// The canonical string of a request. <paramref name="method"/> is uppercase and <paramref name="path"/> the
    /// absolute request path without query string, exactly as sent; <paramref name="timestampMs"/> is never negative.
    /// </summary>
    public static string Canonical(string method, string path, ReadOnlySpan<byte> body, long timestampMs, ReadOnlySpan<byte> nonce)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentOutOfRangeException.ThrowIfNegative(timestampMs);
        if (nonce.Length != CryptoSpec.HeartbeatNonceLength)
        {
            throw new CryptoFailureException(CryptoFailure.InvalidLength);
        }

        return string.Join(
            CryptoSpec.HeartbeatSeparator,
            method,
            path,
            Convert.ToHexStringLower(SHA256.HashData(body)),
            timestampMs.ToString(CultureInfo.InvariantCulture),
            Convert.ToHexStringLower(nonce));
    }

    /// <summary>
    /// The raw signature with the <c>hub-auth/v1</c> key.
    /// </summary>
    public static byte[] Sign(ReadOnlySpan<byte> hubAuthKey, string method, string path, ReadOnlySpan<byte> body, long timestampMs, ReadOnlySpan<byte> nonce) =>
        HMACSHA256.HashData(hubAuthKey, Encoding.UTF8.GetBytes(Canonical(method, path, body, timestampMs, nonce)));

    /// <summary>
    /// Whether <paramref name="signature"/> is the raw signature of the request, compared in constant time.
    /// </summary>
    public static bool Verify(ReadOnlySpan<byte> hubAuthKey, string method, string path, ReadOnlySpan<byte> body, long timestampMs, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> signature) =>
        CryptographicOperations.FixedTimeEquals(Sign(hubAuthKey, method, path, body, timestampMs, nonce), signature);

    /// <summary>
    /// The four authentication headers of a request, by name.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Headers(DeviceKeys keys, string method, string path, ReadOnlySpan<byte> body, long timestampMs, ReadOnlySpan<byte> nonce)
    {
        ArgumentNullException.ThrowIfNull(keys);
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [CryptoSpec.HeartbeatDeviceHeader] = keys.DeviceId.ToString(),
            [CryptoSpec.HeartbeatTimestampHeader] = timestampMs.ToString(CultureInfo.InvariantCulture),
            [CryptoSpec.HeartbeatNonceHeader] = Convert.ToHexStringLower(nonce),
            [CryptoSpec.HeartbeatSignatureHeader] = Convert.ToHexStringLower(Sign(keys.HubAuthKey, method, path, body, timestampMs, nonce)),
        };
    }
}
