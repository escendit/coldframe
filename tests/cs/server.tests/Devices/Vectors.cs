using System.Text.Json;

namespace Coldframe.Server.Tests.Devices;

/// <summary>
/// Reads <c>packages/crypto-spec/vectors.json</c>, the single source of the shared vectors.
/// </summary>
internal static class Vectors
{
    private static readonly Lazy<JsonDocument> Document = new(() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "vectors.json"))));

    public static JsonElement Enrolment(int index) => Document.Value.RootElement.GetProperty("enrolment")[index];

    public static JsonElement Heartbeat(int index) => Document.Value.RootElement.GetProperty("heartbeat")[index];

    public static IEnumerable<JsonElement> Frames() => Document.Value.RootElement.GetProperty("frames").EnumerateArray();

    public static IEnumerable<JsonElement> SensorIds() => Document.Value.RootElement.GetProperty("sensorId").EnumerateArray();

    public static string Text(this JsonElement element, string field) =>
        element.GetProperty(field).GetString() ?? throw new InvalidDataException($"{field} is null");

    public static byte[] Bytes(this JsonElement element, string field) => Convert.FromHexString(element.Text(field));
}
