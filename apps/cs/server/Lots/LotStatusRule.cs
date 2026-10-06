namespace Coldframe.Server.Lots;

/// <summary>
/// The Pause sources of a Lot's Node (AD-8), as the LotStatus rule takes them.
/// </summary>
[Flags]
public enum LotPauseSources
{
    /// <summary>
    /// The Node is not paused.
    /// </summary>
    None = 0,

    /// <summary>
    /// The Node itself is paused.
    /// </summary>
    Device = 1,

    /// <summary>
    /// The Node's Site is paused.
    /// </summary>
    Site = 2,
}

/// <summary>
/// What is silent on a Lot, as the LotStatus rule takes it.
/// </summary>
public enum LotSilence
{
    /// <summary>
    /// Nothing is silent.
    /// </summary>
    None = 0,

    /// <summary>
    /// The Lot's Node is silent.
    /// </summary>
    Node = 1,

    /// <summary>
    /// The Hub that relays the Lot's Node is silent.
    /// </summary>
    Hub = 2,
}

/// <summary>
/// The five inputs of the LotStatus rule (AD-14).
/// </summary>
/// <param name="HasNode">Whether a Node occupies the Lot.</param>
/// <param name="PausedBy">The Pause sources of that Node.</param>
/// <param name="Silence">What is silent: the Node, its relay Hub, or nothing.</param>
/// <param name="UncalibratedSoilSensor">Whether the Node has a <c>calibration: true</c> soil-moisture Sensor without Calibration.</param>
/// <param name="OpenLowAlert">Whether a low-side Threshold Alert is open on a soil-moisture Sensor of the Node.</param>
public readonly record struct LotStatusInputs(
    bool HasNode,
    LotPauseSources PausedBy,
    LotSilence Silence,
    bool UncalibratedSoilSensor,
    bool OpenLowAlert);

/// <summary>
/// What the LotStatus rule decides for a Lot.
/// </summary>
/// <param name="Status">Exactly one <c>LotStatus</c> of the contract.</param>
/// <param name="UnknownCause"><c>node</c> or <c>hub</c> when the status is <c>unknown</c>; otherwise <see langword="null"/>.</param>
/// <param name="PausedBy"><c>device</c> and/or <c>site</c>, in that order, when the status is <c>paused</c>; otherwise empty.</param>
public sealed record LotStatusResult(string Status, string? UnknownCause, IReadOnlyList<string> PausedBy);

/// <summary>
/// The one place that decides a Lot's status (AD-14). Pure: the lots projector feeds it and stores what it
/// returns, and nothing else computes a status.
/// </summary>
public static class LotStatusRule
{
    /// <summary>
    /// The status of a Lot without a Node.
    /// </summary>
    public const string NoNode = "noNode";

    /// <summary>
    /// The status of a Lot whose Node is paused.
    /// </summary>
    public const string Paused = "paused";

    /// <summary>
    /// The status of a Lot that cannot be read.
    /// </summary>
    public const string Unknown = "unknown";

    /// <summary>
    /// The status of a Lot whose soil-moisture Sensor has no Calibration.
    /// </summary>
    public const string NeedsCalibration = "needsCalibration";

    /// <summary>
    /// The status of a Lot with an open low-side Threshold Alert.
    /// </summary>
    public const string NeedsWater = "needsWater";

    /// <summary>
    /// The status of a Lot nothing is wrong with.
    /// </summary>
    public const string Ok = "ok";

    private static readonly IReadOnlyList<string> NoSources = [];

    /// <summary>
    /// Returns the status of a Lot by the precedence <c>noNode &gt; paused &gt; unknown &gt; needsCalibration
    /// &gt; needsWater &gt; ok</c>, with the cause only for <c>unknown</c> and the sources only for <c>paused</c>.
    /// </summary>
    public static LotStatusResult Evaluate(LotStatusInputs inputs)
    {
        if (!inputs.HasNode)
        {
            return new LotStatusResult(NoNode, null, NoSources);
        }

        if (inputs.PausedBy != LotPauseSources.None)
        {
            return new LotStatusResult(Paused, null, SourceNames(inputs.PausedBy));
        }

        return inputs.Silence switch
        {
            LotSilence.Node => new LotStatusResult(Unknown, "node", NoSources),
            LotSilence.Hub => new LotStatusResult(Unknown, "hub", NoSources),
            _ when inputs.UncalibratedSoilSensor => new LotStatusResult(NeedsCalibration, null, NoSources),
            _ when inputs.OpenLowAlert => new LotStatusResult(NeedsWater, null, NoSources),
            _ => new LotStatusResult(Ok, null, NoSources),
        };
    }

    /// <summary>
    /// Returns when a Pause ends: the latest end of its sources, or <see langword="null"/> when there is no
    /// source or any source has no end.
    /// </summary>
    /// <param name="ends">The end of each Pause source; <see langword="null"/> for a source without an end.</param>
    public static DateTimeOffset? PausedUntil(IReadOnlyCollection<DateTimeOffset?> ends)
    {
        ArgumentNullException.ThrowIfNull(ends);

        if (ends.Count == 0 || ends.Any(end => end is null))
        {
            return null;
        }

        return ends.Max();
    }

    private static List<string> SourceNames(LotPauseSources sources)
    {
        var names = new List<string>(2);

        if (sources.HasFlag(LotPauseSources.Device))
        {
            names.Add("device");
        }

        if (sources.HasFlag(LotPauseSources.Site))
        {
            names.Add("site");
        }

        return names;
    }
}
