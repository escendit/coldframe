using System.Buffers.Text;
using Coldframe.Contracts.Devices;
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
}
