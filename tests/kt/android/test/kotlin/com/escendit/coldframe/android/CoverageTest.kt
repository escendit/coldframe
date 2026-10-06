package com.escendit.coldframe.android

import org.junit.Test
import java.io.File
import kotlin.test.assertTrue

/** Every UX requirement stories 1.5, 1.8, 1.9, 3.6, 3.7, 4.3 and 4.7 name has at least one test named after it. */
class CoverageTest {
    private val storyIds =
        listOf(
            12,
            15,
            17,
            18,
            19,
            20,
            21,
            22,
            23,
            24,
            30,
            34,
            35,
            36,
            37,
            38,
            39,
            40,
            41,
            42,
            53,
            54,
            55,
            56,
            57,
            59,
            60,
            61,
            62,
            65,
            66,
            67,
            71,
            74,
            75,
            76,
            77,
            79,
            80,
            82,
            84,
            92,
            93,
            94,
            95,
            96,
            97,
            98,
            99,
            100,
            101,
            103,
            104,
            105,
            106,
            107,
            109,
            110,
            112,
            113,
            114,
            124,
            125,
            126,
            127,
            128,
            129,
            130,
            131,
        )

    /** Named in Swift too (the iOS shell builds these components and surfaces). */
    private val iosIds =
        listOf(
            12,
            17,
            18,
            19,
            20,
            21,
            22,
            23,
            24,
            30,
            34,
            35,
            36,
            37,
            38,
            39,
            40,
            41,
            42,
            54,
            55,
            56,
            61,
            62,
            65,
            66,
            67,
            71,
            74,
            75,
            76,
            77,
            79,
            80,
            82,
            84,
            94,
            95,
            97,
            98,
            99,
            100,
            101,
            103,
            104,
            105,
            106,
            107,
            109,
            112,
            113,
            114,
            125,
            126,
            127,
            128,
            129,
            130,
            131,
        )

    private fun filesUnder(
        path: String,
        extension: String,
    ): List<File> =
        Repo
            .file(path)
            .walkTopDown()
            .filter { it.isFile && it.extension == extension }
            .toList()

    /** Ids at the start of Kotlin test names: `` `UX-DR56 …` `` or `uxDr56…`, each id they list. */
    private fun kotlinIds(): Set<Int> {
        val backtick = Regex("""fun `(UX-DR\d+[^`]*)`""")
        val camel = Regex("""fun (uxDr\d+\w*)\(""")
        val ids = mutableSetOf<Int>()
        for (file in filesUnder("tests/kt", "kt")) {
            val text = file.readText()
            backtick.findAll(text).forEach { match ->
                Regex("""UX-DR(\d+)""").findAll(match.groupValues[1]).forEach {
                    ids +=
                        it.groupValues[1].toInt()
                }
            }
            camel.findAll(text).forEach { match ->
                Regex("""uxDr(\d+)""").findAll(match.groupValues[1]).forEach {
                    ids +=
                        it.groupValues[1].toInt()
                }
            }
        }
        return ids
    }

    /** Ids at the start of Swift Testing names: `@Test("UX-DR56 …")`, each id they list. */
    private fun swiftIds(): Set<Int> {
        val named = Regex("""@Test\(\s*"(UX-DR\d+[^"]*)"""")
        val ids = mutableSetOf<Int>()
        for (file in filesUnder("tests/swift", "swift")) {
            named.findAll(file.readText()).forEach { match ->
                Regex("""UX-DR(\d+)""").findAll(match.groupValues[1]).forEach { ids += it.groupValues[1].toInt() }
            }
        }
        return ids
    }

    @Test
    fun `every story UX-DR id but 109 is named by a Kotlin test`() {
        val missing = storyIds.filter { it != 109 } - kotlinIds()
        assertTrue(missing.isEmpty(), "No Kotlin test names ${missing.joinToString { "UX-DR$it" }}")
    }

    @Test
    fun `the iOS ids are named by a Swift test`() {
        val missing = iosIds - swiftIds()
        assertTrue(missing.isEmpty(), "No Swift test names ${missing.joinToString { "UX-DR$it" }}")
    }
}
