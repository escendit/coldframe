using Coldframe.Contracts.Alerts;

namespace Coldframe.Server.Alerts;

/// <summary>
/// The state of the Alert grain (Story 6.1): what the Alert is about, whether it is open or closed, and what its
/// Site grain acknowledged.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.alert-state")]
public sealed class AlertState
{
    /// <summary>
    /// Where the Alert is in its lifecycle.
    /// </summary>
    [Id(0)]
    public AlertLifecycle Lifecycle { get; private set; }

    /// <summary>
    /// What the Alert is about.
    /// </summary>
    [Id(1)]
    public AlertKind Kind { get; private set; }

    /// <summary>
    /// The Threshold that was crossed; <see langword="null"/> for an Alert that is not a Threshold Alert.
    /// </summary>
    [Id(2)]
    public ThresholdSide? Side { get; private set; }

    /// <summary>
    /// The Site the Alert was opened on, once opened.
    /// </summary>
    [Id(3)]
    public string? SiteId { get; private set; }

    /// <summary>
    /// The Lot the Alert was opened for, once opened.
    /// </summary>
    [Id(4)]
    public string? LotId { get; private set; }

    /// <summary>
    /// The Sensor that opened the Alert; only it closes the Alert.
    /// </summary>
    [Id(5)]
    public Guid SensorId { get; private set; }

    /// <summary>
    /// The Device ID of that Sensor's Node, once opened.
    /// </summary>
    [Id(6)]
    public string? DeviceId { get; private set; }

    /// <summary>
    /// What the Sensor measures, once opened.
    /// </summary>
    [Id(7)]
    public string? Quantity { get; private set; }

    /// <summary>
    /// When the Alert opened.
    /// </summary>
    [Id(8)]
    public DateTimeOffset OpenedAt { get; private set; }

    /// <summary>
    /// Why the Alert closed, once it is.
    /// </summary>
    [Id(9)]
    public AlertCloseReason? Reason { get; private set; }

    /// <summary>
    /// When the Alert closed, once it is.
    /// </summary>
    [Id(10)]
    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>
    /// Whether the Site grain acknowledged that the Alert opened.
    /// </summary>
    [Id(11)]
    public bool OpenReported { get; private set; }

    /// <summary>
    /// Whether the Site grain acknowledged that the Alert closed.
    /// </summary>
    [Id(12)]
    public bool CloseReported { get; private set; }

    /// <summary>
    /// Whether the Site grain still has to be told that the Alert opened or that it closed.
    /// </summary>
    public bool ReportPending => Lifecycle switch
    {
        AlertLifecycle.Open => !OpenReported,
        AlertLifecycle.Closed => !OpenReported || !CloseReported,
        _ => false,
    };

    public void Apply(AlertOpened @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        // Opened once: the first event of the stream is what the Alert is about.
        if (Lifecycle != AlertLifecycle.None)
        {
            return;
        }

        Lifecycle = AlertLifecycle.Open;
        Kind = @event.Kind;
        Side = @event.Side;
        SiteId = @event.SiteId;
        LotId = @event.LotId;
        SensorId = @event.SensorId;
        DeviceId = @event.DeviceId;
        Quantity = @event.Quantity;
        OpenedAt = @event.OpenedAt;
    }

    public void Apply(AlertClosed @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        if (Lifecycle != AlertLifecycle.Open)
        {
            return;
        }

        Lifecycle = AlertLifecycle.Closed;
        Reason = @event.Reason;
        ClosedAt = @event.ClosedAt;
    }

    public void Apply(AlertSiteNotified @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        if (@event.Change == AlertLifecycle.Open)
        {
            OpenReported = true;
        }
        else if (@event.Change == AlertLifecycle.Closed)
        {
            CloseReported = true;
        }
    }
}
