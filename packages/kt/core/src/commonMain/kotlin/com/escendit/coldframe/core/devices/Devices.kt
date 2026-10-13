package com.escendit.coldframe.core.devices

import com.escendit.coldframe.core.lots.ChargeState
import com.escendit.coldframe.core.lots.LotDuration
import com.escendit.coldframe.core.lots.LotPauseSource
import com.escendit.coldframe.core.sites.DevicePause
import com.escendit.coldframe.core.sites.SiteRole
import com.escendit.coldframe.core.sites.SiteSummary

/**
 * How a Device is named where its full ID is too long (Story 7.2): the first four hex digits of
 * the Device ID in upper case, as the firmware advertises it ("Hub 3F2A"). Device rows keep the
 * full ID. Both shells take the name from here, so they agree.
 */
public object DeviceName {
    /** The digits of the short name. */
    public const val SHORT_LENGTH: Int = 4

    /** `3f2a9c0d1e4b5a67` → `3F2A`. A shorter ID is given whole. */
    public fun short(deviceId: String): String = deviceId.take(SHORT_LENGTH).uppercase()
}

/** The unit a Silence Window is entered and shown in. [seconds] is one of it. */
public enum class SilenceWindowUnit(
    /** The catalogue key suffix. */
    public val key: String,
    public val seconds: Int,
) {
    Minutes("minutes", 60),
    Hours("hours", 3_600),
    Days("days", 86_400),
    ;

    public companion object {
        public fun fromKey(key: String): SilenceWindowUnit? = entries.firstOrNull { it.key == key }
    }
}

/** A Silence Window as a number with a unit: "8 h", "5 min", "30 d". */
public data class SilenceWindowLength(
    val value: Int,
    val unit: SilenceWindowUnit,
)

/**
 * The rules of a Device's Silence Window (Story 7.2): 1 min to 30 days in whole seconds, as the
 * contract's `SetSilenceWindowRequest` says. The Server stays the validator; a value outside the
 * range is refused here before anything is sent.
 */
public object SilenceWindow {
    /** 1 min. */
    public const val MIN_SECONDS: Int = 60

    /** 30 days. */
    public const val MAX_SECONDS: Int = 2_592_000

    private const val MAX_DIGITS = 7

    public fun valid(seconds: Int): Boolean = seconds in MIN_SECONDS..MAX_SECONDS

    /**
     * What the User typed as seconds: a whole number of [unit]s within the range, else `null`
     * ("90.5", "0", "31" days, an empty field, a word).
     */
    public fun secondsOf(
        text: String,
        unit: SilenceWindowUnit,
    ): Int? {
        val digits = text.trim()
        if (digits.isEmpty() || digits.length > MAX_DIGITS || digits.any { it !in '0'..'9' }) return null
        val seconds = digits.toLong() * unit.seconds
        return if (seconds in MIN_SECONDS..MAX_SECONDS) seconds.toInt() else null
    }

    /**
     * [seconds] in the largest unit that holds it exactly: 28800 → 8 h, 300 → 5 min, 172800 → 2 d.
     * A window that is no whole number of minutes is rounded down to minutes, at least 1.
     */
    public fun lengthOf(seconds: Int): SilenceWindowLength {
        val unit =
            when {
                seconds >= SilenceWindowUnit.Days.seconds && seconds % SilenceWindowUnit.Days.seconds == 0 -> {
                    SilenceWindowUnit.Days
                }

                seconds >= SilenceWindowUnit.Hours.seconds && seconds % SilenceWindowUnit.Hours.seconds == 0 -> {
                    SilenceWindowUnit.Hours
                }

                else -> {
                    SilenceWindowUnit.Minutes
                }
            }
        return SilenceWindowLength((seconds / unit.seconds).coerceAtLeast(1), unit)
    }
}

/**
 * A Hub of the current Site, as the Server listed it. [online] is the Server's, computed when it
 * answered; the client never computes it and never keeps it past a failed reload.
 * [lastSeenAtEpochMs] is the last accepted heartbeat, `null` for a Hub that never sent one.
 *
 * Silence (Story 7.2): [silentSinceEpochMs] is set exactly while the Hub has an open silent Alert
 * (when it was last heard); [silenceWindowSeconds] is the Silence Window in force, `null` when the
 * Server sent none, and [silenceWindowOverridden] whether it is the Hub's own.
 *
 * Pause (Story 8.2): [pausedBy] are the Server's sources (`device`, `site`), empty when the Hub is
 * not paused, and [pausedUntilEpochMs] the end of that Pause when it has one.
 */
