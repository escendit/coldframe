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
 * settings. [paused] is the Server's `Site.paused`: a paused Site offers Resume, any other Pause,
 * which opens the Pause sheet. In stale mode every item is disabled with "Needs your Server".
 * [pauseAvailable] is false only for a surface that cannot pause.
 */
public object SiteMenu {
    public fun items(
        role: SiteRole,
        pauseAvailable: Boolean = true,
        stale: Boolean = false,
        paused: Boolean = false,
    ): List<SiteMenuItem> =
        buildList {
            if (pauseAvailable && role >= SiteRole.Administrator) {
                add(SiteMenuItem(if (paused) SiteMenuAction.Resume else SiteMenuAction.Pause, enabled = !stale))
            }
            add(SiteMenuItem(SiteMenuAction.SiteSettings, enabled = !stale))
        }

    /** The menu of [site], with Pause or Resume as the Server says the Site is. */
    public fun items(
        site: SiteSummary,
        stale: Boolean = false,
    ): List<SiteMenuItem> = items(site.role, stale = stale, paused = site.paused)
}
