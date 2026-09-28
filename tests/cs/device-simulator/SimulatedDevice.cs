using System.Buffers.Text;
using System.Security.Cryptography;
using Coldframe.Crypto;
using Coldframe.Protocol.Device.V1;
using Coldframe.Protocol.Setup.V1;
using Google.Protobuf;

namespace Coldframe.DeviceSimulator;

/// <summary>
/// A sealed enrolment as the app relays it: the body of <c>POST /sites/{siteId}/devices</c> and the BLE
/// <see cref="EnrolmentResponse"/> it came from.
/// </summary>
/// <param name="SiteId">The Site the Device joins.</param>
/// <param name="DeviceId">The Device ID, 16 lowercase hex digits.</param>
/// <param name="Kind">The contract's Device kind, <c>hub</c> or <c>node</c>.</param>
/// <param name="Enc">The HPKE encapsulated key, base64url without padding.</param>
/// <param name="Ciphertext">The sealed <c>K_dev</c>, base64url without padding.</param>
/// <param name="Response">The Protobuf message the Device sends over BLE.</param>
public sealed record SimulatedEnrolment(string SiteId, string DeviceId, string Kind, string Enc, string Ciphertext, EnrolmentResponse Response);

/// <summary>
/// A simulated Hub or Node for Server and end-to-end tests (AD-24): it has a software root key and does
/// everything a Device does on the wire, from the generated Protobuf types and <c>Coldframe.Crypto</c> only.
/// </summary>
public sealed class SimulatedDevice
{
    private readonly ReplayWindow _downlinks = new();
    private ulong _nextUplink;

    private SimulatedDevice(DeviceKeys keys, DeviceKind kind)
    {
        Keys = keys;
        Kind = kind;
    }

    /// <summary>
    /// Every key the Device holds. The Server learns <c>K_dev</c> only through enrolment.
    /// </summary>
    public DeviceKeys Keys { get; }

    /// <summary>
    /// The Device ID.
    /// </summary>
    public DeviceId DeviceId => Keys.DeviceId;

    /// <summary>
    /// Hub or Node.
    /// </summary>
    public DeviceKind Kind { get; }

    /// <summary>
    /// A Device with a fixed 32-byte root key, as the vectors use.
    /// </summary>
    public static SimulatedDevice Create(ReadOnlySpan<byte> rootKey, DeviceKind kind = DeviceKind.Hub) =>
        new(DeviceKeys.FromRootKey(rootKey), kind);

    /// <summary>
    /// A Device with a random root key.
    /// </summary>
    public static SimulatedDevice Create(DeviceKind kind = DeviceKind.Hub) =>
        Create(RandomNumberGenerator.GetBytes(CryptoSpec.RootKeyLength), kind);

    /// <summary>
    /// The identity the Device reports over BLE.
    /// </summary>
    public Identity Identity(string firmwareVersion = "0.0.0") => new()
    {
        DeviceId = ByteString.CopyFrom(DeviceId.ToBytes()),
        Kind = Kind,
        FirmwareVersion = firmwareVersion,
    };

    /// <summary>
    /// Seals <c>K_dev</c> to the Server's enrolment key with a fresh ephemeral key.
    /// </summary>
    public SimulatedEnrolment SealEnrolment(ReadOnlySpan<byte> serverPublicKey, string siteId) =>
        SealEnrolment(serverPublicKey, siteId, RandomNumberGenerator.GetBytes(CryptoSpec.X25519KeyLength));

    /// <summary>
    /// Seals <c>K_dev</c> to the Server's enrolment key with an ephemeral key derived from <paramref name="ikmE"/>.
    /// </summary>
    public SimulatedEnrolment SealEnrolment(ReadOnlySpan<byte> serverPublicKey, string siteId, ReadOnlySpan<byte> ikmE)
    {
        var sealedEnrolment = Enrolment.Seal(serverPublicKey, ikmE, Keys);
        var response = new EnrolmentResponse
        {
            DeviceId = ByteString.CopyFrom(sealedEnrolment.DeviceId.ToBytes()),
            Enc = ByteString.CopyFrom(sealedEnrolment.Enc),
            Ciphertext = ByteString.CopyFrom(sealedEnrolment.Ciphertext),
        };
        return new SimulatedEnrolment(
            siteId,
            DeviceId.ToString(),
            Kind == DeviceKind.Node ? "node" : "hub",
            Base64Url.EncodeToString(sealedEnrolment.Enc),
            Base64Url.EncodeToString(sealedEnrolment.Ciphertext),
            response);
    }

    /// <summary>
    /// The four authentication headers of a Hub request at <paramref name="time"/> with a random nonce.
    /// </summary>
    public IReadOnlyDictionary<string, string> SignHeartbeat(string method, string path, ReadOnlySpan<byte> body, DateTimeOffset time) =>
        SignHeartbeat(method, path, body, time.ToUnixTimeMilliseconds(), RandomNumberGenerator.GetBytes(CryptoSpec.HeartbeatNonceLength));