public data class HubSummary(
    val id: String,
    val online: Boolean,
    val lastSeenAtEpochMs: Long?,
    val silentSinceEpochMs: Long? = null,
    val silenceWindowSeconds: Int? = null,
    val silenceWindowOverridden: Boolean = false,
    val pausedBy: List<LotPauseSource> = emptyList(),
    val pausedUntilEpochMs: Long? = null,
) {
    /** What the row says and offers about Pause. */
    val pause: DevicePause get() = DevicePause(pausedBy, pausedUntilEpochMs)

    /** "3F2A": the name in "Hub 3F2A". */
    val shortName: String get() = DeviceName.short(id)

    /** The Server says the Hub is silent. */
    val silent: Boolean get() = silentSinceEpochMs != null

    /** How long the Hub has been silent at [nowEpochMs], rounded down ("Silent 12 min"); `null` when it is not. */
    public fun silence(nowEpochMs: Long): LotDuration? = silentSinceEpochMs?.let { LotDuration.between(it, nowEpochMs) }

    /** The Silence Window in force as a number with a unit; `null` when the Server sent none. */
    val silenceWindow: SilenceWindowLength? get() = silenceWindowSeconds?.let(SilenceWindow::lengthOf)
}

/**
 * A Node of the current Site, as the Server listed it, in the Server's order (Lot name, unassigned
 * last, then Device ID). [lotName] is `null` for an unassigned Node; [batteryPercent], [charging]
 * and [lastSeenAtEpochMs] come from its newest device report and are `null` when unknown.
 *
 * Silence (Story 7.2), as on [HubSummary]: [silentSinceEpochMs] exactly while the Node has an open
 * silent Alert, [silenceWindowSeconds] the Silence Window in force and [silenceWindowOverridden]
 * whether it is the Node's own. Pause (Story 8.2), as on [HubSummary]: [pausedBy] and
 * [pausedUntilEpochMs], for a Node on a Lot and for one on none.
 */
public data class NodeSummary(
    val id: String,
    val lotId: String?,
    val lotName: String?,
    val batteryPercent: Int?,
    val batteryLow: Boolean,
    val charging: ChargeState?,
    val lastSeenAtEpochMs: Long?,
    val silentSinceEpochMs: Long? = null,
    val silenceWindowSeconds: Int? = null,
    val silenceWindowOverridden: Boolean = false,
    val pausedBy: List<LotPauseSource> = emptyList(),
    val pausedUntilEpochMs: Long? = null,
) {
    /** What the row says and offers about Pause. */
    val pause: DevicePause get() = DevicePause(pausedBy, pausedUntilEpochMs)

    /** "9C0D": the name in "Node 9C0D". */
    val shortName: String get() = DeviceName.short(id)

    /** The Server says the Node is silent. */
    val silent: Boolean get() = silentSinceEpochMs != null

    /** How long the Node has been silent at [nowEpochMs], rounded down ("Silent 6 h"); `null` when it is not. */
    public fun silence(nowEpochMs: Long): LotDuration? = silentSinceEpochMs?.let { LotDuration.between(it, nowEpochMs) }

    /** The Silence Window in force as a number with a unit; `null` when the Server sent none. */
    val silenceWindow: SilenceWindowLength? get() = silenceWindowSeconds?.let(SilenceWindow::lengthOf)
}

/**
 * A Lot of the Move picker (UX-DR31): a Lot that has another Node, or that the Node is on already,
 * is listed but cannot be picked. [hasNode] is the Server's `noNode` status turned around; the
 * Server still decides, and answers 409 for a Lot that was taken meanwhile.
 */
public data class MoveLotChoice(
    val id: String,
    val name: String,
    val hasNode: Boolean,
    val current: Boolean,
) {
    /** Only a free Lot, other than the Node's own, can be picked. */
    val selectable: Boolean get() = !hasNode && !current
}

/** Why a move or unassign did not happen. Each is inline copy on the Devices surface, naming the Node. */
public enum class NodeActionNotice {
    /** 403: the Role is too low (it may have changed since the list loaded); nothing changed. */
    Forbidden,

    /** 409 `lot-claimed`: the Lot got a Node meanwhile. Pick another Lot. */
    LotTaken,

    /** 404: the Node or the Lot is gone. */
    NotFound,

    /** No answer: nothing changed. */
    Unreachable,

    /** A certificate could not be verified. Never retried insecurely. */
    Certificate,

    /** Any other answer. */
    Unexpected,
}

/** The last move or unassign of [nodeId] that did not happen. */
public data class NodeActionFailure(
    val nodeId: String,
    val notice: NodeActionNotice,
)

/** Why a Device's Silence Window was not changed. Each is inline copy under the Device's row. */
public enum class SilenceWindowNotice {
    /** 403: the Role is too low (it may have changed since the list loaded): "You can't change this on ‹Site›…". */
    Forbidden,

    /** 404: the Device is gone. */
    NotFound,

    /** Outside 1 min to 30 days: refused before sending, or 400 `validation` from the Server. */
    OutOfRange,

    /** No answer: nothing changed. */
    Unreachable,

    /** A certificate could not be verified. Never retried insecurely. */
    Certificate,

    /** Any other answer. */
    Unexpected,
}

/** The last Silence Window change of [deviceId] (a Hub or a Node) that did not happen. */
public data class SilenceWindowFailure(
    val deviceId: String,
    val notice: SilenceWindowNotice,
)

