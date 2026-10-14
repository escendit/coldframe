package com.escendit.coldframe.android

import androidx.activity.ComponentActivity
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.padding
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.test.hasAnyAncestor
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.isDialog
import androidx.compose.ui.test.isHeading
import androidx.compose.ui.test.junit4.v2.createAndroidComposeRule
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.onRoot
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.unit.dp
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.escendit.coldframe.android.ui.alerts.AlertsActions
import com.escendit.coldframe.android.ui.alerts.AlertsScreen
import com.escendit.coldframe.android.ui.pause.PauseActions
import com.escendit.coldframe.android.ui.pause.PauseSheetContent
import com.escendit.coldframe.android.ui.sites.LotDetailActions
import com.escendit.coldframe.android.ui.sites.LotDetailScreen
import com.escendit.coldframe.android.ui.sites.LotTiles
import com.escendit.coldframe.android.ui.sites.SitesActions
import com.escendit.coldframe.android.ui.sites.rememberOverviewCopy
import com.escendit.coldframe.android.ui.theme.Coldframe
import com.escendit.coldframe.android.ui.theme.ColdframeTheme
import com.escendit.coldframe.core.alerts.AlertSummary
import com.escendit.coldframe.core.alerts.AlertsNotice
import com.escendit.coldframe.core.alerts.AlertsState
import com.escendit.coldframe.core.appearance.ThemePreference
import com.escendit.coldframe.core.calibrate.CalibrateState
import com.escendit.coldframe.core.devices.DevicesState
import com.escendit.coldframe.core.devices.HubSummary
import com.escendit.coldframe.core.devices.NodeSummary
import com.escendit.coldframe.core.lots.ChargeState
import com.escendit.coldframe.core.lots.CreateLotForm
import com.escendit.coldframe.core.lots.LotDetailState
import com.escendit.coldframe.core.lots.LotPauseSource
import com.escendit.coldframe.core.lots.LotStatus
import com.escendit.coldframe.core.lots.LotSummary
import com.escendit.coldframe.core.lots.LotsOverview
import com.escendit.coldframe.core.lots.LotsState
import com.escendit.coldframe.core.lots.SiteNameForm
import com.escendit.coldframe.core.lots.SiteReminderCadence
import com.escendit.coldframe.core.lots.StaleReason
import com.escendit.coldframe.core.notifications.NotificationSettingsState
import com.escendit.coldframe.core.notifications.ReminderCadence
import com.escendit.coldframe.core.push.PushState
import com.escendit.coldframe.core.setup.HubSetupState
import com.escendit.coldframe.core.setup.NodeSetupState
import com.escendit.coldframe.core.signin.SignInState
import com.escendit.coldframe.core.sites.CreateSiteForm
import com.escendit.coldframe.core.sites.InviteForm
import com.escendit.coldframe.core.sites.Member
import com.escendit.coldframe.core.sites.MemberEditor
import com.escendit.coldframe.core.sites.MembersNotice
import com.escendit.coldframe.core.sites.MembersState
import com.escendit.coldframe.core.sites.PauseForm
import com.escendit.coldframe.core.sites.PauseScope
import com.escendit.coldframe.core.sites.SiteRole
import com.escendit.coldframe.core.sites.SitesState
import com.escendit.coldframe.core.sites.TimeZoneProposal
import com.escendit.coldframe.core.thresholds.ThresholdsState
import com.escendit.coldframe.designtokens.Spacing
import com.github.takahirom.roborazzi.captureRoboImage
import org.junit.After
import org.junit.Before
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode
import java.time.Instant
import java.time.ZoneOffset
import java.util.TimeZone
import kotlin.test.assertTrue

/**
 * Roborazzi snapshots of Create Site, the Garden with Lots, Site settings, Members, Devices, every Add a
 * Hub step and outcome and every Add a Node step with its outcomes, light and dark, at
 * the largest font scale (2×). The Site overview (Story 4.7) and its Lot tiles of every status,
 * stale and skeleton are taken at the default font scale (two columns) and at 2× (one column).
 * Baselines live in tests/kt/android/snapshots; `check` compares against them and
 * `./gradlew :android:recordRoborazziDebug` rewrites them.
 */
@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
@Config(qualifiers = "w411dp-h914dp-mdpi")
class SnapshotTest {
    @get:Rule
    val compose = createAndroidComposeRule<ComponentActivity>()

    private val createSite =
        SitesState.NeedsSite(
            CreateSiteForm(
                name = "Home",
                nameError = null,
                working = false,
                notice = null,
                idempotencyKey = "key-1",
                timeZone = TimeZoneProposal("Europe/Zurich", null, false),
                cancellable = false,
            ),
        )

    /** "Tomatoes" and "Beans", created in that order, both without a Node (Story 1.9). */
    private fun lotsOf(role: SiteRole) =
        LotsState.Ready(
            site = homeSite(role),
            lots =
                listOf(
                    LotSummary("lot-t", "Tomatoes", LotStatus.NoNode),
                    LotSummary("lot-b", "Beans", LotStatus.NoNode),
                ),
            siteName = SiteNameForm("Home garden", null, false),
            create = CreateLotForm("", null, false, "key-1"),
            renaming = null,
            removing = null,
            notice = null,
        )

    private val defaultZone = TimeZone.getDefault()

    // The last-seen times are told in the phone's zone: fixed, so the baselines hold anywhere.
    @Before
    fun utc() {
        TimeZone.setDefault(TimeZone.getTimeZone("UTC"))
    }

    @After
    fun restoreZone() {
        TimeZone.setDefault(defaultZone)
    }

    private fun snapshot(
        name: String,
        sites: SitesState,
        theme: ThemePreference,
        lots: LotsState = (sites as? SitesState.Ready)?.let { lotsOf(it.current.role) } ?: LotsState.Idle,
        siteSettings: Boolean = false,
        scrollTo: String? = null,
        hubSetup: HubSetupState = HubSetupState.CLOSED,
        devices: DevicesState? = null,
        nodeSetup: NodeSetupState = NodeSetupState.CLOSED,
        calibrate: CalibrateState = CalibrateState.Idle,
        thresholds: ThresholdsState = ThresholdsState.Idle,
    ) {
        compose.setContent {
            AtFontScale(2f) {
                ColdframeRoot(
                    state = SignInState.SignedIn("Simon"),
                    sites = sites,
                    theme = theme,
                    onSignIn = {},
                    onSignOut = {},
                    onSelectTheme = {},
                    sitesActions = SitesActions.None,
                    lots = lots,
                    hubSetup = hubSetup,
                    devices = devices ?: DevicesState.Idle,
                    now = { devicesNow },
                    nodeSetup = nodeSetup,
                    calibrate = calibrate,
                    thresholds = thresholds,
                )
            }
        }
        if (devices != null) {
            compose.onNodeWithText("Devices").performClick()
            compose.onNode(isHeading().and(hasText("Devices"))).assertExists()
        }
        if (siteSettings) {
            compose.onNodeWithText("Settings").performClick()
            compose.onNodeWithText("Site settings").performClick()
            compose.onNode(isHeading().and(hasText("Site settings"))).assertExists()
        }
        scrollTo?.let { compose.onNodeWithContentDescription(it).performScrollTo() }
        compose.assertNothingOverflows(name)
        compose.onRoot().captureRoboImage(Repo.file("tests/kt/android/snapshots/$name.png").path)
    }

    @Test
    fun `UX-DR61 Create Site, light, font scale 2`() = snapshot("create-site-light", createSite, ThemePreference.Light)

    @Test
    fun `UX-DR61 Create Site, dark, font scale 2`() = snapshot("create-site-dark", createSite, ThemePreference.Dark)

    @Test
    fun `UX-DR62 UX-DR82 UX-DR18 Garden with no-Node Lots, light, font scale 2`() =
        snapshot("garden-light", readySites(), ThemePreference.Light)

    @Test
    fun `UX-DR62 UX-DR82 UX-DR18 Garden with no-Node Lots, dark, font scale 2`() =
        snapshot("garden-dark", readySites(), ThemePreference.Dark)

    @Test
    fun `UX-DR18 UX-DR20 the no-Node Lot tiles in one column, light, font scale 2`() =
        snapshot(
            "garden-lots-light",
            readySites(),
            ThemePreference.Light,
            scrollTo = "Beans, no Node, add a Node",
        )

    @Test
    fun `UX-DR54 UX-DR20 Garden with Lots for a Member, light, font scale 2`() =
        snapshot("garden-member-light", readySites(homeSite(SiteRole.Member)), ThemePreference.Light)

    @Test
    fun `UX-DR74 Site settings for an Owner, light, font scale 2`() =
        snapshot("site-settings-light", readySites(), ThemePreference.Light, siteSettings = true)

    @Test
    fun `UX-DR74 Site settings for an Owner, dark, font scale 2`() =
        snapshot("site-settings-dark", readySites(), ThemePreference.Dark, siteSettings = true)

