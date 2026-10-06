package com.escendit.coldframe.android

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.escendit.coldframe.R
import org.json.JSONObject
import org.junit.Test
import org.junit.runner.RunWith
import org.w3c.dom.Element
import javax.xml.parsers.DocumentBuilderFactory
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

@RunWith(AndroidJUnit4::class)
class StringsTest {
    private val context: Context = ApplicationProvider.getApplicationContext()

    /** Every entry of strings.xml, plural forms as `key.form`, unescaped, placeholders as `%#`. */
    private val android: Map<String, String> by lazy {
        val document = DocumentBuilderFactory.newInstance().newDocumentBuilder().parse(Repo.file(STRINGS_XML))
        val entries = mutableMapOf<String, String>()
        val children = document.documentElement.childNodes
        for (index in 0 until children.length) {
            val element = children.item(index) as? Element ?: continue
            val name = element.getAttribute("name")
            when (element.tagName) {
                "string" -> {
                    entries[name] = normalise(element.textContent)
                }

                "plurals" -> {
                    val items = element.getElementsByTagName("item")
                    for (item in 0 until items.length) {
                        val form = items.item(item) as Element
                        entries["$name.${form.getAttribute("quantity")}"] = normalise(form.textContent)
                    }
                }
            }
        }
        entries
    }

    /** Every entry of the iOS catalogue, in the same shape. */
    private val ios: Map<String, String> by lazy {
        val strings = JSONObject(Repo.file(XCSTRINGS).readText()).getJSONObject("strings")
        val entries = mutableMapOf<String, String>()
        for (key in strings.keys()) {
            val english = strings.getJSONObject(key).getJSONObject("localizations").getJSONObject("en")
            if (english.has("variations")) {
                val plural = english.getJSONObject("variations").getJSONObject("plural")
                for (form in plural.keys()) {
                    entries["$key.$form"] =
                        normalise(plural.getJSONObject(form).getJSONObject("stringUnit").getString("value"))
                }
            } else {
                entries[key] = normalise(english.getJSONObject("stringUnit").getString("value"))
            }
        }
        entries
    }

    private fun normalise(value: String): String =
        value
            .replace("\\'", "'")
            .replace("\\\"", "\"")
            .replace(Regex("%(\\d+\\$)?(lld|d|@|s)"), "%#")

    private val glossary: List<String> by lazy {
        val terms = JSONObject(Repo.file("apps/ts/web/src/lib/i18n/glossary.json").readText()).getJSONArray("terms")
        (0 until terms.length()).map { terms.getString(it) }
    }

    private val web: JSONObject by lazy { JSONObject(Repo.file("apps/ts/web/src/lib/i18n/en.json").readText()) }

    @Test
    fun `UX-DR124 strings xml and the iOS catalogue have the same keys and English values`() {
        assertEquals(android.keys.sorted(), ios.keys.sorted())
        for ((key, value) in android) assertEquals(value, ios[key], key)
    }

    @Test
    fun `UX-DR124 the Sign-in notices are the UX-DR92 and UX-DR93 copy of the web catalogue, verbatim`() {
        val pairs =
            mapOf(
                "notice_unreachable" to "notice.unreachable",
                "notice_certificate" to "notice.certificate",
                "notice_keycloak" to "notice.keycloak",
                "notice_signed_out" to "notice.signedOut",
                "notice_try_again" to "notice.tryAgain",
                "notice_sign_in" to "notice.signIn",
                "signin_action" to "signin.action",
                "signin_working" to "signin.working",
            )
        for ((mobile, webKey) in pairs) assertEquals(web.getString(webKey), android[mobile], mobile)
    }

    @Test
    fun `UX-DR124 browser copy says phone on mobile`() {
        assertEquals("Applies at once and only on this phone.", android["appearance_theme_helper"])
        assertEquals("Sign out of Coldframe on this phone?", android["settings_sign_out_question"])
        assertFalse(android.values.any { it.contains("browser", ignoreCase = true) })
    }

    /**
     * "OK" is the name of the `ok` Lot status and appears nowhere else: only in keys with an `ok`
     * segment (the tile label, its stale and spoken forms, and the count of OK Lots).
     */
    private fun namesOkStatus(key: String): Boolean = key.split('_').contains("ok")

    @Test
    fun `UX-DR130 no exclamation marks, emoji, successfully, OK, fine or all good`() {
        for ((key, value) in android) {
            assertFalse(value.contains('!'), key)
            assertFalse(
                // The degree sign of "°C" is a unit, not an emoji.
                value.codePoints().anyMatch {
                    it != DEGREE_SIGN && Character.getType(it) == Character.OTHER_SYMBOL.toInt()
                },
                "$key has an emoji",
            )
            assertFalse(
                Regex(
                    "successfully|\\bokay\\b|\\bfine\\b|all good",
                    RegexOption.IGNORE_CASE,
                ).containsMatchIn(value),
                key,
            )
            if (!namesOkStatus(key)) {
                assertFalse(Regex("\\bOK\\b", RegexOption.IGNORE_CASE).containsMatchIn(value), key)
            }
        }
    }

