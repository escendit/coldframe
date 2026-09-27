using FluentMigrator;

namespace Coldframe.Migrations.Migrations;

/// <summary>
/// Creates <c>projection_checkpoints</c>: the last global position each projector applied (AD-21).
/// </summary>
/// <remarks>
/// A projector writes its read model and its checkpoint in one transaction. Deleting its row
/// makes it rebuild from position 0.
/// </remarks>
[Migration(20260928120200, "Create table projection_checkpoints")]
public sealed class M20260928120200CreateTableProjectionCheckpoints : ForwardOnlyMigration
{
    /// <inheritdoc />
    public override void Up()
    {
        Create.Table("projection_checkpoints")
            .WithColumn("projector").AsString(200).NotNullable().PrimaryKey("pk_projection_checkpoints")
            .WithColumn("position").AsInt64().NotNullable()
            .WithColumn("updated_at").AsCustom("timestamptz").NotNullable();
    }
}
