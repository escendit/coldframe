package com.escendit.coldframe.core.lots

import com.escendit.coldframe.core.api.LotDto
import com.russhwolf.settings.Settings
import kotlinx.serialization.Serializable
import kotlinx.serialization.SerializationException
import kotlinx.serialization.json.Json

/** The last good answer of `GET /sites/{siteId}/lots`, as the Server sent it, with its time. */
@Serializable
internal data class CachedLots(
    val fetchedAt: Long,
    val lots: List<LotDto>,
)

/**
 * The last good Lots per Site, kept on this device only (UX-DR80). They are the Server's rows in
 * the Server's order; the engine shows them in stale mode until a refresh lands. One key per
 * Site, replaced by every successful refresh, removed when the Server refuses the Site (403,
 * 404) and all removed when the session ends. An entry that cannot be read is dropped.
 */
internal class LotsCache(
    private val settings: Settings,
) {
    fun read(siteId: String): CachedLots? {
        val stored = settings.getStringOrNull(keyOf(siteId)) ?: return null
        return try {
            JSON.decodeFromString(CachedLots.serializer(), stored)
        } catch (_: SerializationException) {
            settings.remove(keyOf(siteId))
            null
        } catch (_: IllegalArgumentException) {
            settings.remove(keyOf(siteId))
            null
        }
    }

    fun store(
        siteId: String,
        lots: List<LotDto>,
        fetchedAtEpochMs: Long,
    ) {
        settings.putString(
            keyOf(siteId),
            JSON.encodeToString(CachedLots.serializer(), CachedLots(fetchedAtEpochMs, lots)),
        )
    }

    fun remove(siteId: String) {
        settings.remove(keyOf(siteId))
    }

    /** Every Site's Lots: the session is over. */
    fun clear() {
        settings.keys.filter { it.startsWith(KEY_PREFIX) }.forEach { settings.remove(it) }
    }

    companion object {
        /** Followed by the Site ID. */
        const val KEY_PREFIX: String = "lots.lastGood."

        private val JSON = Json { ignoreUnknownKeys = true }

        fun keyOf(siteId: String): String = KEY_PREFIX + siteId
    }
}
