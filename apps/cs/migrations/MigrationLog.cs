using Microsoft.Extensions.Logging;

namespace Coldframe.Migrations;

internal static partial class MigrationLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Critical, Message = "The migration job failed.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Created {Created} monthly partition(s); the current month and the next {MonthsAhead} have one.")]
    public static partial void PartitionsEnsured(ILogger logger, int created, int monthsAhead);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Advanced the replay state of {Devices} Device(s): uplink high-water mark +{UplinkMargin} with the window fully seen, downlink counter +{DownlinkMargin}.")]
    public static partial void ReplayAdvanced(ILogger logger, int devices, ulong uplinkMargin, ulong downlinkMargin);
}
