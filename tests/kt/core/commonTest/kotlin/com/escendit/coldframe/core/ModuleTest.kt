package com.escendit.coldframe.core

import kotlin.test.Test
import kotlin.test.assertEquals

class ModuleTest {
    @Test
    fun nameIsTheModuleName() {
        assertEquals("coldframe-core", Module.NAME)
    }
}
