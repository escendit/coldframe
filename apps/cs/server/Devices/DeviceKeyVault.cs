using System.Security.Cryptography;
using System.Text;
using Coldframe.Contracts.Devices;
using Coldframe.Crypto;
using Microsoft.Extensions.Options;

namespace Coldframe.Server.Devices;

/// <summary>
/// Keeps every Device's <c>K_dev</c> encrypted at rest (AD-12) under a key-encryption key (KEK) that is
/// separate from the enrolment key, so rotating the enrolment key leaves stored keys readable.
/// </summary>
/// <remarks>
/// <c>kek = HKDF-SHA256(ikm = UTF-8(secret), salt = empty, info = "coldframe/device-kek/v1", L = 32)</c>.
/// A wrapped key is <c>{kekId, nonce, sealed}</c>: <c>sealed = ChaCha20-Poly1305(kek, nonce,
/// aad = "coldframe/device-key/v1" ‖ deviceId (8 bytes), K_dev)</c> with a random 12-byte nonce, and
/// <c>kekId</c> is the first 16 hex digits of SHA-256(kek). The labels are private to the Server, not wire.
/// </remarks>
public sealed class DeviceKeyVault
{
    /// <summary>
    /// The HKDF info of the key-encryption key.
    /// </summary>
    public const string KekLabel = "coldframe/device-kek/v1";

    /// <summary>
    /// The prefix of the associated data of a wrapped key.
    /// </summary>
    public const string WrapLabel = "coldframe/device-key/v1";

    private const int KekIdLength = 16;

    private static readonly byte[] WrapLabelBytes = Encoding.ASCII.GetBytes(WrapLabel);

    private readonly byte[] _kek;

    /// <summary>
    /// Creates the vault from the configured secret.
    /// </summary>
    public DeviceKeyVault(IOptions<EnrolmentOptions> options)
        : this((options ?? throw new ArgumentNullException(nameof(options))).Value.DeviceKeyEncryptionKey)
    {
    }

    /// <summary>
    /// Creates the vault from a secret of at least 32 characters.
    /// </summary>
    public DeviceKeyVault(string? secret)
    {
        if (secret is null || secret.Length < EnrolmentOptions.MinDeviceKeyEncryptionKeyLength)
        {
            throw new ArgumentException(
                $"The Device key-encryption key needs at least {EnrolmentOptions.MinDeviceKeyEncryptionKeyLength} characters.",
                nameof(secret));
        }

        _kek = new byte[CryptoSpec.AeadKeyLength];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(secret), _kek, [], Encoding.ASCII.GetBytes(KekLabel));
        KekId = Convert.ToHexStringLower(SHA256.HashData(_kek))[..KekIdLength];
    }

    /// <summary>
    /// Names the key-encryption key: the first 16 hex digits of its SHA-256.
    /// </summary>
    public string KekId { get; }

    /// <summary>
    /// Wraps <c>K_dev</c> of <paramref name="deviceId"/> with a fresh random nonce.
    /// </summary>
    public WrappedDeviceKey Wrap(DeviceId deviceId, ReadOnlySpan<byte> deviceKey)
    {
        if (deviceKey.Length != CryptoSpec.DeviceKeyLength)
        {
            throw new CryptoFailureException(CryptoFailure.InvalidLength);
        }

        var nonce = RandomNumberGenerator.GetBytes(CryptoSpec.AeadNonceLength);
        return new WrappedDeviceKey(KekId, nonce, Aead.Seal(_kek, nonce, AssociatedData(deviceId), deviceKey));
    }

    /// <summary>
    /// Unwraps <c>K_dev</c> of <paramref name="deviceId"/>; the caller zeroes it after use. Throws
    /// <see cref="CryptoFailureException"/> for another KEK, another Device or a tampered key.
    /// </summary>
    public byte[] Unwrap(DeviceId deviceId, WrappedDeviceKey wrapped)
    {
        ArgumentNullException.ThrowIfNull(wrapped);

        if (!string.Equals(wrapped.KekId, KekId, StringComparison.Ordinal))
        {
            throw new CryptoFailureException("The Device key was wrapped under another key-encryption key.");
        }

        if (wrapped.Nonce is not { Length: CryptoSpec.AeadNonceLength }
            || wrapped.Sealed is not { Length: CryptoSpec.DeviceKeyLength + CryptoSpec.AeadTagLength })
        {
            throw new CryptoFailureException(CryptoFailure.InvalidLength);
        }

        return Aead.Open(_kek, wrapped.Nonce, AssociatedData(deviceId), wrapped.Sealed);
    }

    /// <summary>
    /// Names the key-encryption key only.
    /// </summary>
    public override string ToString() => $"DeviceKeyVault({KekId})";

    private static byte[] AssociatedData(DeviceId deviceId) => [.. WrapLabelBytes, .. deviceId.ToBytes()];
}
