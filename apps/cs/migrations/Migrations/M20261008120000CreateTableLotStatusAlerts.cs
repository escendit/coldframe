using FluentMigrator;

namespace Coldframe.Migrations.Migrations;

/// <summary>
/// Creates <c>lot_status_alerts</c>, the open Alerts the LotStatus rule reads (Story 6.1, AD-14): a Lot
/// <c>needsWater</c> while a low-side Threshold Alert is open on a soil-moisture Sensor of its Node.
/// </summary>
/// <remarks>
/// <para>
/// The lots projector is the only writer: it inserts a row for every <c>alert.opened</c> event and sets
/// <c>closed_at</c> with <c>alert.closed</c>; an Alert is open while <c>closed_at</c> is null. A closed Alert
/// keeps its row, so an event applied again changes nothing. The rule finds an Alert by its Node, not by the
/// Lot it was opened for: the Alert of a Node that moved follows the Node to its new Lot until it closes.
/// </para>
/// <para>
/// The migration also rebuilds the lots read model, as the one that added the status columns did: it deletes
/// the projector's checkpoint and the rows of <c>lots</c> and of its support tables, and the projector builds
/// them again from position 0 when the Server starts (AD-21). The support tables are emptied with <c>lots</c>
/// because a replay over rows that already hold their final state would give a status the time of the wrong
/// event. No Reading stores anything but a Calibration's ID, so emptying <c>calibrations</c> loses nothing: every
/// row comes back from its <c>sensor.calibrated</c> event.
/// </para>
/// </remarks>
[Migration(20261008120000, "Create table lot_status_alerts and rebuild the lots read model")]
public sealed class M20261008120000CreateTableLotStatusAlerts : ForwardOnlyMigration
{
    /// <inheritdoc />
    public override void Up()
    {
        Create.Table("lot_status_alerts")
            .WithColumn("alert_id").AsCustom("uuid").NotNullable().PrimaryKey("pk_lot_status_alerts")
            .WithColumn("device_id").AsCustom("text").NotNullable()
            .WithColumn("sensor_id").AsCustom("uuid").NotNullable()
            .WithColumn("quantity").AsCustom("text").NotNullable()
            .WithColumn("kind").AsCustom("text").NotNullable()
            .WithColumn("side").AsCustom("text").Nullable()
            .WithColumn("opened_at").AsCustom("timestamptz").NotNullable()
            .WithColumn("closed_at").AsCustom("timestamptz").Nullable();

        Create.Index("ix_lot_status_alerts_device_id")
            .OnTable("lot_status_alerts")
            .OnColumn("device_id").Ascending();

        Execute.Sql(
            """
            DELETE FROM projection_checkpoints WHERE projector = 'lots';
            DELETE FROM lots;
            DELETE FROM lot_status_devices;
            DELETE FROM lot_status_sensors;
            DELETE FROM calibrations;
            """);
    }
}