    @Test
    fun `UX-DR84 Site settings for a Member, light, font scale 2`() =
        snapshot(
            "site-settings-member-light",
            readySites(homeSite(SiteRole.Member)),
            ThemePreference.Light,
            siteSettings = true,
        )

    // Story 9.1: Members, the Member tiles and the Invite form after an invitation was sent. Story 9.2: the last
    // Owner's tile locked, and Ana's tile with its editor open.

    private fun members(
        name: String,
        theme: ThemePreference,
    ) {
        val simon = Member("u-simon", "Simon Novak", "simon@example.com", SiteRole.Owner, you = true, lastOwner = true)
        val ana = Member("u-ana", "Ana Costa", "ana@example.com", SiteRole.Member, you = false)
        compose.setContent {
            AtFontScale(2f) {
                ColdframeRoot(
                    state = SignInState.SignedIn("Simon"),
                    sites = readySites(),
                    theme = theme,
                    onSignIn = {},
                    onSignOut = {},
                    onSelectTheme = {},
                    lots = lotsOf(SiteRole.Owner),
                    members =
                        MembersState.Ready(
                            siteName = "Home garden",
                            members =
                                listOf(
                                    simon,
                                    ana,
                                ),
                            form = InviteForm(role = SiteRole.Member),
                            notice = MembersNotice.Sent,
                            sentTo = "bo@example.com",
                            editor = MemberEditor(ana, SiteRole.Administrator),
                        ),
                )
            }
        }
        compose.onNodeWithText("Settings").performClick()
        compose.onNodeWithText("Members").performClick()
        compose.onNode(isHeading().and(hasText("Members"))).assertExists()
        compose.assertNothingOverflows(name)
        compose.onRoot().captureRoboImage(Repo.file("tests/kt/android/snapshots/$name.png").path)
    }

    @Test
    @Config(qualifiers = NOTIFICATIONS)
    fun `UX-DR51 UX-DR52 UX-DR73 UX-DR84 Members after inviting, with an editor open, light, font scale 2`() =
        members("members-light", ThemePreference.Light)

    @Test
    @Config(qualifiers = NOTIFICATIONS)
    fun `UX-DR51 UX-DR52 UX-DR73 UX-DR84 Members after inviting, with an editor open, dark, font scale 2`() =
        members("members-dark", ThemePreference.Dark)

    // Story 6.3: My notifications, and the Reminders section of Site settings, in a window that holds the whole surface.

    private fun notifications(
        name: String,
        state: NotificationSettingsState,
        theme: ThemePreference,
    ) {
        compose.setContent {
            AtFontScale(2f) {
                ColdframeRoot(
                    state = SignInState.SignedIn("Simon"),
                    sites = readySites(),
                    theme = theme,
                    onSignIn = {},
                    onSignOut = {},
                    onSelectTheme = {},
                    lots = lotsOf(SiteRole.Owner),
                    notifications = state,
                )
            }
        }
        compose.onNodeWithText("Settings").performClick()
        compose.onNodeWithText("My notifications").performClick()
        compose.onNode(isHeading().and(hasText("My notifications"))).assertExists()
        compose.assertNothingOverflows(name)
        compose.onRoot().captureRoboImage(Repo.file("tests/kt/android/snapshots/$name.png").path)
    }

    @Test
    @Config(qualifiers = NOTIFICATIONS)
    fun `UX-DR72 UX-DR47 UX-DR49 UX-DR50 My notifications with a Site, zone not confirmed, light, font scale 2`() =
        notifications("my-notifications-light", NotificationStates.unconfirmed, ThemePreference.Light)

    @Test
    @Config(qualifiers = NOTIFICATIONS)
    fun `UX-DR72 UX-DR47 UX-DR49 UX-DR50 My notifications with a Site, zone not confirmed, dark, font scale 2`() =
        notifications("my-notifications-dark", NotificationStates.unconfirmed, ThemePreference.Dark)

    @Test
    @Config(qualifiers = NOTIFICATIONS)
    fun `UX-DR72 UX-DR48 My notifications without a Site, zone confirmed, light, font scale 2`() =
        notifications(
            "my-notifications-no-site-light",
            NotificationStates.confirmedWithoutSite,
            ThemePreference.Light,
        )

    @Test
    @Config(qualifiers = NOTIFICATIONS)
    fun `UX-DR72 UX-DR48 My notifications without a Site, zone confirmed, dark, font scale 2`() =
        notifications("my-notifications-no-site-dark", NotificationStates.confirmedWithoutSite, ThemePreference.Dark)

    private fun reminders(
        name: String,
        role: SiteRole,
        theme: ThemePreference,
    ) = snapshot(
        name,
        readySites(homeSite(role)),
        theme,
        lots = lotsOf(role).copy(reminderCadence = SiteReminderCadence(ReminderCadence.Every2Days)),
        siteSettings = true,
    )

    @Test
    @Config(qualifiers = NOTIFICATIONS)
    fun `UX-DR50 UX-DR74 Site settings with Reminders for an Owner, light, font scale 2`() =
        reminders("site-settings-reminders-light", SiteRole.Owner, ThemePreference.Light)

    @Test
    @Config(qualifiers = NOTIFICATIONS)
    fun `UX-DR50 UX-DR74 Site settings with Reminders for an Owner, dark, font scale 2`() =
        reminders("site-settings-reminders-dark", SiteRole.Owner, ThemePreference.Dark)

    @Test
    @Config(qualifiers = NOTIFICATIONS)
    fun `UX-DR50 UX-DR84 Site settings with Reminders read-only for a Member, light, font scale 2`() =
        reminders("site-settings-reminders-member-light", SiteRole.Member, ThemePreference.Light)

    @Test
    @Config(qualifiers = NOTIFICATIONS)
    fun `UX-DR50 UX-DR84 Site settings with Reminders read-only for a Member, dark, font scale 2`() =
        reminders("site-settings-reminders-member-dark", SiteRole.Member, ThemePreference.Dark)

    private val devicesNow = Instant.parse("2026-10-06T07:04:00Z")
    private val seenAt = Instant.parse("2026-10-06T07:02:00Z").toEpochMilli()

    private val onlineHub = HubSummary("3f2a9c0d1e4b5a67", online = true, lastSeenAtEpochMs = seenAt)
    private val offlineHubs =
        listOf(
            HubSummary("1b00aa11bb22cc33", online = false, lastSeenAtEpochMs = null),
            HubSummary("3f2a9c0d1e4b5a67", online = false, lastSeenAtEpochMs = seenAt),
        )

    private fun devices(
        name: String,
        theme: ThemePreference,
        role: SiteRole,
        hubs: List<HubSummary>,
    ) {
        val site = homeSite(role)
        snapshot(name, readySites(site), theme, devices = DevicesState.Ready(site, hubs))
    }

    @Test
    fun `UX-DR30 UX-DR65 Devices with an online Hub for an Owner, light, font scale 2`() =
        devices("devices-online-light", ThemePreference.Light, SiteRole.Owner, listOf(onlineHub))

    @Test
    fun `UX-DR30 UX-DR65 Devices with an online Hub for an Owner, dark, font scale 2`() =
        devices("devices-online-dark", ThemePreference.Dark, SiteRole.Owner, listOf(onlineHub))

    @Test
    fun `UX-DR30 Devices with an offline Hub and one never seen, light, font scale 2`() =
        devices("devices-offline-light", ThemePreference.Light, SiteRole.Administrator, offlineHubs)

    @Test
    fun `UX-DR30 Devices with an offline Hub and one never seen, dark, font scale 2`() =
        devices("devices-offline-dark", ThemePreference.Dark, SiteRole.Administrator, offlineHubs)

    @Test
    fun `UX-DR30 Devices without Devices, light, font scale 2`() =
        devices("devices-empty-light", ThemePreference.Light, SiteRole.Owner, emptyList())

    @Test
    fun `UX-DR30 Devices without Devices, dark, font scale 2`() =
        devices("devices-empty-dark", ThemePreference.Dark, SiteRole.Owner, emptyList())

    @Test
    fun `UX-DR84 Devices for a Member, light, font scale 2`() =
        devices("devices-member-light", ThemePreference.Light, SiteRole.Member, listOf(onlineHub))

    @Test
    fun `UX-DR84 Devices for a Member, dark, font scale 2`() =
        devices("devices-member-dark", ThemePreference.Dark, SiteRole.Member, listOf(onlineHub))

    // Story 7.2: silent Devices and the Silence Window.

    private val silentHubRow =
        HubSummary(
            "3f2a9c0d1e4b5a67",
            online = false,
            lastSeenAtEpochMs = Instant.parse("2026-10-06T06:52:00Z").toEpochMilli(),
            silentSinceEpochMs = Instant.parse("2026-10-06T06:52:00Z").toEpochMilli(),
            silenceWindowSeconds = 300,
        )

    private val silentNodeRow =
        NodeSummary(
            "9c0d1e4b5a673f2a",
            "lot-b",
            "Beans",
            62,
            false,
            ChargeState.NotCharging,
            Instant.parse("2026-10-06T01:00:00Z").toEpochMilli(),
            silentSinceEpochMs = Instant.parse("2026-10-06T01:00:00Z").toEpochMilli(),
            silenceWindowSeconds = 28_800,
            silenceWindowOverridden = true,
        )

