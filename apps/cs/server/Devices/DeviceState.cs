using Coldframe.Contracts.Devices;

namespace Coldframe.Server.Devices;

/// <summary>
/// The state of the Device grain: its Site, kind and wrapped <c>K_dev</c>, once enrolled.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.device-state")]
public sealed class DeviceState
{
    /// <summary>
    /// The Site the Device is enrolled on, or <see langword="null"/> before enrolment.
    /// </summary>
    [Id(0)]
    public string? SiteId { get; private set; }

    /// <summary>
    /// Hub or Node, once enrolled.
    /// </summary>
    [Id(1)]
    public DeviceKind Kind { get; private set; }

    /// <summary>
    /// The wrapped <c>K_dev</c>, once enrolled.
    /// </summary>
    [Id(2)]
    public WrappedDeviceKey? WrappedKey { get; private set; }

    /// <summary>
    /// When the Device was enrolled.
    /// </summary>
    [Id(3)]
    public DateTimeOffset? EnrolledAt { get; private set; }

    public void Apply(DeviceEnrolled @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        SiteId = @event.SiteId;
        Kind = @event.Kind;
        WrappedKey = @event.WrappedKey;
        EnrolledAt = @event.EnrolledAt;
    }
}
