package com.escendit.coldframe.android

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.escendit.coldframe.android.ui.format.Formats
import org.junit.Test
import org.junit.runner.RunWith
import java.time.Instant
import java.time.ZoneId
import java.util.Locale
import kotlin.test.assertEquals
import kotlin.test.assertNotEquals
import kotlin.test.assertTrue
import kotlin.time.Duration.Companion.days
import kotlin.time.Duration.Companion.hours
import kotlin.time.Duration.Companion.minutes

@RunWith(AndroidJUnit4::class)
class FormatsTest {
    private val resources = ApplicationProvider.getApplicationContext<Context>().resources
    private val zurich = ZoneId.of("Europe/Zurich")
    private val now = Instant.parse("2026-09-28T10:00:00Z")

    @Test
    fun `UX-DR127 durations use min under an hour, h under a day and d from a day`() {
        assertEquals("0 min", Formats.duration(resources, (-5).minutes))
        assertEquals("59 min", Formats.duration(resources, 59.minutes))
        assertEquals("6 h", Formats.duration(resources, 6.hours + 30.minutes))
        assertEquals("23 h", Formats.duration(resources, 23.hours + 59.minutes))
        assertEquals("1 d", Formats.duration(resources, 1.days))
        assertEquals("3 d", Formats.duration(resources, 3.days + 5.hours))
    }

    @Test
    fun `UX-DR127 the stale header adds minutes to hours`() {
        assertEquals("2 h 12 min", Formats.duration(resources, 2.hours + 12.minutes, withMinutes = true))
        assertEquals("2 h", Formats.duration(resources, 2.hours, withMinutes = true))
    }

    @Test
    fun `UX-DR127 today shows the clock in the locale's 12 or 24 hour format`() {
        val reading = Instant.parse("2026-09-28T05:02:00Z")

        assertEquals("07:02", Formats.whenText(reading, now, zurich, Locale.GERMANY))
        assertEquals("7:02 AM", Formats.whenText(reading, now, zurich, Locale.US).replace(' ', ' '))
    }

    @Test
    fun `UX-DR127 earlier than today shows the weekday and older than 7 days the date`() {
        assertEquals("Sat", Formats.whenText(Instant.parse("2026-09-26T08:00:00Z"), now, zurich, Locale.UK))
        val older = Formats.whenText(Instant.parse("2026-09-10T08:00:00Z"), now, zurich, Locale.UK)
        assertTrue(older.startsWith("10 Sep"), older)
    }

    @Test
    fun `UX-DR127 a later calendar day than now shows the date, never a weekday`() {
        val tomorrow = Formats.whenText(Instant.parse("2026-09-29T08:00:00Z"), now, zurich, Locale.UK)

        assertNotEquals("Tue", tomorrow)
        assert(tomorrow.contains("29"))
    }

    @Test
    fun `UX-DR127 numbers and percent spacing follow the locale`() {
        assertEquals("1,234", Formats.number(1234, Locale.US))
        assertEquals("1.234", Formats.number(1234, Locale.GERMANY))
        assertEquals("20%", Formats.percent(20, Locale.US))
        assertEquals("20 %", Formats.percent(20, Locale.GERMANY))
    }
}
