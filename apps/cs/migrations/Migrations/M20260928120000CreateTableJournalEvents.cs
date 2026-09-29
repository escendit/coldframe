using FluentMigrator;
using FluentMigrator.Postgres;

namespace Coldframe.Migrations.Migrations;

/// <summary>
/// Creates <c>journal_events</c>, the one event table of the journal (AD-21).
/// </summary>
/// <remarks>
/// <c>position</c> is the global position projectors read by. Appends take a transaction-scoped
/// advisory lock, so positions become visible in commit order.
/// </remarks>
[Migration(20260928120000, "Create table journal_events")]
public sealed class M20260928120000CreateTableJournalEvents : ForwardOnlyMigration
{
    /// <inheritdoc />
    public override void Up()
    {
        Create.Table("journal_events")
            .WithColumn("position").AsInt64().NotNullable().Identity(PostgresGenerationType.Always)
                .PrimaryKey("pk_journal_events")
            .WithColumn("stream_id").AsString(512).NotNullable()
            .WithColumn("version").AsInt32().NotNullable()
            .WithColumn("type_alias").AsString(200).NotNullable()
            .WithColumn("schema_version").AsInt32().NotNullable()
            .WithColumn("payload").AsCustom("jsonb").NotNullable()
            .WithColumn("recorded_at").AsCustom("timestamptz").NotNullable();

        Create.UniqueConstraint("ux_journal_events_stream_id_version")
            .OnTable("journal_events")
            .Columns("stream_id", "version");
    }
}
