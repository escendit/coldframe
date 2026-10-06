using FluentMigrator;

namespace Coldframe.Migrations.Migrations;

/// <summary>
/// Creates <c>devices</c>: one row per enrolled Device the devices projector has seen (AD-21).
/// </summary>
/// <remarks>
/// The devices projector is its only writer. <c>last_seen_at</c> is the time of the last accepted heartbeat
/// and stays <c>NULL</c> until the first one; whether a Device is online is never stored, the Server computes
/// it when it answers. The index on <c>site_id</c> serves the Devices list of a Site.
/// </remarks>
[Migration(20261006120000, "Create table devices")]
public sealed class M20261006120000CreateTableDevices : ForwardOnlyMigration
{
    /// <inheritdoc />
    public override void Up()
    {
        Create.Table("devices")
            .WithColumn("device_id").AsCustom("text").NotNullable().PrimaryKey("pk_devices")
            .WithColumn("site_id").AsCustom("text").NotNullable()
            .WithColumn("kind").AsCustom("text").NotNullable()
            .WithColumn("lot_id").AsCustom("text").Nullable()
            .WithColumn("enrolled_at").AsCustom("timestamptz").NotNullable()
            .WithColumn("last_seen_at").AsCustom("timestamptz").Nullable();

        Create.Index("ix_devices_site_id")
            .OnTable("devices")
            .OnColumn("site_id").Ascending();
    }
}
