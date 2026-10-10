using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Notifications;
using Coldframe.Contracts.Sites;
using Coldframe.Server.Edge;

namespace Coldframe.Server.Notifications.Push;

/// <summary>
/// A stored value in the unit clients show, as a text needs it.
/// </summary>
/// <param name="Value">The value in <paramref name="Unit"/>.</param>
/// <param name="Unit">The display unit: <c>%</c>, <c>°C</c>, <c>kΩ</c> or <c>raw</c>.</param>
public sealed record PushValue(double Value, string Unit);

/// <summary>
/// The newest Reading of an Alert's Sensor.
/// </summary>
/// <param name="Value">The Reading in display units; soil moisture under a Calibration is the percentage rounded to 5.</param>
/// <param name="MeasuredAt">When the Reading was taken.</param>
public sealed record PushReading(PushValue Value, DateTimeOffset MeasuredAt);

/// <summary>
/// What the Server found out about one Alert of a notification. Anything it did not find is <see langword="null"/>
/// and is left out of the text.
/// </summary>
/// <param name="Entry">The Alert as the User grain holds it.</param>
/// <param name="LotName">The name of the Alert's Lot; <see langword="null"/> when it is gone or was not found in time.</param>
/// <param name="Reading">The newest Reading of the Alert's Sensor.</param>
/// <param name="Threshold">The Threshold of the side that was crossed.</param>
/// <param name="LotGone">
/// Whether the Lot was looked up and does not exist any more. A lookup that failed or ran out of time is no gone
/// Lot: its Alert keeps its place under the name of an unknown Lot.
/// </param>
public sealed record PushEntryFacts(
    NotificationEntry Entry,
    string? LotName,
    PushReading? Reading = null,
    PushValue? Threshold = null,
    bool LotGone = false);

/// <summary>
/// The text of a push (Story 6.5, UX-DR116, UX-DR118, UX-DR119), written on the Server from the English
/// templates in <c>PushText.en.json</c>. It is self-contained: the title is the condition, the body is value and
/// context. Pure: the facts are looked up elsewhere.
/// </summary>
public sealed class PushText
{
    /// <summary>
    /// How many Alerts a summary lists before it says how many more there are: a push payload is 4 KiB.
    /// </summary>
    public const int MaxSummaryLines = 12;

    private const string SoilMoisture = "soil_moisture";

    private readonly FrozenDictionary<string, string> _templates;

    private PushText(FrozenDictionary<string, string> templates) => _templates = templates;

    /// <summary>
    /// The English text.
    /// </summary>
    public static PushText English { get; } = Load();

    /// <summary>
    /// Whether <paramref name="entry"/> reads "needs water": a low-side Threshold Alert on soil moisture.
    /// </summary>
    public static bool NeedsWater(NotificationEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return entry is { Kind: AlertKind.Threshold, Side: ThresholdSide.Low }
            && string.Equals(entry.Quantity, SoilMoisture, StringComparison.Ordinal);
    }

    /// <summary>
    /// The title of an Alert: the condition, such as "Tomatoes needs water".
    /// </summary>
    public string Title(PushEntryFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var entry = facts.Entry;
        var lot = facts.LotName ?? _templates["lot.unknown"];

        if (entry is not { Kind: AlertKind.Threshold, Side: { } side })
        {
            return Format("title.other", ("lot", lot));
        }

        if (string.Equals(entry.Quantity, SoilMoisture, StringComparison.Ordinal))
        {
            return Format(side == ThresholdSide.Low ? "title.needsWater" : "title.tooWet", ("lot", lot));
        }

        var quantity = _templates.GetValueOrDefault($"quantity.{entry.Quantity}") ?? _templates["quantity.other"];

        return facts.Threshold is { } threshold
            ? Format(
                side == ThresholdSide.Low ? "title.below" : "title.above",
                ("lot", lot),
                ("quantity", quantity),
                ("threshold", Number(threshold)),
                ("unit", threshold.Unit))
            : Format(side == ThresholdSide.Low ? "title.tooLow" : "title.tooHigh", ("lot", lot), ("quantity", quantity));
    }

    /// <summary>
    /// The body of an Alert or of its Reminder: value and context, or <see langword="null"/> when the value or
    /// the Threshold was not found (the title stands alone then; a Reminder still says "Still open.").
    /// </summary>
    /// <param name="facts">The Alert.</param>
    /// <param name="reminder">Whether the text is a Reminder's: the Alert text with "Still".</param>
    /// <param name="zone">The User's time zone, for the time of a Reading.</param>
    public string? Body(PushEntryFacts facts, bool reminder, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(zone);

        var entry = facts.Entry;
        var prefix = reminder ? "reminder" : "body";
        string? body = null;

        if (entry is { Kind: AlertKind.Threshold, Side: { } side } && facts.Reading is { } reading)
        {
            if (!string.Equals(entry.Quantity, SoilMoisture, StringComparison.Ordinal))
            {
                var time = TimeZoneInfo.ConvertTime(reading.MeasuredAt, zone).ToString("HH:mm", CultureInfo.InvariantCulture);
                body = Format($"{prefix}.reading", ("value", Number(reading.Value)), ("unit", reading.Value.Unit), ("time", time));
            }
            else if (reading.Value.Unit == "%" && facts.Threshold is { } threshold)
            {
                body = Format(
                    side == ThresholdSide.Low ? $"{prefix}.soil.low" : $"{prefix}.soil.high",
                    ("value", Number(reading.Value)),
                    ("threshold", Number(threshold)));
            }
        }

        return body ?? (reminder ? _templates["reminder.open"] : null);
    }

