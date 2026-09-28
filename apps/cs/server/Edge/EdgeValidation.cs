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
}
