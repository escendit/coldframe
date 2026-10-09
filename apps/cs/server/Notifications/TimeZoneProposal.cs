using System.Net;
using Coldframe.Contracts.Sites;

namespace Coldframe.Server.Notifications;

/// <summary>
/// The time zone the Server proposes to a User who has chosen none (Story 6.3), and the rule for what counts
/// as a time zone. Detection only proposes: the first zone the Server knows wins, in the order the device or
/// browser zone, the detected zone the Server holds, the IP lookup, and then none, where the User picks one.
/// </summary>
public static class TimeZoneProposal
{
    /// <summary>
    /// Whether <paramref name="timeZone"/> is an IANA time zone ID this Server knows, of at most
    /// <see cref="UserNotificationLimits.MaxTimeZoneLength"/> characters and written as the Server's tz
    /// database writes it. A Windows ID (<c>W. Europe Standard Time</c>) is not one, and neither are the
    /// database's own files <c>posixrules</c> and <c>Factory</c>.
    /// </summary>
    public static bool IsKnown(string? timeZone)
    {
        if (timeZone is null
            || timeZone.Length is 0 or > UserNotificationLimits.MaxTimeZoneLength
            || !char.IsAsciiLetter(timeZone[0])
            || !timeZone.All(static character => char.IsAsciiLetterOrDigit(character) || character is '/' or '_' or '-' or '+')
            || timeZone.StartsWith("posix/", StringComparison.Ordinal)
            || timeZone.StartsWith("right/", StringComparison.Ordinal)
            || timeZone is "posixrules" or "Factory")
        {
            return false;
        }

        return TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var found)
            && found.HasIanaId
            && string.Equals(found.Id, timeZone, StringComparison.Ordinal);
    }

    /// <summary>
    /// Returns the zone to propose: <paramref name="deviceTimeZone"/>, else <paramref name="storedTimeZone"/>,
    /// else what <paramref name="lookup"/> finds for <paramref name="address"/>, else <see langword="null"/>.
    /// A candidate the Server does not know is passed over. The lookup is asked only when it is needed.
    /// </summary>
    /// <param name="deviceTimeZone">The zone the device or browser reports, if any.</param>
    /// <param name="storedTimeZone">The detected zone the Server holds for the User, if any.</param>
    /// <param name="lookup">The IP lookup.</param>
    /// <param name="address">The caller's address, if the request has one.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    public static async ValueTask<string?> ResolveAsync(
        string? deviceTimeZone,
        string? storedTimeZone,
        IIpTimeZoneLookup lookup,
        IPAddress? address,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lookup);

        if (IsKnown(deviceTimeZone))
        {
            return deviceTimeZone;
        }

        if (IsKnown(storedTimeZone))
        {
            return storedTimeZone;
        }

        var found = await lookup.FindAsync(address, cancellationToken).ConfigureAwait(false);
        return IsKnown(found) ? found : null;
    }
}
