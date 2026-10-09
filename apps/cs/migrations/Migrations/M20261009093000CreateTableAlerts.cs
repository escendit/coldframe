using FluentMigrator;

namespace Coldframe.Migrations.Migrations;

/// <summary>
/// Creates <c>alerts</c>, the read model of the Alerts list (Story 6.2, AD-21): one row per Alert of every Site,
/// open or closed.
/// </summary>
/// <remarks>
/// <para>
/// The alerts projector is the only writer: it inserts a row for every <c>alert.opened</c> event and sets
/// <c>closed_at</c> and <c>reason</c> with <c>alert.closed</c>; an Alert is open while <c>closed_at</c> is null.
/// A closed Alert keeps its row, so an event applied again changes nothing. <c>lot_id</c> is the Lot the Alert
/// was opened for; its name is joined from <c>lots</c> when the list is read. <c>kind</c>, <c>side</c> and
/// <c>reason</c> hold the contract's lowercase names.
/// </para>
/// <para>
/// The index serves the list of a Site: its open Alerts (<c>closed_at</c> null) by <c>opened_at</c>, and its
/// closed Alerts from a close time on. The projector has no checkpoint yet, so it builds the table from position
/// 0 when the Server starts, with the Alerts journaled since Story 6.1. <c>lot_status_alerts</c> stays as it is:
/// it belongs to the lots projector.
/// </para>
/// </remarks>
[Migration(20261009093000, "Create table alerts")]
public sealed class M20261009093000CreateTableAlerts : ForwardOnlyMigration
{
    /// <inheritdoc />
    public override void Up()
    {
        Create.Table("alerts")
            .WithColumn("alert_id").AsCustom("uuid").NotNullable().PrimaryKey("pk_alerts")
            .WithColumn("site_id").AsCustom("text").NotNullable()
            .WithColumn("lot_id").AsCustom("text").NotNullable()
            .WithColumn("device_id").AsCustom("text").NotNullable()
            .WithColumn("sensor_id").AsCustom("uuid").NotNullable()
            .WithColumn("kind").AsCustom("text").NotNullable()
            .WithColumn("side").AsCustom("text").Nullable()
            .WithColumn("quantity").AsCustom("text").NotNullable()
            .WithColumn("opened_at").AsCustom("timestamptz").NotNullable()
            .WithColumn("closed_at").AsCustom("timestamptz").Nullable()
            .WithColumn("reason").AsCustom("text").Nullable();

        Create.Index("ix_alerts_site_id_closed_at_opened_at")
            .OnTable("alerts")
            .OnColumn("site_id").Ascending()
            .OnColumn("closed_at").Ascending()
            .OnColumn("opened_at").Ascending();
    }
}
