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
 * `°C`, `%` or `kΩ`.
 */
@Serializable
public data class SensorReadingDto(
    val quantity: String,
    val value: Double,
    val unit: String,
    val measuredAt: String,
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

/** `ProblemDetails` (RFC 9457). */
@Serializable
public data class ProblemDto(
    val type: String,
    val title: String? = null,
    val status: Int? = null,
    val detail: String? = null,
)
