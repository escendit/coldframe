using Microsoft.Extensions.Logging;

namespace Coldframe.Migrations;

internal static partial class MigrationLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Critical, Message = "Applying the migrations failed.")]
    public static partial void Failed(ILogger logger, Exception exception);
}