    @Test
    fun `UX-DR130 OK names the ok status only, its tile label, its stale and spoken forms, and its count`() {
        val allowed = android.filterValues { Regex("\\bOK\\b", RegexOption.IGNORE_CASE).containsMatchIn(it) }
        assertEquals(
            listOf(
                "garden_count_ok",
                "lot_tile_label_ok",
                "lot_tile_spoken_ok",
                "lot_tile_spoken_was_ok",
                "lot_tile_was_ok",
            ),
            allowed.keys.sorted(),
        )
        // Always the last word, in capitals: "OK", "Was OK", "2 OK".
        for ((key, value) in allowed) assertTrue(Regex("\\bOK$").containsMatchIn(value), key)
    }

    @Test
    fun `UX-DR130 UX-DR124 uppercase comes from style, never from the string`() {
        // Acronyms are words, not style: the Hub's LED (UX-DR94) and its Device ID.
        val acronyms = setOf("LED", "ID")
        for ((key, value) in android) {
            // "OK" is a word written in capitals; it is allowed only where the test above allows it.
            val words = if (namesOkStatus(key)) acronyms + "OK" else acronyms
            val shouting = Regex("\\b\\p{Lu}{2,}\\b").findAll(value).map { it.value }.filterNot { it in words }
            assertFalse(shouting.any(), key)
        }
    }

    @Test
    fun `UX-DR131 glossary terms are capitalised wherever they appear`() {
        assertTrue(glossary.contains("Site") && glossary.contains("Notification Window"))
        for ((key, value) in android) {
            for (term in glossary) {
                Regex("\\b${Regex.escape(term)}s?\\b", RegexOption.IGNORE_CASE).findAll(value).forEach { match ->
                    val expected = term.split(' ').map { it.first() }
                    val actual = match.value.split(' ').map { it.first() }
                    assertEquals(expected, actual, "$key: \"${match.value}\" must be written \"$term\"")
                }
            }
        }
    }

    @Test
    fun `UX-DR125 plurals follow CLDR rules for 1 and 5`() {
        val resources = context.resources
        assertEquals("1 Lot needs water", resources.getQuantityString(R.plurals.count_lots_need_water, 1, 1))
        assertEquals("5 Lots need water", resources.getQuantityString(R.plurals.count_lots_need_water, 5, 5))
        assertEquals("Alerts, 1 open", resources.getQuantityString(R.plurals.count_open_alerts, 1, 1))
        assertEquals("Alerts, 5 open", resources.getQuantityString(R.plurals.count_open_alerts, 5, 5))
        assertEquals("1 Lot can't be read", resources.getQuantityString(R.plurals.garden_headline_cant_read, 1, 1))
        assertEquals("5 Lots can't be read", resources.getQuantityString(R.plurals.garden_headline_cant_read, 5, 5))
        assertEquals("1 needs Calibration", resources.getQuantityString(R.plurals.garden_count_needs_calibration, 1, 1))
        assertEquals("5 need Calibration", resources.getQuantityString(R.plurals.garden_count_needs_calibration, 5, 5))
        assertEquals("1 hour", resources.getQuantityString(R.plurals.duration_spoken_hours, 1, 1))
        assertEquals("5 hours", resources.getQuantityString(R.plurals.duration_spoken_hours, 5, 5))
    }

