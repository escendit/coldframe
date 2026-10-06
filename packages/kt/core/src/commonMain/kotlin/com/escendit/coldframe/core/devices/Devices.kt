package com.escendit.coldframe.core.devices

import com.escendit.coldframe.core.sites.SiteRole
import com.escendit.coldframe.core.sites.SiteSummary

/**
 * A Hub of the current Site, as the Server listed it. [online] is the Server's, computed when it
 * answered; the client never computes it and never keeps it past a failed reload.
 * [lastSeenAtEpochMs] is the last accepted heartbeat, `null` for a Hub that never sent one.
 */
public data class HubSummary(
    val id: String,
    val online: Boolean,
    val lastSeenAtEpochMs: Long?,
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

    /** [hubs] are the Site's Hubs by Device ID. Nodes are listed by the Server but not shown yet. */
    public data class Ready(
        val site: SiteSummary,
        val hubs: List<HubSummary>,
    ) : DevicesState

    public companion object {
        /**
         * Add a Hub is for Owners and Administrators (UX-DR84): hidden for a Member, never disabled.
         */
        public fun canAddHub(role: SiteRole): Boolean = role >= SiteRole.Administrator
    }
}

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
