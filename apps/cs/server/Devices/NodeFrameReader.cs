using Coldframe.Contracts.Sensors;
using Coldframe.Crypto;
using Coldframe.Protocol.Device.V1;
using Google.Protobuf;

namespace Coldframe.Server.Devices;

/// <summary>
/// What an authentic frame's plaintext turned out to be.
/// </summary>
public enum NodeFrameVerdict
{
    /// <summary>
    /// A valid Node frame.
    /// </summary>
    Valid,

    /// <summary>
    /// Not a Node frame of the supported protocol version, or one that breaks the contract.
    /// </summary>
    Invalid,

    /// <summary>
    /// A valid Node frame whose synced time is too far after the Server clock (AD-11).
    /// </summary>
    FutureTime,
}

/// <summary>
/// A decoded Node frame.
/// </summary>
/// <param name="Verdict">Whether the frame can be stored.</param>
/// <param name="Rows">The rows of a valid frame.</param>
/// <param name="Acknowledged">The <c>reading_seq</c> ranges of a valid frame, its report's included.</param>
/// <param name="SpecHash">The <c>spec_hash</c> of a valid frame; empty when the frame has none.</param>
/// <param name="Specifications">
/// The Specification set attached to a valid frame, or <see langword="null"/> when none is attached.
/// </param>
public sealed record NodeFrameRead(
    NodeFrameVerdict Verdict,
    FrameRows? Rows = null,
    IReadOnlyList<ReadingSeqRange>? Acknowledged = null,
    byte[]? SpecHash = null,
    AttachedSpecifications? Specifications = null);

/// <summary>
/// The Specification set a frame carries (AD-19).
/// </summary>
/// <param name="Valid">
/// The Specifications in slot order, or <see langword="null"/> when the set or the frame's <c>spec_hash</c>
/// breaks the contract. An invalid set is ignored; it never changes the frame's verdict.
/// </param>
public sealed record AttachedSpecifications(IReadOnlyList<SensorSpecification>? Valid);

/// <summary>
/// Decodes the plaintext of an uplink frame into the rows the Device grain stores, its <c>spec_hash</c> and,
/// when one is attached, its Specification set (AD-9, AD-11, AD-19).
/// </summary>
public static class NodeFrameReader
{
    /// <summary>
    /// How far after the Server clock a synced <c>measured_at</c> may be, in milliseconds: 5 min (AD-11).
    /// </summary>
    public const long MaxClockLeadMs = 300_000;

    /// <summary>
    /// The most Readings one frame may carry.
    /// </summary>
    public const int MaxReadings = 64;

    /// <summary>
    /// The highest Sensor slot.
    /// </summary>
    public const uint MaxSlot = 255;

    /// <summary>
    /// The most Specifications one set may hold.
    /// </summary>
    public const int MaxSpecifications = 32;

    /// <summary>
    /// The longest <c>spec_hash</c>, in bytes: a SHA-256.
    /// </summary>
    public const int MaxSpecHashLength = 32;

    /// <summary>
    /// Decodes and checks a frame of <paramref name="deviceId"/> received at <paramref name="receivedAt"/>.
    /// </summary>
    public static NodeFrameRead Read(ReadOnlySpan<byte> plaintext, DeviceId deviceId, DateTimeOffset receivedAt)
    {
        NodeFrame frame;
        try
        {
            frame = NodeFrame.Parser.ParseFrom(plaintext);
        }
        catch (InvalidProtocolBufferException)
        {
            return new NodeFrameRead(NodeFrameVerdict.Invalid);
        }

        if (frame.ProtocolVersion != CryptoSpec.ProtocolMajor
            || frame.Readings.Count > MaxReadings
            || (frame.HasBatteryPercent && frame.BatteryPercent > 100))
        {
            return new NodeFrameRead(NodeFrameVerdict.Invalid);
        }

        var readings = new List<ReadingWrite>(frame.Readings.Count);
        var keys = new HashSet<(Guid SensorId, ulong ReadingSeq)>();
        foreach (var reading in frame.Readings)
        {
            if (reading.Slot > MaxSlot || QuantityToken(reading.Quantity) is not { } quantity)
            {
                return new NodeFrameRead(NodeFrameVerdict.Invalid);
            }

            var sensorId = SensorIds.Derive(deviceId, reading.Slot, quantity);
            if (keys.Add((sensorId, reading.ReadingSeq)))
            {
                readings.Add(new ReadingWrite(sensorId, reading.ReadingSeq, (int)reading.Slot, quantity, reading.Value));
            }
        }

        var receivedMs = receivedAt.ToUnixTimeMilliseconds();
        DateTimeOffset measuredAt;
        NodeFrame.Types.Unsynced? unsynced = null;

        switch (frame.MeasuredCase)
        {
            case NodeFrame.MeasuredOneofCase.MeasuredAtMs:
                if (frame.MeasuredAtMs < 0)
                {
                    return new NodeFrameRead(NodeFrameVerdict.Invalid);
                }

                if (frame.MeasuredAtMs > receivedMs + MaxClockLeadMs)
                {
                    return new NodeFrameRead(NodeFrameVerdict.FutureTime);
                }

                measuredAt = DateTimeOffset.FromUnixTimeMilliseconds(frame.MeasuredAtMs);
                break;

            case NodeFrame.MeasuredOneofCase.Unsynced:
                unsynced = frame.Unsynced;

                // Taken by the boot that sealed the frame: the uptime difference is its age. A Reading of an
                // earlier boot has no known age; it gets the receive time. A negative difference counts as 0.
                var ageMs = unsynced.BootId == frame.BootId && frame.UptimeMs > unsynced.UptimeMs
                    ? Math.Min(frame.UptimeMs - unsynced.UptimeMs, (ulong)Math.Max(receivedMs, 0))
                    : 0;
                measuredAt = receivedAt.AddMilliseconds(-(double)ageMs);
                break;

            default:
                return new NodeFrameRead(NodeFrameVerdict.Invalid);
        }

        var report = new DeviceReportWrite(
            frame.ReportSeq,
            frame.HasBatteryPercent ? (short)frame.BatteryPercent : null,
            ChargingToken(frame.Charging));

        return new NodeFrameRead(
            NodeFrameVerdict.Valid,
            new FrameRows(measuredAt, receivedAt, unsynced is not null, unsynced?.BootId, unsynced?.UptimeMs, readings, report),
            Ranges(frame.Readings.Select(reading => reading.ReadingSeq).Append(frame.ReportSeq)),
            frame.SpecHash.ToByteArray(),
            frame.Specifications is { } set ? new AttachedSpecifications(Specifications(frame.SpecHash.Length, set)) : null);
    }

