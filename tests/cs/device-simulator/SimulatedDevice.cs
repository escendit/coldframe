using System.Buffers.Text;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
/// <param name="LotId">The Lot a Node is put in (the app's choice, not the Device's), or <see langword="null"/>.</param>
public sealed record SimulatedEnrolment(
    string SiteId,
    string DeviceId,
    string Kind,
    string Enc,
    string Ciphertext,
    EnrolmentResponse Response,
    string? LotId = null);

/// <summary>
/// One Sensor value a simulated Node measures at a wake.
/// </summary>
/// <param name="Slot">The Sensor slot.</param>
/// <param name="Quantity">What the Sensor measures.</param>
/// <param name="Value">The raw value.</param>
public sealed record SimulatedReading(uint Slot, Quantity Quantity, long Value);

/// <summary>
/// One entry of the answer to <c>POST /device/ingest</c>, as the Hub reads it.
/// </summary>
/// <param name="Status">The frame's status, such as <c>stored</c>.</param>
/// <param name="Downlink">The sealed downlink the Hub relays to the Node, or <see langword="null"/>.</param>
public sealed record SimulatedIngestResult(string Status, SealedEnvelope? Downlink);

/// <summary>
/// A simulated Hub or Node for Server and end-to-end tests (AD-24): it has a software root key and does
/// everything a Device does on the wire, from the generated Protobuf types and <c>Coldframe.Crypto</c> only.
/// </summary>
public sealed class SimulatedDevice
{
    private readonly ReplayWindow _downlinks = new();
    private ulong _nextUplink;

    /// <summary>
    /// The path of the ingest operation.
    /// </summary>
    public const string IngestPath = "/device/ingest";

    /// <summary>
    /// What a Node measures at a wake unless a test says otherwise: one Reading per Sensor, slots 0 to 3.
    /// </summary>
    public static IReadOnlyList<SimulatedReading> DefaultReadings { get; } =
    [
        new(0, Quantity.SoilMoisture, 1873),
        new(1, Quantity.AirTemperature, 21_500),
        new(2, Quantity.RelativeHumidity, 64_250),
        new(3, Quantity.GasResistance, 48_211),
    ];

    /// <summary>
    /// The next <c>reading_seq</c> a wake draws; every Reading and every device report takes one.
    /// </summary>
    public ulong NextReadingSeq { get; set; }

    /// <summary>
    /// The boot counter of the current power-on; a test "reboots" the Node by raising it.
    /// </summary>
    public ulong BootId { get; set; } = 1;

    /// <summary>
    /// The uptime, in milliseconds, at which the next frame is sealed.
    /// </summary>
    public ulong UptimeMs { get; set; } = 60_000;

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
    /// Seals a Node's <c>K_dev</c> to the Server's enrolment key with a fresh ephemeral key, for the Lot the
    /// app puts it in. The sealed key is the same for any Lot; only the request body names it.
    /// </summary>
    public SimulatedEnrolment SealEnrolment(ReadOnlySpan<byte> serverPublicKey, string siteId, string lotId) =>
        SealEnrolment(serverPublicKey, siteId) with { LotId = lotId };

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
    /// The path of the heartbeat operation.
    /// </summary>
    public const string HeartbeatPath = "/device/heartbeat";

    /// <summary>
    /// A heartbeat body exactly as the Hub writes it: <c>{"protocolVersion":1,"uptimeMs":N}</c>.
    /// </summary>
    public static byte[] HeartbeatBody(long uptimeMs, long protocolVersion = CryptoSpec.ProtocolMajor) =>
        Encoding.UTF8.GetBytes(string.Create(
            CultureInfo.InvariantCulture,
            $"{{\"protocolVersion\":{protocolVersion},\"uptimeMs\":{uptimeMs}}}"));

