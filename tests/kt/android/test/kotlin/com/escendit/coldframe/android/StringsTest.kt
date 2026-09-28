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

    @Test
    fun `UX-DR130 no exclamation marks, emoji, successfully, OK, fine or all good`() {
        for ((key, value) in android) {
            assertFalse(value.contains('!'), key)
            assertFalse(
                value.codePoints().anyMatch {
                    Character.getType(it) == Character.OTHER_SYMBOL.toInt()
                },
                "$key has an emoji",
            )
            assertFalse(
                Regex(
                    "successfully|\\bOK\\b|\\bokay\\b|\\bfine\\b|all good",
                    RegexOption.IGNORE_CASE,
                ).containsMatchIn(value),
                key,
            )
        }
    }

    @Test
    fun `UX-DR130 UX-DR124 uppercase comes from style, never from the string`() {
        for ((key, value) in android) assertFalse(Regex("\\b\\p{Lu}{2,}\\b").containsMatchIn(value), key)
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
        const val STRINGS_XML = "apps/kt/android/src/main/res/values/strings.xml"
        const val XCSTRINGS = "apps/swift/ios/Sources/ColdframeIOS/Resources/Localizable.xcstrings"
    }
}
