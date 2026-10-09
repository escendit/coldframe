package com.escendit.coldframe.core.push

/**
 * [PushState] flattened for Swift.
 *
 * [permission] is `unknown`, `granted` or `denied`. [noticeVisible]: the notice of UX-DR88 shows in My notifications
 * and above the tiles of the overview. [promptDue]: the overview shows the why-line of UX-DR122 and, from it, the OS
 * prompt; it is false again once `promptAnswered` was called. [routeTarget] is `null`, `lotDetail` or `overview`: the
 * tap waiting to be shown on [routeSiteId], which is already the current Site. [routeLotId] and [routeLotName] are
 * set only for `lotDetail`.
 */
public data class PushSnapshot(
    val permission: String,
    val noticeVisible: Boolean,
    val promptDue: Boolean,
    val routeTarget: String?,
    val routeSiteId: String?,
    val routeLotId: String?,
    val routeLotName: String?,
)

/** The snapshot of [state]. */
public fun snapshotOf(state: PushState): PushSnapshot {
    val route = state.route
    val lot = route as? PushRoute.LotDetail
    return PushSnapshot(
        permission = state.permission.name.replaceFirstChar { it.lowercase() },
        noticeVisible = state.noticeVisible,
        promptDue = state.promptDue,
        routeTarget =
            when (route) {
                null -> null
                is PushRoute.LotDetail -> "lotDetail"
                is PushRoute.Overview -> "overview"
            },
        routeSiteId = route?.siteId,
        routeLotId = lot?.lotId,
        routeLotName = lot?.lotName,
    )
}
