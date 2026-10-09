package com.escendit.coldframe.core.api

import kotlinx.serialization.Serializable

/*
 * Hand-written mirrors of `packages/openapi/coldframe.openapi.json` (AD-10 deviation, see
 * packages/openapi/README.md). A jvmTest checks every name here against the contract.
 */

/** `Site`: a Site with the caller's Role. */
@Serializable
public data class SiteDto(
    val id: String,
    val name: String,
    val role: String,
)

/** `SiteList`: the caller's Sites in the Server's order. */
@Serializable
public data class SiteListDto(
    val sites: List<SiteDto>,
)

/** `CreateSiteRequest`: only the name; the time zone is the User's and is not sent (AD-11). */
@Serializable
public data class CreateSiteRequestDto(
    val name: String,
)

/** `RenameSiteRequest` and `RenameLotRequest`: the new name. */
@Serializable
public data class RenameRequestDto(
    val name: String,
)

/**
 * `Lot`: [status] is a `LotStatus` value computed by the Server (AD-14), with the fields that
 * support it. Timestamps are ISO-8601 UTC. [unknownCause] (`node` or `hub`) comes only with
 * `unknown`; [pausedBy] (`device`, `site`) and [pausedUntil] only with `paused`. The contract
 * requires [statusSince]; it is tolerated missing so that a Server one release behind still
 * lists its Lots. [removed] only on a removed Lot.
 */
@Serializable
public data class LotDto(
    val id: String,
    val name: String,
    val status: String,
    val removed: Boolean? = null,
    val statusSince: String? = null,
    val lastReadingAt: String? = null,
    val unknownCause: String? = null,
    val pausedBy: List<String>? = null,
    val pausedUntil: String? = null,
    val moisturePercent: Double? = null,
    val lowThresholdPercent: Double? = null,
    val node: NodeStatusDto? = null,
    val sensors: List<SensorReadingDto>? = null,
)

/**
 * `NodeStatus`: the Node of a Lot with its battery and last seen from its newest device report.
 * [charging] is `charging` or `notCharging`; each optional field is absent when unknown.
 */
@Serializable
public data class NodeStatusDto(
    val deviceId: String,
    val batteryPercent: Int? = null,
    val charging: String? = null,
    val lastSeenAt: String? = null,
)

/**
 * `SensorReading`: the newest Reading of a Sensor, converted by the Server. [quantity] is
 * `soil_moisture`, `air_temperature`, `relative_humidity` or `gas_resistance`; [unit] is `raw`,
 * `°C`, `%` or `kΩ`. [sensorId] names the Sensor for Calibration; [calibratable] is true when its
 * Specification says `calibration: true`.
 */
@Serializable
public data class SensorReadingDto(
    val quantity: String,
    val value: Double,
    val unit: String,
    val measuredAt: String,
    val sensorId: String? = null,
    val calibratable: Boolean? = null,
)

/** `LotHistoryDay`: one UTC day (`yyyy-MM-dd`) with Readings. */
@Serializable
public data class LotHistoryDayDto(
    val day: String,
    val low: Double,
    val high: Double,
    val readingCount: Int,
)

/** `LotHistory`: a page of daily history of one quantity, days ascending; [nextCursor] only when more follow. */
@Serializable
public data class LotHistoryDto(
    val quantity: String,
    val unit: String,
    val days: List<LotHistoryDayDto>,
    val nextCursor: String? = null,
)

/** `LotList`: the Site's live Lots in the Server's order. */
@Serializable
public data class LotListDto(
    val lots: List<LotDto>,
)

/** `CreateLotRequest`: only the name. */
@Serializable
public data class CreateLotRequestDto(
    val name: String,
)

/** `EnrolmentKey`: the raw X25519 key, base64url without padding, and its lowercase hex SHA-256. */
@Serializable
public data class EnrolmentKeyDto(
    val publicKey: String,
    val fingerprint: String,
)

/**
 * `EnrolDeviceRequest`: the Device's sealed enrolment, relayed unread. [deviceId] is lowercase
 * hex; [enc] and [ciphertext] are base64url without padding; [kind] is `hub` or `node`. [lotId]
 * is the Lot a Node is assigned to; it is left out for a Hub.
 */
@Serializable
public data class EnrolDeviceRequestDto(
    val deviceId: String,
    val kind: String,
    val enc: String,
    val ciphertext: String,
    val lotId: String? = null,
)

/** `Device`: an enrolled Device and its Site; [lotId] only for a Node assigned to a Lot. */
@Serializable
public data class DeviceDto(
    val id: String,
    val kind: String,
    val siteId: String,
    val lotId: String? = null,
)

/** `MoveDeviceRequest`: the Lot of the Site to move a Node to. */
@Serializable
public data class MoveDeviceRequestDto(
    val lotId: String,
)

