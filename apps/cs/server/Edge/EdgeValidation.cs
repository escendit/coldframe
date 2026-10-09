using System.Buffers.Text;
using System.Globalization;
using System.Text.Json;
using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Notifications;
using Coldframe.Contracts.Sites;
using Coldframe.Crypto;

namespace Coldframe.Server.Edge;

/// <summary>
/// The request rules the Edge API checks before it calls a grain.
/// </summary>
public static class EdgeValidation
{
    /// <summary>
    /// The longest Site name, after trimming.
    /// </summary>
    public const int MaxSiteNameLength = 100;

    /// <summary>
    /// The longest Lot name, after trimming: the same rule as Site names.
    /// </summary>
    public const int MaxLotNameLength = MaxSiteNameLength;

    /// <summary>
    /// The longest <c>Idempotency-Key</c>.
    /// </summary>
    public const int MaxIdempotencyKeyLength = 200;

    /// <summary>
    /// The largest heartbeat body read, in bytes: a heartbeat is about 40.
    /// </summary>
    public const int MaxHeartbeatBodyLength = 4096;

    /// <summary>
    /// The heartbeat body's wire major, the only one served.
    /// </summary>
    public const long HeartbeatProtocolVersion = 1;

    /// <summary>
    /// The largest ingest body read, in bytes.
    /// </summary>
    public const int MaxIngestBodyLength = 16 * 1024;

    /// <summary>
    /// The most frames one ingest envelope may carry.
    /// </summary>
    public const int MaxIngestFrames = 32;

    /// <summary>
    /// The longest frame of an ingest envelope, in base64 characters.
    /// </summary>
    public const int MaxIngestFrameLength = 1024;

    private const int SignatureHexLength = 64;

    private const int MaxTimestampDigits = 19;

    /// <summary>
    /// The outcome of checking an <c>Idempotency-Key</c>.
    /// </summary>
    public enum KeyCheck
    {
        /// <summary>
        /// The key is usable.
        /// </summary>
        Valid,

        /// <summary>
        /// There is no key, or it is empty.
        /// </summary>
        Missing,

        /// <summary>
        /// The key is too long, repeated, or not printable ASCII.
        /// </summary>
        Invalid,
    }

    /// <summary>
    /// Checks the <c>Idempotency-Key</c> header values: exactly one, 1–200 printable ASCII characters.
    /// </summary>
    public static KeyCheck CheckIdempotencyKey(IReadOnlyList<string?> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Count == 0 || (values.Count == 1 && string.IsNullOrEmpty(values[0])))
        {
            return KeyCheck.Missing;
        }

        if (values.Count > 1 || values[0] is not { } key || key.Length > MaxIdempotencyKeyLength)
        {
            return KeyCheck.Invalid;
        }