    /// <summary>
    /// The Specifications of a set in slot order, or <see langword="null"/> when the set breaks the contract:
    /// a hash of 0 or more than 32 bytes, 0 or more than 32 Specifications, an unknown quantity or unit, a
    /// range that is empty or reversed, default Thresholds that are not low &lt; high, or a default outside
    /// 0 to 100 percent (a calibrating Sensor) or outside the range (any other).
    /// </summary>
    public static IReadOnlyList<SensorSpecification>? Specifications(int specHashLength, SpecificationSet set)
    {
        ArgumentNullException.ThrowIfNull(set);

        if (specHashLength is < 1 or > MaxSpecHashLength || set.Specifications.Count is < 1 or > MaxSpecifications)
        {
            return null;
        }

        var specifications = new List<SensorSpecification>(set.Specifications.Count);
        foreach (var specification in set.Specifications)
        {
            if (QuantityToken(specification.Quantity) is not { } quantity
                || UnitOf(specification.Unit) is not { } unit
                || specification.RangeMin >= specification.RangeMax)
            {
                return null;
            }

            long? low = specification.HasDefaultLow ? specification.DefaultLow : null;
            long? high = specification.HasDefaultHigh ? specification.DefaultHigh : null;

            // A calibrating Sensor's Thresholds are in percent of its calibrated span; any other's in its unit.
            var (lowest, highest) = specification.Calibration ? (0L, 100L) : (specification.RangeMin, specification.RangeMax);
            if (low < lowest || low > highest || high < lowest || high > highest || low >= high)
            {
                return null;
            }

            specifications.Add(new SensorSpecification(
                quantity,
                unit,
                specification.RangeMin,
                specification.RangeMax,
                specification.Calibration,
                low,
                high));
        }

        return specifications;
    }

    /// <summary>
    /// The ascending, non-overlapping ranges that hold exactly <paramref name="readingSeqs"/>.
    /// </summary>
    public static IReadOnlyList<ReadingSeqRange> Ranges(IEnumerable<ulong> readingSeqs)
    {
        ArgumentNullException.ThrowIfNull(readingSeqs);

        var ranges = new List<ReadingSeqRange>();
        foreach (var seq in readingSeqs.Distinct().Order())
        {
            if (ranges.Count > 0 && ranges[^1].Last + 1 == seq)
            {
                ranges[^1].Last = seq;
            }
            else
            {
                ranges.Add(new ReadingSeqRange { First = seq, Last = seq });
            }
        }

        return ranges;
    }

    /// <summary>
    /// The Sensor ID token of a quantity (AD-19), or <see langword="null"/> for one the contract does not name.
    /// </summary>
    public static string? QuantityToken(Quantity quantity) => quantity switch
    {
        Quantity.SoilMoisture => CryptoSpec.SensorQuantitySoilMoisture,
        Quantity.AirTemperature => CryptoSpec.SensorQuantityAirTemperature,
        Quantity.RelativeHumidity => CryptoSpec.SensorQuantityRelativeHumidity,
        Quantity.GasResistance => CryptoSpec.SensorQuantityGasResistance,
        _ => null,
    };

    /// <summary>
    /// The unit of a Specification, or <see langword="null"/> for one the contract does not name.
    /// </summary>
    public static SensorUnit? UnitOf(Unit unit) => unit switch
    {
        Unit.RawCount => SensorUnit.RawCount,
        Unit.MilliDegreeCelsius => SensorUnit.MilliDegreeCelsius,
        Unit.MilliPercent => SensorUnit.MilliPercent,
        Unit.Ohm => SensorUnit.Ohm,
        _ => null,
    };

    /// <summary>
    /// How a charger status is stored: <c>charging</c>, <c>not_charging</c> or <c>unknown</c>.
    /// </summary>
    public static string ChargingToken(ChargeStatus charging) => charging switch
    {
        ChargeStatus.Charging => "charging",
        ChargeStatus.NotCharging => "not_charging",
        _ => "unknown",
    };
}
