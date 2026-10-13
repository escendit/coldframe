using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Events;

namespace Coldframe.Contracts.Alerts;

/// <summary>
/// The Alert opened (Story 6.1). The first event of every <c>alert/{id}</c> stream. It records everything the
/// Alert is about, so the lots projector and the Site grain need no other stream to place it.
/// </summary>
/// <param name="Kind">What the Alert is about.</param>
/// <param name="Side">The Threshold that was crossed; <see langword="null"/> for an Alert that is not a Threshold Alert.</param>
/// <param name="SiteId">The Site of the Sensor's Node, or of the Device, when the Alert opened.</param>
/// <param name="LotId">The Lot of the Node when the Alert opened; <see langword="null"/> for a Device's Alert without a Lot (Story 7.1).</param>
/// <param name="SensorId">The Sensor that opened the Alert, which alone closes it; <see langword="null"/> for a Device's Alert, which its Device closes.</param>
/// <param name="DeviceId">The Device ID of that Sensor's Node, or of the Device the Alert is about.</param>
/// <param name="Quantity">What the Sensor measures, such as <c>soil_moisture</c>; <see langword="null"/> for a Device's Alert.</param>
/// <param name="Episode">The episode of the opening grain the Alert ID is derived from.</param>
/// <param name="OpenedAt">When the opening grain journaled the episode.</param>
/// <param name="DeviceKind">
/// Whether a Device's Alert is about a Hub or a Node (Story 7.2), and <c>Node</c> for a Sensor's Health Alert
/// (Story 7.4); <see langword="null"/> for a Threshold Alert and in an event journaled before Story 7.2.
/// </param>
/// <param name="SilentSince">
/// For a silent Alert, when the Device was last heard, or when it was enrolled if it never reported (Story 7.2):
/// every displayed silence duration counts from here. <see langword="null"/> for another kind and in an event
/// journaled before that story.
/// </param>
/// <param name="BatteryPercent">
/// For a battery Alert, the battery level of the device report that opened it (Story 7.3). <see langword="null"/>
/// for another kind and in an event journaled before that story.
/// </param>
[EventType("alert.opened")]
[GenerateSerializer]
[Alias("coldframe.alert-opened")]
public sealed record AlertOpened(
    [property: Id(0)] AlertKind Kind,
    [property: Id(1)] ThresholdSide? Side,
    [property: Id(2)] string SiteId,
    [property: Id(3)] string? LotId,
    [property: Id(4)] Guid? SensorId,
    [property: Id(5)] string DeviceId,
    [property: Id(6)] string? Quantity,
    [property: Id(7)] int Episode,
    [property: Id(8)] DateTimeOffset OpenedAt,
    [property: Id(9)] DeviceKind? DeviceKind = null,
    [property: Id(10)] DateTimeOffset? SilentSince = null,
    [property: Id(11)] int? BatteryPercent = null);

/// <summary>
/// The Alert closed (Story 6.1). Final: a closed Alert never opens again, the Sensor's next episode is another
/// Alert.
/// </summary>
/// <param name="Reason">Why the Alert closed.</param>
/// <param name="ClosedAt">When the opening grain journaled the close.</param>
[EventType("alert.closed")]
[GenerateSerializer]
[Alias("coldframe.alert-closed")]
public sealed record AlertClosed(
    [property: Id(0)] AlertCloseReason Reason,
    [property: Id(1)] DateTimeOffset ClosedAt);

/// <summary>
/// The Site grain acknowledged that the Alert opened or closed: the Alert grain stops reporting it. Until this
/// event is journaled, the Alert grain reports again on a timer, by a reminder and on activation.
/// </summary>
/// <param name="Change">
/// What the Site now knows: <see cref="AlertLifecycle.Open"/> that the Alert opened,
/// <see cref="AlertLifecycle.Closed"/> that it closed.
/// </param>
/// <param name="NotifiedAt">When the Site acknowledged it.</param>
[EventType("alert.site-notified")]
[GenerateSerializer]
[Alias("coldframe.alert-site-notified")]
public sealed record AlertSiteNotified(
    [property: Id(0)] AlertLifecycle Change,
    [property: Id(1)] DateTimeOffset NotifiedAt);
