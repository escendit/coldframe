package com.escendit.coldframe.android

import androidx.activity.ComponentActivity
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.semantics.getOrNull
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.assertIsSelected
import androidx.compose.ui.test.hasAnyAncestor
import androidx.compose.ui.test.hasTestTag
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.isHeading
import androidx.compose.ui.test.isSelectable
import androidx.compose.ui.test.junit4.v2.createAndroidComposeRule
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.escendit.coldframe.R
import com.escendit.coldframe.android.ui.pause.PAUSE_CALENDAR_TAG
import com.escendit.coldframe.android.ui.pause.PAUSE_UNTIL_TAG
import com.escendit.coldframe.android.ui.pause.PauseActions
import com.escendit.coldframe.android.ui.pause.PauseSheetContent
import com.escendit.coldframe.android.ui.theme.ColdframeTheme
import com.escendit.coldframe.core.sites.PauseForm
import com.escendit.coldframe.core.sites.PauseNotice
import com.escendit.coldframe.core.sites.PauseScope
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.GraphicsMode
import java.time.LocalDate
import kotlin.test.assertEquals
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

/**
 * The Pause sheet (UX-DR46, Story 8.2): its content, drawn from the core's [PauseForm]. Which
 * scopes it offers, what the button names and why it stayed open are fixed in [PauseFixtures].
 */
