package com.escendit.coldframe.android

import com.escendit.coldframe.android.ui.pause.PauseActions
import com.escendit.coldframe.core.sites.PauseForm
import com.escendit.coldframe.core.sites.PauseScope
import com.escendit.coldframe.core.sites.PauseState

/**
 * Pause as the core would hand it over (Story 8.2). The shell tests only render it: which scopes a
 * sheet offers, what its button names and why it stayed open are the core's answers, fixed here.
 */
object PauseFixtures {
    const val NODE_ID = "7c19a1b2c3d4e5f6"

    /** Opened from the Site menu: Whole Site only. */
    val forSite = PauseForm("Home garden", deviceId = null, scope = PauseScope.Site)

    /** Opened from Lot detail or a Devices row: This Device selected. */
    val forDevice = PauseForm("Home garden", deviceId = NODE_ID, scope = PauseScope.Device)

    /** The Server refused the picked date. */
    val refused =
        forDevice.copy(until = "2026-10-06", notice = com.escendit.coldframe.core.sites.PauseNotice.DateRefused)

    /** [PauseActions] that write what was asked into [calls], as "name argument". */
    fun recording(calls: MutableList<String>): PauseActions =
        PauseActions(
            openForSite = { calls += "openForSite" },
            openForDevice = { calls += "openForDevice $it" },
            setScope = { calls += "scope ${it.key}" },
            setUntil = { calls += "until ${it ?: "none"}" },
            cancel = { calls += "cancel" },
            save = { calls += "save" },
            resumeSite = { calls += "resumeSite" },
            resumeDevice = { calls += "resumeDevice $it" },
            retry = { calls += "retry" },
        )

    fun sheet(form: PauseForm): PauseState = PauseState(sheet = form)
}
