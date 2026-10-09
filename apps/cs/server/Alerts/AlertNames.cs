using Coldframe.Contracts.Alerts;

namespace Coldframe.Server.Alerts;

/// <summary>
/// The contract's names of the Alert enums (Story 6.2): lowercase strings, mapped by hand, so renaming an enum
/// member never changes a stored row or a response. The alerts read model stores these names.
/// </summary>
public static class AlertNames
{
    /// <summary>
    /// The contract's <c>AlertKind</c>. Only <c>threshold</c> is produced; a kind added to
    /// <see cref="AlertKind"/> must be named here before any grain journals it.
    /// </summary>
    /// <param name="kind">What the Alert is about.</param>
    public static string Kind(AlertKind kind) => kind switch
    {
        AlertKind.Threshold => "threshold",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The Alert kind has no contract name."),
    };

    /// <summary>
    /// The contract's <c>AlertSide</c>, or <see langword="null"/> for an Alert without a side.
    /// </summary>
    /// <param name="side">The Threshold that was crossed.</param>
    public static string? Side(ThresholdSide? side) => side switch
    {
        null => null,
        ThresholdSide.Low => "low",
        ThresholdSide.High => "high",
        _ => throw new ArgumentOutOfRangeException(nameof(side), side, "The Threshold side has no contract name."),
    };

    /// <summary>
    /// The contract's <c>AlertCloseReason</c>.
    /// </summary>
    /// <param name="reason">Why the Alert closed.</param>
    public static string Reason(AlertCloseReason reason) => reason switch
    {
        AlertCloseReason.Recovered => "recovered",
        AlertCloseReason.Paused => "paused",
        AlertCloseReason.Unassigned => "unassigned",
        AlertCloseReason.Calibrated => "calibrated",
        AlertCloseReason.Removed => "removed",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "The close reason has no contract name."),
    };
}
