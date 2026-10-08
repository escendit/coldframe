namespace Coldframe.Contracts.Alerts;

/// <summary>
/// An Alert, keyed by its Alert ID in the lowercase hyphenated form (Story 6.1). The ID of a Threshold Alert is
/// the UUIDv5 of <c>sensor:{sensorId}:threshold:{episode}</c> in a Server-only namespace, so the grain that
/// evaluates journals the episode first and a retried <see cref="Open"/> can never create a second Alert. The
/// Alert grain is the only writer of the Alert's state and reports every open and close to its Site grain,
/// again until the Site acknowledged it.
/// </summary>
[Alias("coldframe.alert")]
public interface IAlertGrain : IGrainWithStringKey
{
    /// <summary>
    /// Opens the Alert. Idempotent: the first call journals <see cref="AlertOpened"/>, a later one journals
    /// nothing, also once the Alert is closed. Each call reports to the Site grain what it does not know yet.
    /// A request that does not derive this grain's Alert ID is refused and journals nothing.
    /// </summary>
    /// <param name="request">The Sensor and episode the Alert is for, and what it records.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("open")]
    Task<AlertResult> Open(OpenAlert request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes the Alert. Accepted only from the Sensor grain that opened it; any other caller is refused and the
    /// Alert stays open. Idempotent: the first call journals <see cref="AlertClosed"/>, a later one journals
    /// nothing. Each call reports to the Site grain what it does not know yet.
    /// </summary>
    /// <param name="request">Why and when the Alert closes.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("close")]
    Task<AlertResult> Close(CloseAlert request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the Alert as the grain holds it, or <see langword="null"/> when it was never opened.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("describe")]
    Task<AlertSnapshot?> Describe(CancellationToken cancellationToken = default);
}

/// <summary>
/// What an Alert is about.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.alert-kind")]
public enum AlertKind
{
    /// <summary>
    /// A Sensor stayed beyond one of its Thresholds.
    /// </summary>
    Threshold = 0,
}

/// <summary>
/// The Threshold a Threshold Alert is about.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.threshold-side")]
public enum ThresholdSide
{
    /// <summary>
    /// Below the low Threshold; for soil moisture, the Lot needs water.
    /// </summary>
    Low = 0,

    /// <summary>
    /// Above the high Threshold.
    /// </summary>
    High = 1,
}

/// <summary>
/// Why an Alert closed.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.alert-close-reason")]
public enum AlertCloseReason
{
    /// <summary>
    /// The Sensor is back within its Thresholds.
    /// </summary>
    Recovered = 0,

    /// <summary>
    /// The Device was paused.
    /// </summary>
    Paused = 1,

    /// <summary>
    /// The Node was unassigned from its Lot.
    /// </summary>
    Unassigned = 2,

    /// <summary>
    /// The Sensor was calibrated.
    /// </summary>
    Calibrated = 3,

    /// <summary>
    /// The Sensor, its Node or its Lot was removed.
    /// </summary>
    Removed = 4,
}

/// <summary>
/// Where an Alert is in its lifecycle.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.alert-lifecycle")]
public enum AlertLifecycle
{
    /// <summary>
    /// The Alert was never opened.
    /// </summary>
    None = 0,

    /// <summary>
    /// The Alert is open.
    /// </summary>
    Open = 1,

