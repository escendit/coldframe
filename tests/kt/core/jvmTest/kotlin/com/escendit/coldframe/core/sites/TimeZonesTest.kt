package com.escendit.coldframe.core.sites

import java.util.TimeZone
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

class TimeZonesTest {
    @Test
    fun uxDr61TheDetectedZoneIsTheOsZone() {
        assertEquals(TimeZone.getDefault().id, detectedTimeZone())
    }

    @Test
    fun uxDr61TheListHoldsSortedIanaIds() {
        val zones = availableTimeZones()

        assertTrue("Europe/Zurich" in zones)
        assertEquals(zones.sorted(), zones)
    }
}