    /**
     * Devices with a silent Hub and a silent Node at font scale 2; with [dialog] the Node's Silence Window
     * dialog is open, at the default font scale: a dialog is a window of its own, which the font scale of
     * these tests does not reach.
     */
    private fun silentDevices(
        name: String,
        theme: ThemePreference,
        role: SiteRole,
        dialog: Boolean = false,
    ) {
        val site = homeSite(role)
        compose.setContent {
            AtFontScale(if (dialog) 1f else 2f) {
                ColdframeRoot(
                    state = SignInState.SignedIn("Simon"),
                    sites = readySites(site),
                    theme = theme,
                    onSignIn = {},
                    onSignOut = {},
                    onSelectTheme = {},
                    lots = lotsOf(role),
                    devices = DevicesState.Ready(site, listOf(silentHubRow), listOf(silentNodeRow)),
                    now = { devicesNow },
                )
            }
        }
        compose.onNodeWithText("Devices").performClick()
        compose.onNode(isHeading().and(hasText("Devices"))).assertExists()
        if (dialog) {
            compose
                .onNodeWithContentDescription("Silence Window for Node 9c0d1e4b5a673f2a")
                .performScrollTo()
                .performClick()
            compose
                .onNode(hasText("Silence Window for Node 9c0d1e4b5a673f2a").and(hasAnyAncestor(isDialog())))
                .assertExists()
            // The dialog is a root of its own: it is checked and drawn alone.
            compose.assertNothingOverflows(
                name,
                compose.onNode(isDialog(), useUnmergedTree = true).fetchSemanticsNode(),
            )
            compose.onNode(isDialog()).captureRoboImage(Repo.file("tests/kt/android/snapshots/$name.png").path)
            return
        }
        compose.assertNothingOverflows(name)
        compose.onRoot().captureRoboImage(Repo.file("tests/kt/android/snapshots/$name.png").path)
    }

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR30 UX-DR31 Devices with a silent Hub and Node for an Administrator, light, font scale 2`() =
        silentDevices("devices-silent-light", ThemePreference.Light, SiteRole.Administrator)

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR30 UX-DR31 Devices with a silent Hub and Node for an Administrator, dark, font scale 2`() =
        silentDevices("devices-silent-dark", ThemePreference.Dark, SiteRole.Administrator)

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR84 Devices with a silent Hub and Node for a Member, light, font scale 2`() =
        silentDevices("devices-silent-member-light", ThemePreference.Light, SiteRole.Member)

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR84 Devices with a silent Hub and Node for a Member, dark, font scale 2`() =
        silentDevices("devices-silent-member-dark", ThemePreference.Dark, SiteRole.Member)

    @Test
    @Config(qualifiers = NARROW)
    fun `UX-DR31 the open Silence Window dialog of a Node with its own window, light`() =
        silentDevices("devices-silence-window-light", ThemePreference.Light, SiteRole.Owner, dialog = true)

    @Test
    @Config(qualifiers = NARROW)
    fun `UX-DR31 the open Silence Window dialog of a Node with its own window, dark`() =
        silentDevices("devices-silence-window-dark", ThemePreference.Dark, SiteRole.Owner, dialog = true)

    private fun hub(
        name: String,
        state: HubSetupState,
    ) {
        snapshot("$name-light", readySites(), ThemePreference.Light, hubSetup = state)
    }

    private fun hubDark(
        name: String,
        state: HubSetupState,
    ) {
        snapshot("$name-dark", readySites(), ThemePreference.Dark, hubSetup = state)
    }

    @Test
    fun `UX-DR39 UX-DR37 Add a Hub step 1, light, font scale 2`() = hub("add-hub-scan", HubStates.scan)

    @Test
    fun `UX-DR39 UX-DR37 Add a Hub step 1, dark, font scale 2`() = hubDark("add-hub-scan", HubStates.scan)

    @Test
    fun `UX-DR41 Add a Hub step 2, light, font scale 2`() = hub("add-hub-code", HubStates.wrongCode)

    @Test
    fun `UX-DR41 Add a Hub step 2, dark, font scale 2`() = hubDark("add-hub-code", HubStates.code)

    @Test
    fun `UX-DR42 Add a Hub step 3, light, font scale 2`() = hub("add-hub-wifi", HubStates.wifi)

    @Test
    fun `UX-DR42 Add a Hub step 3, dark, font scale 2`() = hubDark("add-hub-wifi", HubStates.wifi)

    @Test
    fun `UX-DR66 Add a Hub step 4, light, font scale 2`() = hub("add-hub-site", HubStates.site)

    @Test
    fun `UX-DR66 Add a Hub step 4, dark, font scale 2`() = hubDark("add-hub-site", HubStates.site)

    @Test
    fun `UX-DR40 Add a Hub step 5, light, font scale 2`() = hub("add-hub-progress", HubStates.progress)

    @Test
    fun `UX-DR40 Add a Hub step 5, dark, font scale 2`() = hubDark("add-hub-progress", HubStates.progress)

    @Test
    fun `UX-DR55 Hub is online, light, font scale 2`() = hub("add-hub-online", HubStates.online)

    @Test
    fun `UX-DR55 Hub is online, dark, font scale 2`() = hubDark("add-hub-online", HubStates.online)

    @Test
    fun `UX-DR55 UX-DR95 wrong Wi-Fi password, light, font scale 2`() =
        hub("add-hub-wrong-password", HubStates.wrongPassword)

    @Test
    fun `UX-DR55 UX-DR95 wrong Wi-Fi password, dark, font scale 2`() =
        hubDark("add-hub-wrong-password", HubStates.wrongPassword)

    private fun node(
        name: String,
        state: NodeSetupState,
    ) {
        snapshot("$name-light", readySites(), ThemePreference.Light, nodeSetup = state)
    }

    private fun nodeDark(
        name: String,
        state: NodeSetupState,
    ) {
        snapshot("$name-dark", readySites(), ThemePreference.Dark, nodeSetup = state)
    }

    @Test
    fun `UX-DR39 UX-DR67 Add a Node step 1, light, font scale 2`() = node("add-node-press", NodeStates.press)

    @Test
    fun `UX-DR39 UX-DR67 Add a Node step 1, dark, font scale 2`() = nodeDark("add-node-press", NodeStates.press)

    @Test
    fun `UX-DR37 Add a Node step 2, light, font scale 2`() = node("add-node-scan", NodeStates.scan)

    @Test
    fun `UX-DR37 Add a Node step 2, dark, font scale 2`() = nodeDark("add-node-scan", NodeStates.scan)

    @Test
    fun `UX-DR94 Add a Node step 3 with a wrong code, light, font scale 2`() =
        node("add-node-code-wrong", NodeStates.wrongCode)

    @Test
    fun `UX-DR94 Add a Node step 3 with a wrong code, dark, font scale 2`() =
        nodeDark("add-node-code-wrong", NodeStates.wrongCode)

    @Test
    fun `UX-DR41 Add a Node step 3 accepted, light, font scale 2`() =
        node("add-node-code-accepted", NodeStates.accepted)

    @Test
    fun `UX-DR41 Add a Node step 3 accepted, dark, font scale 2`() =
        nodeDark("add-node-code-accepted", NodeStates.accepted)

    @Test
    fun `UX-DR38 Add a Node step 4, light, font scale 2`() = node("add-node-lot", NodeStates.lot)

    @Test
    fun `UX-DR38 Add a Node step 4, dark, font scale 2`() = nodeDark("add-node-lot", NodeStates.lot)

    @Test
    fun `UX-DR94 UX-DR38 Lot taken meanwhile, light, font scale 2`() = node("add-node-lot-taken", NodeStates.lotTaken)

    @Test
    fun `UX-DR94 UX-DR38 Lot taken meanwhile, dark, font scale 2`() =
        nodeDark("add-node-lot-taken", NodeStates.lotTaken)

    @Test
    fun `UX-DR55 UX-DR67 Lot has a Node, light, font scale 2`() = node("add-node-assigned", NodeStates.assigned)

    @Test
    fun `UX-DR55 UX-DR67 Lot has a Node, dark, font scale 2`() = nodeDark("add-node-assigned", NodeStates.assigned)

    @Test
    fun `UX-DR55 UX-DR94 the Node stopped listening, light, font scale 2`() =
        node("add-node-stopped-listening", NodeStates.stoppedListening)

    @Test
    fun `UX-DR55 UX-DR94 the Node stopped listening, dark, font scale 2`() =
        nodeDark("add-node-stopped-listening", NodeStates.stoppedListening)

    // Story 4.7: the Site overview with Lots of every status.

