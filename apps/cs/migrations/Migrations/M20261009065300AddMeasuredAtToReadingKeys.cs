using FluentMigrator;

namespace Coldframe.Migrations.Migrations;

/// <summary>
/// Adds <c>measured_at</c> to <c>reading_keys</c> (Story 6.1): the time a Reading was given at its first
/// delivery, kept with the key that makes the Reading exactly-once.
/// </summary>
/// <remarks>
/// <para>
/// A Node without synced time gets <c>measured_at = receive time - age</c>, so every resend of a frame would
/// give its Readings a later time, and the Sensor grain, which evaluates in <c>measured_at</c> order, would
/// count one Reading again on every resend. The Device grain therefore writes the time with a new key and
/// evaluates a duplicate with the stored one, never with the time of the frame that carries the Reading again.
/// </para>
/// <para>
/// The column is nullable and there is no backfill: a key stored before this migration has no time, and a
/// frame that carries its Reading again is evaluated with its own time, as before. A device report's key
/// keeps its time too, though nothing reads it.
/// </para>
/// </remarks>
[Migration(20261009065300, "Add measured_at to reading_keys")]
public sealed class M20261009065300AddMeasuredAtToReadingKeys : ForwardOnlyMigration
{
    /// <inheritdoc />
    public override void Up()
    {
        Alter.Table("reading_keys")
            .AddColumn("measured_at").AsCustom("timestamptz").Nullable();
    }
}
