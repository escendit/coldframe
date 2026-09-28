package com.escendit.coldframe.core.lots

/**
 * [LotsState] flattened for Swift. Enum values cross as catalogue key suffixes (`owner`,
 * `lotClaimed`, `tooLong`, …), the Lots as parallel lists in the Server's order, and statuses
 * as contract values (`noNode`). No token, URL or Idempotency-Key crosses this boundary.
 *
 * [surface] is `idle`, `loading`, `failed` or `ready`. [notice] is the load failure (the Sites
 * load keys); [actionNotice] is the last change that did not happen, with [actionNoticeSubject]
 * the Site name for `forbidden` and the Lot name for `lotClaimed`.
 */
public data class LotsSnapshot(
    val surface: String,
    val notice: String?,
    val noticeTryAgain: Boolean,
    val siteId: String?,
    val siteName: String?,
    val role: String?,
    val canRenameSite: Boolean,
    val canEditLots: Boolean,
    val readOnlyNotice: Boolean,
    val siteNameDraft: String,
    val siteNameError: String?,
    val siteRenameWorking: Boolean,
    val lotIds: List<String>,
    val lotNames: List<String>,
    val lotStatuses: List<String>,
    val newLotName: String,
    val newLotNameError: String?,
    val createWorking: Boolean,
    val renamingLotId: String?,
    val renameDraft: String,
    val renameError: String?,
    val renameWorking: Boolean,
    val removingLotId: String?,
    val removingLotName: String?,
    val removeWorking: Boolean,
    val actionNotice: String?,
    val actionNoticeSubject: String?,
)

private fun Enum<*>.key(): String = name.replaceFirstChar { it.lowercase() }

/** The snapshot of [state]. */
public fun snapshotOf(state: LotsState): LotsSnapshot {
    val surface =
        when (state) {
            LotsState.Idle -> "idle"
            is LotsState.Loading -> "loading"
            is LotsState.Failed -> "failed"
            is LotsState.Ready -> "ready"
        }
    val site =
        when (state) {
            LotsState.Idle -> null
            is LotsState.Loading -> state.site
            is LotsState.Failed -> state.site
            is LotsState.Ready -> state.site
        }
    val ready = state as? LotsState.Ready
    val settings = ready?.settings
    val failed = state as? LotsState.Failed
    return LotsSnapshot(
        surface = surface,
        notice = failed?.notice?.key(),
        noticeTryAgain = failed?.notice?.tryAgain == true,
        siteId = site?.id,
        siteName = site?.name,
        role = site?.role?.key(),
        canRenameSite = settings?.canRenameSite == true,
        canEditLots = settings?.canEditLots == true,
        readOnlyNotice = settings?.readOnlyNotice == true,
        siteNameDraft = ready?.siteName?.draft.orEmpty(),
        siteNameError = ready?.siteName?.error?.key(),
        siteRenameWorking = ready?.siteName?.working == true,
        lotIds = ready?.lots?.map { it.id }.orEmpty(),
        lotNames = ready?.lots?.map { it.name }.orEmpty(),
        lotStatuses = ready?.lots?.map { it.status.key }.orEmpty(),
        newLotName = ready?.create?.name.orEmpty(),
        newLotNameError = ready?.create?.error?.key(),
        createWorking = ready?.create?.working == true,
        renamingLotId = ready?.renaming?.lotId,
        renameDraft = ready?.renaming?.draft.orEmpty(),
        renameError = ready?.renaming?.error?.key(),
        renameWorking = ready?.renaming?.working == true,
        removingLotId = ready?.removing?.lotId,
        removingLotName = ready?.removing?.lotName,
        removeWorking = ready?.removing?.working == true,
        actionNotice = ready?.notice?.kind?.key(),
        actionNoticeSubject = ready?.notice?.subject,
    )
}
