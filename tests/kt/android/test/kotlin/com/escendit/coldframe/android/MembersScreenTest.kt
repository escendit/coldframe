package com.escendit.coldframe.android

import androidx.activity.ComponentActivity
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.toPixelMap
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.assertHasClickAction
import androidx.compose.ui.test.assertHasNoClickAction
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.assertIsSelected
import androidx.compose.ui.test.captureToImage
import androidx.compose.ui.test.hasAnyAncestor
import androidx.compose.ui.test.hasContentDescription
import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.hasTestTag
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.isHeading
import androidx.compose.ui.test.isSelectable
import androidx.compose.ui.test.junit4.v2.createAndroidComposeRule
import androidx.compose.ui.test.onAllNodesWithTag
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.performTextReplacement
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.escendit.coldframe.android.ui.members.MembersActions
import com.escendit.coldframe.core.appearance.ThemePreference
import com.escendit.coldframe.core.signin.SignInState
import com.escendit.coldframe.core.sites.InviteEmailError
import com.escendit.coldframe.core.sites.InviteForm
import com.escendit.coldframe.core.sites.Member
import com.escendit.coldframe.core.sites.MemberEditor
import com.escendit.coldframe.core.sites.MemberRoleError
import com.escendit.coldframe.core.sites.MembersNotice
import com.escendit.coldframe.core.sites.MembersState
import com.escendit.coldframe.core.sites.RemoveMemberConfirmation
import com.escendit.coldframe.core.sites.SiteRole
import com.escendit.coldframe.designtokens.ColorTokens
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.GraphicsMode
import kotlin.test.assertEquals
import kotlin.test.assertTrue

