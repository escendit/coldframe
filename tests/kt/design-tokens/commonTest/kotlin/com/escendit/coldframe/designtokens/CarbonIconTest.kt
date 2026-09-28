package com.escendit.coldframe.designtokens

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertIs
import kotlin.test.assertTrue

class CarbonIconTest {
    @Test
    fun holdsTheUxDr13Icons() {
        assertEquals(
            listOf(
                "rain-drop",
                "checkmark--outline",
                "checkmark",
                "help",
                "tools",
                "pause--outline",
                "add",
                "cloud--offline",
                "overflow-menu--vertical",
                "chevron--down",
                "arrow--up",
                "arrow--down",
                "battery--low",
                "error--filled",
                "view",
                "in-progress",
                "time",
                "grid",
                "notification",
                "box",
                "settings",
            ),
            CarbonIcon.entries.map { it.carbonName },
        )
    }

    @Test
    fun parsesEveryIconIntoDrawableCommandsInsideTheViewport() {
        for (icon in CarbonIcon.entries) {
            val commands = IconPath.parse(icon.pathData)
            assertIs<PathCommand.MoveTo>(commands.first(), icon.name)
            assertTrue(commands.any { it is PathCommand.LineTo || it is PathCommand.CubicTo }, icon.name)
            for (value in commands.flatMap(::coordinates)) {
                assertTrue(value in 0f..IconPath.VIEWPORT_SIZE, "${icon.name}: $value")
            }
        }
    }

    @Test
    fun parsesEachCommand() {
        assertEquals(
            listOf(
                PathCommand.MoveTo(1f, 2f),
                PathCommand.LineTo(3f, 4.5f),
                PathCommand.CubicTo(1f, 2f, 3f, 4f, 5f, 6f),
                PathCommand.Close,
            ),
            IconPath.parse("M 1 2 L 3 4.5 C 1 2 3 4 5 6 Z"),
        )
    }

    @Test
    fun rejectsDataItCannotDraw() {
        assertFailsWith<IllegalArgumentException> { IconPath.parse("M 1 2 A 1 1 0 0 1 2 2") }
        assertFailsWith<IllegalArgumentException> { IconPath.parse("M 1") }
        assertFailsWith<IllegalArgumentException> { IconPath.parse("L x 2") }
    }

    private fun coordinates(command: PathCommand): List<Float> =
        when (command) {
            is PathCommand.MoveTo -> listOf(command.x, command.y)
            is PathCommand.LineTo -> listOf(command.x, command.y)
            is PathCommand.CubicTo -> listOf(command.x1, command.y1, command.x2, command.y2, command.x, command.y)
            PathCommand.Close -> emptyList()
        }
}
