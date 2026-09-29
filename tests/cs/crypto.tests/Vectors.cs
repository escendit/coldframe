using System.Globalization;
using System.Text.Json;

namespace Coldframe.Crypto.Tests;

/// <summary>
/// Reads <c>packages/crypto-spec/vectors.json</c>, the single source of the shared vectors.
/// </summary>
internal static class Vectors
{
    private static readonly Lazy<JsonDocument> Document = new(() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "vectors.json"))));

    public static JsonElement Root => Document.Value.RootElement;

    public static IEnumerable<JsonElement> List(string category) => Root.GetProperty(category).EnumerateArray();

    public static JsonElement First(string category) => List(category).First();

    public static JsonElement Anchor(string name) => Root.GetProperty("anchors").GetProperty(name);

    public static string Text(this JsonElement element, string field) =>
        element.GetProperty(field).GetString() ?? throw new InvalidDataException($"{field} is null");

    public static byte[] Bytes(this JsonElement element, string field) => Convert.FromHexString(element.Text(field));

    public static ulong Number(this JsonElement element, string field) => ulong.Parse(element.Text(field), CultureInfo.InvariantCulture);

    public static string Hex(byte[] bytes) => Convert.ToHexStringLower(bytes);
}
