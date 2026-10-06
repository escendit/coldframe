package com.escendit.coldframe.core.setup

import com.escendit.coldframe.core.sites.NameError

/** The five steps of Add a Node (UX-DR67), "01 / 05" to "05 / 05". */
public enum class NodeSetupStep {
    /** "Press the setup button on the Node": an instruction, no radio yet. */
    Press,

    /** The Nodes in BLE range. */
    Scan,

    /** The setup code; the BLE session runs here. */
    Code,

    /** The Lot picker; no BLE. */
    Lot,

    /** "Tomatoes has a Node". */
    Outcome,
    ;

    /** 1 to 5. */
    public val number: Int get() = ordinal + 1

    public companion object {
        public const val COUNT: Int = 5
    }
}

/**
 * A Node in setup mode in BLE range (UX-DR37): [shortId] is the four digits of its advertised
 * name ("7C19"); [peripheralId] is never shown. [pressedJustNow] marks the one candidate that was
 * first heard most recently. Neither the advert nor `Identity` carries a battery level or a
 * Sensor count, so the tile shows none.
 */
public data class NodeCandidate(
    val peripheralId: String,
    val shortId: String,
    val rssi: Int,
    val pressedJustNow: Boolean,
) {
    val signal: SignalStrength get() = SignalStrength.of(rssi)
}

/** One Lot of the picker (UX-DR38). A Lot that has a Node is listed but cannot be picked. */
public data class LotChoice(
    val id: String,
    val name: String,
    val selectable: Boolean,
)

/** Why step 4 could not go on. Every one leaves the sealed key in memory for another try. */
public enum class LotPickerNoticeKind {
    /** The Lots could not be read: no answer. */
    Unreachable,

    /** The Lots could not be read: a certificate could not be verified. */
    Certificate,

    /** The Lots could not be read, or the new Lot could not be created: another answer. */
    Unexpected,

    /** 409 `lot-claimed`: "Tomatoes got a Node meanwhile. Pick another Lot." */
    LotTaken,

    /** 404: "That Lot is gone. Pick another Lot." */
    LotGone,

    /** No answer, 5xx, 422 or 400 on the assignment: "Can't reach your Server. Nothing was assigned." */
    AssignFailed,
}

/** An Inline notice on step 4; [lotName] is the Lot of [LotPickerNoticeKind.LotTaken]. */
public data class LotPickerNotice(
    val kind: LotPickerNoticeKind,
    val lotName: String? = null,
)

/**
 * Step 4. [choices] is null while the Lots are being read (or could not be). [newLotOpen] shows
 * the inline name field of "+ New Lot". [creating] and [assigning] put the progress label on
 * their buttons.
 */
public data class LotForm(
    val choices: List<LotChoice>?,
    val selectedId: String?,
    val newLotOpen: Boolean,
    val newLotName: String,
    val newLotError: NameError?,
    val creating: Boolean,
    val assigning: Boolean,
    val notice: LotPickerNotice?,
) {
    /** Try again shows only while the list itself is unread; a certificate failure is never retried. */
    val retryable: Boolean
        get() = choices == null && notice != null && notice.kind != LotPickerNoticeKind.Certificate

    /** The picked Lot, when it can still be picked. */
    val selected: LotChoice? get() = choices?.firstOrNull { it.id == selectedId && it.selectable }

    public companion object {
        public val EMPTY: LotForm =
            LotForm(
                choices = null,
                selectedId = null,
                newLotOpen = false,
                newLotName = "",
                newLotError = null,
                creating = false,
                assigning = false,
                notice = null,
            )
    }
}

/** Every way Add a Node ends on an outcome screen (UX-DR55). */
public enum class NodeOutcomeKind {
    /** 201: "Tomatoes has a Node". */
    Assigned,

    /** The Node could not be connected to: its setup window is over. Try again from step 1. */
    StoppedListening,

    /** The link dropped, or no reply came, in the middle of the session. Try again from step 1. */
    LostConnection,

    /** Not a Node, a `SetupError`, or something the flow did not expect. */
    NodeRefused,

    /** The enrolment key could not be read. Try again from step 1. */
    ServerUnreachable,

    /** The key's SHA-256 is not its fingerprint, or the Node said `FINGERPRINT_MISMATCH`. */
    FingerprintMismatch,

