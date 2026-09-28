using System.Buffers.Binary;

namespace Coldframe.Crypto;

/// <summary>
/// Frame and downlink sealing (AD-12). Nonce = <c>device_id[0..4] ‖ counter_u64_be</c>; AAD =
/// <c>u8 protocol_major ‖ device_id ‖ counter_u64_be</c>. Uplink frames use the <c>seal/v1</c> key, downlinks
/// the <c>ack/v1</c> key.
/// </summary>
public static class Frames
{
    /// <summary>
    /// The 12-byte nonce of the frame with <paramref name="counter"/>.
    /// </summary>
    public static byte[] Nonce(DeviceId deviceId, ulong counter)
    {
        var nonce = new byte[CryptoSpec.AeadNonceLength];
        deviceId.ToBytes().AsSpan(0, CryptoSpec.FrameNonceDeviceIdPrefixLength).CopyTo(nonce);
        BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(CryptoSpec.FrameNonceDeviceIdPrefixLength), counter);
        return nonce;
    }

    /// <summary>
    /// The 17-byte associated data of the frame with <paramref name="counter"/>.
    /// </summary>
    public static byte[] Aad(DeviceId deviceId, ulong counter)
    {
        var aad = new byte[CryptoSpec.FrameAadLength];
        aad[0] = CryptoSpec.ProtocolMajor;
        deviceId.ToBytes().CopyTo(aad, 1);
        BinaryPrimitives.WriteUInt64BigEndian(aad.AsSpan(1 + CryptoSpec.DeviceIdLength), counter);
        return aad;
    }

    /// <summary>
    /// Seals <paramref name="plaintext"/> with a purpose key.
    /// </summary>
    public static byte[] Seal(ReadOnlySpan<byte> key, DeviceId deviceId, ulong counter, ReadOnlySpan<byte> plaintext) =>
        Aead.Seal(key, Nonce(deviceId, counter), Aad(deviceId, counter), plaintext);

    /// <summary>
    /// Opens a sealed frame, checking authenticity only.
    /// </summary>
    public static byte[] Open(ReadOnlySpan<byte> key, DeviceId deviceId, ulong counter, ReadOnlySpan<byte> sealedBytes) =>
        Aead.Open(key, Nonce(deviceId, counter), Aad(deviceId, counter), sealedBytes);

    /// <summary>
    /// Opens a sealed frame and records its counter: a replay is refused before decrypting, and the window
    /// moves only after the frame authenticates.
    /// </summary>
    public static byte[] Open(ReadOnlySpan<byte> key, DeviceId deviceId, ulong counter, ReadOnlySpan<byte> sealedBytes, ReplayWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.Check(counter);
        var plaintext = Open(key, deviceId, counter, sealedBytes);
        window.Accept(counter);
        return plaintext;
    }
}

/// <summary>
/// Accepts a counter above the high-water mark, or an unseen one among the 64 counters ending at it (AD-17).
/// Anything else is a replay.
/// </summary>
public sealed class ReplayWindow
{
    private ulong _seen;

    /// <summary>
    /// The highest accepted counter, or <see langword="null"/> before the first.
    /// </summary>
    public ulong? Highest { get; private set; }

    /// <summary>
    /// Whether <paramref name="counter"/> would be accepted, without recording it.
    /// </summary>
    public bool WouldAccept(ulong counter)
    {
        if (Highest is not { } highest || counter > highest)
        {
            return true;
        }

        var age = highest - counter;
        return age < (ulong)CryptoSpec.ReplayWindow && (_seen & (1UL << (int)age)) == 0;
    }

    /// <summary>
    /// Throws <see cref="CryptoFailure.Replay"/> unless <paramref name="counter"/> would be accepted.
    /// </summary>
    public void Check(ulong counter)
    {
        if (!WouldAccept(counter))
        {
            throw new CryptoFailureException(CryptoFailure.Replay);
        }
    }

    /// <summary>
    /// Records <paramref name="counter"/>, or throws <see cref="CryptoFailure.Replay"/>.
    /// </summary>
    public void Accept(ulong counter)
    {
        Check(counter);
        if (Highest is not { } highest)
        {
            _seen = 1;
            Highest = counter;
        }
        else if (counter <= highest)
        {
            _seen |= 1UL << (int)(highest - counter);
        }
        else
        {
            var shift = counter - highest;
            _seen = (shift >= 64 ? 0 : _seen << (int)shift) | 1;
            Highest = counter;
        }
    }
}