    /// <summary>
    /// A signed <c>POST /device/heartbeat</c> at <paramref name="time"/> with a fresh random nonce, as the Hub
    /// sends it: the body (by default <see cref="HeartbeatBody"/> with 61 000 ms uptime) as
    /// <c>application/json</c> and the four authentication headers.
    /// </summary>
    public HttpRequestMessage HeartbeatRequest(DateTimeOffset time, byte[]? body = null) =>
        HeartbeatRequest(time.ToUnixTimeMilliseconds(), RandomNumberGenerator.GetBytes(CryptoSpec.HeartbeatNonceLength), body);

    /// <summary>
    /// A signed <c>POST /device/heartbeat</c> with an explicit timestamp and nonce (a replay test resends one).
    /// </summary>
    public HttpRequestMessage HeartbeatRequest(long timestampMs, ReadOnlySpan<byte> nonce, byte[]? body = null)
    {
        return SignedRequest(HeartbeatPath, timestampMs, nonce, body ?? HeartbeatBody(61_000));
    }

    // A POST of a JSON body to `path`, signed with the hub-auth key.
    private HttpRequestMessage SignedRequest(string path, long timestampMs, ReadOnlySpan<byte> nonce, byte[] body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = new ByteArrayContent(body),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        foreach (var (name, value) in SignHeartbeat(HttpMethod.Post.Method, path, body, timestampMs, nonce))
        {
            request.Headers.Add(name, value);
        }

        return request;
    }

    private NodeFrame NewFrame(uint? batteryPercent, ChargeStatus charging, IReadOnlyList<SimulatedReading>? readings)
    {
        var frame = new NodeFrame { ProtocolVersion = CryptoSpec.ProtocolMajor, Charging = charging };
        foreach (var reading in readings ?? DefaultReadings)
        {
            frame.Readings.Add(new Reading
            {
                Slot = reading.Slot,
                ReadingSeq = NextReadingSeq++,
                Quantity = reading.Quantity,
                Value = reading.Value,
            });
        }

        frame.ReportSeq = NextReadingSeq++;
        if (batteryPercent is { } percent)
        {
            frame.BatteryPercent = percent;
        }

        return frame;
    }

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
    /// One wake of a Node whose clock is set: the Readings and the device report, each with a fresh
    /// <c>reading_seq</c>. Nothing is sealed yet; <see cref="SealFrame(NodeFrame)"/> seals it, again for a resend.
    /// </summary>
    public NodeFrame Wake(
        DateTimeOffset measuredAt,
        uint? batteryPercent = 87,
        ChargeStatus charging = ChargeStatus.Charging,
        IReadOnlyList<SimulatedReading>? readings = null)
    {
        var frame = NewFrame(batteryPercent, charging, readings);
        frame.MeasuredAtMs = measuredAt.ToUnixTimeMilliseconds();
        return frame;
    }

    /// <summary>
    /// One wake of a Node whose clock was never set, taken at <paramref name="uptimeMs"/> of the current boot
    /// (<see cref="BootId"/>): the Readings are <c>time_unsynced</c>.
    /// </summary>
    public NodeFrame WakeUnsynced(
        ulong uptimeMs,
        uint? batteryPercent = 87,
        ChargeStatus charging = ChargeStatus.Charging,
        IReadOnlyList<SimulatedReading>? readings = null)
    {
        var frame = NewFrame(batteryPercent, charging, readings);
        frame.Unsynced = new NodeFrame.Types.Unsynced { BootId = BootId, UptimeMs = uptimeMs };
        return frame;
    }

    /// <summary>
    /// Seals a wake report with the next counter, stamped with the current <see cref="BootId"/> and
    /// <see cref="UptimeMs"/>. Sealing the same report again is a resend: a new counter, the same Readings.
    /// </summary>
    public SealedEnvelope SealFrame(NodeFrame frame) => SealFrame(_nextUplink, frame);

    /// <summary>
    /// Seals a wake report with an explicit counter; later frames continue above it.
    /// </summary>
    public SealedEnvelope SealFrame(ulong counter, NodeFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var stamped = frame.Clone();
        stamped.BootId = BootId;
        stamped.UptimeMs = UptimeMs;
        return SealFrame(counter, stamped.ToByteArray());
    }

