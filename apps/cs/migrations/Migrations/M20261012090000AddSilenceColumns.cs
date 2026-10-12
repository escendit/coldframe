using FluentMigrator;

namespace Coldframe.Migrations.Migrations;

/// <summary>
/// Lets the read models say what is silent and since when (Story 7.2): the Alerts list names a Health Alert's
/// Device kind and when the Device was last heard, an unknown Lot carries that time, and the Devices list shows a
/// silent Device and its Silence Window.
/// </summary>
/// <remarks>
/// <para>
/// All five columns are nullable. <c>alerts.device_kind</c>, <c>alerts.silent_since</c> and
/// <c>lot_status_alerts.silent_since</c> come from <c>alert.opened</c>, which carries them only from this story
/// on: a replay would add nothing, so neither table is rebuilt, and a row of an older Alert keeps NULL (the
/// Alerts list then reads the kind from <c>devices</c>, and clients count from another time).
/// </para>
/// <para>
/// <c>devices.silent_since</c> (NULL while the Device is heard) and <c>devices.silence_window_seconds</c> (NULL
/// while the default of the kind is in force) come from <c>device.silence-started</c>,
/// <c>device.silence-ended</c> and <c>device.silence-window-set</c>, which the devices projector ignored before.
/// The migration therefore deletes the projector's checkpoint, and the projector reads the journal again from
/// position 0 when the Server starts (AD-21). The rows stay: every statement of that projector gives the same
/// row when its events are applied again in order, and the Server reads the table as it starts to activate
/// every enrolled Device, which an empty table would skip.
/// </para>
/// </remarks>
[Migration(20261012090000, "Add the silence columns to the read models and replay the devices read model")]
public sealed class M20261012090000AddSilenceColumns : ForwardOnlyMigration
{
    /// <inheritdoc />
    public override void Up()
    {
        Alter.Table("alerts")
            .AddColumn("device_kind").AsCustom("text").Nullable()
            .AddColumn("silent_since").AsCustom("timestamptz").Nullable();

        Alter.Table("lot_status_alerts")
            .AddColumn("silent_since").AsCustom("timestamptz").Nullable();

        Alter.Table("devices")
            .AddColumn("silent_since").AsCustom("timestamptz").Nullable()
            .AddColumn("silence_window_seconds").AsCustom("bigint").Nullable();

        Execute.Sql("DELETE FROM projection_checkpoints WHERE projector = 'devices';");
    }
}
