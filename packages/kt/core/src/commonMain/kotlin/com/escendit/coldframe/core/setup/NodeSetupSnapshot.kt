package com.escendit.coldframe.core.setup

/**
 * [NodeSetupState] flattened for Swift. Enum values cross as catalogue key suffixes
 * (`stoppedListening`, `lotTaken`, `strong`, …), lists as parallel lists in the engine's order.
 * The setup code crosses only as the text of its own field; no key, sealed payload, token or URL
 * crosses this boundary.
 */
public data class NodeSetupSnapshot(
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
    val candidatePressed: List<Boolean>,
    val noNodeYet: Boolean,
    val selectedId: String?,
    /** The selected Node's four digits ("7C19"). */
    val node: String?,
    val codeText: String,
    val codeError: String?,
    val codeWorking: Boolean,
    val codeAccepted: Boolean,
    val deviceId: String?,
    val siteName: String,
    val lotsLoaded: Boolean,
    val lotIds: List<String>,
    val lotNames: List<String>,
    val lotSelectable: List<Boolean>,
    val selectedLotId: String?,
    /** The picked Lot's name, for the primary button and the outcome. */
    val lotName: String?,
    val newLotOpen: Boolean,
    val newLotName: String,
    /** `blank` or `tooLong`. */
    val newLotError: String?,
    val creatingLot: Boolean,
    val assigning: Boolean,
    val lotNotice: String?,
    val lotNoticeLot: String?,
    val lotsRetryable: Boolean,
    val outcome: String?,
    val outcomePrimary: String?,
    val stoppedStep: Int,
    val confirmingLeave: Boolean,
    val announcementId: Int,
    val announcementKind: String?,
    val announcementAssertive: Boolean,
    val announcementNode: String?,
    val announcementLot: String?,
    val announcementSignal: String?,
    val announcementOutcome: String?,
) {
    /** Never prints the setup code. */
    override fun toString(): String =
        "NodeSetupSnapshot(open=$open, step=$step, node=$node, codeText=<redacted>, " +
            "outcome=$outcome, announcementId=$announcementId)"
}

private fun Enum<*>.key(): String = name.replaceFirstChar { it.lowercase() }

/** The [NodeOutcomeAction] of an `outcomePrimary` key, or null for an unknown one. */
internal fun nodeOutcomeActionOf(key: String): NodeOutcomeAction? =
    NodeOutcomeAction.entries.firstOrNull { it.key() == key }

/** The snapshot of [state]. */
public fun snapshotOf(state: NodeSetupState): NodeSetupSnapshot {
    val choices = state.lots.choices.orEmpty()
    val outcome = state.outcome
    val announcement = state.announcement
    return NodeSetupSnapshot(
        open = state.open,
        step = state.step.number,
        keepAwake = state.keepAwake,
        showsCancel = state.showsCancel,
        radio = state.radio.key(),
        candidateIds = state.candidates.map { it.peripheralId },
        candidateNames = state.candidates.map { it.shortId },
        candidateSignals = state.candidates.map { it.signal.key() },
        candidatePressed = state.candidates.map { it.pressedJustNow },
        noNodeYet = state.noNodeYet,
        selectedId = state.selected?.peripheralId,
        node = state.nodeId,
        codeText = state.code.text,
        codeError = state.code.error?.key(),
        codeWorking = state.code.working,
        codeAccepted = state.code.accepted,
        deviceId = state.deviceId,
        siteName = state.siteName,
        lotsLoaded = state.lots.choices != null,
        lotIds = choices.map { it.id },
        lotNames = choices.map { it.name },
        lotSelectable = choices.map { it.selectable },
        selectedLotId = state.lots.selected?.id,
        lotName = state.lotName,
        newLotOpen = state.lots.newLotOpen,
        newLotName = state.lots.newLotName,
        newLotError = state.lots.newLotError?.key(),
        creatingLot = state.lots.creating,
        assigning = state.lots.assigning,
        lotNotice =
            state.lots.notice
                ?.kind
                ?.key(),
        lotNoticeLot = state.lots.notice?.lotName,
        lotsRetryable = state.lots.retryable,
        outcome = outcome?.kind?.key(),
        outcomePrimary = outcome?.primary?.key(),
        stoppedStep = outcome?.stoppedStep ?: 0,
        confirmingLeave = state.confirmingLeave,
        announcementId = announcement?.id ?: 0,
        announcementKind = announcement?.kind?.key(),
        announcementAssertive = announcement?.assertive == true,
        announcementNode = announcement?.node,
        announcementLot = announcement?.lot,
        announcementSignal = announcement?.signal?.key(),
        announcementOutcome = announcement?.outcome?.key(),
    )
}
