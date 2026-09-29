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

/** `Lot`: [status] is a `LotStatus` value computed by the Server (AD-14); [removed] only on a removed Lot. */
@Serializable
public data class LotDto(
    val id: String,
    val name: String,
    val status: String,
    val removed: Boolean? = null,
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

/** `ProblemDetails` (RFC 9457). */
@Serializable
public data class ProblemDto(
    val type: String,
    val title: String? = null,
    val status: Int? = null,
    val detail: String? = null,
)