    /** The Garden as the shell shows it, at [fontScale], with the clock of the Lot fixtures. */
    private fun overview(
        name: String,
        lots: LotsState,
        theme: ThemePreference,
        fontScale: Float,
        scrollTo: String? = null,
    ) {
        compose.setContent {
            AtFontScale(fontScale) {
                ColdframeRoot(
                    state = SignInState.SignedIn("Simon"),
                    sites = readySites(),
                    theme = theme,
                    onSignIn = {},
                    onSignOut = {},
                    onSelectTheme = {},
                    lots = lots,
                    now = { LotFixtures.now },
                )
            }
        }
        scrollTo?.let { compose.onNodeWithText(it).performScrollTo() }
        compose.assertNothingOverflows(name)
        compose.onRoot().captureRoboImage(Repo.file("tests/kt/android/snapshots/$name.png").path)
    }

    private val staleLots = LotFixtures.ready(staleReason = StaleReason.Unreachable)

    @Test
    fun `UX-DR21 UX-DR129 UX-DR107 the overview with every status, light`() =
        overview("garden-overview-light", LotFixtures.ready(), ThemePreference.Light, fontScale = 1f)

    @Test
    fun `UX-DR21 UX-DR129 UX-DR107 the overview with every status, dark`() =
        overview("garden-overview-dark", LotFixtures.ready(), ThemePreference.Dark, fontScale = 1f)

    @Test
    fun `UX-DR21 UX-DR129 the overview with every status, light, font scale 2`() =
        overview("garden-overview-large-light", LotFixtures.ready(), ThemePreference.Light, fontScale = 2f)

    @Test
    fun `UX-DR21 UX-DR129 the overview with every status, dark, font scale 2`() =
        overview("garden-overview-large-dark", LotFixtures.ready(), ThemePreference.Dark, fontScale = 2f)

    // Story 7.2: the Hub notice above the tiles, with the Lots behind the Hub unknown.

    @Test
    fun `UX-DR81 UX-DR18 the overview with a silent Hub, light`() =
        overview("garden-hub-silent-light", LotFixtures.hubSilent(), ThemePreference.Light, fontScale = 1f)

    @Test
    fun `UX-DR81 UX-DR18 the overview with a silent Hub, dark`() =
        overview("garden-hub-silent-dark", LotFixtures.hubSilent(), ThemePreference.Dark, fontScale = 1f)

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR81 UX-DR96 the overview with a silent Hub, light, font scale 2`() =
        overview("garden-hub-silent-large-light", LotFixtures.hubSilent(), ThemePreference.Light, fontScale = 2f)

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR81 UX-DR96 the overview with a silent Hub, dark, font scale 2`() =
        overview("garden-hub-silent-large-dark", LotFixtures.hubSilent(), ThemePreference.Dark, fontScale = 2f)

    @Test
    fun `UX-DR24 UX-DR79 the stale header over stale tiles, light`() =
        overview("garden-stale-light", staleLots, ThemePreference.Light, fontScale = 1f)

    @Test
    fun `UX-DR24 UX-DR79 the stale header over stale tiles, dark`() =
        overview("garden-stale-dark", staleLots, ThemePreference.Dark, fontScale = 1f)

    @Test
    fun `UX-DR24 UX-DR79 the stale header, light, font scale 2`() =
        overview("garden-stale-large-light", staleLots, ThemePreference.Light, fontScale = 2f)

    @Test
    fun `UX-DR24 UX-DR79 the stale header, dark, font scale 2`() =
        overview("garden-stale-large-dark", staleLots, ThemePreference.Dark, fontScale = 2f)

    @Test
    fun `UX-DR80 UX-DR19 Loading with skeleton tiles, light`() =
        overview(
            "garden-loading-light",
            LotsState.Loading(homeSite()),
            ThemePreference.Light,
            fontScale = 1f,
            scrollTo = "Lots",
        )

    @Test
    fun `UX-DR80 UX-DR19 Loading with skeleton tiles, dark`() =
        overview(
            "garden-loading-dark",
            LotsState.Loading(homeSite()),
            ThemePreference.Dark,
            fontScale = 1f,
            scrollTo = "Lots",
        )

    @Test
    fun `UX-DR80 UX-DR19 Loading with skeleton tiles in one column, light, font scale 2`() =
        overview(
            "garden-loading-large-light",
            LotsState.Loading(homeSite()),
            ThemePreference.Light,
            fontScale = 2f,
            scrollTo = "Lots",
        )

    /**
     * The Alerts surface alone, whole: every row of [state] at once, on a window tall enough to hold
     * them (the surface scrolls, so the shell's window shows only the first rows).
     */
    private fun alerts(
        name: String,
        state: AlertsState,
        theme: ThemePreference,
        fontScale: Float = 1f,
    ) {
        compose.setContent {
            AtFontScale(fontScale) {
                ColdframeTheme(isDark = theme == ThemePreference.Dark) {
                    Box(Modifier.testTag("alerts").background(Coldframe.colors.background)) {
                        AlertsScreen(
                            alerts = state,
                            actions = AlertsActions.None,
                            onOpenLot = { _, _ -> },
                            onOpenDevices = {},
                            now = { AlertFixtures.now },
                            zone = ZoneOffset.UTC,
                            fill = false,
                        )
                    }
                }
            }
        }
        compose.assertNothingOverflows(name)
        val surface = compose.onNodeWithTag("alerts")
        val window = compose.activity.window.decorView.height
        assertTrue(surface.fetchSemanticsNode().size.height < window, "$name fits its window of $window px")
        surface.captureRoboImage(Repo.file("tests/kt/android/snapshots/$name.png").path)
    }

    private fun alertsOf(rows: List<AlertSummary>) = AlertFixtures.ready(rows)

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR25 UX-DR64 UX-DR14 every Alert row variant in both groups and Closed, light`() =
        alerts("alerts-light", AlertFixtures.ready(), ThemePreference.Light)

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR25 UX-DR64 UX-DR14 every Alert row variant in both groups and Closed, dark`() =
        alerts("alerts-dark", AlertFixtures.ready(), ThemePreference.Dark)

    @Test
    @Config(qualifiers = TALLER)
    fun `UX-DR25 UX-DR96 the Threshold Alert rows, light, font scale 2`() =
        alerts("alerts-threshold-large-light", alertsOf(AlertFixtures.threshold), ThemePreference.Light, 2f)

    @Test
    @Config(qualifiers = TALLER)
    fun `UX-DR25 UX-DR96 the Threshold Alert rows, dark, font scale 2`() =
        alerts("alerts-threshold-large-dark", alertsOf(AlertFixtures.threshold), ThemePreference.Dark, 2f)

    @Test
    @Config(qualifiers = TALLER)
    fun `UX-DR25 UX-DR96 the Health Alert rows, light, font scale 2`() =
        alerts("alerts-health-large-light", alertsOf(AlertFixtures.health), ThemePreference.Light, 2f)

    @Test
    @Config(qualifiers = TALLER)
    fun `UX-DR25 UX-DR96 the Health Alert rows, dark, font scale 2`() =
        alerts("alerts-health-large-dark", alertsOf(AlertFixtures.health), ThemePreference.Dark, 2f)

    // Story 7.2: Health rows of a silent Node and a silent Hub.

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR117 UX-DR25 Alerts with Node and Hub Health rows, light`() =
        alerts("alerts-silent-light", alertsOf(AlertFixtures.silentDevices), ThemePreference.Light)

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR117 UX-DR25 Alerts with Node and Hub Health rows, dark`() =
        alerts("alerts-silent-dark", alertsOf(AlertFixtures.silentDevices), ThemePreference.Dark)

    @Test
    @Config(qualifiers = TALLER)
    fun `UX-DR117 UX-DR96 Alerts with Node and Hub Health rows, light, font scale 2`() =
        alerts("alerts-silent-large-light", alertsOf(AlertFixtures.silentDevices), ThemePreference.Light, 2f)

    @Test
    @Config(qualifiers = TALLER)
    fun `UX-DR117 UX-DR96 Alerts with Node and Hub Health rows, dark, font scale 2`() =
        alerts("alerts-silent-large-dark", alertsOf(AlertFixtures.silentDevices), ThemePreference.Dark, 2f)

    // Story 7.3: Health rows of a low battery.

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR25 UX-DR26 Alerts with low-battery Health rows, light`() =
        alerts("alerts-battery-light", alertsOf(AlertFixtures.lowBatteries), ThemePreference.Light)

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR25 UX-DR26 Alerts with low-battery Health rows, dark`() =
        alerts("alerts-battery-dark", alertsOf(AlertFixtures.lowBatteries), ThemePreference.Dark)

    @Test
    @Config(qualifiers = TALLER)
    fun `UX-DR25 UX-DR96 Alerts with low-battery Health rows, light, font scale 2`() =
        alerts("alerts-battery-large-light", alertsOf(AlertFixtures.lowBatteries), ThemePreference.Light, 2f)

    @Test
    @Config(qualifiers = TALLER)
    fun `UX-DR25 UX-DR96 Alerts with low-battery Health rows, dark, font scale 2`() =
        alerts("alerts-battery-large-dark", alertsOf(AlertFixtures.lowBatteries), ThemePreference.Dark, 2f)

