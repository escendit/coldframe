package com.escendit.coldframe.core.api

import kotlinx.serialization.Serializable

/*
 * Hand-written mirrors of `packages/openapi/coldframe.openapi.json` (AD-10 deviation, see
 * packages/openapi/README.md). A jvmTest checks every name here against the contract.
 */

/**
 * `Site`: a Site with the caller's Role. [paused] is true while the Site is paused and [pausedUntil]
 * (ISO-8601 UTC) the end of that Pause, absent when it has none. The answer to `POST /sites` carries
 * neither: a Site without [paused] reads as not paused.
 */
@Serializable
public data class SiteDto(
    val id: String,
    val name: String,
    val role: String,
    val paused: Boolean? = null,
    val pausedUntil: String? = null,
)

/**
 * `SetPauseRequest`: a Pause of a Device or of a Site. [until] is the calendar date the User picked,
 * `yyyy-MM-dd`; the Server resolves it. Left out, the Pause lasts until someone resumes.
 */
@Serializable
public data class SetPauseRequestDto(
    val until: String? = null,
)

/** `SitePause`: the Site's Pause after a pause or resume; [pausedUntil] only while paused with an end. */
@Serializable
public data class SitePauseDto(
    val paused: Boolean,
    val pausedUntil: String? = null,
)

/**
 * `DevicePause`: the Device's Pause after a pause or resume. [pausedBy] holds its sources (`device`
 * before `site`) and is empty when it is not paused; [pausedUntil] only while paused with an end.
 */
@Serializable
public data class DevicePauseDto(
    val pausedBy: List<String>,
    val pausedUntil: String? = null,
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
 * `unknown`, and with it [silentSince] (when the silent Device was last heard) and, for `hub`,
 * [silentHubId]; [pausedBy] (`device`, `site`) and [pausedUntil] only with `paused`. The contract
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
    val silentHubId: String? = null,
    val silentSince: String? = null,
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

/**
 * `LotList`: the Site's live Lots in the Server's order. [silentHubs] are the Site's Hubs with an
 * open silent Alert, by Device ID, whether or not a Lot is behind them; absent when none is.
 */
@Serializable
public data class LotListDto(
    val lots: List<LotDto>,
    val silentHubs: List<SilentHubDto>? = null,
)

/** `SilentHub`: a silent Hub and when its last heartbeat was accepted (its enrolment if it never sent one). */
@Serializable
public data class SilentHubDto(
    val deviceId: String,
    val silentSince: String,
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
 * it answers; [lastSeenAt] is ISO-8601 UTC and absent until the first heartbeat. [silentSince] is
 * present exactly while the Device has an open silent Alert; [silenceWindowSeconds] is the Silence
 * Window in force and [silenceWindowOverridden] whether it is the Device's own. [pausedBy] (`device`
 * before `site`) is present exactly while the Device is paused, for a Hub as for a Node, and with it
 * [pausedUntil] when that Pause has an end.
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
    val silentSince: String? = null,
    val silenceWindowSeconds: Int? = null,
    val silenceWindowOverridden: Boolean? = null,
    val pausedBy: List<String>? = null,
    val pausedUntil: String? = null,
)

/** `SetSilenceWindowRequest`: the Device's own Silence Window in whole seconds; left out means "use the default". */
@Serializable
public data class SetSilenceWindowRequestDto(
    val windowSeconds: Int? = null,
)

