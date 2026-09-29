package com.escendit.coldframe.designtokens

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

class TypographyTest {
    @Test
    fun holdsEveryRole() {
        assertEquals(14, Typography.all.size)
    }

    @Test
    fun givesEveryRoleAnSpSizeAndCapsRolesOf36AndMoreAt2x() {
        for ((name, role) in Typography.all) {
            assertTrue(role.sizeSp > 0f, name)
            assertTrue(role.fontWeight == 300 || role.fontWeight == 400, name)
            if (role.sizeSp >= 36f) {
                assertEquals(2f, role.maxFontScale, name)
                assertEquals(role.sizeSp * 2f, role.scaledSizeSp(fontScale = 3f), name)
            } else {
                assertNull(role.maxFontScale, name)
                assertEquals(role.sizeSp * 3f, role.scaledSizeSp(fontScale = 3f), name)
            }
        }
    }

    @Test
    fun holdsTheDesignValues() {
        assertEquals(36f, Typography.headline.sizeSp)
        assertEquals("Ubuntu Condensed", Typography.headline.fontFamily)
        assertEquals(72f, Typography.heroValue.sizeSp)
        assertEquals(300, Typography.heroValue.fontWeight)
        assertEquals(14f, Typography.body.sizeSp)
        assertEquals(1.43f, Typography.body.lineHeight)
        assertEquals(0.06f, Typography.statusLabel.letterSpacingEm)
    }

    @Test
    fun uppercasesOnlyStatusLabelAndButton() {
        assertEquals(
            setOf("status-label", "button"),
            Typography.all.filterValues { it.uppercase }.keys,
        )
        assertFalse(Typography.headline.uppercase)
    }
}