    @Test
    fun `UX-DR82 UX-DR64 no open Alerts, light, font scale 2`() =
        alerts("alerts-empty-light", alertsOf(emptyList()), ThemePreference.Light, 2f)

    @Test
    fun `UX-DR82 UX-DR64 no open Alerts, dark, font scale 2`() =
        alerts("alerts-empty-dark", alertsOf(emptyList()), ThemePreference.Dark, 2f)

    @Test
    fun `UX-DR82 UX-DR64 only closed Alerts, light, font scale 2`() =
        alerts("alerts-only-closed-light", alertsOf(AlertFixtures.closed), ThemePreference.Light, 2f)

    @Test
    fun `UX-DR82 UX-DR64 only closed Alerts, dark, font scale 2`() =
        alerts("alerts-only-closed-dark", alertsOf(AlertFixtures.closed), ThemePreference.Dark, 2f)

    @Test
    fun `UX-DR64 Alerts that cannot be read, light, font scale 2`() =
        alerts(
            "alerts-failed-light",
            AlertsState.Failed(homeSite(), AlertsNotice.Unreachable),
            ThemePreference.Light,
            2f,
        )

    @Test
    fun `UX-DR64 Alerts that cannot be read, dark, font scale 2`() =
        alerts("alerts-failed-dark", AlertsState.Failed(homeSite(), AlertsNotice.Unreachable), ThemePreference.Dark, 2f)

    /** The shell on the Alerts tab: the tab label carries the open count. */
    private fun alertsTab(
        name: String,
        theme: ThemePreference,
    ) {
        val state = alertsOf(List(5) { AlertFixtures.needsWater.copy(id = "a$it", lotName = "Tomatoes ${it + 1}") })
        compose.setContent {
            ColdframeRoot(
                state = SignInState.SignedIn("Simon"),
                sites = readySites(),
                theme = theme,
                onSignIn = {},
                onSignOut = {},
                onSelectTheme = {},
                lots = lotsOf(SiteRole.Owner),
                alerts = state,
                now = { AlertFixtures.now },
            )
        }
        compose.onNodeWithText("Alerts · 5").performClick()
        compose.onNode(isHeading().and(hasText("Alerts"))).assertExists()
        compose.assertNothingOverflows(name)
        compose.onRoot().captureRoboImage(Repo.file("tests/kt/android/snapshots/$name.png").path)
    }

    @Test
    fun `UX-DR57 UX-DR64 the Alerts tab with five open Alerts reads Alerts 5, light`() =
        alertsTab("alerts-tab-count-light", ThemePreference.Light)

    @Test
    fun `UX-DR57 UX-DR64 the Alerts tab with five open Alerts reads Alerts 5, dark`() =
        alertsTab("alerts-tab-count-dark", ThemePreference.Dark)

    /**
     * The Lot grid alone, whole: every tile of [lots] at once, on a window tall enough to hold
     * them (the Garden scrolls, so the shell's window shows only the first rows).
     */
    private fun grid(
        name: String,
        lots: List<LotSummary>,
        theme: ThemePreference,
        fontScale: Float,
        stale: Boolean = false,
    ) {
        val ready = LotFixtures.ready(lots, staleReason = if (stale) StaleReason.Unreachable else null)
        val overview = LotsOverview.of(ready, LotFixtures.now.toEpochMilli())
        compose.setContent {
            AtFontScale(fontScale) {
                ColdframeTheme(isDark = theme == ThemePreference.Dark) {
                    Box(
                        Modifier
                            .testTag("grid")
                            .background(Coldframe.colors.background)
                            .padding(Spacing.GUTTER_MOBILE.dp),
                    ) {
                        LotTiles(overview.tiles, rememberOverviewCopy(LotFixtures.now, ZoneOffset.UTC))
                    }
                }
            }
        }
        val grid = compose.onNodeWithTag("grid")
        val window = compose.activity.window.decorView.height
        assertTrue(grid.fetchSemanticsNode().size.height < window, "$name fits its window of $window px")
        grid.captureRoboImage(Repo.file("tests/kt/android/snapshots/$name.png").path)
    }

    private val largeA = listOf(LotFixtures.needsWater, LotFixtures.needsCalibration, LotFixtures.unknownNode)
    private val largeB =
        listOf(LotFixtures.unknownHub, LotFixtures.unknownNoReading, LotFixtures.ok, LotFixtures.okBare)
    private val largeC = listOf(LotFixtures.paused, LotFixtures.pausedBySite, LotFixtures.noNode)
    private val staleA = LotFixtures.everyVariant.take(5)
    private val staleB = LotFixtures.everyVariant.drop(5)

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR18 UX-DR17 UX-DR99 every tile variant in two columns, light`() =
        grid("lot-tiles-light", LotFixtures.everyVariant, ThemePreference.Light, fontScale = 1f)

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR18 UX-DR17 UX-DR99 every tile variant in two columns, dark`() =
        grid("lot-tiles-dark", LotFixtures.everyVariant, ThemePreference.Dark, fontScale = 1f)

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR19 UX-DR99 every stale tile in two columns, light`() =
        grid("lot-tiles-stale-light", LotFixtures.everyVariant, ThemePreference.Light, fontScale = 1f, stale = true)

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR19 UX-DR99 every stale tile in two columns, dark`() =
        grid("lot-tiles-stale-dark", LotFixtures.everyVariant, ThemePreference.Dark, fontScale = 1f, stale = true)

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR18 UX-DR97 needs water, needs Calibration and unknown in one column, light, font scale 2`() =
        grid("lot-tiles-large-a-light", largeA, ThemePreference.Light, fontScale = 2f)

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR18 UX-DR97 needs water, needs Calibration and unknown in one column, dark, font scale 2`() =
        grid("lot-tiles-large-a-dark", largeA, ThemePreference.Dark, fontScale = 2f)

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR18 UX-DR97 Hub silent, no Readings and OK in one column, light, font scale 2`() =
        grid("lot-tiles-large-b-light", largeB, ThemePreference.Light, fontScale = 2f)

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR18 UX-DR97 Hub silent, no Readings and OK in one column, dark, font scale 2`() =
        grid("lot-tiles-large-b-dark", largeB, ThemePreference.Dark, fontScale = 2f)

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR18 UX-DR97 paused and no Node in one column, light, font scale 2`() =
        grid("lot-tiles-large-c-light", largeC, ThemePreference.Light, fontScale = 2f)

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR18 UX-DR97 paused and no Node in one column, dark, font scale 2`() =
        grid("lot-tiles-large-c-dark", largeC, ThemePreference.Dark, fontScale = 2f)

    @Test
    @Config(qualifiers = TALLER)
    fun `UX-DR19 UX-DR97 the first stale tiles in one column, light, font scale 2`() =
        grid("lot-tiles-stale-large-a-light", staleA, ThemePreference.Light, fontScale = 2f, stale = true)

    @Test
    @Config(qualifiers = TALLER)
    fun `UX-DR19 UX-DR97 the first stale tiles in one column, dark, font scale 2`() =
        grid("lot-tiles-stale-large-a-dark", staleA, ThemePreference.Dark, fontScale = 2f, stale = true)

    @Test
    @Config(qualifiers = TALLER)
    fun `UX-DR19 UX-DR97 the other stale tiles in one column, light, font scale 2`() =
        grid("lot-tiles-stale-large-b-light", staleB, ThemePreference.Light, fontScale = 2f, stale = true)

    @Test
    @Config(qualifiers = TALLER)
    fun `UX-DR19 UX-DR97 the other stale tiles in one column, dark, font scale 2`() =
        grid("lot-tiles-stale-large-b-dark", staleB, ThemePreference.Dark, fontScale = 2f, stale = true)

    /** Lot detail whole, in one column at font scale 2 and with 3-up and 2-up cells at 1, on a window tall enough to hold it. */
    private fun detail(
        name: String,
        state: LotDetailState,
        theme: ThemePreference,
        fontScale: Float,
    ) {
        compose.setContent {
            AtFontScale(fontScale) {
                ColdframeTheme(isDark = theme == ThemePreference.Dark) {
                    LotDetailScreen(state = state, actions = LotDetailActions.None, now = { LotFixtures.now })
                }
            }
        }
        compose.assertNothingOverflows(name)
        compose.onRoot().captureRoboImage(Repo.file("tests/kt/android/snapshots/$name.png").path)
    }

    private val detailStates =
        object {
            val needsCalibration = LotDetailFixtures.ready(LotDetailFixtures.needsCalibration)
            val needsWater = LotDetailFixtures.ready(LotDetailFixtures.needsWater)
            val ok = LotDetailFixtures.ready(LotDetailFixtures.ok)
            val unknown = LotDetailFixtures.ready(LotDetailFixtures.unknown)
            val pausedBySite = LotDetailFixtures.ready(LotDetailFixtures.pausedBySite)
            val noNode = LotDetailFixtures.ready(LotDetailFixtures.noNode)
            val stale =
                LotDetailFixtures.ready(
                    LotDetailFixtures.needsCalibration,
                    staleReason = StaleReason.Unreachable,
                )
        }

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR63 UX-DR27 UX-DR28 UX-DR29 UX-DR32 UX-DR33 UX-DR78 Calibration detail, light`() =
        detail(
            "lot-detail-needs-calibration-light",
            detailStates.needsCalibration,
            ThemePreference.Light,
            fontScale = 1f,
        )

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR63 UX-DR27 UX-DR28 UX-DR29 UX-DR32 UX-DR33 UX-DR78 Calibration detail, dark`() =
        detail("lot-detail-needs-calibration-dark", detailStates.needsCalibration, ThemePreference.Dark, fontScale = 1f)

