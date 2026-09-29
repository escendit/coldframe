using FluentMigrator;

namespace Coldframe.Migrations.Migrations;

/// <summary>
/// Creates <c>lots</c>: one row per Lot the lots projector has seen (AD-20, AD-21).
/// </summary>
/// <remarks>
/// The lots projector is its only writer. A removed Lot keeps its row with <c>removed_at</c> set, so its
/// ID stays resolvable. The index on <c>site_id</c> serves the Lot list of a Site.
/// </remarks>
[Migration(20260928140000, "Create table lots")]
public sealed class M20260928140000CreateTableLots : ForwardOnlyMigration
{
    /// <inheritdoc />
    public override void Up()
    {
        Create.Table("lots")
            .WithColumn("lot_id").AsCustom("text").NotNullable().PrimaryKey("pk_lots")
            .WithColumn("site_id").AsCustom("text").NotNullable()
            .WithColumn("name").AsCustom("text").NotNullable()
            .WithColumn("status").AsCustom("text").NotNullable()
            .WithColumn("claimed_by").AsCustom("text").Nullable()
            .WithColumn("created_at").AsCustom("timestamptz").NotNullable()
            .WithColumn("removed_at").AsCustom("timestamptz").Nullable();

        Create.Index("ix_lots_site_id")
            .OnTable("lots")
            .OnColumn("site_id").Ascending();
    }
}
