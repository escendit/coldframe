using FluentMigrator;

namespace Coldframe.Migrations.Migrations;

/// <summary>
/// Creates <c>journal_outbox</c>: one row per journal event, written in the same transaction (AD-21).
/// </summary>
/// <remarks>
/// The outbox dispatcher publishes wake-up hints for undispatched rows and stamps
/// <c>dispatched_at</c>. Hints carry no event data; projectors read the journal itself.
/// </remarks>
[Migration(20260928120100, "Create table journal_outbox")]
public sealed class M20260928120100CreateTableJournalOutbox : ForwardOnlyMigration
{
    /// <inheritdoc />
    public override void Up()
    {
        Create.Table("journal_outbox")
            .WithColumn("position").AsInt64().NotNullable().PrimaryKey("pk_journal_outbox")
                .ForeignKey("fk_journal_outbox_journal_events", "journal_events", "position")
            .WithColumn("dispatched_at").AsCustom("timestamptz").Nullable();

        Execute.Sql(
            "CREATE INDEX ix_journal_outbox_undispatched ON journal_outbox (position) WHERE dispatched_at IS NULL;");
    }
}