    @Test
    @Config(qualifiers = HUGE)
    fun `UX-DR63 UX-DR27 UX-DR28 UX-DR29 UX-DR32 UX-DR33 UX-DR78 Calibration detail, light, font scale 2`() =
        detail(
            "lot-detail-needs-calibration-large-light",
            detailStates.needsCalibration,
            ThemePreference.Light,
            fontScale = 2f,
        )

    @Test
    @Config(qualifiers = HUGE)
    fun `UX-DR63 UX-DR27 UX-DR28 UX-DR29 UX-DR32 UX-DR33 UX-DR78 Calibration detail, dark, font scale 2`() =
        detail(
            "lot-detail-needs-calibration-large-dark",
            detailStates.needsCalibration,
            ThemePreference.Dark,
            fontScale = 2f,
        )

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR27 UX-DR63 Lot detail needing water, light`() =
        detail("lot-detail-needs-water-light", detailStates.needsWater, ThemePreference.Light, fontScale = 1f)

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR27 UX-DR63 Lot detail needing water, dark`() =
        detail("lot-detail-needs-water-dark", detailStates.needsWater, ThemePreference.Dark, fontScale = 1f)

    @Test
    @Config(qualifiers = HUGE)
    fun `UX-DR27 UX-DR63 Lot detail needing water, light, font scale 2`() =
        detail("lot-detail-needs-water-large-light", detailStates.needsWater, ThemePreference.Light, fontScale = 2f)

    @Test
    @Config(qualifiers = HUGE)
    fun `UX-DR27 UX-DR63 Lot detail needing water, dark, font scale 2`() =
        detail("lot-detail-needs-water-large-dark", detailStates.needsWater, ThemePreference.Dark, fontScale = 2f)

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR27 UX-DR63 Lot detail, OK, light`() =
        detail("lot-detail-ok-light", detailStates.ok, ThemePreference.Light, fontScale = 1f)

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR27 UX-DR63 Lot detail, OK, dark`() =
        detail("lot-detail-ok-dark", detailStates.ok, ThemePreference.Dark, fontScale = 1f)

    @Test
    @Config(qualifiers = HUGE)
    fun `UX-DR27 UX-DR63 Lot detail, OK, light, font scale 2`() =
        detail("lot-detail-ok-large-light", detailStates.ok, ThemePreference.Light, fontScale = 2f)

    @Test
    @Config(qualifiers = HUGE)
    fun `UX-DR27 UX-DR63 Lot detail, OK, dark, font scale 2`() =
        detail("lot-detail-ok-large-dark", detailStates.ok, ThemePreference.Dark, fontScale = 2f)

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR78 UX-DR63 Lot detail of a silent Node, light`() =
        detail("lot-detail-unknown-light", detailStates.unknown, ThemePreference.Light, fontScale = 1f)

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR78 UX-DR63 Lot detail of a silent Node, dark`() =
        detail("lot-detail-unknown-dark", detailStates.unknown, ThemePreference.Dark, fontScale = 1f)

    @Test
    @Config(qualifiers = HUGE)
    fun `UX-DR78 UX-DR63 Lot detail of a silent Node, light, font scale 2`() =
        detail("lot-detail-unknown-large-light", detailStates.unknown, ThemePreference.Light, fontScale = 2f)

    @Test
    @Config(qualifiers = HUGE)
    fun `UX-DR78 UX-DR63 Lot detail of a silent Node, dark, font scale 2`() =
        detail("lot-detail-unknown-large-dark", detailStates.unknown, ThemePreference.Dark, fontScale = 2f)

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR27 UX-DR78 UX-DR63 Lot detail paused by the Site, light`() =
        detail("lot-detail-paused-light", detailStates.pausedBySite, ThemePreference.Light, fontScale = 1f)

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR27 UX-DR78 UX-DR63 Lot detail paused by the Site, dark`() =
        detail("lot-detail-paused-dark", detailStates.pausedBySite, ThemePreference.Dark, fontScale = 1f)

    @Test
    @Config(qualifiers = HUGE)
    fun `UX-DR27 UX-DR78 UX-DR63 Lot detail paused by the Site, light, font scale 2`() =
        detail("lot-detail-paused-large-light", detailStates.pausedBySite, ThemePreference.Light, fontScale = 2f)

    @Test
    @Config(qualifiers = HUGE)
    fun `UX-DR27 UX-DR78 UX-DR63 Lot detail paused by the Site, dark, font scale 2`() =
        detail("lot-detail-paused-large-dark", detailStates.pausedBySite, ThemePreference.Dark, fontScale = 2f)

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR63 Lot detail without a Node, light`() =
        detail("lot-detail-no-node-light", detailStates.noNode, ThemePreference.Light, fontScale = 1f)

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR63 Lot detail without a Node, dark`() =
        detail("lot-detail-no-node-dark", detailStates.noNode, ThemePreference.Dark, fontScale = 1f)

    @Test
    @Config(qualifiers = HUGE)
    fun `UX-DR63 Lot detail without a Node, light, font scale 2`() =
        detail("lot-detail-no-node-large-light", detailStates.noNode, ThemePreference.Light, fontScale = 2f)

    @Test
    @Config(qualifiers = HUGE)
    fun `UX-DR63 Lot detail without a Node, dark, font scale 2`() =
        detail("lot-detail-no-node-large-dark", detailStates.noNode, ThemePreference.Dark, fontScale = 2f)

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR63 UX-DR24 Lot detail in stale mode, light`() =
        detail("lot-detail-stale-light", detailStates.stale, ThemePreference.Light, fontScale = 1f)

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR63 UX-DR24 Lot detail in stale mode, dark`() =
        detail("lot-detail-stale-dark", detailStates.stale, ThemePreference.Dark, fontScale = 1f)

    @Test
    @Config(qualifiers = HUGE)
    fun `UX-DR63 UX-DR24 Lot detail in stale mode, light, font scale 2`() =
        detail("lot-detail-stale-large-light", detailStates.stale, ThemePreference.Light, fontScale = 2f)

    @Test
    @Config(qualifiers = HUGE)
    fun `UX-DR63 UX-DR24 Lot detail in stale mode, dark, font scale 2`() =
        detail("lot-detail-stale-large-dark", detailStates.stale, ThemePreference.Dark, fontScale = 2f)

    // Story 6.5: the one line of why and the notifications-off notice, on the overview and in My notifications.

    private fun push(
        name: String,
        push: PushState,
        theme: ThemePreference,
        myNotifications: Boolean = false,
    ) {
        compose.setContent {
            AtFontScale(2f) {
                ColdframeRoot(
                    state = SignInState.SignedIn("Simon"),
                    sites = readySites(),
                    theme = theme,
                    onSignIn = {},
                    onSignOut = {},
                    onSelectTheme = {},
                    lots = LotFixtures.ready(lots = LotFixtures.everyStatus),
                    now = { LotFixtures.now },
                    notifications = NotificationStates.unconfirmed,
                    push = push,
                )
            }
        }
        if (myNotifications) {
            compose.onNodeWithText("Settings").performClick()
            compose.onNodeWithText("My notifications").performClick()
            compose.onNode(isHeading().and(hasText("My notifications"))).assertExists()
        }
        compose.assertNothingOverflows(name)
        compose.onRoot().captureRoboImage(Repo.file("tests/kt/android/snapshots/$name.png").path)
    }

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR122 the overview with the one line of why, light, font scale 2`() =
        push("push-why-light", PushStates.promptDue, ThemePreference.Light)

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR122 the overview with the one line of why, dark, font scale 2`() =
        push("push-why-dark", PushStates.promptDue, ThemePreference.Dark)

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR88 the overview with notifications off, light, font scale 2`() =
        push("push-off-overview-light", PushStates.denied, ThemePreference.Light)

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR88 the overview with notifications off, dark, font scale 2`() =
        push("push-off-overview-dark", PushStates.denied, ThemePreference.Dark)

    @Test
    @Config(qualifiers = NOTIFICATIONS)
    fun `UX-DR88 My notifications with notifications off, light, font scale 2`() =
        push("push-off-my-notifications-light", PushStates.denied, ThemePreference.Light, myNotifications = true)

    @Test
    @Config(qualifiers = NOTIFICATIONS)
    fun `UX-DR88 My notifications with notifications off, dark, font scale 2`() =
        push("push-off-my-notifications-dark", PushStates.denied, ThemePreference.Dark, myNotifications = true)

