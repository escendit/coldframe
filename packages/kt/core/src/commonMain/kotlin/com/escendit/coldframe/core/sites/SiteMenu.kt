package com.escendit.coldframe.core.sites

/** What a Site menu item does. */
public enum class SiteMenuAction {
    Pause,
    Resume,
    SiteSettings,
}

/** One Site menu item; a disabled item shows "Needs your Server". */
public data class SiteMenuItem(
    val action: SiteMenuAction,
    val enabled: Boolean,
)

/**
 * The Site menu (UX-DR22): Pause or Resume the Site (Admin+, hidden for Members), then Site
 * settings. In stale mode every item is disabled with "Needs your Server". Story 1.8 renders it
 * with [pauseAvailable] false, because the Pause sheet arrives later and a Pause item that does
 * nothing would break "controls you cannot use are hidden".
 */
public object SiteMenu {
    public fun items(
        role: SiteRole,
        pauseAvailable: Boolean = false,
        stale: Boolean = false,
        paused: Boolean = false,
    ): List<SiteMenuItem> =
        buildList {
            if (pauseAvailable && role >= SiteRole.Administrator) {
                add(SiteMenuItem(if (paused) SiteMenuAction.Resume else SiteMenuAction.Pause, enabled = !stale))
            }
            add(SiteMenuItem(SiteMenuAction.SiteSettings, enabled = !stale))
        }
}