    /// <summary>
    /// The four authentication headers of a Hub request.
    /// </summary>
    public IReadOnlyDictionary<string, string> SignHeartbeat(string method, string path, ReadOnlySpan<byte> body, long timestampMs, ReadOnlySpan<byte> nonce) =>
        Heartbeat.Headers(Keys, method, path, body, timestampMs, nonce);

    /// <summary>
    /// Seals an uplink payload with the next counter.
    /// </summary>
    public SealedEnvelope SealFrame(ReadOnlySpan<byte> payload) => SealFrame(_nextUplink, payload);

    /// <summary>
    /// Seals an uplink payload with an explicit counter; later frames continue above it.
    /// </summary>
    public SealedEnvelope SealFrame(ulong counter, ReadOnlySpan<byte> payload)
    {
        _nextUplink = Math.Max(_nextUplink, checked(counter + 1));
        return Envelopes.Seal(Keys.SealKey, DeviceId, counter, payload);
    }

    /// <summary>
    /// Verifies and opens a downlink addressed to this Device, refusing a replay.
    /// </summary>
    public Downlink OpenDownlink(SealedEnvelope envelope)
    {
        var plaintext = Envelopes.Open(Keys.AckKey, DeviceId, envelope, _downlinks);
        var downlink = Downlink.Parser.ParseFrom(plaintext);
        Envelopes.CheckVersion(downlink.ProtocolVersion);
        return downlink;
    }
}

/// <summary>
/// The Server's side of one enrolled Device, for tests: it seals downlinks and verifies frames with the keys
/// derived from <c>K_dev</c>, as the Device grain will.
/// </summary>
public sealed class SimulatedServer
{
    private readonly ReplayWindow _frames = new();
    private ulong _nextDownlink;

    /// <summary>
    /// The Server's view of a Device whose <c>K_dev</c> it holds.
    /// </summary>
    public SimulatedServer(ReadOnlySpan<byte> deviceKey)
    {
        Keys = DeviceKeys.FromDeviceKey(deviceKey);
    }

    /// <summary>
    /// The keys derived from <c>K_dev</c>.
    /// </summary>
    public DeviceKeys Keys { get; }

    /// <summary>
    /// The Device ID.
    /// </summary>
    public DeviceId DeviceId => Keys.DeviceId;

    /// <summary>
    /// Opens a relayed enrolment with the Server's enrolment private key.
    /// </summary>
    public static SimulatedServer FromEnrolment(ReadOnlySpan<byte> serverPrivateKey, SimulatedEnrolment enrolment)
    {
        ArgumentNullException.ThrowIfNull(enrolment);
        var deviceKey = Enrolment.Open(
            serverPrivateKey,
            Crypto.DeviceId.Parse(enrolment.DeviceId),
            Base64Url.DecodeFromChars(enrolment.Enc),
            Base64Url.DecodeFromChars(enrolment.Ciphertext));
        return new SimulatedServer(deviceKey);
    }

    /// <summary>
    /// Seals a downlink with the next counter.
    /// </summary>
    public SealedEnvelope SealDownlink(Downlink downlink) => SealDownlink(_nextDownlink, downlink);

    /// <summary>
    /// Seals a downlink with an explicit counter; later downlinks continue above it.
    /// </summary>
    public SealedEnvelope SealDownlink(ulong counter, Downlink downlink)
    {
        ArgumentNullException.ThrowIfNull(downlink);
        _nextDownlink = Math.Max(_nextDownlink, checked(counter + 1));
        return Envelopes.Seal(Keys.AckKey, DeviceId, counter, downlink.ToByteArray());
    }

    /// <summary>
    /// Verifies an uplink frame (Device ID, version, replay window, tag) and returns its payload.
    /// </summary>
    public byte[] VerifyFrame(SealedEnvelope envelope) => Envelopes.Open(Keys.SealKey, DeviceId, envelope, _frames);
}

/// <summary>
/// <see cref="SealedEnvelope"/> sealing shared by both sides.
/// </summary>
internal static class Envelopes
{
    public static SealedEnvelope Seal(ReadOnlySpan<byte> key, DeviceId deviceId, ulong counter, ReadOnlySpan<byte> payload) => new()
    {
        ProtocolVersion = CryptoSpec.ProtocolMajor,
        DeviceId = ByteString.CopyFrom(deviceId.ToBytes()),
        Counter = counter,
        Ciphertext = ByteString.CopyFrom(Frames.Seal(key, deviceId, counter, payload)),
    };

    public static byte[] Open(ReadOnlySpan<byte> key, DeviceId deviceId, SealedEnvelope envelope, ReplayWindow window)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        CheckVersion(envelope.ProtocolVersion);
        if (Crypto.DeviceId.FromBytes(envelope.DeviceId.Span) != deviceId)
        {
            throw new CryptoFailureException(CryptoFailure.AuthenticationFailed);
        }

        return Frames.Open(key, deviceId, envelope.Counter, envelope.Ciphertext.Span, window);
    }

    public static void CheckVersion(uint protocolVersion)
    {
        if (protocolVersion != CryptoSpec.ProtocolMajor)
        {
            throw new InvalidDataException($"Protocol version {protocolVersion} is not {CryptoSpec.ProtocolMajor}.");
        }
    }
}