    /// <summary>
    /// The Alert is closed; that is final.
    /// </summary>
    Closed = 2,
}

/// <summary>
/// A Threshold Alert to open, as the Sensor grain hands it to the Alert grain.
/// </summary>
/// <param name="SensorId">The Sensor that evaluated; only it closes the Alert.</param>
/// <param name="Episode">The Sensor's episode; with the Sensor ID it derives the Alert ID.</param>
/// <param name="Side">The Threshold that was crossed.</param>
/// <param name="SiteId">The Site of the Sensor's Node when the Alert opened.</param>
/// <param name="LotId">The Lot of the Sensor's Node when the Alert opened.</param>
/// <param name="DeviceId">The Device ID of the Sensor's Node.</param>
/// <param name="Quantity">What the Sensor measures, such as <c>soil_moisture</c>.</param>
/// <param name="OpenedAt">When the Sensor journaled the episode.</param>
[GenerateSerializer]
[Alias("coldframe.open-alert")]
public sealed record OpenAlert(
    [property: Id(0)] Guid SensorId,
    [property: Id(1)] int Episode,
    [property: Id(2)] ThresholdSide Side,
    [property: Id(3)] string SiteId,
    [property: Id(4)] string LotId,
    [property: Id(5)] string DeviceId,
    [property: Id(6)] string Quantity,
    [property: Id(7)] DateTimeOffset OpenedAt);

/// <summary>
/// The close of an Alert, as the grain that opened it hands it to the Alert grain.
/// </summary>
/// <param name="Reason">Why the Alert closes.</param>
/// <param name="ClosedAt">When the opening grain journaled the close.</param>
[GenerateSerializer]
[Alias("coldframe.close-alert")]
public sealed record CloseAlert(
    [property: Id(0)] AlertCloseReason Reason,
    [property: Id(1)] DateTimeOffset ClosedAt);

/// <summary>
/// How a call to the Alert grain ended.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.alert-outcome")]
public enum AlertOutcome
{
    /// <summary>
    /// The Alert is open, by this call or an earlier one.
    /// </summary>
    Open = 0,

    /// <summary>
    /// The Alert is closed, by this call or an earlier one.
    /// </summary>
    Closed = 1,

    /// <summary>
    /// The Alert was never opened, so there is nothing to close. Nothing was journaled.
    /// </summary>
    NotOpened = 2,

    /// <summary>
    /// The request does not derive this Alert's ID, or the caller is not the Sensor grain that opened the Alert.
    /// Nothing was journaled.
    /// </summary>
    Refused = 3,
}

/// <summary>
/// The result of <see cref="IAlertGrain.Open"/> and <see cref="IAlertGrain.Close"/>.
/// </summary>
/// <param name="Outcome">How the call ended.</param>
/// <param name="Reported">
/// Whether the Site grain acknowledged everything the Alert has to report. While it is <see langword="false"/>
/// the Alert grain reports again by itself; the caller repeats its call until it is <see langword="true"/>.
/// </param>
[GenerateSerializer]
[Alias("coldframe.alert-result")]
public sealed record AlertResult(
    [property: Id(0)] AlertOutcome Outcome,
    [property: Id(1)] bool Reported = false);

/// <summary>
/// An Alert as the grain holds it.
/// </summary>
/// <param name="Id">The Alert ID.</param>
/// <param name="Kind">What the Alert is about.</param>
/// <param name="Lifecycle">Open or closed.</param>
/// <param name="Side">The Threshold that was crossed; <see langword="null"/> for an Alert that is not a Threshold Alert.</param>
/// <param name="SiteId">The Site the Alert was opened on.</param>
/// <param name="LotId">The Lot the Alert was opened for.</param>
/// <param name="SensorId">The Sensor that opened the Alert.</param>
/// <param name="DeviceId">The Device ID of that Sensor's Node.</param>
/// <param name="Quantity">What the Sensor measures.</param>
/// <param name="OpenedAt">When the Alert opened.</param>
/// <param name="Reason">Why the Alert closed, once it is.</param>
/// <param name="ClosedAt">When the Alert closed, once it is.</param>
[GenerateSerializer]
[Alias("coldframe.alert-snapshot")]
public sealed record AlertSnapshot(
    [property: Id(0)] Guid Id,
    [property: Id(1)] AlertKind Kind,
    [property: Id(2)] AlertLifecycle Lifecycle,
    [property: Id(3)] ThresholdSide? Side,
    [property: Id(4)] string SiteId,
    [property: Id(5)] string LotId,
    [property: Id(6)] Guid SensorId,
    [property: Id(7)] string DeviceId,
    [property: Id(8)] string Quantity,
    [property: Id(9)] DateTimeOffset OpenedAt,
    [property: Id(10)] AlertCloseReason? Reason = null,
    [property: Id(11)] DateTimeOffset? ClosedAt = null);