    private companion object {
        /**
         * For a dialog that holds a text field. Under Robolectric a text field in a dialog never lets Compose come
         * to rest on a window of 360 dp or wider (a bare `BasicTextField` in an `AlertDialog` shows it), so such a
         * dialog is drawn at 320 dp, the narrowest phone, where it does.
         */
        const val NARROW = "w320dp-h914dp-mdpi"

        /** A window that holds four one-column tiles, or ten in two columns. */
        const val TALL = "w411dp-h1700dp-mdpi"

        /** Five one-column tiles. */
        const val TALLER = "w411dp-h2100dp-mdpi"

        /** Lot detail in one column at font scale 2. */
        const val HUGE = "w411dp-h3600dp-mdpi"

        /** My notifications, and Site settings down to its Reminders, at font scale 2. */
        const val NOTIFICATIONS = "w411dp-h2600dp-mdpi"
    }

    // Story 8.2: Pause and Resume. The sheet's content alone, then the paused surfaces as the shell shows them.

    /** The Pause sheet's content on the sheet's ground, as a `ModalBottomSheet` holds it. */
    private fun pauseSheet(
        name: String,
        form: PauseForm,
        theme: ThemePreference,
        fontScale: Float = 1f,
    ) {
        compose.setContent {
            AtFontScale(fontScale) {
                ColdframeTheme(isDark = theme == ThemePreference.Dark) {
                    Box(Modifier.testTag("sheet").background(Coldframe.colors.background)) {
                        PauseSheetContent(form = form, actions = PauseActions.None)
                    }
                }
            }
        }
        compose.assertNothingOverflows(name)
        compose.onNodeWithTag("sheet").captureRoboImage(Repo.file("tests/kt/android/snapshots/$name.png").path)
    }

    @Test
    fun `UX-DR46 the Pause sheet from the Site menu, Whole Site only and no date, light`() =
        pauseSheet("pause-sheet-site-light", PauseFixtures.forSite, ThemePreference.Light)

    @Test
    fun `UX-DR46 the Pause sheet from the Site menu, Whole Site only and no date, dark`() =
        pauseSheet("pause-sheet-site-dark", PauseFixtures.forSite, ThemePreference.Dark)

    private val pauseDeviceUntil = PauseFixtures.forDevice.copy(until = "2026-11-01")

    @Test
    fun `UX-DR46 the Pause sheet from a Device, This Device selected with a date, light`() =
        pauseSheet("pause-sheet-device-light", pauseDeviceUntil, ThemePreference.Light)

    @Test
    fun `UX-DR46 the Pause sheet from a Device, This Device selected with a date, dark`() =
        pauseSheet("pause-sheet-device-dark", pauseDeviceUntil, ThemePreference.Dark)

    @Test
    fun `UX-DR46 the Pause sheet from a Device with Whole Site chosen, light`() =
        pauseSheet(
            "pause-sheet-device-site-light",
            pauseDeviceUntil.copy(scope = PauseScope.Site, until = "2027-03-01"),
            ThemePreference.Light,
        )

    @Test
    fun `UX-DR46 the Pause sheet from a Device with Whole Site chosen, dark`() =
        pauseSheet(
            "pause-sheet-device-site-dark",
            pauseDeviceUntil.copy(scope = PauseScope.Site, until = "2027-03-01"),
            ThemePreference.Dark,
        )

    @Test
    fun `UX-DR46 the Pause sheet with a refused date, light`() =
        pauseSheet("pause-sheet-refused-light", PauseFixtures.refused, ThemePreference.Light)

    @Test
    fun `UX-DR46 the Pause sheet with a refused date, dark`() =
        pauseSheet("pause-sheet-refused-dark", PauseFixtures.refused, ThemePreference.Dark)

    @Test
    fun `UX-DR46 UX-DR96 the Pause sheet with a refused date, light, font scale 2`() =
        pauseSheet("pause-sheet-refused-large-light", PauseFixtures.refused, ThemePreference.Light, fontScale = 2f)

    @Test
    fun `UX-DR46 UX-DR96 the Pause sheet with a refused date, dark, font scale 2`() =
        pauseSheet("pause-sheet-refused-large-dark", PauseFixtures.refused, ThemePreference.Dark, fontScale = 2f)

    /** The overview of a paused Site for [role], with the Site the Lots belong to as the current one. */
    private fun pausedOverview(
        name: String,
        role: SiteRole,
        theme: ThemePreference,
        fontScale: Float,
    ) {
        val lots = LotFixtures.pausedSite(role)
        compose.setContent {
            AtFontScale(fontScale) {
                ColdframeRoot(
                    state = SignInState.SignedIn("Simon"),
                    sites = readySites(lots.site),
                    theme = theme,
                    onSignIn = {},
                    onSignOut = {},
                    onSelectTheme = {},
                    lots = lots,
                    now = { LotFixtures.now },
                )
            }
        }
        compose.assertNothingOverflows(name)
        compose.onRoot().captureRoboImage(Repo.file("tests/kt/android/snapshots/$name.png").path)
    }

    @Test
    fun `UX-DR83 the overview of a paused Site, light`() =
        pausedOverview("garden-paused-light", SiteRole.Owner, ThemePreference.Light, fontScale = 1f)

    @Test
    fun `UX-DR83 the overview of a paused Site, dark`() =
        pausedOverview("garden-paused-dark", SiteRole.Owner, ThemePreference.Dark, fontScale = 1f)

    @Test
    fun `UX-DR83 UX-DR96 the overview of a paused Site, light, font scale 2`() =
        pausedOverview("garden-paused-large-light", SiteRole.Owner, ThemePreference.Light, fontScale = 2f)

    @Test
    fun `UX-DR83 UX-DR96 the overview of a paused Site, dark, font scale 2`() =
        pausedOverview("garden-paused-large-dark", SiteRole.Owner, ThemePreference.Dark, fontScale = 2f)

    @Test
    fun `UX-DR83 UX-DR84 the overview of a paused Site for a Member, light`() =
        pausedOverview("garden-paused-member-light", SiteRole.Member, ThemePreference.Light, fontScale = 1f)

    @Test
    fun `UX-DR83 UX-DR84 the overview of a paused Site for a Member, dark`() =
        pausedOverview("garden-paused-member-dark", SiteRole.Member, ThemePreference.Dark, fontScale = 1f)

    /** A Hub paused with its Site, a Hub paused by itself, a Node paused with its Site, one by both, and a live one. */
    private fun pausedDevices(
        name: String,
        theme: ThemePreference,
        role: SiteRole,
    ) {
        val site = homeSite(role)
        val bySite = listOf(LotPauseSource.Site)
        val hubs =
            listOf(
                HubSummary("1b00aa11bb22cc33", online = false, lastSeenAtEpochMs = seenAt, pausedBy = bySite),
                onlineHub.copy(pausedBy = listOf(LotPauseSource.Device)),
            )
        val nodes =
            listOf(
                NodeSummary(
                    "7c19a1b2c3d4e5f6",
                    "lot-t",
                    "Tomatoes",
                    62,
                    false,
                    ChargeState.Charging,
                    seenAt,
                    pausedBy = bySite,
                    pausedUntilEpochMs = LotFixtures.sitePausedUntil.toEpochMilli(),
                ),
                NodeSummary(
                    "7c19000000000003",
                    null,
                    null,
                    null,
                    false,
                    null,
                    null,
                    pausedBy = listOf(LotPauseSource.Device, LotPauseSource.Site),
                ),
                NodeSummary("9c0d1e4b5a673f2a", "lot-b", "Beans", 62, false, ChargeState.NotCharging, seenAt),
            )
        snapshot(name, readySites(site), theme, devices = DevicesState.Ready(site, hubs, nodes))
    }

    @Test
    @Config(qualifiers = TALLER)
    fun `UX-DR70 UX-DR30 Devices with paused rows for an Administrator, light, font scale 2`() =
        pausedDevices("devices-paused-light", ThemePreference.Light, SiteRole.Administrator)

    @Test
    @Config(qualifiers = TALLER)
    fun `UX-DR70 UX-DR30 Devices with paused rows for an Administrator, dark, font scale 2`() =
        pausedDevices("devices-paused-dark", ThemePreference.Dark, SiteRole.Administrator)

    @Test
    @Config(qualifiers = TALLER)
    fun `UX-DR70 UX-DR84 Devices with paused rows for a Member, light, font scale 2`() =
        pausedDevices("devices-paused-member-light", ThemePreference.Light, SiteRole.Member)

    @Test
    @Config(qualifiers = TALLER)
    fun `UX-DR70 UX-DR84 Devices with paused rows for a Member, dark, font scale 2`() =
        pausedDevices("devices-paused-member-dark", ThemePreference.Dark, SiteRole.Member)

