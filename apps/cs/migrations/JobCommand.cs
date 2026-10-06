using System.Globalization;

namespace Coldframe.Migrations;

/// <summary>
/// What one run of the migration job does.
/// </summary>
public enum JobCommandKind
{
    /// <summary>
    /// No argument: apply the pending migrations, then ensure the partitions.
    /// </summary>
    Migrate,

    /// <summary>
    /// <c>partitions</c>: ensure the monthly partitions only.
    /// </summary>
    Partitions,

    /// <summary>
    /// <c>advance-replay</c>: after a restore, advance every Device's replay window and downlink counter.
    /// </summary>
    AdvanceReplay,
}

/// <summary>
/// The command line of the migration job:
/// <c>[partitions [--months-ahead N] | advance-replay [--uplink-margin N] [--downlink-margin N]]</c>.
/// </summary>
/// <param name="Kind">What to do.</param>
/// <param name="MonthsAhead">The months after the current one that get a partition.</param>
/// <param name="UplinkMargin">How far <c>advance-replay</c> moves every uplink high-water mark.</param>
/// <param name="DownlinkMargin">How far <c>advance-replay</c> moves every downlink counter.</param>
public sealed record JobCommand(
    JobCommandKind Kind,
    int MonthsAhead = ReadingsMaintenance.DefaultMonthsAhead,
    ulong UplinkMargin = ReadingsMaintenance.DefaultUplinkMargin,
    ulong DownlinkMargin = ReadingsMaintenance.DefaultDownlinkMargin)
{
    /// <summary>
    /// How to call the job.
    /// </summary>
    public const string Usage =
        """
        Usage: Coldframe.Migrations [command]
          (no command)                                              apply pending migrations, then ensure partitions
          partitions [--months-ahead N]                             ensure this month's partitions and N more (default 3, minimum 2)
          advance-replay [--uplink-margin N] [--downlink-margin N]  after a restore, with the apps stopped (defaults 64 and 1048576;
                                                                    each margin from 1 to 4294967296)
        """;

    /// <summary>
    /// Parses the arguments. Returns <see langword="null"/> and an <paramref name="error"/> for an unknown
    /// command or option, a repeated option, a missing or malformed value, or a value out of range.
    /// </summary>
    public static JobCommand? Parse(IReadOnlyList<string> arguments, out string? error)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        error = null;

        if (arguments.Count == 0)
        {
            return new JobCommand(JobCommandKind.Migrate);
        }

        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < arguments.Count; index += 2)
        {
            if (index + 1 >= arguments.Count)
            {
                error = $"'{arguments[index]}' needs a value.";
                return null;
            }

            if (!options.TryAdd(arguments[index], arguments[index + 1]))
            {
                error = $"'{arguments[index]}' is given twice.";
                return null;
            }
        }

        switch (arguments[0])
        {
            case "partitions":
                var months = ReadingsMaintenance.DefaultMonthsAhead;
                if (!Take(options, "--months-ahead", ref months, ParseMonths, ref error) || !NoneLeft(options, ref error))
                {
                    return null;
                }

                return new JobCommand(JobCommandKind.Partitions, months);

            case "advance-replay":
                var uplink = ReadingsMaintenance.DefaultUplinkMargin;
                var downlink = ReadingsMaintenance.DefaultDownlinkMargin;
                if (!Take(options, "--uplink-margin", ref uplink, ParseMargin, ref error)
                    || !Take(options, "--downlink-margin", ref downlink, ParseMargin, ref error)
                    || !NoneLeft(options, ref error))
                {
                    return null;
                }

                return new JobCommand(JobCommandKind.AdvanceReplay, UplinkMargin: uplink, DownlinkMargin: downlink);

            default:
                error = $"Unknown command '{arguments[0]}'.";
                return null;
        }
    }

    private static bool Take<T>(Dictionary<string, string> options, string name, ref T value, Func<string, T?> parse, ref string? error)
        where T : struct
    {
        if (!options.Remove(name, out var text))
        {
            return true;
        }

        if (parse(text) is not { } parsed)
        {
            error = $"'{text}' is not a valid value for {name}.";
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool NoneLeft(Dictionary<string, string> options, ref string? error)
    {
        if (options.Count == 0)
        {
            return true;
        }

        error = $"Unknown option '{options.Keys.Order(StringComparer.Ordinal).First()}'.";
        return false;
    }

    private static int? ParseMonths(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var months)
        && months is >= ReadingsMaintenance.MinMonthsAhead and <= ReadingsMaintenance.MaxMonthsAhead
            ? months
            : null;

    private static ulong? ParseMargin(string text) =>
        ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var margin)
        && margin is > 0 and <= ReadingsMaintenance.MaxMargin
            ? margin
            : null;
}
