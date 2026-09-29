package com.escendit.coldframe.android

import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.SemanticsActions
import androidx.compose.ui.semantics.SemanticsNode
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.semantics.getOrNull
import androidx.compose.ui.test.junit4.ComposeContentTestRule
import androidx.compose.ui.test.onRoot
import androidx.compose.ui.text.TextLayoutResult
import androidx.compose.ui.unit.Density
import com.escendit.coldframe.core.sites.SiteRole
import com.escendit.coldframe.core.sites.SiteSummary
import com.escendit.coldframe.core.sites.SitesState
import java.io.File
import kotlin.test.assertTrue
import kotlin.test.fail

/** Repository files, found from the module directory Gradle runs the tests in. */
object Repo {
    val root: File =
        generateSequence(File("").absoluteFile) { it.parentFile }
            .first { File(it, "settings.gradle.kts").isFile && File(it, "apps").isDirectory }

    fun file(path: String): File = File(root, path)

    fun kotlinSources(path: String): List<File> =
        file(path)
            .walkTopDown()
            .filter {
                it.isFile && it.extension == "kt"
            }.toList()
}

/** Renders [content] at [fontScale], as Android's font size setting does. */
@Composable
fun AtFontScale(
    fontScale: Float,
    content: @Composable () -> Unit,
) {
    val density = LocalDensity.current
    CompositionLocalProvider(LocalDensity provides Density(density.density, fontScale), content = content)
}

/** Every semantics node below the root, unmerged. */
fun ComposeContentTestRule.allNodes(): List<SemanticsNode> {
    val root = onRoot(useUnmergedTree = true).fetchSemanticsNode()
    return generateSequence(listOf(root)) { level ->
        level
            .flatMap {
                it.children
            }.takeIf { it.isNotEmpty() }
    }.flatten().toList()
}

/** Text layouts of every text node, as laid out now. */
fun SemanticsNode.textLayouts(): List<TextLayoutResult> {
    val action = config.getOrNull(SemanticsActions.GetTextLayoutResult) ?: return emptyList()
    val results = mutableListOf<TextLayoutResult>()
    action.action?.invoke(results)
    return results
}

val SemanticsNode.role: Role? get() = config.getOrNull(SemanticsProperties.Role)

val SemanticsNode.isClickable: Boolean get() = config.getOrNull(SemanticsActions.OnClick) != null

/** The label TalkBack reads: its own text or content description, or its merged children's. */
fun SemanticsNode.spokenLabel(): String {
    val own =
        (
            config.getOrNull(SemanticsProperties.ContentDescription).orEmpty() +
                config.getOrNull(SemanticsProperties.Text).orEmpty().map { it.text }
        )
    val children = children.flatMap { listOf(it.spokenLabel()) }
    return (own + children).filter { it.isNotBlank() }.joinToString(" ")
}

/** The one Site of most shell tests: "Home garden", held as [role]. */
fun homeSite(role: SiteRole = SiteRole.Owner): SiteSummary = SiteSummary("site-home", "Home garden", role)

/** Signed in with Sites: the tab shell on [current]. */
fun readySites(
    current: SiteSummary = homeSite(),
    sites: List<SiteSummary> = listOf(current),
): SitesState.Ready = SitesState.Ready(sites, current, creating = null)

/** Fails when any text on screen is cut off, truncated or wider than its node (UX-DR96). */
fun ComposeContentTestRule.assertNothingOverflows(screen: String) {
    waitForIdle()
    val texts = allNodes().flatMap { node -> node.textLayouts().map { node to it } }
    assertTrue(texts.isNotEmpty(), "$screen has text")
    for ((node, layout) in texts) {
        val text = layout.layoutInput.text.text
        // Clipped: cut off at the bottom, more lines than allowed, an ellipsis, or a line wider
        // than the node that shows it. (didOverflowWidth compares against the layout width.)
        if (layout.didOverflowHeight ||
            layout.multiParagraph.didExceedMaxLines
        ) {
            fail("$screen: \"$text\" is cut off")
        }
        for (line in 0 until layout.lineCount) {
            if (layout.isLineEllipsized(line)) fail("$screen: \"$text\" is truncated")
            val right = layout.getLineRight(line)
            if (right >
                layout.size.width + 1f
            ) {
                fail("$screen: \"$text\" is wider than its node ($right > ${layout.size.width})")
            }
        }
        assertTrue(node.size.height > 0, "$screen: \"$text\" is laid out")
    }
}