/** `SilenceWindow`: the Silence Window in force for a Device, and whether it is the Device's own. */
@Serializable
public data class SilenceWindowDto(
    val windowSeconds: Int,
    val overridden: Boolean,
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
 * `HealthAlert`: an Alert about a Device, not about a Reading (Story 7.2). [kind] is the extensible
 * `AlertKind`, never `threshold`; [deviceKind] is `hub` or `node` and is tolerated missing. [lotId]
 * and [lotName] are absent for a Hub and for a Node on no Lot. [silentSince] is when a silent Device
 * was last heard, absent for another kind and for an Alert opened before the Server recorded it.
 * [batteryPercent] is the level a `battery` Alert opened with (Story 7.3), absent for another kind
 * and for an Alert that did not record it.
 */
@Serializable
public data class HealthAlertDto(
    val id: String,
    val kind: String,
    val deviceId: String,
    val openedAt: String,
    val deviceKind: String? = null,
    val lotId: String? = null,
    val lotName: String? = null,
    val silentSince: String? = null,
    val closedAt: String? = null,
    val reason: String? = null,
    val batteryPercent: Int? = null,
)

/**
 * `AlertList`: a page of the Site's Threshold Alerts ([alerts]), open newest first, then closed newest
 * first. [healthAlerts] are the Site's Health Alerts in the same order; they are not paged and come
 * with the first page only. [openCount] is every open Alert of the Site, Threshold and Health,
 * whatever the page; [nextCursor] only when more Threshold Alerts follow.
 */
@Serializable
public data class AlertListDto(
    val alerts: List<AlertDto>,
    val openCount: Int,
    val nextCursor: String? = null,
    val healthAlerts: List<HealthAlertDto>? = null,
)

/** `NotificationWindow`: the daily Notification Window, `"HH:mm"` (24 h) wall-clock times in the User's time zone. */
@Serializable
public data class NotificationWindowDto(
    val from: String,
    val to: String,
)

/**
 * `NotificationSettings` (`getMyNotificationSettings`): the User's own settings. [timeZone] is the IANA zone the User
 * chose, else one a device detected, else absent; only a choice makes [timeZoneConfirmed] true.
 */
@Serializable
public data class NotificationSettingsDto(
    val window: NotificationWindowDto,
    val timeZoneConfirmed: Boolean,
    val timeZone: String? = null,
)

/** The `window` of an `UpdateNotificationSettingsRequest`: [to] left out means 22:00. */
@Serializable
public data class NotificationWindowRequestDto(
    val from: String,
    val to: String? = null,
)

/**
 * `UpdateNotificationSettingsRequest` (`updateMyNotificationSettings`): at least one field. [timeZone] is the User's
 * own choice and always wins; [detectedTimeZone] is kept only while the User has chosen none.
 */
@Serializable
public data class UpdateNotificationSettingsRequestDto(
    val window: NotificationWindowRequestDto? = null,
    val timeZone: String? = null,
    val detectedTimeZone: String? = null,
)

/**
 * `SiteNotificationSettings`: the User's settings for one Site. [reminderCadence] absent means "use Site setting";
 * [siteReminderCadence] is the Site's. Both are `ReminderCadence` values (`daily`, `every2Days`).
 */
@Serializable
public data class SiteNotificationSettingsDto(
    val muted: Boolean,
    val siteReminderCadence: String,
    val reminderCadence: String? = null,
)

/** `SetSiteNotificationSettingsRequest`: both values every time; [reminderCadence] left out means "use Site setting". */
@Serializable
public data class SetSiteNotificationSettingsRequestDto(
    val muted: Boolean,
    val reminderCadence: String? = null,
)

/** `SiteReminderCadence`: the Site's Reminder cadence, read and written. */
@Serializable
public data class SiteReminderCadenceDto(
    val cadence: String,
)

/**
 * `RegisterPushDeviceRequest` (`registerPushDevice`): [platform] is a `PushPlatform` (`apns`, `fcm`), [token] the
 * provider's device token, and [environment] an `ApnsEnvironment` (`production`, `sandbox`), which an `apns`
 * registration must carry and an `fcm` registration must not.
 */
@Serializable
public data class RegisterPushDeviceRequestDto(
    val platform: String,
    val token: String,
    val environment: String? = null,
)

/** `InvitationRequest` (Story 9.1): the invitee's email and the Role they get, a `SiteRole` name. */
@Serializable
public data class InvitationRequestDto(
    val email: String,
    val role: String,
)

/**
 * `SiteMember`: one Membership of a Site. [role] is the Server's (its identity projection); [name] and
 * [email] are read from Keycloak with the request. [name] may be empty, [email] absent.
 */
@Serializable
public data class SiteMemberDto(
    val userId: String,
    val name: String,
    val role: String,
    val email: String? = null,
)

/** `SiteMemberList`: Owners first, then by name. */
@Serializable
public data class SiteMemberListDto(
    val members: List<SiteMemberDto>,
)