    private val pausedDetail = LotDetailFixtures.ready(LotDetailFixtures.paused)
    private val pausedDetailMember = LotDetailFixtures.ready(LotDetailFixtures.paused, role = SiteRole.Member)

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR70 UX-DR27 Lot detail of a paused Node with Resume, light`() =
        detail("lot-detail-resume-light", pausedDetail, ThemePreference.Light, fontScale = 1f)

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR70 UX-DR27 Lot detail of a paused Node with Resume, dark`() =
        detail("lot-detail-resume-dark", pausedDetail, ThemePreference.Dark, fontScale = 1f)

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR84 UX-DR27 Lot detail of a paused Node for a Member, light`() =
        detail("lot-detail-paused-member-light", pausedDetailMember, ThemePreference.Light, fontScale = 1f)

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR84 UX-DR27 Lot detail of a paused Node for a Member, dark`() =
        detail("lot-detail-paused-member-dark", pausedDetailMember, ThemePreference.Dark, fontScale = 1f)

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR70 UX-DR27 Lot detail paused by the Site only, with Pause and no Resume, light`() =
        detail(
            "lot-detail-paused-site-only-light",
            LotDetailFixtures.ready(LotDetailFixtures.pausedBySiteOnly),
            ThemePreference.Light,
            fontScale = 1f,
        )

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR70 UX-DR27 Lot detail paused by the Site only, with Pause and no Resume, dark`() =
        detail(
            "lot-detail-paused-site-only-dark",
            LotDetailFixtures.ready(LotDetailFixtures.pausedBySiteOnly),
            ThemePreference.Dark,
            fontScale = 1f,
        )

    // Thresholds (Story 5.4): the editor, a Member's read-only view and the failures, light and dark, font scale 2.

    @Test
    fun `UX-DR45 UX-DR69 Thresholds editor, light, font scale 2`() =
        snapshot("thresholds-editor-light", readySites(), ThemePreference.Light, thresholds = ThresholdsStates.changed)

    @Test
    fun `UX-DR45 UX-DR69 Thresholds editor, dark, font scale 2`() =
        snapshot("thresholds-editor-dark", readySites(), ThemePreference.Dark, thresholds = ThresholdsStates.changed)

    @Test
    fun `UX-DR45 UX-DR91 Thresholds with Low above high, light, font scale 2`() =
        snapshot(
            "thresholds-low-above-high-light",
            readySites(),
            ThemePreference.Light,
            thresholds = ThresholdsStates.lowAboveHigh,
        )

    @Test
    fun `UX-DR45 UX-DR91 Thresholds with Low above high, dark, font scale 2`() =
        snapshot(
            "thresholds-low-above-high-dark",
            readySites(),
            ThemePreference.Dark,
            thresholds = ThresholdsStates.lowAboveHigh,
        )

    @Test
    fun `UX-DR84 Thresholds read-only for a Member, light, font scale 2`() =
        snapshot(
            "thresholds-member-light",
            readySites(homeSite(SiteRole.Member)),
            ThemePreference.Light,
            thresholds = ThresholdsStates.member,
        )

    @Test
    fun `UX-DR84 Thresholds read-only for a Member, dark, font scale 2`() =
        snapshot(
            "thresholds-member-dark",
            readySites(homeSite(SiteRole.Member)),
            ThemePreference.Dark,
            thresholds = ThresholdsStates.member,
        )

    @Test
    fun `UX-DR91 Thresholds not saved, light, font scale 2`() =
        snapshot(
            "thresholds-not-saved-light",
            readySites(),
            ThemePreference.Light,
            thresholds = ThresholdsStates.notSaved,
        )

    @Test
    fun `UX-DR91 Thresholds not saved, dark, font scale 2`() =
        snapshot(
            "thresholds-not-saved-dark",
            readySites(),
            ThemePreference.Dark,
            thresholds = ThresholdsStates.notSaved,
        )

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR5 UX-DR32 UX-DR33 Lot detail chart with the Threshold band and a below-low bar, light`() =
        detail("lot-detail-band-light", ThresholdsStates.detail(), ThemePreference.Light, fontScale = 1f)

    @Test
    @Config(qualifiers = TALL)
    fun `UX-DR5 UX-DR32 UX-DR33 Lot detail chart with the Threshold band and a below-low bar, dark`() =
        detail("lot-detail-band-dark", ThresholdsStates.detail(), ThemePreference.Dark, fontScale = 1f)

    @Test
    @Config(qualifiers = HUGE)
    fun `UX-DR5 UX-DR32 UX-DR33 Lot detail chart with the Threshold band, light, font scale 2`() =
        detail("lot-detail-band-large-light", ThresholdsStates.detail(), ThemePreference.Light, fontScale = 2f)

    @Test
    @Config(qualifiers = HUGE)
    fun `UX-DR5 UX-DR32 UX-DR33 Lot detail chart with the Threshold band, dark, font scale 2`() =
        detail("lot-detail-band-large-dark", ThresholdsStates.detail(), ThemePreference.Dark, fontScale = 2f)

    // Calibrate (Story 5.2): every step, the resume path and the paused explanation, light and dark, font scale 2.

    @Test
    fun `UX-DR66 Calibrate dry waiting, light, font scale 2`() =
        snapshot(
            "calibrate-dry-waiting-light",
            readySites(),
            ThemePreference.Light,
            calibrate = CalibrateStates.dry,
        )

    @Test
    fun `UX-DR66 Calibrate dry waiting, dark, font scale 2`() =
        snapshot(
            "calibrate-dry-waiting-dark",
            readySites(),
            ThemePreference.Dark,
            calibrate = CalibrateStates.dry,
        )

    @Test
    fun `UX-DR66 Calibrate dry fresh, light, font scale 2`() =
        snapshot(
            "calibrate-dry-fresh-light",
            readySites(),
            ThemePreference.Light,
            calibrate = CalibrateStates.dryFresh,
        )

    @Test
    fun `UX-DR66 Calibrate dry fresh, dark, font scale 2`() =
        snapshot(
            "calibrate-dry-fresh-dark",
            readySites(),
            ThemePreference.Dark,
            calibrate = CalibrateStates.dryFresh,
        )

    @Test
    fun `UX-DR66 Calibrate wet resumed, light, font scale 2`() =
        snapshot(
            "calibrate-wet-resumed-light",
            readySites(),
            ThemePreference.Light,
            calibrate = CalibrateStates.wetResumed,
        )

    @Test
    fun `UX-DR66 Calibrate wet resumed, dark, font scale 2`() =
        snapshot(
            "calibrate-wet-resumed-dark",
            readySites(),
            ThemePreference.Dark,
            calibrate = CalibrateStates.wetResumed,
        )

    @Test
    fun `UX-DR66 Calibrate wet fresh, light, font scale 2`() =
        snapshot(
            "calibrate-wet-fresh-light",
            readySites(),
            ThemePreference.Light,
            calibrate = CalibrateStates.wetFresh,
        )

    @Test
    fun `UX-DR66 Calibrate wet fresh, dark, font scale 2`() =
        snapshot(
            "calibrate-wet-fresh-dark",
            readySites(),
            ThemePreference.Dark,
            calibrate = CalibrateStates.wetFresh,
        )

    @Test
    fun `UX-DR66 Calibrate confirm pending, light, font scale 2`() =
        snapshot(
            "calibrate-confirm-pending-light",
            readySites(),
            ThemePreference.Light,
            calibrate = CalibrateStates.confirmPending,
        )

    @Test
    fun `UX-DR66 Calibrate confirm pending, dark, font scale 2`() =
        snapshot(
            "calibrate-confirm-pending-dark",
            readySites(),
            ThemePreference.Dark,
            calibrate = CalibrateStates.confirmPending,
        )

    @Test
    fun `UX-DR66 Calibrate confirm percent, light, font scale 2`() =
        snapshot(
            "calibrate-confirm-percent-light",
            readySites(),
            ThemePreference.Light,
            calibrate = CalibrateStates.confirmPercent,
        )

    @Test
    fun `UX-DR66 Calibrate confirm percent, dark, font scale 2`() =
        snapshot(
            "calibrate-confirm-percent-dark",
            readySites(),
            ThemePreference.Dark,
            calibrate = CalibrateStates.confirmPercent,
        )

    @Test
    fun `UX-DR66 Calibrate paused, light, font scale 2`() =
        snapshot(
            "calibrate-paused-light",
            readySites(),
            ThemePreference.Light,
            calibrate = CalibrateStates.paused,
        )

    @Test
    fun `UX-DR66 Calibrate paused, dark, font scale 2`() =
        snapshot(
            "calibrate-paused-dark",
            readySites(),
            ThemePreference.Dark,
            calibrate = CalibrateStates.paused,
        )

    @Test
    fun `UX-DR66 Calibrate indistinct, light, font scale 2`() =
        snapshot(
            "calibrate-indistinct-light",
            readySites(),
            ThemePreference.Light,
            calibrate = CalibrateStates.indistinct,
        )

    @Test
    fun `UX-DR66 Calibrate indistinct, dark, font scale 2`() =
        snapshot(
            "calibrate-indistinct-dark",
            readySites(),
            ThemePreference.Dark,
            calibrate = CalibrateStates.indistinct,
        )
}