    /// <summary>
    /// Every <c>reading_seq</c> of a wake report, the report's own included, ascending.
    /// </summary>
    public static IReadOnlyList<ulong> ReadingSeqs(NodeFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return [.. frame.Readings.Select(reading => reading.ReadingSeq).Append(frame.ReportSeq).Distinct().Order()];
    }

    /// <summary>
    /// Every <c>reading_seq</c> a downlink acknowledges, ascending.
    /// </summary>
    public static IReadOnlyList<ulong> AckedReadings(Downlink downlink)
    {
        ArgumentNullException.ThrowIfNull(downlink);
        var acked = new List<ulong>();
        foreach (var range in downlink.AckedReadings)
        {
            for (var seq = range.First; seq <= range.Last; seq++)
            {
                acked.Add(seq);
                if (seq == ulong.MaxValue)
                {
                    break;
                }
            }
        }

        return acked;
    }

    /// <summary>
    /// The token of a quantity in a Sensor ID (AD-19).
    /// </summary>
    public static string QuantityToken(Quantity quantity) => quantity switch
    {
        Quantity.SoilMoisture => CryptoSpec.SensorQuantitySoilMoisture,
        Quantity.AirTemperature => CryptoSpec.SensorQuantityAirTemperature,
        Quantity.RelativeHumidity => CryptoSpec.SensorQuantityRelativeHumidity,
        Quantity.GasResistance => CryptoSpec.SensorQuantityGasResistance,
        _ => throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Not a quantity of the contract."),
    };

    /// <summary>
    /// The Sensor ID of one of this Node's slots, as the Node computes it (AD-19).
    /// </summary>
    public Guid SensorId(uint slot, Quantity quantity) => SensorIds.Derive(DeviceId, slot, QuantityToken(quantity));

    /// <summary>
    /// A sealed frame as the Hub puts it in the ingest envelope: the <see cref="SealedEnvelope"/> bytes in base64.
    /// </summary>
    public static string EncodeFrame(SealedEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return Convert.ToBase64String(envelope.ToByteArray());
    }

    /// <summary>
    /// The ingest envelope exactly as the Hub writes it: <c>{"frames":["…","…"]}</c>.
    /// </summary>
    public static byte[] IngestBody(params SealedEnvelope[] frames) => IngestBody(frames.Select(EncodeFrame));

    /// <summary>
    /// The ingest envelope over frames already encoded (a test tampers with one first).
    /// </summary>
    public static byte[] IngestBody(IEnumerable<string> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("frames");
            foreach (var frame in frames)
            {
                writer.WriteStringValue(frame);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// A signed <c>POST /device/ingest</c> at <paramref name="time"/> with a fresh random nonce, as a Hub sends it.
    /// </summary>
    public HttpRequestMessage IngestRequest(DateTimeOffset time, byte[] body) =>
        IngestRequest(time.ToUnixTimeMilliseconds(), RandomNumberGenerator.GetBytes(CryptoSpec.HeartbeatNonceLength), body);

    /// <summary>
    /// A signed <c>POST /device/ingest</c> with an explicit timestamp and nonce (a replay test resends one).
    /// </summary>
    public HttpRequestMessage IngestRequest(long timestampMs, ReadOnlySpan<byte> nonce, byte[] body) =>
        SignedRequest(IngestPath, timestampMs, nonce, body);

    /// <summary>
    /// Reads the answer to <c>POST /device/ingest</c>: one status per frame, in request order, each with its
    /// sealed downlink when the Server sent one.
    /// </summary>
    public static IReadOnlyList<SimulatedIngestResult> ReadIngestResponse(ReadOnlySpan<byte> json)
    {
        var reader = new Utf8JsonReader(json);
        using var document = JsonDocument.ParseValue(ref reader);
        return
        [
            .. document.RootElement.GetProperty("results").EnumerateArray().Select(result => new SimulatedIngestResult(
                result.GetProperty("status").GetString() ?? throw new InvalidDataException("A result has no status."),
                result.TryGetProperty("downlink", out var downlink)
                    ? SealedEnvelope.Parser.ParseFrom(downlink.GetBytesFromBase64())
                    : null)),
        ];
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
