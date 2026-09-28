using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Coldframe.Crypto;

/// <summary>
/// The Device ID: <c>HKDF-SHA256(K_dev, "device-id/v1", L = 8)</c>. Its text form is 16 lowercase hex digits.
/// </summary>
/// <param name="Value">The 8 bytes read big-endian.</param>
public readonly record struct DeviceId(ulong Value)
{
    /// <summary>
    /// The Device ID of 8 raw bytes.
    /// </summary>
    public static DeviceId FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != CryptoSpec.DeviceIdLength)
        {
            throw new CryptoFailureException(CryptoFailure.InvalidLength);
        }

        return new DeviceId(BinaryPrimitives.ReadUInt64BigEndian(bytes));
    }

    /// <summary>
    /// Parses the text form, exactly 16 lowercase hex digits; anything else throws <see cref="CryptoFailureException"/>.
    /// </summary>
    public static DeviceId Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length != CryptoSpec.DeviceIdHexLength || !text.All(static c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f'))
        {
            throw new CryptoFailureException(CryptoFailure.InvalidLength);
        }

        return FromBytes(Convert.FromHexString(text));
    }

    /// <summary>
    /// The 8 raw bytes.
    /// </summary>
    public byte[] ToBytes()
    {
        var bytes = new byte[CryptoSpec.DeviceIdLength];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, Value);
        return bytes;
    }

    /// <summary>
    /// The text form: 16 lowercase hex digits.
    /// </summary>
    public override string ToString() => Value.ToString("x16", CultureInfo.InvariantCulture);
}

/// <summary>
/// The key hierarchy (AD-12): root → <c>K_dev</c> → purpose keys and the Device ID.
/// </summary>
public static class KeyHierarchy
{
    /// <summary>
    /// <c>K_dev = HMAC-SHA256(root, "coldframe/device/v1")</c>. On hardware the eFuse HMAC computes it.
    /// </summary>
    public static byte[] DeriveDeviceKey(ReadOnlySpan<byte> rootKey)
    {
        if (rootKey.Length != CryptoSpec.RootKeyLength)
        {
            throw new CryptoFailureException(CryptoFailure.InvalidLength);
        }

        return HMACSHA256.HashData(rootKey, Encoding.ASCII.GetBytes(CryptoSpec.DeviceKeyLabel));
    }

    /// <summary>
    /// <c>HKDF-SHA256(salt = empty, ikm = K_dev, info = label, L = 32)</c>.
    /// </summary>
    public static byte[] DerivePurposeKey(ReadOnlySpan<byte> deviceKey, string label) =>
        Expand(deviceKey, label, CryptoSpec.PurposeKeyLength);

    /// <summary>
    /// The Device ID of <c>K_dev</c>.
    /// </summary>
    public static DeviceId DeriveDeviceId(ReadOnlySpan<byte> deviceKey) =>
        DeviceId.FromBytes(Expand(deviceKey, CryptoSpec.DeviceIdLabel, CryptoSpec.DeviceIdLength));

    private static byte[] Expand(ReadOnlySpan<byte> deviceKey, string label, int length)
    {
        if (deviceKey.Length != CryptoSpec.DeviceKeyLength)
        {
            throw new CryptoFailureException(CryptoFailure.InvalidLength);
        }

        var output = new byte[length];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, deviceKey, output, [], Encoding.ASCII.GetBytes(label));
        return output;
    }
}

/// <summary>
/// Every key a Device holds, derived from <c>K_dev</c>. <see cref="ToString"/> never prints key material.
/// </summary>
public sealed class DeviceKeys
{
    private readonly byte[] _deviceKey;
    private readonly byte[] _sealKey;
    private readonly byte[] _ackKey;
    private readonly byte[] _hubAuthKey;

    private DeviceKeys(byte[] deviceKey)
    {
        _deviceKey = deviceKey;
        _sealKey = KeyHierarchy.DerivePurposeKey(deviceKey, CryptoSpec.SealLabel);
        _ackKey = KeyHierarchy.DerivePurposeKey(deviceKey, CryptoSpec.AckLabel);
        _hubAuthKey = KeyHierarchy.DerivePurposeKey(deviceKey, CryptoSpec.HubAuthLabel);
        DeviceId = KeyHierarchy.DeriveDeviceId(deviceKey);
    }

    /// <summary>
    /// The Device ID.
    /// </summary>
    public DeviceId DeviceId { get; }

    /// <summary>
    /// <c>K_dev</c>, which enrolment seals to the Server.
    /// </summary>
    public ReadOnlySpan<byte> DeviceKey => _deviceKey;

    /// <summary>
    /// <c>seal/v1</c>: seals uplink frames.
    /// </summary>
    public ReadOnlySpan<byte> SealKey => _sealKey;

    /// <summary>
    /// <c>ack/v1</c>: seals downlinks.
    /// </summary>
    public ReadOnlySpan<byte> AckKey => _ackKey;

    /// <summary>
    /// <c>hub-auth/v1</c>: signs Hub requests.
    /// </summary>
    public ReadOnlySpan<byte> HubAuthKey => _hubAuthKey;

    /// <summary>
    /// Derives every key from <c>K_dev</c>, as the Server does after enrolment.
    /// </summary>
    public static DeviceKeys FromDeviceKey(ReadOnlySpan<byte> deviceKey)
    {
        if (deviceKey.Length != CryptoSpec.DeviceKeyLength)
        {
            throw new CryptoFailureException(CryptoFailure.InvalidLength);
        }

        return new DeviceKeys(deviceKey.ToArray());
    }

    /// <summary>
    /// Derives every key from a software root key (dev mode and tests).
    /// </summary>
    public static DeviceKeys FromRootKey(ReadOnlySpan<byte> rootKey) => new(KeyHierarchy.DeriveDeviceKey(rootKey));

    /// <summary>
    /// Names the Device only.
    /// </summary>
    public override string ToString() => $"DeviceKeys({DeviceId})";
}