/**
 * `DeviceListItem`: a Device of the Site's Devices list. [online] is computed by the Server when
 * it answers; [lastSeenAt] is ISO-8601 UTC and absent until the first heartbeat.
 */
@Serializable
public data class DeviceListItemDto(
    val id: String,
    val kind: String,
    val online: Boolean,
    val lotId: String? = null,
    val lastSeenAt: String? = null,
    val lotName: String? = null,
    val batteryPercent: Int? = null,
    val charging: String? = null,
)

/** `DeviceList`: every enrolled Device of the Site: Hubs by Device ID, then Nodes by Lot name. */
@Serializable
public data class DeviceListDto(
    val devices: List<DeviceListItemDto>,
)

/** `CalibrationValue`: the raw value of a stored Reading a Calibration point was taken from. */
@Serializable
public data class CalibrationValueDto(
    val rawValue: Long,
)

/** `CalibrationReading`: a recent stored Reading of a Sensor; [readingSeq] is what a Calibration point names. */
@Serializable
public data class CalibrationReadingDto(
    val readingSeq: Long,
    val rawValue: Long,
    val measuredAt: String,
)

/**
 * `CalibrationState` (`getSensorCalibration`): where the Sensor's Calibration stands and its recent
 * stored [readings], newest first. [dry] and [wet] are the Calibration in force; [pendingDry] and
 * [pendingWet] a point the Server kept while its partner is missing.
 */
@Serializable
public data class CalibrationStateDto(
    val calibrated: Boolean,
    val readings: List<CalibrationReadingDto> = emptyList(),
    val calibrationId: String? = null,
    val dry: CalibrationValueDto? = null,
    val wet: CalibrationValueDto? = null,
    val pendingDry: CalibrationValueDto? = null,
    val pendingWet: CalibrationValueDto? = null,
)

/** `Calibration` (`calibrateSensor` 200): the same as [CalibrationStateDto] without the Readings. */
@Serializable
public data class CalibrationDto(
    val calibrated: Boolean,
    val calibrationId: String? = null,
    val dry: CalibrationValueDto? = null,
    val wet: CalibrationValueDto? = null,
    val pendingDry: CalibrationValueDto? = null,
    val pendingWet: CalibrationValueDto? = null,
)

/** `CalibrationPoint`: the `reading_seq` of a stored Reading of the Sensor. */
@Serializable
public data class CalibrationPointDto(
    val readingSeq: Long,
)

/** `CalibrateSensorRequest`: a dry and/or a wet point; the absent one is left out of the body. */
@Serializable
public data class CalibrateSensorRequestDto(
    val dry: CalibrationPointDto? = null,
    val wet: CalibrationPointDto? = null,
)

/** `ThresholdSide` and `ThresholdSideRequest`: [kind] is `default`, `override` or `cleared`; [value] is in the display unit. */
@Serializable
public data class ThresholdSideDto(
    val kind: String,
    val value: Double? = null,
)

/**
 * `SensorThresholds` (`getSensorThresholds`): the Thresholds in force in the display [unit] (a calibrating Sensor:
 * whole percent 0 to 100). [proposedLow] is the Server's suggestion for a Sensor with no default low, never a high.
 */
@Serializable
public data class SensorThresholdsDto(
    val unit: String,
    val low: ThresholdSideDto,
    val high: ThresholdSideDto,
    val proposedLow: Double? = null,
)

/** `SetSensorThresholdsRequest`: a side that is absent stays as it is. */
@Serializable
public data class SetSensorThresholdsRequestDto(
    val low: ThresholdSideDto? = null,
    val high: ThresholdSideDto? = null,
)

/** `ProblemDetails` (RFC 9457). */
@Serializable
public data class ProblemDto(
    val type: String,
    val title: String? = null,
    val status: Int? = null,
    val detail: String? = null,
)

/**
 * `Alert`: an Alert of the Site. [kind] is the extensible `AlertKind` (`threshold`, `silent`,
 * `battery`, `uncalibrated`, or one this app does not know yet); [side] is `low` or `high` on a
 * Threshold Alert; [quantity] is a `SensorQuantity`. [closedAt] and [reason] are absent while the
 * Alert is open. It carries no value and no Threshold.
 */
@Serializable
public data class AlertDto(
    val id: String,
    val kind: String,
    val quantity: String,
    val lotId: String,
    val lotName: String,
    val deviceId: String,
    val openedAt: String,
    val side: String? = null,
    val closedAt: String? = null,
    val reason: String? = null,
)

/**
 * `AlertList`: a page of the Site's Alerts, open newest first, then closed newest first.
 * [openCount] is every open Alert of the Site whatever the page; [nextCursor] only when more follow.
 */
@Serializable
public data class AlertListDto(
    val alerts: List<AlertDto>,
    val openCount: Int,
    val nextCursor: String? = null,
)
