package com.escendit.coldframe.core.setup

/**
 * [HubSetupState] flattened for Swift. Enum values cross as catalogue key suffixes
 * (`wrongPassword`, `wpa3Only`, `strong`, …), lists as parallel lists in the engine's order.
 * The setup code and the Wi-Fi password cross only as the text of their own fields; no key,
 * token or URL crosses this boundary.
 */
public data class HubSetupSnapshot(
    val open: Boolean,
    /** 1 to 5. */
    val step: Int,
    val keepAwake: Boolean,
    val showsCancel: Boolean,
    /** `ready`, `off`, `unauthorized` or `unsupported`. */
    val radio: String,
    val candidateIds: List<String>,
    val candidateNames: List<String>,
    val candidateSignals: List<String>,
    val noHubYet: Boolean,
    val selectedId: String?,
    /** The selected Hub's four digits ("3F2A"). */
    val hub: String?,
    val codeText: String,
    val codeError: String?,
    val codeWorking: Boolean,
    val codeAccepted: Boolean,
    val deviceId: String?,
    val networksLoaded: Boolean,
    val networkSsids: List<String>,
    val networkSecurities: List<String>,
    val networkSupported: List<Boolean>,
    val ssid: String,
    val otherNetwork: Boolean,
    val password: String,
    val wifiError: String?,
    val siteIds: List<String>,
    val siteNames: List<String>,
    val selectedSiteId: String?,
    val fingerprint: String?,
    val loadingKey: Boolean,
    val keyNotice: String?,
    /** How many of the four segments are done. */
    val progressReached: Int,
    val elapsedSeconds: Int,
    val outcome: String?,
    val outcomePrimary: String?,
    val outcomeSecondary: String?,
    val helpShown: Boolean,
    val confirmingLeave: Boolean,
    val announcementId: Int,
    val announcementKind: String?,
    val announcementAssertive: Boolean,
    val announcementHub: String?,
    val announcementSsid: String?,
    val announcementSignal: String?,
    val announcementOutcome: String?,
    val serverHost: String,
) {
    /** Never prints the setup code or the Wi-Fi password. */
    override fun toString(): String =
        "HubSetupSnapshot(open=$open, step=$step, hub=$hub, codeText=<redacted>, password=<redacted>, " +
            "outcome=$outcome, announcementId=$announcementId)"
}

private fun Enum<*>.key(): String = name.replaceFirstChar { it.lowercase() }

/** The [OutcomeAction] of an `outcomePrimary` / `outcomeSecondary` key, or null for an unknown one. */
internal fun outcomeActionOf(key: String): OutcomeAction? = OutcomeAction.entries.firstOrNull { it.key() == key }

/** The snapshot of [state]. */
public fun snapshotOf(state: HubSetupState): HubSetupSnapshot {
    val networks = state.wifi.networks.orEmpty()
    val outcome = state.outcome
    val announcement = state.announcement
    return HubSetupSnapshot(
        open = state.open,
        step = state.step.number,
        keepAwake = state.keepAwake,
        showsCancel = state.showsCancel,
        radio = state.radio.key(),
        candidateIds = state.candidates.map { it.peripheralId },
        candidateNames = state.candidates.map { it.shortId },
        candidateSignals = state.candidates.map { it.signal.key() },
        noHubYet = state.noHubYet,
        selectedId = state.selected?.peripheralId,
        hub = state.hubId,
        codeText = state.code.text,
        codeError = state.code.error?.key(),
        codeWorking = state.code.working,
        codeAccepted = state.code.accepted,
        deviceId = state.identity?.deviceId,
        networksLoaded = state.wifi.networks != null,
        networkSsids = networks.map { it.ssid },
        networkSecurities = networks.map { it.security.key() },
        networkSupported = networks.map { it.security.supported },
        ssid = state.wifi.ssid,
        otherNetwork = state.wifi.other,
        password = state.wifi.password,
        wifiError = state.wifi.error?.key(),
        siteIds = state.site.sites.map { it.id },
        siteNames = state.site.sites.map { it.name },
        selectedSiteId = state.site.selectedId,
        fingerprint = state.site.fingerprint,
        loadingKey = state.site.loadingKey,
        keyNotice = state.site.notice?.key(),
        progressReached = state.progress.reached,
        elapsedSeconds = state.progress.elapsedSeconds,
        outcome = outcome?.kind?.key(),
        outcomePrimary = outcome?.primary?.key(),
        outcomeSecondary = outcome?.secondary?.key(),
        helpShown = outcome?.helpShown == true,
        confirmingLeave = state.confirmingLeave,
        announcementId = announcement?.id ?: 0,
        announcementKind = announcement?.kind?.key(),
        announcementAssertive = announcement?.assertive == true,
        announcementHub = announcement?.hub,
        announcementSsid = announcement?.ssid,
        announcementSignal = announcement?.signal?.key(),
        announcementOutcome = announcement?.outcome?.key(),
        serverHost = state.serverHost,
    )
}