/** Members: the Settings row, the Member tiles and the Invite form (Story 9.1). */
@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class MembersScreenTest {
    @get:Rule
    val compose = createAndroidComposeRule<ComponentActivity>()

    private val calls = mutableListOf<String>()
    private var members by mutableStateOf<MembersState>(MembersState.Hidden)

    private val actions =
        MembersActions(
            load = { calls += "load" },
            setEmail = { calls += "email $it" },
            setRole = { calls += "role $it" },
            sendInvite = { calls += "send" },
            retry = { calls += "retry" },
            select = { calls += "select $it" },
            closeEditor = { calls += "close" },
            setEditRole = { calls += "edit role $it" },
            saveRole = { calls += "save role" },
            askRemove = { calls += "ask remove" },
            cancelRemove = { calls += "cancel remove" },
            confirmRemove = { calls += "confirm remove" },
        )

    private val simon =
        Member("u-simon", "Simon Novak", "simon@example.com", SiteRole.Owner, you = true, lastOwner = true)
    private val ana = Member("u-ana", "Ana Costa", "ana@example.com", SiteRole.Member, you = false)

    private fun ready(
        form: InviteForm = InviteForm(),
        notice: MembersNotice? = null,
        sentTo: String? = null,
        editor: MemberEditor? = null,
        removing: RemoveMemberConfirmation? = null,
        noticeName: String? = null,
        noticeRole: SiteRole? = null,
    ) = MembersState.Ready(
        "Home garden",
        listOf(simon, ana),
        form,
        notice,
        sentTo,
        editor,
        removing,
        noticeName,
        noticeRole,
    )

    private var role by mutableStateOf(SiteRole.Owner)
    private var shown = false

    /** Sets the content once; later calls change only the state it shows. */
    private fun show(
        state: MembersState,
        role: SiteRole = SiteRole.Owner,
    ) {
        members = state
        this.role = role
        if (shown) {
            compose.waitForIdle()
            return
        }
        shown = true
        compose.setContent {
            ColdframeRoot(
                state = SignInState.SignedIn("Simon"),
                sites = readySites(homeSite(this.role)),
                theme = ThemePreference.Light,
                onSignIn = {},
                onSignOut = {},
                onSelectTheme = {},
                members = members,
                membersActions = actions,
            )
        }
    }

    private fun openMembers(state: MembersState) {
        val again = shown
        show(state)
        if (again) return
        compose.onNodeWithText("Settings").performClick()
        compose.onNodeWithText("Members").performClick()
        compose.onNode(isHeading().and(hasText("Members"))).assertExists()
    }

    @Test
    fun `UX-DR73 UX-DR71 an Owner has a Members row in Settings, and entering it reads the Memberships`() {
        show(ready())

        compose.onNodeWithText("Settings").performClick()
        compose.onNodeWithText("People on Home garden and their Roles").assertExists()
        compose.onNodeWithText("Members").performClick()

        compose.onNode(isHeading().and(hasText("Members"))).assertExists()
        assertEquals(listOf("load"), calls)
        compose.onNodeWithContentDescription("Back").performClick()
        compose.onNode(isHeading().and(hasText("Settings"))).assertExists()
    }

    @Test
    fun `UX-DR76 UX-DR84 an Administrator and a Member have no Members row`() {
        show(MembersState.Hidden, SiteRole.Administrator)
        compose.onNodeWithText("Settings").performClick()
        for (role in listOf(SiteRole.Administrator, SiteRole.Member)) {
            show(MembersState.Hidden, role)
            compose.onNode(isHeading().and(hasText("Settings"))).assertExists()

            compose.onAllNodesWithText("Members").assertCountEquals(0)
            compose.onAllNodesWithText("Send invite", ignoreCase = true).assertCountEquals(0)
        }
        assertTrue(calls.isEmpty())
    }

    @Test
    fun `UX-DR51 one tile per Membership with initials, name and the Role in upper case by style`() {
        openMembers(ready())

        compose.onAllNodesWithTag("member-tile").assertCountEquals(2)
        compose
            .onNodeWithContentDescription("Simon Novak, Owner · last one, you. A Site always keeps at least one Owner.")
            .assertExists()
        compose.onNodeWithContentDescription("Ana Costa, Member · change").assertExists()
        compose.onNodeWithTag("member-initials-you", useUnmergedTree = true).assertExists()
        compose.onAllNodesWithTag("member-initials", useUnmergedTree = true).assertCountEquals(1)
        compose.onNodeWithText("SN", useUnmergedTree = true).assertExists()
        compose.onNodeWithText("AC", useUnmergedTree = true).assertExists()
        compose
            .onNode(hasText("OWNER · LAST ONE").and(hasAnyAncestor(hasTestTag("member-tile"))), useUnmergedTree = true)
            .assertExists()
        compose
            .onNode(hasText("MEMBER · CHANGE").and(hasAnyAncestor(hasTestTag("member-tile"))), useUnmergedTree = true)
            .assertExists()
    }

    @Test
    fun `UX-DR51 the signed-in User's initials square is primary, the others layer-01`() {
        openMembers(ready())

        val you = compose.onNodeWithTag("member-initials-you", useUnmergedTree = true).captureToImage().toPixelMap()
        val other = compose.onNodeWithTag("member-initials", useUnmergedTree = true).captureToImage().toPixelMap()

        assertEquals(Color(ColorTokens.primary.light.toInt()), you[2, 2])
        assertEquals(Color(ColorTokens.layer01.light.toInt()), other[2, 2])
    }

    @Test
    fun `UX-DR52 the Invite form has an email field, the Role choice with Member selected and its description`() {
        openMembers(ready())

        compose.onNode(hasSetTextAction().and(hasContentDescription("Email"))).assertExists()
        compose.onNode(isSelectable().and(hasText("MEMBER"))).assertIsSelected()
        compose.onNode(isSelectable().and(hasText("ADMINISTRATOR"))).assertExists()
        compose.onNode(isSelectable().and(hasText("OWNER"))).assertExists()
        compose
            .onNodeWithText("Member sees everything and gets the same Alerts, but can't change settings.")
            .assertExists()
        compose.onNodeWithText("SEND INVITE").performScrollTo().assertExists()
    }

    @Test
    fun `UX-DR52 the email field uses the email keyboard`() {
        openMembers(ready())

        val field = compose.onNode(hasSetTextAction().and(hasContentDescription("Email"))).fetchSemanticsNode()
        assertTrue(field.config.contains(SemanticsProperties.EditableText))
        val source =
            Repo.file(
                "apps/kt/android/src/main/kotlin/com/escendit/coldframe/android/ui/members/MembersScreen.kt",
            )
        assertTrue(source.readText().contains("KeyboardType.Email"))
    }

    @Test
    fun `UX-DR52 the description follows the selected Role`() {
        openMembers(ready(form = InviteForm(role = SiteRole.Administrator)))
        compose
            .onNodeWithText("Administrator also changes Devices, Thresholds, Calibration and Pause, but not Members.")
            .assertExists()

        openMembers(ready(form = InviteForm(role = SiteRole.Owner)))
        compose
            .onNodeWithText("Owner can do everything, including inviting people and changing Roles.")
            .assertExists()
    }

    @Test
    fun `UX-DR52 typing, picking a Role and Send invite go to the core`() {
        openMembers(ready())

        compose.onNode(hasSetTextAction().and(hasContentDescription("Email"))).performTextReplacement("ana@example.com")
        compose.onNode(isSelectable().and(hasText("ADMINISTRATOR"))).performScrollTo().performClick()
        compose.onNodeWithText("SEND INVITE").performScrollTo().performClick()

        assertEquals(listOf("load", "email ana@example.com", "role Administrator", "send"), calls)
    }

    @Test
    fun `UX-DR52 after sending the notice names the email and says to accept on the garden's Wi-Fi`() {
        openMembers(ready(notice = MembersNotice.Sent, sentTo = "ana@example.com"))

        compose
            .onNodeWithText("Invitation sent to ana@example.com. They accept from the email, on the garden's Wi-Fi.")
            .assertExists()
        compose.onAllNodesWithTag("member-tile").assertCountEquals(2)
    }

    @Test
    fun `UX-DR52 while sending the button reads Sending invite in place`() {
        openMembers(ready(form = InviteForm("ana@example.com", working = true)))

        compose.onNodeWithText("SENDING INVITE…").performScrollTo().assertIsNotEnabled()
    }

    @Test
    fun `UX-DR52 an invalid email shows one line under the field`() {
        openMembers(ready(form = InviteForm("ana", emailError = InviteEmailError.Invalid)))

        compose.onNodeWithText("Enter one email address, like ana@example.com.").assertExists()
    }

    @Test
    fun `UX-DR52 already invited says so, and a failed send offers Try again`() {
        openMembers(ready(form = InviteForm("ana@example.com"), notice = MembersNotice.AlreadyInvited))
        compose.onNodeWithText("That person is already invited or a member of this Site.").assertExists()
        compose.onAllNodesWithText("TRY AGAIN").assertCountEquals(0)

        openMembers(ready(form = InviteForm("ana@example.com"), notice = MembersNotice.NotSent))
        compose.onNodeWithText("The invitation was not sent. Try again.").assertExists()
        compose.onNodeWithText("TRY AGAIN").performScrollTo().performClick()
        assertTrue("retry" in calls)
    }

    // Story 9.2: change and remove Roles, always keeping an Owner.

    @Test
    fun `UX-DR51 the last Owner's tile is locked with its reason and taps on it do nothing`() {
        openMembers(ready())

        compose
            .onNode(
                hasText("A Site always keeps at least one Owner.").and(hasAnyAncestor(hasTestTag("member-tile"))),
                useUnmergedTree = true,
            ).assertExists()
        compose.onNodeWithContentDescription("Simon Novak", substring = true).assertHasNoClickAction()
        compose.onNodeWithContentDescription("Ana Costa, Member · change").assertHasClickAction().performClick()
        assertEquals(listOf("load", "select u-ana"), calls)
    }

    @Test
    fun `UX-DR84 the editor offers the Role choice, Save Role and Remove from Site`() {
        openMembers(ready(editor = MemberEditor(ana, SiteRole.Administrator)))

        compose.onNodeWithTag("member-editor").assertExists()
        compose
            .onNode(isSelectable().and(hasText("ADMINISTRATOR")).and(hasAnyAncestor(hasTestTag("member-editor"))))
            .assertIsSelected()
        compose
            .onNode(isSelectable().and(hasText("OWNER")).and(hasAnyAncestor(hasTestTag("member-editor"))))
            .performScrollTo()
            .performClick()
        compose.onNodeWithText("SAVE ROLE").performScrollTo().performClick()
        compose.onNodeWithText("REMOVE FROM SITE").performScrollTo().performClick()
        compose.onNodeWithText("CANCEL").performScrollTo().performClick()

        assertEquals(listOf("load", "edit role Owner", "save role", "ask remove", "close"), calls)
    }

    @Test
    fun `UX-DR84 last-owner shows one line under the Role field and Saving Role works in place`() {
        openMembers(ready(editor = MemberEditor(ana, SiteRole.Member, MemberRoleError.LastOwner)))
        compose
            .onNode(
                hasText("A Site always keeps at least one Owner.").and(hasAnyAncestor(hasTestTag("member-editor"))),
                useUnmergedTree = true,
            ).assertExists()

        openMembers(ready(editor = MemberEditor(ana, SiteRole.Owner, working = true)))
        compose.onNodeWithText("SAVING ROLE…").performScrollTo().assertIsNotEnabled()
    }

    @Test
    fun `UX-DR113 removing asks in a dialog that names the person and the Site`() {
        openMembers(ready(removing = RemoveMemberConfirmation(ana)))

        compose.onNodeWithText("Remove Ana Costa from Home garden?").assertExists()
        compose.onNodeWithText("They lose access to Home garden and stop getting its Alerts.").assertExists()
        compose.onNodeWithText("REMOVE").performClick()
        compose.onNodeWithText("CANCEL").performClick()

        assertEquals(listOf("load", "confirm remove", "cancel remove"), calls)
    }

    @Test
    fun `UX-DR84 UX-DR113 notices name the person, Role and Site, and a change not saved offers Try again`() {
        openMembers(
            ready(notice = MembersNotice.RoleChanged, noticeName = "Ana Costa", noticeRole = SiteRole.Administrator),
        )
        compose.onNodeWithText("Ana Costa is now Administrator.").assertExists()

        openMembers(ready(notice = MembersNotice.Removed, noticeName = "Ana Costa"))
        compose.onNodeWithText("Ana Costa was removed from Home garden.").assertExists()

        openMembers(ready(notice = MembersNotice.MemberGone, noticeName = "Ana Costa"))
        compose.onNodeWithText("Ana Costa is no longer a member of Home garden.").assertExists()

        openMembers(ready(notice = MembersNotice.Forbidden))
        compose.onNodeWithText("You can't change this on Home garden. Ask an Owner or Administrator.").assertExists()

        openMembers(ready(notice = MembersNotice.NotChanged))
        compose.onNodeWithText("The change was not saved. Try again.").assertExists()
        compose.onNodeWithText("TRY AGAIN").performScrollTo().performClick()
        assertTrue("retry" in calls)
    }
}
