package com.escendit.coldframe.android

import org.junit.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

/** Rules that hold for every Kotlin file of the Android shell, checked on the source. */
class SourceScanTest {
    private val sources = Repo.kotlinSources("apps/kt/android/src/main/kotlin")

    private fun offenders(pattern: Regex): List<String> =
        sources.flatMap { file ->
            file.readLines().mapIndexedNotNull { index, line ->
                if (pattern.containsMatchIn(line)) "${file.name}:${index + 1}: ${line.trim()}" else null
            }
        }

    @Test
    fun `the shell has sources to check`() {
        assertTrue(sources.size >= 10)
    }

    @Test
    fun `UX-DR101 no animations or transitions, state changes swap instantly`() {
        assertEquals(
            emptyList(),
            offenders(
                Regex(
                    """\banimate\w*\(|AnimatedVisibility|AnimatedContent|Crossfade|updateTransition|rememberInfiniteTransition""",
                ),
            ),
        )
    }

    @Test
    fun `UX-DR114 no toasts, snackbars, spinners, carousels, snooze or streaks`() {
        assertEquals(
            emptyList(),
            offenders(
                Regex(
                    """Toast|Snackbar|CircularProgressIndicator|LinearProgressIndicator|HorizontalPager|VerticalPager|Carousel|[Ss]nooze|[Ss]treak""",
                ),
            ),
        )
    }

    @Test
    fun `UX-DR113 tap to act only, no long-press actions`() {
        assertEquals(emptyList(), offenders(Regex("""combinedClickable|onLongClick|detectTapGestures""")))
    }

    @Test
    fun `UX-DR124 no literal strings in composables`() {
        assertEquals(
            emptyList(),
            offenders(Regex("""Text\(\s*"|contentDescription\s*=\s*"|label\s*=\s*"|title\s*=\s*"""")),
        )
    }

    @Test
    fun `AD-23 there is no WebView, no custom trust manager and no certificate bypass`() {
        val all = Repo.kotlinSources("apps/kt/android/src") + Repo.kotlinSources("packages/kt/core/src")
        val bypass =
            Regex(
                """android\.webkit|X509TrustManager|HostnameVerifier|useWebView\s*=\s*true|trustAll|cleartextTrafficPermitted="true"""",
            )
        assertEquals(emptyList(), all.filter { bypass.containsMatchIn(it.readText()) }.map { it.name })
    }

    @Test
    fun `AD-14 the shell never names a token`() {
        val tokens = Regex("""accessToken|refreshToken|idToken|access_token|refresh_token|id_token""")
        assertEquals(emptyList(), offenders(tokens))
    }
}
