using FluentMigrator;

namespace Coldframe.Migrations.Migrations;

/// <summary>
/// Gives Pause a read surface beyond the Lot (Story 8.2, DW-112): <c>GET /sites</c> and
/// <c>GET /sites/{siteId}</c> say whether the Site is paused and until when, and the Devices list names each
/// Device's Pause sources, also for a Hub, which has no Lot.
/// </summary>
/// <remarks>
/// <para>
/// <c>devices</c> gets one pair of columns per Pause source, as <c>lot_status_devices</c> has them:
/// <c>device_paused</c>, <c>device_paused_until</c>, <c>site_paused</c>, <c>site_paused_until</c>. They come from
/// <c>device.paused</c> and <c>device.resumed</c>, which the devices projector ignored before, so the migration
/// deletes that projector's checkpoint and the projector reads the journal again from position 0 when the Server
/// starts (AD-21). The rows stay, as in <see cref="M20261012090000AddSilenceColumns"/>: every statement of the
/// projector gives the same row when its events are applied again in order, and the Server reads the table as
/// it starts to activate every enrolled Device.
/// </para>
/// <para>
/// <c>identity_sites</c> gets <c>paused</c> and <c>paused_until</c>, from <c>site.paused</c> and
/// <c>site.resumed</c>. The identity projection is not replayed: until it caught up it would show revoked
/// Memberships and deleted Sites as live, and the access rules read that table. Instead the rows are filled here
/// from the newest of the two events on each Site stream, so a Pause journaled before this change shows too. An
/// event the identity projector has not reached yet is applied by it afterwards, in order, to the same end.
/// </para>
/// </remarks>
[Migration(20261014090000, "Add the pause columns to identity_sites and devices and replay the devices read model")]
public sealed class M20261014090000AddPauseReadColumns : ForwardOnlyMigration
{
    /// <inheritdoc />
    public override void Up()
    {
        Alter.Table("identity_sites")
            .AddColumn("paused").AsBoolean().NotNullable().WithDefaultValue(false)
            .AddColumn("paused_until").AsCustom("timestamptz").Nullable();

        Alter.Table("devices")
            .AddColumn("device_paused").AsBoolean().NotNullable().WithDefaultValue(false)
            .AddColumn("device_paused_until").AsCustom("timestamptz").Nullable()
            .AddColumn("site_paused").AsBoolean().NotNullable().WithDefaultValue(false)
            .AddColumn("site_paused_until").AsCustom("timestamptz").Nullable();

        Execute.Sql(
            """
            UPDATE identity_sites s
            SET paused = (e.type_alias = 'site.paused'),
                paused_until = CASE WHEN e.type_alias = 'site.paused' THEN (e.payload ->> 'endsAt')::timestamptz END
            FROM (
                SELECT DISTINCT ON (stream_id) stream_id, type_alias, payload
                FROM journal_events
                WHERE type_alias IN ('site.paused', 'site.resumed') AND stream_id LIKE 'site/%'
                ORDER BY stream_id, version DESC) e
            WHERE e.stream_id = 'site/' || s.site_id;
            """);

        Execute.Sql("DELETE FROM projection_checkpoints WHERE projector = 'devices';");
    }
}
