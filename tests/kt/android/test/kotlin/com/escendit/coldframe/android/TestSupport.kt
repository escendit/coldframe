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
import java.io.File

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
