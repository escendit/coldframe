using FluentMigrator;

namespace Coldframe.Migrations.Migrations;

/// <summary>
/// Creates the ingestion tables (AD-9, AD-17, AD-22): <c>readings</c> and <c>device_reports</c>, append-only
/// and range-partitioned by <c>measured_at</c>, each with a default partition; <c>reading_keys</c>, which
/// makes a Reading exactly-once; and <c>device_replay</c>, the replay window and downlink counter of a Device.
/// </summary>
/// <remarks>
/// <para>
/// A unique index on a partitioned table must contain the partition key, and the <c>measured_at</c> of an
/// unsynced Reading differs between resends, so uniqueness lives in <c>reading_keys</c>
/// (<c>device_id, sensor_id, reading_seq</c>): a Reading row is inserted only for a key that was new. A
/// device report takes the nil UUID as <c>sensor_id</c> and its <c>report_seq</c> as <c>reading_seq</c>.
/// </para>
/// <para>
/// Only the Device grain writes these tables, and it never updates or deletes a Reading or a device
/// report. The monthly partitions are created by the migration job's <c>partitions</c> command, never by a
/// migration, so they keep up with the calendar; a row of a month without a partition lands in the default
/// partition until the next run moves it. <c>calibration_id</c> stays <c>NULL</c> until Epic 5.
/// <c>device_replay.seen</c> is the 64-bit window bitmap; <c>downlink_counter</c> is the next counter to use.
/// </para>
/// </remarks>
[Migration(20261006150000, "Create tables readings, device_reports, reading_keys and device_replay")]
public sealed class M20261006150000CreateTablesReadings : ForwardOnlyMigration
{
    /// <inheritdoc />
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE readings (
                device_id text NOT NULL,
                sensor_id uuid NOT NULL,
                reading_seq numeric(20,0) NOT NULL,
                measured_at timestamptz NOT NULL,
                slot integer NOT NULL,
                quantity text NOT NULL,
                raw_value bigint NOT NULL,
                calibration_id uuid NULL,
                time_unsynced boolean NOT NULL,
                boot_id numeric(20,0) NULL,
                uptime_ms numeric(20,0) NULL,
                received_at timestamptz NOT NULL
            ) PARTITION BY RANGE (measured_at);

            CREATE TABLE readings_default PARTITION OF readings DEFAULT;

            CREATE INDEX ix_readings_sensor_id_measured_at ON readings (sensor_id, measured_at);
            CREATE INDEX ix_readings_device_id_measured_at ON readings (device_id, measured_at);

            CREATE TABLE device_reports (
                device_id text NOT NULL,
                reading_seq numeric(20,0) NOT NULL,
                measured_at timestamptz NOT NULL,
                battery_percent smallint NULL,
                charging text NOT NULL,
                time_unsynced boolean NOT NULL,
                boot_id numeric(20,0) NULL,
                uptime_ms numeric(20,0) NULL,
                received_at timestamptz NOT NULL
            ) PARTITION BY RANGE (measured_at);

            CREATE TABLE device_reports_default PARTITION OF device_reports DEFAULT;

            CREATE INDEX ix_device_reports_device_id_measured_at ON device_reports (device_id, measured_at);

            CREATE TABLE reading_keys (
                device_id text NOT NULL,
                sensor_id uuid NOT NULL,
                reading_seq numeric(20,0) NOT NULL,
                CONSTRAINT pk_reading_keys PRIMARY KEY (device_id, sensor_id, reading_seq)
            );

            CREATE TABLE device_replay (
                device_id text NOT NULL,
                high_water numeric(20,0) NOT NULL,
                seen bigint NOT NULL,
                downlink_counter numeric(20,0) NOT NULL,
                CONSTRAINT pk_device_replay PRIMARY KEY (device_id)
            );
            """);
    }
}