    /** 409 `device-assigned`: the Node is in another Lot already. */
    AlreadyAssigned,

    /** 409 `device-on-another-site`. */
    OnAnotherSite,

    /** 403: the caller may not add Devices to the Site. */
    NotAllowed,
}

/** The one action of an Add a Node outcome screen. */
public enum class NodeOutcomeAction {
    /** Closes the flow after "Tomatoes has a Node". */
    Done,

    /** Back to step 1. */
    StartOver,

    /** Closes the flow. */
    Close,
}

/** An outcome and the step it stopped on ([stoppedStep], 3 or 4; 5 for the success). */
public data class NodeOutcome(
    val kind: NodeOutcomeKind,
    val stoppedStep: Int,
) {
    val success: Boolean get() = kind == NodeOutcomeKind.Assigned

    val primary: NodeOutcomeAction
        get() =
            when (kind) {
                NodeOutcomeKind.Assigned -> NodeOutcomeAction.Done

                NodeOutcomeKind.StoppedListening,
                NodeOutcomeKind.LostConnection,
                NodeOutcomeKind.ServerUnreachable,
                -> NodeOutcomeAction.StartOver

                NodeOutcomeKind.NodeRefused,
                NodeOutcomeKind.FingerprintMismatch,
                NodeOutcomeKind.AlreadyAssigned,
                NodeOutcomeKind.OnAnotherSite,
                NodeOutcomeKind.NotAllowed,
                -> NodeOutcomeAction.Close
            }
}

/** A screen-reader announcement of Add a Node, made once per id (UX-DR105). */
public enum class NodeAnnouncementKind {
    /** Polite, once per Device: "Node 7C19 found, strong signal." */
    CandidateFound,

    /** Assertive: the wrong-code reason. */
    WrongCode,

    /** Assertive: "Tomatoes got a Node meanwhile. Pick another Lot." */
    LotTaken,

    /** Assertive: an error outcome's headline and next action. */
    Error,

    /** Polite: "Tomatoes has a Node." */
    Assigned,
}

public data class NodeAnnouncement(
    val id: Int,
    val kind: NodeAnnouncementKind,
    val assertive: Boolean,
    /** The Node's four digits, the Lot's name, the signal or the outcome, as the copy needs. */
    val node: String?,
    val lot: String?,
    val signal: SignalStrength?,
    val outcome: NodeOutcomeKind?,
)

/**
 * Add a Node as the shells render it (UX-DR67). [open] is false while the flow is closed.
 * [outcome] replaces the step with an outcome screen. [confirmingLeave] shows "Stop setting up
 * Node 7C19? Nothing is saved on the Node." [siteId] and [siteName] are the one Site the flow
 * runs on. [deviceId] is the Node's full ID once its code was accepted; the sealed key the engine
 * holds from then on is not part of the state.
 */
public data class NodeSetupState(
    val open: Boolean,
    val step: NodeSetupStep,
    val radio: RadioState,
    val candidates: List<NodeCandidate>,
    val noNodeYet: Boolean,
    val selected: NodeCandidate?,
    val code: CodeForm,
    val deviceId: String?,
    val siteId: String,
    val siteName: String,
    val lots: LotForm,
    val outcome: NodeOutcome?,
    val confirmingLeave: Boolean,
    val announcement: NodeAnnouncement?,
) {
    val keepAwake: Boolean get() = open

    /** Cancel on step 1, Back afterwards (UX-DR39); an outcome screen has neither. */
    val showsCancel: Boolean get() = step == NodeSetupStep.Press

    /** The Node's short name ("7C19") once one is selected. */
    val nodeId: String? get() = selected?.shortId

    /** The Lot the outcome and the primary button name. */
    val lotName: String? get() = lots.choices?.firstOrNull { it.id == lots.selectedId }?.name

    public companion object {
        public val CLOSED: NodeSetupState =
            NodeSetupState(
                open = false,
                step = NodeSetupStep.Press,
                radio = RadioState.Ready,
                candidates = emptyList(),
                noNodeYet = false,
                selected = null,
                code = CodeForm("", error = null, working = false, accepted = false),
                deviceId = null,
                siteId = "",
                siteName = "",
                lots = LotForm.EMPTY,
                outcome = null,
                confirmingLeave = false,
                announcement = null,
            )
    }
}
