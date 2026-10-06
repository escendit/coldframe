using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Coldframe.Crypto;

/// <summary>
/// Sensor identity (AD-19): <c>sensorId = UUIDv5(namespace, "{deviceIdHex}:{slot}:{quantity}")</c>, computed
/// identically by the Node and the Server from <c>packages/crypto-spec</c>. The quantity is one of the
/// <c>SensorQuantity…</c> tokens of <see cref="CryptoSpec"/>.
/// </summary>
public static class SensorIds
{
    /// <summary>
    /// The namespace of every Sensor ID: <see cref="CryptoSpec.SensorIdNamespace"/>.
    /// </summary>
    public static Guid Namespace { get; } = Guid.ParseExact(CryptoSpec.SensorIdNamespace, "D");

    /// <summary>
    /// The nil UUID that stands in for the Sensor in the key of a device report (AD-9).
    /// </summary>
    public static Guid DeviceReport { get; } = Guid.ParseExact(CryptoSpec.DeviceReportSensorId, "D");

    /// <summary>
    /// The UUIDv5 name of a Sensor: the Device ID in lowercase hex, the slot and the quantity token.
    /// </summary>
    public static string Name(DeviceId deviceId, uint slot, string quantity)
    {
        ArgumentException.ThrowIfNullOrEmpty(quantity);

        return CryptoSpec.SensorIdNameFormat
            .Replace("{deviceIdHex}", deviceId.ToString(), StringComparison.Ordinal)
            .Replace("{slot}", slot.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{quantity}", quantity, StringComparison.Ordinal);
    }

    /// <summary>
    /// The Sensor ID of a Device's slot and quantity token.
    /// </summary>
    public static Guid Derive(DeviceId deviceId, uint slot, string quantity) => UuidV5(Namespace, Name(deviceId, slot, quantity));

    /// <summary>
    /// RFC 9562 UUIDv5: SHA-1 over the namespace's 16 bytes in network order and the UTF-8 name.
    /// </summary>
    public static Guid UuidV5(Guid namespaceId, string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var nameBytes = Encoding.UTF8.GetBytes(name);
        var input = new byte[16 + nameBytes.Length];
        namespaceId.TryWriteBytes(input, bigEndian: true, out _);
        nameBytes.CopyTo(input, 16);

#pragma warning disable CA5350 // UUIDv5 is defined over SHA-1 (RFC 9562); it names a Sensor and protects nothing.
        var digest = SHA1.HashData(input);
#pragma warning restore CA5350
        digest[6] = (byte)((digest[6] & 0x0F) | 0x50);
        digest[8] = (byte)((digest[8] & 0x3F) | 0x80);

        return new Guid(digest.AsSpan(0, 16), bigEndian: true);
    }
}
