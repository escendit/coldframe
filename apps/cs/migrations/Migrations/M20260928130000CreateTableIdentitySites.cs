using FluentMigrator;

namespace Coldframe.Migrations.Migrations;

/// <summary>
/// Creates <c>identity_sites</c>: one row per Site the identity projector has seen (AD-4).
/// </summary>
/// <remarks>
/// The identity projector is its only writer. The authorization policy reads whether a Site exists
/// and is active from here.
/// </remarks>
[Migration(20260928130000, "Create table identity_sites")]
public sealed class M20260928130000CreateTableIdentitySites : ForwardOnlyMigration
{
    /// <inheritdoc />
    public override void Up()
    {
        Create.Table("identity_sites")
            .WithColumn("site_id").AsCustom("text").NotNullable().PrimaryKey("pk_identity_sites")
            .WithColumn("name").AsCustom("text").NotNullable()
            .WithColumn("lifecycle").AsCustom("text").NotNullable()
            .WithColumn("created_at").AsCustom("timestamptz").NotNullable();
    }
}