@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class PauseSheetTest {
    @get:Rule
    val compose = createAndroidComposeRule<ComponentActivity>()

    private val calls = mutableListOf<String>()
    private var form by mutableStateOf(PauseFixtures.forSite)

    /** The sheet over actions that act as the core does: the scope and the date land in the form. */
    private fun show(
        initial: PauseForm,
        fontScale: Float = 1f,
        calendarOpen: Boolean = false,
        unreachable: Int = R.string.notice_unreachable,
    ) {
        form = initial
        val recording = PauseFixtures.recording(calls)
        val actions =
            PauseActions(
                setScope = {
                    recording.setScope(it)
                    form = form.copy(scope = it)
                },
                setUntil = {
                    recording.setUntil(it)
                    form = form.copy(until = it, notice = null)
                },
                cancel = recording.cancel,
                save = recording.save,
            )
        compose.setContent {
            AtFontScale(fontScale) {
                ColdframeTheme(isDark = false) {
                    PauseSheetContent(
                        form = form,
                        actions = actions,
                        unreachable = unreachable,
                        calendarOpen = calendarOpen,
                        displayedMonth = LocalDate.of(2027, 3, 1),
                    )
                }
            }
        }
    }

    private fun segments(): List<String> =
        compose.onAllNodes(isSelectable()).fetchSemanticsNodes().map { it.spokenLabel() }

    @Test
    fun `UX-DR46 from the Site menu the sheet offers Whole Site alone, an optional date and a button naming it`() {
        show(PauseFixtures.forSite)

        compose.onNode(isHeading().and(hasText("Pause"))).assertExists()
        // No Device in this context: This Device is not offered at all, never shown disabled.
        assertEquals(listOf("WHOLE SITE"), segments())
        compose.onNode(isSelectable().and(hasText("WHOLE SITE"))).assertIsSelected()
        compose.onNodeWithText("Until").assertExists()
        compose.onNodeWithText("No end date").assertExists()
        compose.onNodeWithText("Leave empty to pause until someone resumes.").assertExists()
        compose.onNodeWithText("CANCEL").assertExists()
        compose.onNodeWithText("PAUSE HOME GARDEN").assertExists()
        // The calendar is closed until it is asked for.
        compose.onNodeWithTag(PAUSE_CALENDAR_TAG).assertDoesNotExist()
    }

    @Test
    fun `UX-DR46 from a Device the sheet selects This Device, and the primary button follows the scope`() {
        show(PauseFixtures.forDevice)

        assertEquals(listOf("THIS DEVICE", "WHOLE SITE"), segments())
        compose.onNode(isSelectable().and(hasText("THIS DEVICE"))).assertIsSelected()
        compose.onNodeWithText("PAUSE 7C19").assertExists()

        compose.onNode(isSelectable().and(hasText("WHOLE SITE"))).performClick()

        assertEquals(listOf("scope site"), calls)
        compose.onNode(isSelectable().and(hasText("WHOLE SITE"))).assertIsSelected()
        compose.onNodeWithText("PAUSE HOME GARDEN").assertExists()
        compose.onAllNodesWithText("PAUSE 7C19").assertCountEquals(0)
    }

    @Test
    fun `UX-DR46 UX-DR70 the primary button saves and Cancel cancels`() {
        show(PauseFixtures.forDevice)

        compose.onNodeWithText("PAUSE 7C19").performClick()
        compose.onNodeWithText("CANCEL").performClick()

        assertEquals(listOf("save", "cancel"), calls)
    }

    @Test
    fun `UX-DR46 a day picked in the calendar inside the sheet is sent as that date, and Clear date empties it`() {
        show(PauseFixtures.forSite)

        compose.onNodeWithTag(PAUSE_UNTIL_TAG).performClick()
        // The calendar opens inside the sheet: one modal level, no dialog on top.
        compose.onNodeWithTag(PAUSE_CALENDAR_TAG).assertExists()
        compose
            .onAllNodes(
                androidx.compose.ui.test
                    .isDialog(),
            ).assertCountEquals(0)
        // A day of the Material calendar is one element that reads its whole date.
        compose
            .onNode(hasText("March 15, 2027", substring = true).and(hasAnyAncestor(hasTestTag(PAUSE_CALENDAR_TAG))))
            .performScrollTo()
            .performClick()

        // The picked calendar date, whatever the phone's zone is.
        assertEquals(listOf("until 2027-03-15"), calls)
        compose.onNodeWithTag(PAUSE_CALENDAR_TAG).assertDoesNotExist()
        assertTrue(
            compose
                .onNodeWithTag(PAUSE_UNTIL_TAG)
                .fetchSemanticsNode()
                .spokenLabel()
                .contains("2027"),
        )
        compose.onAllNodesWithText("No end date").assertCountEquals(0)

        compose.onNodeWithText("CLEAR DATE").performClick()

        assertEquals(listOf("until 2027-03-15", "until none"), calls)
        compose.onNodeWithText("No end date").assertExists()
        compose.onAllNodesWithText("CLEAR DATE").assertCountEquals(0)
    }

    @Test
    fun `UX-DR46 a date the Server refused keeps the sheet filled and marks the date field`() {
        show(PauseFixtures.refused)

        compose.onNodeWithText("Choose a date after today.").assertExists()
        // The reason replaces the helper and is the field's error for TalkBack.
        compose.onAllNodesWithText("Leave empty to pause until someone resumes.").assertCountEquals(0)
        val field = compose.onNodeWithTag(PAUSE_UNTIL_TAG).fetchSemanticsNode()
        assertEquals("Choose a date after today.", field.config.getOrNull(SemanticsProperties.Error))
        assertTrue(field.spokenLabel().contains("2026"), field.spokenLabel())
        compose.onNode(isSelectable().and(hasText("THIS DEVICE"))).assertIsSelected()
        // Not a second notice: there is no Try again for a refused date.
        compose.onAllNodesWithText("TRY AGAIN").assertCountEquals(0)
    }

    @Test
    fun `UX-DR84 a Role lost meanwhile says You can't change this on the Site, in the open sheet`() {
        show(PauseFixtures.forDevice.copy(until = "2027-03-01", notice = PauseNotice.Forbidden))

        compose.onNodeWithText("You can't change this on Home garden. Ask an Owner or Administrator.").assertExists()
        compose.onAllNodesWithText("TRY AGAIN").assertCountEquals(0)
        assertNull(
            compose
                .onNodeWithTag(PAUSE_UNTIL_TAG)
                .fetchSemanticsNode()
                .config
                .getOrNull(SemanticsProperties.Error),
        )
        compose.onNodeWithText("PAUSE 7C19").assertExists()
    }

    @Test
    fun `UX-DR70 an unreachable Server shows the surface's notice with Try again, and the sheet keeps the entries`() {
        show(
            PauseFixtures.forDevice.copy(
                scope = PauseScope.Site,
                until = "2027-03-01",
                notice = PauseNotice.Unreachable,
            ),
            unreachable = R.string.devices_unreachable,
        )

        compose.onNodeWithText("Can't reach your Server.").assertExists()
        compose.onNode(isSelectable().and(hasText("WHOLE SITE"))).assertIsSelected()
        assertTrue(
            compose
                .onNodeWithTag(PAUSE_UNTIL_TAG)
                .fetchSemanticsNode()
                .spokenLabel()
                .contains("2027"),
        )

        compose.onNodeWithText("TRY AGAIN").performScrollTo().performClick()

        assertEquals(listOf("save"), calls)
    }

    @Test
    fun `UX-DR70 a Device that is gone and a certificate failure say so without Try again`() {
        show(PauseFixtures.forDevice.copy(notice = PauseNotice.NotFound))

        compose.onNodeWithText("That Device is gone.").assertExists()
        compose.onAllNodesWithText("TRY AGAIN").assertCountEquals(0)

        form = PauseFixtures.forDevice.copy(notice = PauseNotice.Certificate)
        compose.waitForIdle()
        compose.onNode(hasText("certificate isn't trusted", substring = true)).assertExists()
        compose.onAllNodesWithText("TRY AGAIN").assertCountEquals(0)
    }

    @Test
    fun `UX-DR46 while the Pause is sent the button says Pausing and nothing in the sheet can be pressed twice`() {
        show(PauseFixtures.forDevice.copy(until = "2027-03-01", working = true))

        compose.onNodeWithText("PAUSING…").assertIsNotEnabled()
        compose.onNodeWithText("CANCEL").assertIsNotEnabled()
        compose.onNodeWithText("CLEAR DATE").assertIsNotEnabled()
        compose.onNodeWithTag(PAUSE_UNTIL_TAG).assertIsNotEnabled()
        compose.onAllNodesWithText("PAUSE 7C19").assertCountEquals(0)
    }

    @Test
    fun `UX-DR46 UX-DR96 nothing in the sheet is cut off at font scale 2, with the date refused and a notice`() {
        show(PauseFixtures.refused, fontScale = 2f)
        compose.assertNothingOverflows("Pause sheet, refused date")

        form = PauseFixtures.forDevice.copy(scope = PauseScope.Site, notice = PauseNotice.Forbidden)
        compose.waitForIdle()
        compose.assertNothingOverflows("Pause sheet, forbidden")
        assertNotNull(compose.onNodeWithText("PAUSE HOME GARDEN").fetchSemanticsNode())
    }

    @Test
    fun `UX-DR46 the calendar opens on the picked date, and tapping the field again closes it`() {
        show(PauseFixtures.forDevice.copy(until = "2027-03-15"), calendarOpen = true)

        compose
            .onNode(hasText("March 15, 2027", substring = true).and(hasAnyAncestor(hasTestTag(PAUSE_CALENDAR_TAG))))
            .assertIsSelected()

        compose.onNodeWithTag(PAUSE_UNTIL_TAG).performClick()

        compose.onNodeWithTag(PAUSE_CALENDAR_TAG).assertDoesNotExist()
        // Opening and closing the calendar picks nothing.
        assertTrue(calls.isEmpty())
    }
}
