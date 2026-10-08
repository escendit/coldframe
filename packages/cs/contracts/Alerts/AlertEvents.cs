using Coldframe.Contracts.Events;

namespace Coldframe.Contracts.Alerts;

/// <summary>
/// The Alert opened (Story 6.1). The first event of every <c>alert/{id}</c> stream. It records everything the
/// Alert is about, so the lots projector and the Site grain need no other stream to place it.
/// </summary>
/// <param name="Kind">What the Alert is about.</param>
/// <param name="Side">The Threshold that was crossed; <see langword="null"/> for an Alert that is not a Threshold Alert.</param>
/// <param name="SiteId">The Site of the Sensor's Node when the Alert opened.</param>
/// <param name="LotId">The Lot of the Sensor's Node when the Alert opened.</param>
/// <param name="SensorId">The Sensor that opened the Alert; only it closes the Alert.</param>
/// <param name="DeviceId">The Device ID of that Sensor's Node.</param>
/// <param name="Quantity">What the Sensor measures, such as <c>soil_moisture</c>.</param>
/// <param name="Episode">The Sensor's episode the Alert ID is derived from.</param>
/// <param name="OpenedAt">When the Sensor journaled the episode.</param>
[EventType("alert.opened")]
[GenerateSerializer]
[Alias("coldframe.alert-opened")]
public sealed record AlertOpened(
    [property: Id(0)] AlertKind Kind,
    [property: Id(1)] ThresholdSide? Side,
    [property: Id(2)] string SiteId,
    [property: Id(3)] string LotId,
    [property: Id(4)] Guid SensorId,
    [property: Id(5)] string DeviceId,
    [property: Id(6)] string Quantity,
    [property: Id(7)] int Episode,
    [property: Id(8)] DateTimeOffset OpenedAt);

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
