using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Coldframe.Crypto;

/// <summary>
/// Which end of a setup session this is.
/// </summary>
public enum SetupRole
{
    /// <summary>The phone, which sends with the app → Device key.</summary>
    App,

    /// <summary>The Hub or Node, which sends with the Device → app key.</summary>
    Device,
}

/// <summary>
/// The two direction keys of a setup session.
/// </summary>
/// <param name="AppToDevice">Seals app → Device messages.</param>
/// <param name="DeviceToApp">Seals Device → app messages.</param>
public sealed record SetupKeys(byte[] AppToDevice, byte[] DeviceToApp)
{
    /// <summary>
    /// Never prints key material.
    /// </summary>
    public override string ToString() => nameof(SetupKeys);
}

/// <summary>
/// One end of the BLE setup session (AD-25): X25519, the PoP code as the HKDF salt, then ChaCha20-Poly1305
/// per message with one counter per direction. The first message that fails to open means a wrong code.
/// </summary>
public sealed class SetupSession
{
    private static readonly byte[] SessionAad = [CryptoSpec.ProtocolMajor];

    private readonly byte[] _sendKey;
    private readonly byte[] _receiveKey;
    private ulong _nextSend = CryptoSpec.SetupFirstCounter;
    private ulong _nextReceive = CryptoSpec.SetupFirstCounter;
    private bool _openedAny;

    /// <summary>
    /// Starts a session from this end's private key, the peer's public key and the PoP code.
    /// </summary>
    public SetupSession(SetupRole role, ReadOnlySpan<byte> ownPrivateKey, ReadOnlySpan<byte> peerPublicKey, string popCode)
    {
        var ownPublicKey = X25519.PublicKey(ownPrivateKey);
        var keys = role == SetupRole.App
            ? DeriveKeys(ownPrivateKey, peerPublicKey, ownPublicKey, peerPublicKey, popCode)
            : DeriveKeys(ownPrivateKey, peerPublicKey, peerPublicKey, ownPublicKey, popCode);
        (_sendKey, _receiveKey) = role == SetupRole.App
            ? (keys.AppToDevice, keys.DeviceToApp)
            : (keys.DeviceToApp, keys.AppToDevice);
    }

    /// <summary>
    /// <c>HKDF-SHA256(salt = uppercase PoP code, ikm = X25519, info = "coldframe/setup/v1" ‖ app_pub ‖ device_pub, L = 64)</c>.
    /// </summary>
    public static SetupKeys DeriveKeys(
        ReadOnlySpan<byte> ownPrivateKey,
        ReadOnlySpan<byte> peerPublicKey,
        ReadOnlySpan<byte> appPublicKey,
        ReadOnlySpan<byte> devicePublicKey,
        string popCode)
    {
        ArgumentNullException.ThrowIfNull(popCode);
        if (popCode.Length is 0 or > CryptoSpec.SetupMaxCodeLength || !Ascii.IsValid(popCode))
        {
            throw new CryptoFailureException(CryptoFailure.InvalidSetupCode);
        }

        var salt = Encoding.ASCII.GetBytes(popCode.ToUpperInvariant());
        var shared = X25519.SharedSecret(ownPrivateKey, peerPublicKey);
        byte[] info = [.. Encoding.ASCII.GetBytes(CryptoSpec.SetupLabel), .. appPublicKey, .. devicePublicKey];
        var okm = new byte[CryptoSpec.SetupOkmLength];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, okm, salt, info);
        return new SetupKeys(
            okm.AsSpan(CryptoSpec.SetupAppToHubKeyOffset, CryptoSpec.SetupKeyLength).ToArray(),
            okm.AsSpan(CryptoSpec.SetupHubToAppKeyOffset, CryptoSpec.SetupKeyLength).ToArray());
    }

    /// <summary>
    /// The nonce of the message with <paramref name="counter"/>: <c>0x00000000 ‖ counter_u64_be</c>.
    /// </summary>
    public static byte[] Nonce(ulong counter)
    {
        var nonce = new byte[CryptoSpec.AeadNonceLength];
        BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(CryptoSpec.SetupNoncePrefixLength), counter);
        return nonce;
    }

    /// <summary>
    /// Seals the next outgoing message and returns its counter and ciphertext.
    /// </summary>
    public (ulong Counter, byte[] Ciphertext) Seal(ReadOnlySpan<byte> plaintext)
    {
        var counter = _nextSend;
        if (counter == ulong.MaxValue)
        {
            throw new CryptoFailureException(CryptoFailure.Replay);
        }

        var ciphertext = Aead.Seal(_sendKey, Nonce(counter), SessionAad, plaintext);
        _nextSend = counter + 1;
        return (counter, ciphertext);
    }

    /// <summary>
    /// Opens an incoming message whose counter is above every counter opened before (and below
    /// <see cref="ulong.MaxValue"/>, so the next one can follow). The first failure to
    /// open throws <see cref="CryptoFailure.WrongSetupCode"/>; later ones <see cref="CryptoFailure.AuthenticationFailed"/>.
    /// </summary>
    public byte[] Open(ulong counter, ReadOnlySpan<byte> ciphertext)
    {
        if (counter < _nextReceive || counter == ulong.MaxValue)
        {
            throw new CryptoFailureException(CryptoFailure.Replay);
        }

        byte[] plaintext;
        try
        {
            plaintext = Aead.Open(_receiveKey, Nonce(counter), SessionAad, ciphertext);
        }
        catch (CryptoFailureException exception) when (!_openedAny && exception.Failure == CryptoFailure.AuthenticationFailed)
        {
            throw new CryptoFailureException(CryptoFailure.WrongSetupCode);
        }

        _openedAny = true;
        _nextReceive = counter + 1;
        return plaintext;
    }
}
