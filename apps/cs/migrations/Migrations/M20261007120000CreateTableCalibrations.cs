using FluentMigrator;

namespace Coldframe.Migrations.Migrations;

/// <summary>
/// Creates <c>calibrations</c>, the reference points of every Calibration by its ID (Story 5.1, AD-9), and adds
/// <c>calibrated</c> to <c>lot_status_sensors</c>, which the LotStatus rule reads.
/// </summary>
/// <remarks>
/// <para>
/// The lots projector is the only writer of both: it inserts a row for every <c>sensor.calibrated</c> event and
/// marks the Sensor calibrated. A Reading stores only the ID of the Calibration it was taken under
/// (<c>readings.calibration_id</c>, which has existed since the Readings tables); its percentage is derived from
/// these points when it is read, so history keeps the Calibration it was stored with and compensation can be
/// added later without a migration. A row is never updated or deleted.
/// </para>
/// <para>
/// No <c>sensor.calibrated</c> event exists before this migration, so no projection needs a rebuild: every
/// existing Sensor row is uncalibrated, which is the column's default.
/// </para>
/// </remarks>
[Migration(20261007120000, "Create table calibrations and add calibrated to lot_status_sensors")]
public sealed class M20261007120000CreateTableCalibrations : ForwardOnlyMigration
{
    /// <inheritdoc />
    public override void Up()
    {
        Create.Table("calibrations")
            .WithColumn("calibration_id").AsCustom("uuid").NotNullable().PrimaryKey("pk_calibrations")
            .WithColumn("sensor_id").AsCustom("uuid").NotNullable()
            .WithColumn("dry_raw").AsInt64().NotNullable()
            .WithColumn("wet_raw").AsInt64().NotNullable()
            .WithColumn("calibrated_at").AsCustom("timestamptz").NotNullable();

        Alter.Table("lot_status_sensors")
            .AddColumn("calibrated").AsBoolean().NotNullable().WithDefaultValue(false);
    }
}
