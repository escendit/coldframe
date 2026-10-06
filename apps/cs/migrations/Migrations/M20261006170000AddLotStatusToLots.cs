using FluentMigrator;

namespace Coldframe.Migrations.Migrations;

/// <summary>
/// Gives <c>lots</c> what the LotStatus projection stores next to the status (AD-14), and creates the two
/// support tables the lots projector keeps for it: <c>lot_status_devices</c> and <c>lot_status_sensors</c>.
/// </summary>
/// <remarks>
/// <para>
/// The lots projector is the only writer of all three. <c>status_since</c> is the time of the event that
/// changed the status; <c>claimed_at</c> the time the Node took the Lot, from which <c>lastReadingAt</c> is
/// read; <c>unknown_cause</c>, <c>paused_by</c> and <c>paused_until</c> are set only while the status is
/// <c>unknown</c> or <c>paused</c>. <c>lot_status_devices</c> holds a Device's Pause sources and the Sensor
/// IDs of its accepted Specification set; <c>lot_status_sensors</c> what the status rule needs of a Sensor.
/// </para>
/// <para>
/// Rows that exist were projected from Lot events alone and know nothing of the Device and Sensor events
/// already in the journal, so they cannot be backfilled in place. The migration empties <c>lots</c> and
/// deletes the projector's checkpoint instead: the projector rebuilds the table from position 0 when the
/// Server starts (AD-21), with the time of every status from the journal.
/// </para>
/// </remarks>
[Migration(20261006170000, "Add the LotStatus columns to lots and create the lot status support tables")]
public sealed class M20261006170000AddLotStatusToLots : ForwardOnlyMigration
{
    /// <inheritdoc />
    public override void Up()
    {
        Execute.Sql(
            """
            DELETE FROM projection_checkpoints WHERE projector = 'lots';
            DELETE FROM lots;
            """);

        Alter.Table("lots")
            .AddColumn("status_since").AsCustom("timestamptz").NotNullable()
            .AddColumn("claimed_at").AsCustom("timestamptz").Nullable()
            .AddColumn("unknown_cause").AsCustom("text").Nullable()
            .AddColumn("paused_by").AsCustom("text[]").Nullable()
            .AddColumn("paused_until").AsCustom("timestamptz").Nullable();

        Create.Index("ix_lots_claimed_by")
            .OnTable("lots")
            .OnColumn("claimed_by").Ascending();

        Create.Table("lot_status_devices")
            .WithColumn("device_id").AsCustom("text").NotNullable().PrimaryKey("pk_lot_status_devices")
            .WithColumn("device_paused").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("device_paused_until").AsCustom("timestamptz").Nullable()
            .WithColumn("site_paused").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("site_paused_until").AsCustom("timestamptz").Nullable()
            .WithColumn("sensor_ids").AsCustom("uuid[]").Nullable();

        Create.Table("lot_status_sensors")
            .WithColumn("sensor_id").AsCustom("uuid").NotNullable().PrimaryKey("pk_lot_status_sensors")
            .WithColumn("device_id").AsCustom("text").NotNullable()
            .WithColumn("quantity").AsCustom("text").NotNullable()
            .WithColumn("calibration").AsBoolean().NotNullable();
    }
}
