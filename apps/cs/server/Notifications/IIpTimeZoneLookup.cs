using System.Net;

namespace Coldframe.Server.Notifications;

/// <summary>
/// Looks a caller's time zone up by IP address: the last resort of time-zone detection (Story 6.3), behind
/// the zone a device or browser reports and the one the Server already holds. It only ever proposes a zone.
/// </summary>
/// <remarks>
/// The address it is handed is the remote address of the connection to the Server. For the web app that is
/// the SvelteKit BFF, and behind a reverse proxy it is the proxy, not the User's device: the Server reads no
/// forwarding header. An implementation must not take the address for the User's own unless the deployment
/// guarantees it.
/// </remarks>
public interface IIpTimeZoneLookup
{
    /// <summary>
    /// Returns the IANA time zone of <paramref name="address"/>, or <see langword="null"/> when it is unknown.
    /// </summary>
    /// <param name="address">The caller's address, if the request has one.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    ValueTask<string?> FindAsync(IPAddress? address, CancellationToken cancellationToken = default);
}

/// <summary>
/// The lookup the Server ships with: it knows no zone. Coldframe runs on a home network, where an address
/// says nothing, and it bundles no GeoIP database and calls no geolocation service.
/// </summary>
public sealed class NoIpTimeZoneLookup : IIpTimeZoneLookup
{
    /// <inheritdoc />
    public ValueTask<string?> FindAsync(IPAddress? address, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<string?>(null);
}