    /// <summary>
    /// The title of a summary: "Home garden: 2 need water, 3 to check", with plural rules; a group without an
    /// Alert is left out.
    /// </summary>
    /// <param name="siteName">The Site's name; <see langword="null"/> when it was not found.</param>
    /// <param name="needWater">How many Alerts read "needs water".</param>
    /// <param name="toCheck">How many other Alerts there are.</param>
    public string SummaryTitle(string? siteName, int needWater, int toCheck)
    {
        var water = needWater > 0 ? Plural("summary.needWater", needWater) : null;
        var check = toCheck > 0 || needWater == 0 ? Plural("summary.toCheck", toCheck) : null;
        var counts = water is not null && check is not null
            ? Format("summary.counts.both", ("needWater", water), ("toCheck", check))
            : water ?? check!;

        return siteName is null
            ? Format("summary.title.noSite", ("counts", counts))
            : Format("summary.title", ("site", siteName), ("counts", counts));
    }

    /// <summary>
    /// The body of a summary: one line per Alert, the "needs water" ones first and otherwise in the order
    /// given, at most <see cref="MaxSummaryLines"/> and then how many more, and the footer naming the held hours.
    /// </summary>
    /// <param name="alerts">The Alerts whose Lot is not gone.</param>
    /// <param name="window">The User's Notification Window.</param>
    public string SummaryBody(IReadOnlyList<PushEntryFacts> alerts, NotificationWindow window)
    {
        ArgumentNullException.ThrowIfNull(alerts);
        ArgumentNullException.ThrowIfNull(window);

        var ordered = alerts.Where(alert => NeedsWater(alert.Entry)).Concat(alerts.Where(alert => !NeedsWater(alert.Entry))).ToList();
        var lines = ordered.Take(MaxSummaryLines).Select(Title).ToList();

        if (ordered.Count > MaxSummaryLines)
        {
            lines.Add(Plural("summary.more", ordered.Count - MaxSummaryLines));
        }

        lines.Add(Format(
            "summary.footer",
            ("to", EdgeValidation.TimeOfDayName(window.ToMinutes)),
            ("from", EdgeValidation.TimeOfDayName(window.FromMinutes))));

        return string.Join('\n', lines);
    }

    // Whole numbers where they are whole; one decimal otherwise, three significant digits for kΩ.
    private static string Number(PushValue value)
    {
        var decimals = value.Unit == "kΩ" ? Math.Abs(value.Value) switch { >= 100 => 0, >= 10 => 1, _ => 2 } : 1;
        var rounded = Math.Round(value.Value, decimals, MidpointRounding.AwayFromZero);

        // No "-0".
        return (rounded == 0 ? 0 : rounded).ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static PushText Load()
    {
        var name = $"{typeof(PushText).Namespace}.PushText.en.json";
        using var stream = typeof(PushText).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The embedded resource {name} is missing.");
        var templates = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException($"The embedded resource {name} is empty.");

        return new PushText(templates.ToFrozenDictionary(StringComparer.Ordinal));
    }

    private string Plural(string key, int count) =>
        Format(count == 1 ? $"{key}.one" : $"{key}.other", ("count", count.ToString(CultureInfo.InvariantCulture)));

    // One pass over the template: a value is never read again, so a Lot named "{unit}" stays what it is.
    private string Format(string key, params ReadOnlySpan<(string Name, string Value)> arguments)
    {
        var template = _templates[key];
        var text = new StringBuilder(template.Length + 32);
        var position = 0;

        while (position < template.Length)
        {
            var open = template.IndexOf('{', position);
            var close = open < 0 ? -1 : template.IndexOf('}', open);

            if (close < 0)
            {
                break;
            }

            var name = template.AsSpan(open + 1, close - open - 1);
            string? value = null;

            foreach (var (argument, replacement) in arguments)
            {
                if (name.SequenceEqual(argument))
                {
                    value = replacement;
                    break;
                }
            }

            // A placeholder without an argument stays as it is written.
            text.Append(template, position, open - position).Append(value ?? template[open..(close + 1)]);
            position = close + 1;
        }

        return text.Append(template, position, template.Length - position).ToString();
    }
}
