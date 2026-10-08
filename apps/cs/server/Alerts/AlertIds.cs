using System.Globalization;
using Coldframe.Crypto;

namespace Coldframe.Server.Alerts;

/// <summary>
/// Alert IDs (Story 6.1): the UUIDv5 of <c>{subjectKind}:{subjectId}:{alertKind}:{episode}</c>. The grain that
/// evaluates journals its episode counter before it calls the Alert grain, so a retry derives the same ID and
/// never opens a second Alert.
/// </summary>
public static class AlertIds
{
    /// <summary>
    /// The UUIDv5 namespace of Alert IDs. A Server-only constant: no Device and no client derives an Alert ID,
    /// so it is not part of <c>packages/crypto-spec</c>. It must never change: every journaled Alert ID was
    /// derived in it.
    /// </summary>
    public static Guid Namespace { get; } = Guid.ParseExact("f6ef9b10-668e-492d-a9b3-0b889778fd63", "D");

    /// <summary>
    /// The name a Threshold Alert's ID is derived from: <c>sensor:{sensorId}:threshold:{episode}</c>, the
    /// Sensor ID in the lowercase hyphenated form.
    /// </summary>
    /// <param name="sensorId">The Sensor that evaluates.</param>
    /// <param name="episode">The Sensor's episode, from 1.</param>
    public static string ThresholdName(Guid sensorId, int episode) =>
        string.Create(CultureInfo.InvariantCulture, $"sensor:{sensorId:D}:threshold:{episode}");

    /// <summary>
    /// The ID of the Threshold Alert of a Sensor's episode.
    /// </summary>
    /// <param name="sensorId">The Sensor that evaluates.</param>
    /// <param name="episode">The Sensor's episode, from 1.</param>
    public static Guid Threshold(Guid sensorId, int episode) => SensorIds.UuidV5(Namespace, ThresholdName(sensorId, episode));
}
