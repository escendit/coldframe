package com.escendit.coldframe.core.sites

/**
 * [SitesState] flattened for Swift. Enum values cross as catalogue key suffixes (`owner`,
 * `addHub`, `keyReused`, …) and the Sites as parallel lists in the Server's order. No token or
 * URL ever crosses this boundary.
 *
 * [surface] is `idle`, `loading`, `failed`, `needsSite` or `ready`. [formShown] is true on
 * `needsSite`, and on `ready` while Create Site is open from "New Site". The Garden fields
 * ([steps], [menuItems], …) describe [currentId] and are empty unless `ready`.
 */
public data class SitesSnapshot(
    val surface: String,
    val notice: String?,
    val noticeTryAgain: Boolean,
    val siteIds: List<String>,
    val siteNames: List<String>,
    val siteRoles: List<String>,
    val currentId: String?,
    val currentName: String?,
    val currentRole: String?,
    val formShown: Boolean,
    val formName: String,
    val formNameError: String?,
    val formWorking: Boolean,
    val formNotice: String?,
    val formNoticeTryAgain: Boolean,
    val formCancellable: Boolean,
    val timeZoneDetected: String,
    val timeZoneChosen: String?,
    val timeZoneChanging: Boolean,
    val steps: List<String>,
    val stepStates: List<String>,
    val stepsActionable: Boolean,
    val memberNotice: Boolean,
    val menuItems: List<String>,
    val menuEnabled: Boolean,
)

/** The catalogue key suffix of an enum value: `KeyReused` → `keyReused`. */
internal fun Enum<*>.key(): String = name.replaceFirstChar { it.lowercase() }

/** The snapshot of [state]. */
public fun snapshotOf(state: SitesState): SitesSnapshot {
    val surface =
        when (state) {
            SitesState.Idle -> "idle"
            SitesState.Loading -> "loading"
            is SitesState.Failed -> "failed"
            is SitesState.NeedsSite -> "needsSite"
            is SitesState.Ready -> "ready"
        }
    val ready = state as? SitesState.Ready
    val form =
        when (state) {
            is SitesState.NeedsSite -> state.form
            is SitesState.Ready -> state.creating
            else -> null
        }
    val notice = (state as? SitesState.Failed)?.notice
    val steps = ready?.let { FirstRunSteps.of(it.current.role) }
    val menu = ready?.let { SiteMenu.items(it.current.role) }.orEmpty()
    return SitesSnapshot(
        surface = surface,
        notice = notice?.key(),
        noticeTryAgain = notice?.tryAgain == true,
        siteIds = ready?.sites?.map { it.id }.orEmpty(),
        siteNames = ready?.sites?.map { it.name }.orEmpty(),
        siteRoles = ready?.sites?.map { it.role.key() }.orEmpty(),
        currentId = ready?.current?.id,
        currentName = ready?.current?.name,
        currentRole = ready?.current?.role?.key(),
        formShown = form != null,
        formName = form?.name.orEmpty(),
        formNameError = form?.nameError?.key(),
        formWorking = form?.working == true,
        formNotice = form?.notice?.key(),
        formNoticeTryAgain = form?.notice?.tryAgain == true,
        formCancellable = form?.cancellable == true,
        timeZoneDetected = form?.timeZone?.detected.orEmpty(),
        timeZoneChosen = form?.timeZone?.chosen,
        timeZoneChanging = form?.timeZone?.changing == true,
        steps = steps?.tiles?.map { it.step.key() }.orEmpty(),
        stepStates = steps?.tiles?.map { it.state.key() }.orEmpty(),
        stepsActionable = steps?.actionable == true,
        memberNotice = steps?.memberNotice == true,
        menuItems = menu.map { it.action.key() },
        menuEnabled = menu.all { it.enabled },
    )
}