        return key.All(character => character is >= ' ' and <= '~') ? KeyCheck.Valid : KeyCheck.Invalid;
    }

    /// <summary>
    /// Trims a Site name and returns it when it has 1–100 characters (Unicode code points, as the contract's
    /// <c>maxLength</c> counts them), no control characters and no
    /// unpaired surrogates (which the journal's <c>jsonb</c> cannot store), otherwise <see langword="null"/>.
    /// Characters outside the Basic Multilingual Plane, such as emoji, are allowed.
    /// </summary>
    public static string? NormalizeSiteName(string? name)
    {
        var trimmed = name?.Trim();

        // A code point takes at most two UTF-16 code units, so longer input is refused before any copying.
        return string.IsNullOrEmpty(trimmed)
            || trimmed.Length > MaxSiteNameLength * 2
            || trimmed.Any(char.IsControl)
            || System.Text.Unicode.Utf8.FromUtf16(trimmed, new byte[trimmed.Length * 3], out _, out _, replaceInvalidSequences: false)
                != System.Buffers.OperationStatus.Done
            || trimmed.EnumerateRunes().Count() > MaxSiteNameLength
                ? null
                : trimmed;
    }

    /// <summary>
    /// Trims a Lot name and returns it under the same rule as <see cref="NormalizeSiteName"/>, otherwise
    /// <see langword="null"/>. Lot names need not be unique.
    /// </summary>
    public static string? NormalizeLotName(string? name) => NormalizeSiteName(name);

    /// <summary>
    /// Parses a Device ID of the contract, exactly 16 lowercase hex digits, otherwise <see langword="null"/>.
    /// </summary>
    public static DeviceId? NormalizeDeviceId(string? deviceId)
    {
        if (deviceId is null)
        {
            return null;
        }

        try
        {
            return Coldframe.Crypto.DeviceId.Parse(deviceId);
        }
        catch (CryptoFailureException)
        {
            return null;
        }
    }

    /// <summary>
    /// Parses a <c>DeviceKind</c> of the contract, exactly <c>hub</c> or <c>node</c>, otherwise <see langword="null"/>.
    /// </summary>
    public static DeviceKind? NormalizeDeviceKind(string? kind) => kind switch
    {
        "hub" => DeviceKind.Hub,
        "node" => DeviceKind.Node,
        _ => null,
    };

    /// <summary>
    /// The contract's name of a <see cref="DeviceKind"/>: <c>hub</c> or <c>node</c>.
    /// </summary>
    public static string DeviceKindName(DeviceKind kind) => kind switch
    {
        DeviceKind.Hub => "hub",
        DeviceKind.Node => "node",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown Device kind."),
    };

    /// <summary>
    /// Parses a wall-clock time of the contract, exactly <c>HH:mm</c> from <c>00:00</c> to <c>23:59</c>, into
    /// minutes since midnight, otherwise <see langword="null"/>. <c>7:00</c> and <c>24:00</c> are not times.
    /// </summary>
    public static int? NormalizeTimeOfDay(string? time)
    {
        if (time is not { Length: 5 }
            || time[2] != ':'
            || !char.IsAsciiDigit(time[0])
            || !char.IsAsciiDigit(time[1])
            || !char.IsAsciiDigit(time[3])
            || !char.IsAsciiDigit(time[4]))
        {
            return null;
        }

        var hours = ((time[0] - '0') * 10) + (time[1] - '0');
        var minutes = ((time[3] - '0') * 10) + (time[4] - '0');

        return hours <= 23 && minutes <= 59 ? (hours * 60) + minutes : null;
    }

    /// <summary>
    /// Parses a contract <c>PushPlatform</c> (<c>apns</c>, <c>fcm</c>), otherwise <see langword="null"/>.
    /// </summary>
    public static PushPlatform? NormalizePushPlatform(string? platform) => platform switch
    {
        "apns" => PushPlatform.Apns,
        "fcm" => PushPlatform.Fcm,
        _ => null,
    };

    /// <summary>
    /// Parses a contract <c>ApnsEnvironment</c> (<c>production</c>, <c>sandbox</c>), otherwise <see langword="null"/>.
    /// </summary>
    public static ApnsEnvironment? NormalizeApnsEnvironment(string? environment) => environment switch
    {
        "production" => ApnsEnvironment.Production,
        "sandbox" => ApnsEnvironment.Sandbox,
        _ => null,
    };

    /// <summary>
    /// The contract's <c>HH:mm</c> of minutes since midnight.
    /// </summary>
    public static string TimeOfDayName(int minutes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minutes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minutes, NotificationWindow.LastMinute);

        return string.Create(CultureInfo.InvariantCulture, $"{minutes / 60:00}:{minutes % 60:00}");
    }

    /// <summary>
    /// Parses a Notification Window of the contract: <paramref name="from"/> and <paramref name="to"/> as
    /// <c>HH:mm</c>, <paramref name="to"/> omitted meaning 22:00, and <paramref name="from"/> before
    /// <paramref name="to"/>. Returns <see langword="null"/> for anything else.
    /// </summary>
    public static NotificationWindow? NormalizeNotificationWindow(string? from, string? to)
    {
        if (NormalizeTimeOfDay(from) is not { } fromMinutes)
        {
            return null;
        }

        int toMinutes;
        if (to is null)
        {
            toMinutes = NotificationWindow.DefaultToMinutes;
        }
        else if (NormalizeTimeOfDay(to) is { } parsed)
        {
            toMinutes = parsed;
        }
        else
        {
            return null;
        }

        var window = new NotificationWindow(fromMinutes, toMinutes);
        return window.IsValid ? window : null;
    }

    /// <summary>
    /// Parses a <c>ReminderCadence</c> of the contract, exactly <c>daily</c> or <c>every2Days</c>, otherwise
    /// <see langword="null"/>.
    /// </summary>
    public static ReminderCadence? NormalizeReminderCadence(string? cadence) => cadence switch
    {
        "daily" => ReminderCadence.Daily,
        "every2Days" => ReminderCadence.Every2Days,
        _ => null,
    };

    /// <summary>
    /// The contract's name of a <see cref="ReminderCadence"/>: <c>daily</c> or <c>every2Days</c>.
    /// </summary>
    public static string ReminderCadenceName(ReminderCadence cadence) => cadence switch
    {
        ReminderCadence.Daily => "daily",
        ReminderCadence.Every2Days => "every2Days",
        _ => throw new ArgumentOutOfRangeException(nameof(cadence), cadence, "Unknown Reminder cadence."),
    };

    /// <summary>
    /// Decodes base64url without padding that holds exactly <paramref name="length"/> bytes, otherwise
    /// <see langword="null"/>.
    /// </summary>
    public static byte[]? DecodeBase64Url(string? text, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);

        if (text is null
            || text.Length != Base64Url.GetEncodedLength(length)
            || !text.All(static character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
        {
            return null;
        }

        var bytes = new byte[length];
        return Base64Url.TryDecodeFromChars(text, bytes, out var written) && written == length ? bytes : null;
    }

    /// <summary>
    /// Parses the Device authentication headers (AD-12) of a request: exactly one each of
    /// <c>X-Coldframe-Device</c> (16 lowercase hex digits), <c>X-Coldframe-Timestamp</c> (1 to 19 digits that
    /// fit a <see cref="long"/>), <c>X-Coldframe-Nonce</c> (32 lowercase hex digits) and
    /// <c>X-Coldframe-Signature</c> (64 lowercase hex digits). Returns <see langword="null"/> for a missing or
    /// malformed one. Nothing is verified here.
    /// </summary>
    public static DeviceAuthentication? ParseDeviceHeaders(IHeaderDictionary headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        if (Single(headers, CryptoSpec.HeartbeatDeviceHeader) is not { } device
            || NormalizeDeviceId(device) is not { } deviceId
            || Single(headers, CryptoSpec.HeartbeatTimestampHeader) is not { } timestamp
            || timestamp.Length is 0 or > MaxTimestampDigits
            || !timestamp.All(char.IsAsciiDigit)
            || !long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var timestampMs)
            || LowercaseHex(Single(headers, CryptoSpec.HeartbeatNonceHeader), 2 * CryptoSpec.HeartbeatNonceLength) is not { } nonce
            || LowercaseHex(Single(headers, CryptoSpec.HeartbeatSignatureHeader), SignatureHexLength) is not { } signature)
        {
            return null;
        }

        return new DeviceAuthentication(deviceId, timestampMs, nonce, signature);
    }

    /// <summary>
    /// Parses a heartbeat body: a JSON object whose <c>protocolVersion</c> is the integer 1 and whose optional
    /// <c>uptimeMs</c> is a non-negative integer. Other properties are ignored (additive changes, AD-10).
    /// Returns <see langword="null"/> for anything else.
    /// </summary>
    public static HeartbeatBody? ParseHeartbeatBody(ReadOnlyMemory<byte> body)
    {
        try
        {
            // The whole body is one JSON value: trailing content is malformed.
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("protocolVersion", out var version)
                || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt64(out var major)
                || major != HeartbeatProtocolVersion)
            {
                return null;
            }

            if (!root.TryGetProperty("uptimeMs", out var uptime))
            {
                return new HeartbeatBody(null);
            }

            return uptime.ValueKind == JsonValueKind.Number && uptime.TryGetInt64(out var uptimeMs) && uptimeMs >= 0
                ? new HeartbeatBody(uptimeMs)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Parses an ingest envelope (AD-9): a JSON object whose <c>frames</c> is an array of at most
    /// <see cref="MaxIngestFrames"/> strings of at most <see cref="MaxIngestFrameLength"/> characters each.
    /// Other properties are ignored (additive changes, AD-10). Returns <see langword="null"/> for anything
    /// else. What a frame holds is not looked at here: a frame that is not base64 is that frame's failure.
    /// </summary>
    public static IReadOnlyList<string>? ParseIngestBody(ReadOnlyMemory<byte> body)
    {
        try
        {
            // The whole body is one JSON value: trailing content is malformed.
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("frames", out var frames)
                || frames.ValueKind != JsonValueKind.Array
                || frames.GetArrayLength() > MaxIngestFrames)
            {
                return null;
            }

            var parsed = new List<string>(frames.GetArrayLength());
            foreach (var frame in frames.EnumerateArray())
            {
                if (frame.ValueKind != JsonValueKind.String || frame.GetString() is not { Length: <= MaxIngestFrameLength } text)
                {
                    return null;
                }

                parsed.Add(text);
            }

            return parsed;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            // A string that is not valid text, such as a lone surrogate escape: no envelope either.
            return null;
        }
    }

    /// <summary>
    /// Decodes standard base64 with padding (RFC 4648 section 4) and nothing else: no whitespace, no
    /// base64url. Returns <see langword="null"/> for anything else.
    /// </summary>
    public static byte[]? DecodeBase64(string? text)
    {
        if (text is null || text.Length % 4 != 0 || text.Any(static character => !char.IsAsciiLetterOrDigit(character) && character is not ('+' or '/' or '=')))
        {
            return null;
        }

        var bytes = new byte[text.Length / 4 * 3];
        return Convert.TryFromBase64String(text, bytes, out var written) ? bytes[..written] : null;
    }

    /// <summary>
    /// The contract's name of a frame status (<c>IngestFrameStatus</c>), such as <c>rejected_auth</c>.
    /// </summary>
    public static string IngestStatusName(DeviceIngestStatus status) => status switch
    {
        DeviceIngestStatus.Stored => "stored",
        DeviceIngestStatus.Duplicate => "duplicate",
        DeviceIngestStatus.RejectedAuth => "rejected_auth",
        DeviceIngestStatus.RejectedReplay => "rejected_replay",
        DeviceIngestStatus.RejectedTime => "rejected_time",
        DeviceIngestStatus.UnknownDevice => "unknown_device",
        DeviceIngestStatus.Retry => "retry",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown frame status."),
    };

    private static string? Single(IHeaderDictionary headers, string name) =>
        headers.TryGetValue(name, out var values) && values.Count == 1 ? values[0] : null;

    private static byte[]? LowercaseHex(string? text, int length)
    {
        if (text is null || text.Length != length || !text.All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f'))
        {
            return null;
        }

        return Convert.FromHexString(text);
    }
}

/// <summary>
/// The Device authentication headers of a request, parsed but not verified.
/// </summary>
/// <param name="DeviceId">The Device ID the request claims.</param>
/// <param name="TimestampMs">The request time, Unix milliseconds.</param>
/// <param name="Nonce">The 16-byte nonce.</param>
/// <param name="Signature">The 32-byte signature.</param>
public sealed record DeviceAuthentication(DeviceId DeviceId, long TimestampMs, byte[] Nonce, byte[] Signature);

/// <summary>
/// A parsed heartbeat body.
/// </summary>
/// <param name="UptimeMs">The Device's uptime, when sent.</param>
public sealed record HeartbeatBody(long? UptimeMs);
