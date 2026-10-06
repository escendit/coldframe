using Coldframe.Crypto;
using Coldframe.DeviceSimulator;
using Coldframe.Protocol.Device.V1;
using Coldframe.Server.Devices;
using Google.Protobuf;
using ProtocolKind = Coldframe.Protocol.Setup.V1.DeviceKind;

namespace Coldframe.Server.Tests.Devices;

/// <summary>
/// How the plaintext of an authentic frame becomes rows and acknowledged ranges (AD-9, AD-11, AD-19). The
/// frames come from the Device simulator; a broken one is a simulator frame with one field changed.
/// </summary>
public sealed class NodeFrameReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ASyncedWakeBecomesOneRowPerReadingAndADeviceReport()
    {
        var node = SimulatedDevice.Create(ProtocolKind.Node);
        node.NextReadingSeq = 100;
        var measuredAt = Now.AddMinutes(-15);
        var frame = Stamp(node, node.Wake(measuredAt, batteryPercent: 87, ChargeStatus.NotCharging));

        var read = NodeFrameReader.Read(frame.ToByteArray(), node.DeviceId, Now);

        Assert.Equal(NodeFrameVerdict.Valid, read.Verdict);
        var rows = Assert.IsType<FrameRows>(read.Rows);
        Assert.Equal((measuredAt, Now, false), (rows.MeasuredAt, rows.ReceivedAt, rows.TimeUnsynced));
        Assert.Null(rows.BootId);
        Assert.Null(rows.UptimeMs);
        Assert.Equal(
            SimulatedDevice.DefaultReadings.Select((reading, index) => new ReadingWrite(
                node.SensorId(reading.Slot, reading.Quantity),
                100 + (ulong)index,
                (int)reading.Slot,
                SimulatedDevice.QuantityToken(reading.Quantity),
                reading.Value)),
            rows.Readings);
        Assert.Equal(new DeviceReportWrite(104, 87, "not_charging"), rows.Report);

        var range = Assert.Single(read.Acknowledged!);
        Assert.Equal((100UL, 104UL), (range.First, range.Last));
    }

    [Fact]
    public void ATimeMoreThanFiveMinutesAheadIsFutureTimeAndExactlyFiveMinutesIsNot()
    {
        var node = SimulatedDevice.Create(ProtocolKind.Node);

        var ahead = Stamp(node, node.Wake(Now.AddMinutes(5).AddMilliseconds(1)));
        var read = NodeFrameReader.Read(ahead.ToByteArray(), node.DeviceId, Now);
        Assert.Equal(NodeFrameVerdict.FutureTime, read.Verdict);
        Assert.Null(read.Rows);
        Assert.Null(read.Acknowledged);

        var edge = Stamp(node, node.Wake(Now.AddMinutes(5)));
        Assert.Equal(NodeFrameVerdict.Valid, NodeFrameReader.Read(edge.ToByteArray(), node.DeviceId, Now).Verdict);
    }

    [Fact]
    public void AnUnsyncedReadingOfTheSealingBootIsRebasedByTheUptimeDifference()
    {
        var node = SimulatedDevice.Create(ProtocolKind.Node);
        node.BootId = 4;
        var wake = node.WakeUnsynced(uptimeMs: 10_000, batteryPercent: null, ChargeStatus.Unspecified);
        node.UptimeMs = 70_000;

        var rows = NodeFrameReader.Read(Stamp(node, wake).ToByteArray(), node.DeviceId, Now).Rows!;

        Assert.Equal(Now.AddSeconds(-60), rows.MeasuredAt);
        Assert.Equal((true, 4UL, 10_000UL), (rows.TimeUnsynced, rows.BootId!.Value, rows.UptimeMs!.Value));
        Assert.Equal(new DeviceReportWrite(wake.ReportSeq, null, "unknown"), rows.Report);

        // An uptime below the Reading's is a negative difference: treated as 0.
        node.UptimeMs = 5_000;
        Assert.Equal(Now, NodeFrameReader.Read(Stamp(node, wake).ToByteArray(), node.DeviceId, Now).Rows!.MeasuredAt);

        // An absurd difference never leaves the calendar.
        node.UptimeMs = ulong.MaxValue;
        Assert.Equal(DateTimeOffset.UnixEpoch, NodeFrameReader.Read(Stamp(node, wake).ToByteArray(), node.DeviceId, Now).Rows!.MeasuredAt);
    }

    [Fact]
    public void AnUnsyncedReadingOfAnEarlierBootGetsTheReceiveTimeAndKeepsItsValues()
    {
        var node = SimulatedDevice.Create(ProtocolKind.Node);
        node.BootId = 4;
        var wake = node.WakeUnsynced(uptimeMs: 10_000);
        node.BootId = 5;
        node.UptimeMs = 70_000;

        var rows = NodeFrameReader.Read(Stamp(node, wake).ToByteArray(), node.DeviceId, Now).Rows!;

        Assert.Equal(Now, rows.MeasuredAt);
        Assert.Equal((true, 4UL, 10_000UL), (rows.TimeUnsynced, rows.BootId!.Value, rows.UptimeMs!.Value));
    }

    [Fact]
    public void AFrameThatBreaksTheContractIsInvalid()
    {
        var node = SimulatedDevice.Create(ProtocolKind.Node);
        var good = Stamp(node, node.Wake(Now));
        Assert.Equal(NodeFrameVerdict.Valid, NodeFrameReader.Read(good.ToByteArray(), node.DeviceId, Now).Verdict);

        var broken = new List<NodeFrame>();
        void Break(Action<NodeFrame> change)
        {
            var frame = good.Clone();
            change(frame);
            broken.Add(frame);
        }

        Break(frame => frame.ProtocolVersion = 2);
        Break(frame => frame.ProtocolVersion = 0);
        Break(frame => frame.ClearMeasured());
        Break(frame => frame.MeasuredAtMs = -1);
        Break(frame => frame.BatteryPercent = 101);
        Break(frame => frame.Readings[0].Quantity = Quantity.Unspecified);
        Break(frame => frame.Readings[0].Quantity = (Quantity)99);
        Break(frame => frame.Readings[0].Slot = NodeFrameReader.MaxSlot + 1);
        Break(frame => frame.Readings.Add(Enumerable.Repeat(frame.Readings[0], NodeFrameReader.MaxReadings)));

        Assert.All(broken, frame => Assert.Equal(NodeFrameVerdict.Invalid, NodeFrameReader.Read(frame.ToByteArray(), node.DeviceId, Now).Verdict));
        Assert.Equal(NodeFrameVerdict.Invalid, NodeFrameReader.Read([0xFF, 0xFF, 0xFF], node.DeviceId, Now).Verdict);
        Assert.Equal(NodeFrameVerdict.Invalid, NodeFrameReader.Read([], node.DeviceId, Now).Verdict);
    }

    [Fact]
    public void AFrameExactlyAtTheLimitsIsValid()
    {
        var node = SimulatedDevice.Create(ProtocolKind.Node);
        var most = Enumerable.Range(0, NodeFrameReader.MaxReadings)
            .Select(index => new SimulatedReading((uint)index, Quantity.SoilMoisture, index))
            .ToList();
        Assert.Equal(64, most.Count);

        var full = NodeFrameReader.Read(Stamp(node, node.Wake(Now, batteryPercent: 100, readings: most)).ToByteArray(), node.DeviceId, Now);
        Assert.Equal(NodeFrameVerdict.Valid, full.Verdict);
        Assert.Equal(64, full.Rows!.Readings.Count);
        Assert.Equal((short)100, full.Rows.Report.BatteryPercent);

        var lastSlot = NodeFrameReader.Read(
            Stamp(node, node.Wake(Now, readings: [new SimulatedReading(255, Quantity.GasResistance, 1)])).ToByteArray(),
            node.DeviceId,
            Now);
        Assert.Equal(NodeFrameVerdict.Valid, lastSlot.Verdict);
        Assert.Equal(255, Assert.Single(lastSlot.Rows!.Readings).Slot);
        Assert.Equal(255U, NodeFrameReader.MaxSlot);
    }

    [Fact]
    public void ARepeatedReadingIsStoredOnceAndAFrameWithoutReadingsStillReports()
    {
        var node = SimulatedDevice.Create(ProtocolKind.Node);
        var frame = Stamp(node, node.Wake(Now));
        frame.Readings.Add(frame.Readings[0].Clone());

        var read = NodeFrameReader.Read(frame.ToByteArray(), node.DeviceId, Now);
        Assert.Equal(4, read.Rows!.Readings.Count);

        var bare = Stamp(node, node.Wake(Now, readings: []));
        var bareRead = NodeFrameReader.Read(bare.ToByteArray(), node.DeviceId, Now);
        Assert.Empty(bareRead.Rows!.Readings);
        Assert.Equal(bare.ReportSeq, Assert.Single(bareRead.Acknowledged!).First);
    }

    [Fact]
    public void RangesHoldExactlyTheSequencesAscendingAndMerged()
    {
        var ranges = NodeFrameReader.Ranges([7, 3, 4, 5, 9, 10, 3, ulong.MaxValue, 0]);

        Assert.Equal(
            [(0UL, 0UL), (3UL, 5UL), (7UL, 7UL), (9UL, 10UL), (ulong.MaxValue, ulong.MaxValue)],
            ranges.Select(range => (range.First, range.Last)));
        Assert.Empty(NodeFrameReader.Ranges([]));

        var adjacentToEnd = NodeFrameReader.Ranges([ulong.MaxValue - 1, ulong.MaxValue]);
        Assert.Equal((ulong.MaxValue - 1, ulong.MaxValue), (adjacentToEnd[0].First, adjacentToEnd[0].Last));
    }

    [Fact]
    public void TheVectorNodeFrameReadsWithTheVectorSensorId()
    {
        var vector = Vectors.Frames().Single(frame => frame.TryGetProperty("message", out var message) && message.Text("type") == "NodeFrame");
        var deviceId = DeviceId.Parse(vector.Text("deviceId"));

        var read = NodeFrameReader.Read(vector.Bytes("plaintext"), deviceId, DateTimeOffset.FromUnixTimeMilliseconds(1_790_000_000_000));

        Assert.Equal(NodeFrameVerdict.Valid, read.Verdict);
        var soil = Vectors.SensorIds().First();
        Assert.Equal(vector.Text("deviceId"), soil.Text("deviceId"));
        Assert.Equal(Guid.Parse(soil.Text("sensorId")), read.Rows!.Readings[0].SensorId);
        Assert.Equal(-2500, read.Rows.Readings[1].RawValue);
    }

    // The frame as the Node seals it: with the sealing boot and uptime.
    private static NodeFrame Stamp(SimulatedDevice node, NodeFrame wake) =>
        NodeFrame.Parser.ParseFrom(new SimulatedServer(node.Keys.DeviceKey).VerifyFrame(node.SealFrame(wake)));
}