/** Why the Devices could not be read. No row is shown with it, so nothing stays "Online". */
public enum class DevicesNotice(
    public val tryAgain: Boolean,
) {
    /** No answer, or an answer that is not the list: "Can't reach your Server." */
    Unreachable(true),

    /** A certificate could not be verified. Never retried insecurely. */
    Certificate(false),
}

/** The Devices surface of the current Site. Follows the current Site of the Sites engine. */
public sealed interface DevicesState {
    /** Signed out, or no current Site. */
    public data object Idle : DevicesState

    /** The list is being read. No row is shown meanwhile: an earlier answer's "Online" is not kept. */
    public data class Loading(
        val site: SiteSummary,
    ) : DevicesState

    /** The list could not be read; no rows, [notice] in their place. */
    public data class Failed(
        val site: SiteSummary,
        val notice: DevicesNotice,
    ) : DevicesState

    /**
     * [hubs] are the Site's Hubs by Device ID, [nodes] its Nodes in the Server's order, never re-sorted.
     * [lots] are the Site's Lots in the Server's order, read only for a Role that may move Nodes.
     * [workingNodeId] is the Node whose move or unassign is under way; [failure] the last one that
     * did not happen. [silenceWindowWorkingId] is the Device (Hub or Node) whose Silence Window is
     * being saved; [silenceWindowFailure] the last such change that did not happen.
     */
    public data class Ready(
        val site: SiteSummary,
        val hubs: List<HubSummary>,
        val nodes: List<NodeSummary> = emptyList(),
        val lots: List<LotOption> = emptyList(),
        val workingNodeId: String? = null,
        val failure: NodeActionFailure? = null,
        val silenceWindowWorkingId: String? = null,
        val silenceWindowFailure: SilenceWindowFailure? = null,
    ) : DevicesState {
        /** The picker of [node]: every Lot in the Server's order, the ones that cannot be picked marked. */
        public fun choicesFor(node: NodeSummary): List<MoveLotChoice> =
            lots.map {
                MoveLotChoice(
                    it.id,
                    it.name,
                    hasNode = it.hasNode && it.id != node.lotId,
                    current =
                        it.id == node.lotId,
                )
            }
    }

    public companion object {
        /**
         * Add a Hub is for Owners and Administrators (UX-DR84): hidden for a Member, never disabled.
         */
        public fun canAddHub(role: SiteRole): Boolean = role >= SiteRole.Administrator

        /**
         * Move a Node to another Lot and Unassign are for Owners and Administrators (UX-DR31) and need
         * no Bluetooth: hidden for a Member, never disabled.
         */
        public fun canManageNodes(role: SiteRole): Boolean = role >= SiteRole.Administrator

        /** Add a Node follows the same rule: Owners and Administrators only, hidden for a Member. */
        public fun canAddNode(role: SiteRole): Boolean = canAddHub(role)

        /**
         * The Silence Window of a Hub or Node is changed by Owners and Administrators only (Story 7.2):
         * the control does not exist for a Member, it is never merely disabled.
         */
        public fun canSetSilenceWindow(role: SiteRole): Boolean = role >= SiteRole.Administrator

        /**
         * Pause and Resume of a Hub or Node are for Owners and Administrators (Story 8.2): the controls
         * do not exist for a Member. Which of them a row has is [DevicePause]'s answer.
         */
        public fun canPause(role: SiteRole): Boolean = DevicePause.allowed(role)
    }
}

/** A Lot of the Site as the Move picker reads it: [hasNode] is true unless the Server's status is `noNode`. */
public data class LotOption(
    val id: String,
    val name: String,
    val hasNode: Boolean,
)

/** The Site the surface is on, or `null` while [DevicesState.Idle]. */
public val DevicesState.site: SiteSummary?
    get() =
        when (this) {
            DevicesState.Idle -> null
            is DevicesState.Loading -> site
            is DevicesState.Failed -> site
            is DevicesState.Ready -> site
        }

/** Whether the Add a Hub header action shows: a current Site on which the Role is Administrator or Owner. */
public val DevicesState.canAddHub: Boolean
    get() = site?.let { DevicesState.canAddHub(it.role) } == true

/** Whether the Add a Node header action shows: the same rule as [canAddHub]. */
public val DevicesState.canAddNode: Boolean
    get() = site?.let { DevicesState.canAddNode(it.role) } == true

/** Whether Move and Unassign show on Node rows: a current Site on which the Role is Administrator or Owner. */
public val DevicesState.canManageNodes: Boolean
    get() = site?.let { DevicesState.canManageNodes(it.role) } == true

/** Whether the Silence Window action shows on Device rows: a current Site on which the Role is Administrator or Owner. */
public val DevicesState.canSetSilenceWindow: Boolean
    get() = site?.let { DevicesState.canSetSilenceWindow(it.role) } == true

/** Whether Pause and Resume show on Device rows: a current Site on which the Role is Administrator or Owner. */
public val DevicesState.canPause: Boolean
    get() = site?.let { DevicesState.canPause(it.role) } == true
