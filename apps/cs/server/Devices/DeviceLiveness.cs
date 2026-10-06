namespace Coldframe.Server.Devices;

/// <summary>
/// Whether a Device counts as online. The Server decides it when it answers, from the last accepted
/// heartbeat and its own clock; it is never stored, and clients never compute it.
/// </summary>
public static class DeviceLiveness
{
    /// <summary>
    /// How long after its last heartbeat a Hub still counts as online: two missed heartbeats at the slowest
    /// interval of 60 s, so one late beat does not flip it.
    /// </summary>
    public static readonly TimeSpan HubOnlineWindow = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Returns whether a Device last seen at <paramref name="lastSeenAt"/> is online at <paramref name="now"/>:
    /// it was seen, and at most <see cref="HubOnlineWindow"/> ago. A Device never seen is offline.
    /// </summary>
    /// <param name="lastSeenAt">When the Server accepted the last heartbeat, or <see langword="null"/>.</param>
    /// <param name="now">The Server clock, from the injected <see cref="TimeProvider"/>.</param>
    public static bool IsOnline(DateTimeOffset? lastSeenAt, DateTimeOffset now) =>
        lastSeenAt is { } seen && now - seen <= HubOnlineWindow;
}