    @Test
    fun `UX-DR124 the Site overview says what the web catalogue says`() {
        fun webText(key: String): String {
            val value = web.get(key)
            return if (value is JSONObject) "${value.getString("one")}|${value.getString("other")}" else value as String
        }

        fun mobile(key: String): String =
            android[key] ?: "${android.getValue("$key.one")}|${android.getValue("$key.other")}"
        val pairs =
            mapOf(
                "garden_headline_lot_needs_water" to "garden.headline.lotNeedsWater",
                "garden_headline_cant_read" to "garden.headline.cantRead",
                "garden_headline_paused_until" to "garden.headline.pausedUntil",
                "garden_headline_paused" to "garden.headline.paused",
                "garden_headline_nothing" to "garden.headline.nothing",
                "garden_count_needs_calibration" to "garden.count.needsCalibration",
                "garden_count_unknown" to "garden.count.unknown",
                "garden_count_ok" to "garden.count.ok",
                "garden_count_paused" to "garden.count.paused",
                "garden_count_no_node" to "garden.count.noNode",
                "soil_approx" to "soil.approx",
                "lot_tile_label_needs_water" to "lotTile.label.needsWater",
                "lot_tile_label_ok" to "lotTile.label.ok",
                "lot_tile_label_unknown_node" to "lotTile.label.unknownNode",
                "lot_tile_label_unknown_hub" to "lotTile.label.unknownHub",
                "lot_tile_label_needs_calibration" to "lotTile.label.needsCalibration",
                "lot_tile_label_paused" to "lotTile.label.paused",
                "lot_tile_label_paused_by_site" to "lotTile.label.pausedBySite",
                "lot_tile_was_needs_water" to "lotTile.was.needsWater",
                "lot_tile_was_ok" to "lotTile.was.ok",
                "lot_tile_was_unknown_node" to "lotTile.was.unknownNode",
                "lot_tile_was_unknown_hub" to "lotTile.was.unknownHub",
                "lot_tile_was_needs_calibration" to "lotTile.was.needsCalibration",
                "lot_tile_was_paused" to "lotTile.was.paused",
                "lot_tile_was_paused_by_site" to "lotTile.was.pausedBySite",
                "lot_tile_was_no_node" to "lotTile.was.noNode",
                "lot_tile_value_raw" to "lotTile.value.raw",
                "lot_tile_foot_low" to "lotTile.foot.low",
                "lot_tile_foot_was" to "lotTile.foot.was",
                "lot_tile_foot_last_reading" to "lotTile.foot.lastReading",
                "lot_tile_foot_no_readings" to "lotTile.foot.noReadings",
                "lot_tile_foot_uncalibrated" to "lotTile.foot.uncalibrated",
                "lot_tile_foot_until" to "lotTile.foot.until",
                "lot_tile_foot_paused" to "lotTile.foot.paused",
                "lot_tile_as_of" to "lotTile.asOf",
                "lot_tile_spoken_needs_water" to "lotTile.spoken.needsWater",
                "lot_tile_spoken_ok" to "lotTile.spoken.ok",
                "lot_tile_spoken_unknown" to "lotTile.spoken.unknown",
                "lot_tile_spoken_needs_calibration" to "lotTile.spoken.needsCalibration",
                "lot_tile_spoken_uncalibrated" to "lotTile.spoken.uncalibrated",
                "lot_tile_spoken_percent" to "lotTile.spoken.percent",
                "lot_tile_spoken_low" to "lotTile.spoken.low",
                "lot_tile_spoken_reading" to "lotTile.spoken.reading",
                "lot_tile_spoken_node_silent" to "lotTile.spoken.nodeSilent",
                "lot_tile_spoken_hub_silent" to "lotTile.spoken.hubSilent",
                "lot_tile_spoken_last_percent" to "lotTile.spoken.lastPercent",
                "lot_tile_spoken_last_reading" to "lotTile.spoken.lastReading",
                "lot_tile_spoken_no_readings" to "lotTile.spoken.noReadings",
                "lot_tile_spoken_paused" to "lotTile.spoken.paused",
                "lot_tile_spoken_paused_until" to "lotTile.spoken.pausedUntil",
                "lot_tile_spoken_paused_site" to "lotTile.spoken.pausedSite",
                "lot_tile_spoken_not_live" to "lotTile.spoken.notLive",
                "lot_tile_spoken_was_needs_water" to "lotTile.spokenWas.needsWater",
                "lot_tile_spoken_was_ok" to "lotTile.spokenWas.ok",
                "lot_tile_spoken_was_unknown" to "lotTile.spokenWas.unknown",
                "lot_tile_spoken_was_needs_calibration" to "lotTile.spokenWas.needsCalibration",
                "lot_tile_spoken_was_paused" to "lotTile.spokenWas.paused",
                "lot_tile_spoken_was_no_node" to "lotTile.spokenWas.noNode",
                "stale_title" to "stale.title",
                "stale_age" to "stale.age",
                "stale_detail" to "stale.detail",
                "stale_entered" to "stale.entered",
                "stale_left" to "stale.left",
                "duration_spoken_minutes" to "duration.spoken.minutes",
                "duration_spoken_hours" to "duration.spoken.hours",
                "duration_spoken_days" to "duration.spoken.days",
            )
        // The web names its placeholders ({count}); the phone numbers them (%1$d). Only the words are compared.
        val placeholder = Regex("\\{\\w+}")
        for ((key, webKey) in pairs) {
            assertEquals(placeholder.replace(webText(webKey), "%#"), mobile(key), key)
        }
    }

    @Test
    fun `UX-DR125 every plural has the CLDR one and other forms on both platforms`() {
        val plurals =
            android.keys
                .filter { it.contains('.') }
                .map { it.substringBefore('.') }
                .toSet()
        assertTrue(plurals.isNotEmpty())
        for (plural in plurals) {
            assertTrue(android.containsKey("$plural.one") && android.containsKey("$plural.other"), plural)
            assertTrue(ios.containsKey("$plural.one") && ios.containsKey("$plural.other"), plural)
        }
    }

    private companion object {
        const val DEGREE_SIGN = 0xB0
        const val STRINGS_XML = "apps/kt/android/src/main/res/values/strings.xml"
        const val XCSTRINGS = "apps/swift/ios/Sources/ColdframeIOS/Resources/Localizable.xcstrings"
    }
}
