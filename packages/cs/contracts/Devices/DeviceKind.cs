namespace Coldframe.Contracts.Devices;

/// <summary>
/// What a Device is. The contract's <c>DeviceKind</c> is its lowercase name (<c>hub</c>, <c>node</c>).
/// </summary>
[GenerateSerializer]
[Alias("coldframe.device-kind")]
public enum DeviceKind
{
    /// <summary>
    /// A Hub: mains powered, relays Node frames to the Server.
    /// </summary>
    Hub = 0,

    /// <summary>
    /// A Node: carries the Sensors of a Lot.
    /// </summary>
    Node = 1,
}
