package com.escendit.coldframe.core.sites

import com.escendit.coldframe.core.api.SiteDto
import com.escendit.coldframe.core.api.SiteListDto
import com.russhwolf.settings.Settings
import kotlinx.serialization.SerializationException
import kotlinx.serialization.json.Json

/**
 * The last good answer of `GET /sites`, kept on this device only, so that a cold start without
 * the Server still has a current Site and the overview can show its last good Lots in stale mode
 * (UX-DR80). Replaced by every successful read, removed when the session ends. An entry that
 * cannot be read is dropped; an empty list counts as none.
 */
internal class SitesCache(
    private val settings: Settings,
) {
    fun read(): List<SiteDto>? {
        val stored = settings.getStringOrNull(KEY) ?: return null
        return try {
            JSON.decodeFromString(SiteListDto.serializer(), stored).sites.ifEmpty { null }
        } catch (_: SerializationException) {
            clear()
            null
        } catch (_: IllegalArgumentException) {
            clear()
            null
        }
    }

    fun store(sites: List<SiteDto>) {
        settings.putString(KEY, JSON.encodeToString(SiteListDto.serializer(), SiteListDto(sites)))
    }

    fun clear() {
        settings.remove(KEY)
    }

    companion object {
        const val KEY: String = "sites.lastGood"

        private val JSON = Json { ignoreUnknownKeys = true }
    }
}
