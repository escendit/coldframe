using FluentMigrator;

namespace Coldframe.Migrations.Migrations;

/// <summary>
/// Lets the read models hold a Device's Health Alert (Story 7.1): an Alert of kind <c>silent</c> has no Sensor and
/// no quantity, and one of a Hub or of an unassigned Node has no Lot. Adds the relay Hub of a Node to the lots
/// projector's support table, so that a Lot behind a silent Hub is <c>unknown</c> with cause <c>hub</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>alerts.lot_id</c>, <c>alerts.sensor_id</c>, <c>alerts.quantity</c>, <c>lot_status_alerts.sensor_id</c> and
/// <c>lot_status_alerts.quantity</c> lose NOT NULL. The rows of Threshold Alerts keep their values. The Alerts
/// list still selects Threshold Alerts only; the contract is extended with the clients (Story 7.2).
/// </para>
/// <para>
/// <c>lot_status_devices.relay_hub_id</c> comes from <c>device.relay-changed</c>, an event the lots projector did
/// not read before. The migration therefore rebuilds the lots read model, as the one that added
/// <c>lot_status_alerts</c> did: it deletes the projector's checkpoint and the rows of <c>lots</c> and of its
/// support tables, and the projector builds them again from position 0 when the Server starts (AD-21). Every
/// row comes back from the journal. The <c>alerts</c> table is not rebuilt: no <c>silent</c> Alert was journaled
/// before this migration.
/// </para>
/// </remarks>
[Migration(20261011090000, "Allow Device Alerts in the read models, add the relay Hub and rebuild the lots read model")]
public sealed class M20261011090000AllowDeviceAlerts : ForwardOnlyMigration
{
    /// <inheritdoc />
    public override void Up()
    {
        // DROP NOT NULL only: the type and the values of every column stay as they are.
        Execute.Sql(
            """
            ALTER TABLE alerts
                ALTER COLUMN lot_id DROP NOT NULL,
                ALTER COLUMN sensor_id DROP NOT NULL,
                ALTER COLUMN quantity DROP NOT NULL;
            ALTER TABLE lot_status_alerts
                ALTER COLUMN sensor_id DROP NOT NULL,
                ALTER COLUMN quantity DROP NOT NULL;
            """);

        Alter.Table("lot_status_devices")
            .AddColumn("relay_hub_id").AsCustom("text").Nullable();

        Create.Index("ix_lot_status_devices_relay_hub_id")
            .OnTable("lot_status_devices")
            .OnColumn("relay_hub_id").Ascending();

        Execute.Sql(
            """
            DELETE FROM projection_checkpoints WHERE projector = 'lots';
            DELETE FROM lots;
            DELETE FROM lot_status_devices;
            DELETE FROM lot_status_sensors;
            DELETE FROM lot_status_alerts;
            DELETE FROM calibrations;
            """);
    }
}
