using FluentMigrator;

namespace Coldframe.Migrations.Migrations;

/// <summary>
/// Creates <c>identity_memberships</c>: each User's Role per Site (AD-4).
/// </summary>
/// <remarks>
/// The identity projector is its only writer. The authorization policy reads the caller's Role from
/// here, never from token claims. The index on <c>user_id</c> serves "my Sites" lookups.
/// </remarks>
[Migration(20260928130100, "Create table identity_memberships")]
public sealed class M20260928130100CreateTableIdentityMemberships : ForwardOnlyMigration
{
    /// <inheritdoc />
    public override void Up()
    {
        Create.Table("identity_memberships")
            .WithColumn("site_id").AsCustom("text").NotNullable().PrimaryKey("pk_identity_memberships")
            .WithColumn("user_id").AsCustom("text").NotNullable().PrimaryKey("pk_identity_memberships")
            .WithColumn("role").AsCustom("text").NotNullable();

        Create.Index("ix_identity_memberships_user_id")
            .OnTable("identity_memberships")
            .OnColumn("user_id").Ascending();
    }
}
