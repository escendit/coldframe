using FluentMigrator;

namespace Coldframe.Migrations.Migrations;

/// <summary>
/// Lets the Alerts list say the battery level a low-battery Alert opened with (Story 7.3).
/// </summary>
/// <remarks>
/// <c>alerts.battery_percent</c> is nullable and comes from <c>alert.opened</c>, which carries it only for a
/// <c>battery</c> Alert and only from this story on: a replay would add nothing, so the table is not rebuilt,
/// and every row written before keeps NULL.
/// </remarks>
[Migration(20261013090000, "Add the battery level to the alerts read model")]
public sealed class M20261013090000AddAlertBatteryPercent : ForwardOnlyMigration
{
    /// <inheritdoc />
    public override void Up()
    {
        Alter.Table("alerts")
            .AddColumn("battery_percent").AsCustom("smallint").Nullable();
    }
}
