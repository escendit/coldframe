package com.escendit.coldframe.core.notifications

import com.escendit.coldframe.core.api.ApiFailure
import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.NotificationSettingsDto
import com.escendit.coldframe.core.api.NotificationWindowDto
import com.escendit.coldframe.core.api.NotificationWindowRequestDto
import com.escendit.coldframe.core.api.SetSiteNotificationSettingsRequestDto
import com.escendit.coldframe.core.api.SiteDto
import com.escendit.coldframe.core.api.SiteNotificationSettingsDto
import com.escendit.coldframe.core.api.UpdateNotificationSettingsRequestDto
import com.escendit.coldframe.core.signin.SignInState
import com.escendit.coldframe.core.sites.DeviceChoices
import com.escendit.coldframe.core.sites.FakeSitesApi
import com.escendit.coldframe.core.sites.SitesEngine
import com.escendit.coldframe.core.sites.SitesState
import com.russhwolf.settings.MapSettings
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertIs
import kotlin.test.assertNull
import kotlin.test.assertTrue

/** An in-memory Server for the notification settings, with the Server's rules for the time zone. */
class FakeNotificationSettingsApi : NotificationSettingsApi {
    var me = NotificationSettingsDto(NotificationWindowDto("07:00", "22:00"), timeZoneConfirmed = false)
    val sites = mutableMapOf<String, SiteNotificationSettingsDto>()
    val knownZones = setOf("Europe/Zurich", "Europe/Vienna", "America/New_York", "Pacific/Auckland")
    val calls = mutableListOf<String>()
    val patches = mutableListOf<UpdateNotificationSettingsRequestDto>()
    val puts = mutableListOf<Pair<String, SetSiteNotificationSettingsRequestDto>>()

    /** Fail the next reads of my settings, one each. */
    val readFailures = ArrayDeque<ApiFailure>()

    /** Fail the next writes (PATCH or PUT), one each. */
    val writeFailures = ArrayDeque<ApiFailure>()
    var siteReadFailure: ApiFailure? = null
    var writeGate: CompletableDeferred<Unit>? = null
    var siteReadGate: CompletableDeferred<Unit>? = null

    override suspend fun getMyNotificationSettings(): ApiResult<NotificationSettingsDto> {
        calls += "get me"
        readFailures.removeFirstOrNull()?.let { return ApiResult.Failed(it) }
        return ApiResult.Ok(me)
    }

    override suspend fun updateMyNotificationSettings(
        request: UpdateNotificationSettingsRequestDto,
    ): ApiResult<NotificationSettingsDto> {
        calls += "patch me"
        patches += request
        writeGate?.await()
        writeFailures.removeFirstOrNull()?.let { return ApiResult.Failed(it) }
        val zones = listOfNotNull(request.timeZone, request.detectedTimeZone)
        if (zones.any { it !in knownZones }) return ApiResult.Failed(ApiFailure.Validation)
        request.window?.let { me = me.copy(window = NotificationWindowDto(it.from, it.to ?: "22:00")) }
        request.timeZone?.let { me = me.copy(timeZone = it, timeZoneConfirmed = true) }
        request.detectedTimeZone?.let { if (!me.timeZoneConfirmed) me = me.copy(timeZone = it) }
        return ApiResult.Ok(me)
    }

    override suspend fun getSiteNotificationSettings(siteId: String): ApiResult<SiteNotificationSettingsDto> {
        calls += "get site $siteId"
        siteReadGate?.await()
        siteReadFailure?.let { return ApiResult.Failed(it) }
        return ApiResult.Ok(sites[siteId] ?: SiteNotificationSettingsDto(muted = false, siteReminderCadence = "daily"))
    }

    override suspend fun setSiteNotificationSettings(
        siteId: String,
        request: SetSiteNotificationSettingsRequestDto,
    ): ApiResult<SiteNotificationSettingsDto> {
        calls += "put site $siteId"
        puts += siteId to request
        writeGate?.await()
        writeFailures.removeFirstOrNull()?.let { return ApiResult.Failed(it) }
        val before = sites[siteId] ?: SiteNotificationSettingsDto(muted = false, siteReminderCadence = "daily")
        val after = before.copy(muted = request.muted, reminderCadence = request.reminderCadence)
        sites[siteId] = after
        return ApiResult.Ok(after)
    }
}

/** Story 6.3: My notifications in the core: window, time zone and its hand-over (DW-23), mute and my Reminder cadence. */
class NotificationSettingsEngineTest {
    private val sitesApi = FakeSitesApi()
    private val api = FakeNotificationSettingsApi()
    private val choices = DeviceChoices(MapSettings())
    private val signIn = MutableStateFlow<SignInState>(SignInState.Restoring)
    private var detected = "Europe/Zurich"
    private val zoneList = listOf("America/New_York", "Europe/Vienna", "Europe/Zurich", "Pacific/Auckland")
    private lateinit var sites: SitesEngine

    private val home = SiteDto("a", "Home garden", "Owner")
    private val allotment = SiteDto("b", "Allotment", "Member")

    private fun TestScope.engine(vararg held: SiteDto = arrayOf(home)): NotificationSettingsEngine {
        sitesApi.sites += held
        sites =
            SitesEngine(
                api = sitesApi,
                choices = choices,
                scope = backgroundScope,
                signIn = signIn,
                detectTimeZone = { detected },
                newKey = { "k" },
                zones = { zoneList },
            )
        return NotificationSettingsEngine(
            api = api,
            sites = sites,
            choices = choices,
            scope = backgroundScope,
            signIn = signIn,
            detectTimeZone = { detected },
            zones = { zoneList },
        ).also { runCurrent() }
    }

    private fun TestScope.signedIn(vararg held: SiteDto = arrayOf(home)): NotificationSettingsEngine =
        engine(*held).also {
            signIn.value = SignInState.SignedIn("Simon")
            runCurrent()
        }

    private fun NotificationSettingsEngine.ready() = assertIs<NotificationSettingsState.Ready>(state.value)

    private fun window(
        from: String,
        to: String? = null,
    ) = UpdateNotificationSettingsRequestDto(window = NotificationWindowRequestDto(from, to))

    // Session and load

    @Test
    fun staysIdleWhileSignedOutAndReadsNothing() =
        runTest {
            val engine = engine()

            assertEquals(NotificationSettingsState.Idle, engine.state.value)
            assertTrue(api.calls.isEmpty())
        }

    @Test
    fun uxDr72ANewUserReadsTheDefaultWindowTheProposedZoneAndTheSitesSettings() =
        runTest {
            val engine = signedIn()

            val ready = engine.ready()
            assertEquals(NotificationWindow("07:00", "22:00"), ready.window)
            assertEquals(ready.window, ready.draft)
            assertEquals(
                NotificationTimeZone(detected = "Europe/Zurich", chosen = null, changing = false),
                ready.timeZone,
            )
            assertFalse(ready.timeZone.confirmed)
            val site = ready.site!!
            assertEquals("Home garden", site.site.name)
            assertFalse(site.muted)
            assertNull(site.reminderCadence)
            assertEquals(ReminderCadence.Daily, site.siteReminderCadence)
            assertNull(ready.notice)
        }

    @Test
    fun uxDr72WithoutACurrentSiteOnlyTheWindowAndTheZoneAreRead() =
        runTest {
            val engine = signedIn(*emptyArray())

            assertIs<SitesState.NeedsSite>(sites.state.value)
            assertNull(engine.ready().site)
            assertFalse(api.calls.any { it.startsWith("get site") })
        }

    @Test
    fun uxDr72SwitchingSiteReadsThatSitesMuteAndCadence() =
        runTest {
            api.sites["b"] = SiteNotificationSettingsDto(true, "every2Days", "daily")
            val engine = signedIn(home, allotment)

            sites.select("b")
            runCurrent()

            val site = engine.ready().site!!
            assertEquals("Allotment", site.site.name)
            assertTrue(site.muted)
            assertEquals(ReminderCadence.Daily, site.reminderCadence)
            assertEquals(ReminderCadence.Every2Days, site.siteReminderCadence)
        }

    @Test
    fun uxDr72EveryEntryOfTheSurfaceReadsTheSettingsAgainFromTheServer() =
        runTest {
            val engine = signedIn()
            api.me = api.me.copy(window = NotificationWindowDto("06:30", "21:00"))
            api.sites["a"] = SiteNotificationSettingsDto(true, "daily")

            engine.load()
            runCurrent()

            assertEquals(NotificationWindow("06:30", "21:00"), engine.ready().window)
            assertEquals(NotificationWindow("06:30", "21:00"), engine.ready().draft)
            assertTrue(engine.ready().site!!.muted)
        }

    @Test
    fun aFailedLoadShowsItsNoticeAndTryAgainLoads() =
        runTest {
            api.readFailures += ApiFailure.Unreachable
            val engine = signedIn()

            val failed = assertIs<NotificationSettingsState.Failed>(engine.state.value)
            assertEquals(NotificationSettingsNotice.Unreachable, failed.notice)
            assertTrue(failed.notice.tryAgain)

            engine.retry()
            runCurrent()

            engine.ready()
        }

    @Test
    fun aCertificateFailureHasNoTryAgainAndAnotherAnswerIsUnexpected() =
        runTest {
            api.readFailures += ApiFailure.Certificate
            val engine = signedIn()
            assertEquals(
                NotificationSettingsState.Failed(NotificationSettingsNotice.Certificate),
                engine.state.value,
            )
            assertFalse(NotificationSettingsNotice.Certificate.tryAgain)

            api.readFailures += ApiFailure.Unexpected
            engine.load()
            runCurrent()

            assertEquals(NotificationSettingsState.Failed(NotificationSettingsNotice.Unexpected), engine.state.value)
        }

    @Test
    fun aSiteWhoseSettingsCannotBeReadFailsTheLoad() =
        runTest {
            api.siteReadFailure = ApiFailure.Unreachable
            val engine = signedIn()

            assertEquals(NotificationSettingsState.Failed(NotificationSettingsNotice.Unreachable), engine.state.value)
        }

    @Test
    fun uxDr93A401OnTheReadEndsTheSession() =
        runTest {
            choices.timeZone = "Pacific/Auckland"
            api.readFailures += ApiFailure.Unauthorized
            val engine = signedIn()

            assertEquals(NotificationSettingsState.Idle, engine.state.value)
            assertNull(choices.timeZone)
        }

    @Test
    fun signingOutGoesIdleAndDropsALateAnswer() =
        runTest {
            val engine = signedIn()
            api.writeGate = CompletableDeferred()
            engine.setMuted(true)
            runCurrent()

            signIn.value = SignInState.SignedOut(null)
            runCurrent()
            api.writeGate?.complete(Unit)
            runCurrent()

            assertEquals(NotificationSettingsState.Idle, engine.state.value)
        }

    // Time zone: detection and hand-over (DW-23)

    @Test
    fun uxDr48TheDeviceZoneIsSentAsDetectedWhileTheUserHasChosenNone() =
        runTest {
            val engine = signedIn()

            assertEquals(listOf(UpdateNotificationSettingsRequestDto(detectedTimeZone = "Europe/Zurich")), api.patches)
            assertEquals("Europe/Zurich", api.me.timeZone)
            assertFalse(api.me.timeZoneConfirmed)
            assertFalse(engine.ready().timeZone.confirmed)
        }

    @Test
    fun uxDr48TheDeviceZoneIsProposedBeforeTheServersDetectedOne() =
        runTest {
            api.me = api.me.copy(timeZone = "America/New_York")
            detected = "Europe/Zurich"

            val engine = signedIn()

            assertEquals("Europe/Zurich", engine.ready().timeZone.detected)
            assertEquals("Europe/Zurich", engine.ready().timeZone.shown)
        }

    @Test
    fun uxDr48WithoutADeviceZoneTheServersDetectedOneIsProposedAndWithoutEitherNone() =
        runTest {
            detected = ""
            api.me = api.me.copy(timeZone = "America/New_York")
            val engine = signedIn()

            assertEquals("America/New_York", engine.ready().timeZone.detected)
            assertTrue(api.patches.isEmpty())

            api.me = api.me.copy(timeZone = null)
            engine.load()
            runCurrent()

            assertNull(engine.ready().timeZone.detected)
            assertNull(engine.ready().timeZone.shown)
            assertTrue(api.patches.isEmpty())
        }

    @Test
    fun uxDr48TheSameDetectedZoneIsNotSentAgain() =
        runTest {
            api.me = api.me.copy(timeZone = "Europe/Zurich")

            signedIn()

            assertTrue(api.patches.isEmpty())
        }

    @Test
    fun uxDr48AZoneTheUserChoseIsNeverReplacedByTheDeviceZone() =
        runTest {
            api.me = api.me.copy(timeZone = "Pacific/Auckland", timeZoneConfirmed = true)
            detected = "Europe/Zurich"

            val engine = signedIn()

            assertTrue(api.patches.isEmpty())
            assertEquals("Pacific/Auckland", engine.ready().timeZone.chosen)
            assertEquals("Pacific/Auckland", engine.ready().timeZone.shown)
            assertTrue(engine.ready().timeZone.confirmed)
        }

    @Test
    fun dw23AStoredDeviceZoneIsSentOnceAsTheUsersChoiceAndThenRemoved() =
        runTest {
            choices.timeZone = "Pacific/Auckland"

            val engine = signedIn()

            assertEquals(listOf(UpdateNotificationSettingsRequestDto(timeZone = "Pacific/Auckland")), api.patches)
            assertNull(choices.timeZone)
            assertEquals("Pacific/Auckland", engine.ready().timeZone.chosen)

            engine.load()
            runCurrent()
            assertEquals(1, api.patches.size)
        }

    @Test
    fun dw23AStoredZoneTheServerRefusesIsRemovedTooBecauseTheServerAnswered() =
        runTest {
            choices.timeZone = "Mars/Olympus"

            val engine = signedIn()

            assertEquals(listOf(UpdateNotificationSettingsRequestDto(timeZone = "Mars/Olympus")), api.patches)
            assertNull(choices.timeZone)
            assertFalse(engine.ready().timeZone.confirmed)
            assertEquals("Europe/Zurich", engine.ready().timeZone.shown)
        }

    @Test
    fun dw23AStoredZoneIsKeptWhenTheServerDoesNotAnswer() =
        runTest {
            val engine = engine()
            for (failure in listOf(ApiFailure.Unreachable, ApiFailure.Unexpected, ApiFailure.Certificate)) {
                choices.timeZone = "Pacific/Auckland"
                api.writeFailures += failure
                api.patches.clear()

                signIn.value = SignInState.SignedIn("Simon")
                runCurrent()

                assertEquals("Pacific/Auckland", choices.timeZone, failure.name)
                assertEquals(1, api.patches.size, failure.name)
                assertFalse(engine.ready().timeZone.confirmed, failure.name)
                // A start that is still restoring its session removes nothing.
                signIn.value = SignInState.Restoring
                runCurrent()
            }
        }

    @Test
    fun dw23AKeptZoneIsSentByTheNextLoadOfTheSameSession() =
        runTest {
            choices.timeZone = "Pacific/Auckland"
            api.writeFailures += ApiFailure.Unreachable
            val engine = signedIn()
            assertFalse(engine.ready().timeZone.confirmed)

            engine.load()
            runCurrent()

            assertEquals(2, api.patches.size)
            assertNull(choices.timeZone)
            assertEquals("Pacific/Auckland", engine.ready().timeZone.chosen)
        }

    @Test
    fun dw23AZoneChosenOnTheServerWinsOverTheStoredDeviceZone() =
        runTest {
            api.me = api.me.copy(timeZone = "Europe/Vienna", timeZoneConfirmed = true)
            choices.timeZone = "Pacific/Auckland"

            val engine = signedIn()

            assertTrue(api.patches.isEmpty())
            assertNull(choices.timeZone)
            assertEquals("Europe/Vienna", engine.ready().timeZone.chosen)
        }

    @Test
    fun dw23SigningOutRemovesTheStoredZone() =
        runTest {
            choices.timeZone = "Pacific/Auckland"
            api.writeFailures += ApiFailure.Unreachable
            signedIn()
            assertEquals("Pacific/Auckland", choices.timeZone)

            signIn.value = SignInState.SignedOut(null)
            runCurrent()

            assertNull(choices.timeZone)
        }

    // Time-zone confirm panel (UX-DR48)

    @Test
    fun uxDr48ConfirmSendsTheProposedZoneAsTheUsersChoice() =
        runTest {
            val engine = signedIn()
            api.patches.clear()

            engine.confirmTimeZone()
            runCurrent()

            assertEquals(listOf(UpdateNotificationSettingsRequestDto(timeZone = "Europe/Zurich")), api.patches)
            assertEquals(
                NotificationTimeZone("Europe/Zurich", "Europe/Zurich", changing = false),
                engine.ready().timeZone,
            )
            assertTrue(api.me.timeZoneConfirmed)
        }

    @Test
    fun uxDr48ChangeShowsTheListAndAPickIsSentAndShownAtOnce() =
        runTest {
            val engine = signedIn()
            api.patches.clear()
            api.writeGate = CompletableDeferred()

            engine.changeTimeZone()
            assertTrue(engine.ready().timeZone.changing)
            engine.pickTimeZone("Pacific/Auckland")
            runCurrent()

            assertEquals("Pacific/Auckland", engine.ready().timeZone.chosen)
            assertTrue(engine.ready().timeZone.working)
            api.writeGate?.complete(Unit)
            runCurrent()

            assertEquals(listOf(UpdateNotificationSettingsRequestDto(timeZone = "Pacific/Auckland")), api.patches)
            assertEquals(
                NotificationTimeZone("Europe/Zurich", "Pacific/Auckland", changing = false),
                engine.ready().timeZone,
            )
            assertEquals(zoneList, engine.timeZones())
        }

    @Test
    fun uxDr48AZoneThatIsNotInTheListIsIgnored() =
        runTest {
            val engine = signedIn()
            api.patches.clear()

            engine.pickTimeZone("Mars/Olympus")
            runCurrent()

            assertTrue(api.patches.isEmpty())
            assertFalse(engine.ready().timeZone.confirmed)
        }

    @Test
    fun uxDr48AZoneTheServerDoesNotKnowReturnsToTheServersValueWithANotice() =
        runTest {
            val engine = signedIn()
            api.writeFailures += ApiFailure.Validation

            engine.changeTimeZone()
            engine.pickTimeZone("Pacific/Auckland")
            runCurrent()

            val ready = engine.ready()
            assertNull(ready.timeZone.chosen)
            assertEquals(NotificationSettingsNotice.Invalid, ready.notice)
            assertEquals(NotificationControl.TimeZone, ready.noticeControl)
            assertFalse(ready.notice!!.tryAgain)
        }

    @Test
    fun uxDr48AZoneThatCouldNotBeSentReturnsAndTryAgainSendsItAgain() =
        runTest {
            val engine = signedIn()
            api.patches.clear()
            api.writeFailures += ApiFailure.Unreachable

            engine.confirmTimeZone()
            runCurrent()

            assertFalse(engine.ready().timeZone.confirmed)
            assertEquals(NotificationSettingsNotice.NotSaved, engine.ready().notice)
            assertEquals(NotificationControl.TimeZone, engine.ready().noticeControl)

            engine.retry()
            runCurrent()

            assertEquals(2, api.patches.size)
            assertEquals("Europe/Zurich", engine.ready().timeZone.chosen)
            assertNull(engine.ready().notice)
        }

    @Test
    fun uxDr61TheZoneConfirmedOnCreateSiteIsSentToTheServerAndThenReadFromIt() =
        runTest {
            val engine = signedIn(*emptyArray())
            api.patches.clear()

            sites.confirmTimeZone()
            runCurrent()

            assertEquals(listOf(UpdateNotificationSettingsRequestDto(timeZone = "Europe/Zurich")), api.patches)
            assertNull(choices.timeZone)
            assertEquals("Europe/Zurich", engine.ready().timeZone.chosen)
            val form = assertIs<SitesState.NeedsSite>(sites.state.value).form
            assertEquals("Europe/Zurich", form.timeZone.chosen)
            assertTrue(sitesApi.created.isEmpty())
        }

    @Test
    fun uxDr61AZonePickedOnCreateSiteThatCouldNotBeSentStaysOnTheDeviceForTheNextStart() =
        runTest {
            signedIn(*emptyArray())
            api.writeFailures += ApiFailure.Unreachable

            sites.pickTimeZone("Pacific/Auckland")
            runCurrent()

            assertEquals("Pacific/Auckland", choices.timeZone)
        }

    @Test
    fun uxDr61ALaterCreateSiteShowsTheZoneTheServerHolds() =
        runTest {
            api.me = api.me.copy(timeZone = "Europe/Vienna", timeZoneConfirmed = true)
            signedIn()

            sites.newSite()

            val form = assertIs<SitesState.Ready>(sites.state.value).creating!!
            assertEquals("Europe/Vienna", form.timeZone.chosen)
            assertEquals("Europe/Vienna", form.timeZone.shown)
        }

    // Notification Window (UX-DR47)

    @Test
    fun uxDr47TheWindowIsADraftUntilSaveAndSaveSendsFromAndTo() =
        runTest {
            val engine = signedIn()
            api.patches.clear()
            assertFalse(engine.ready().canSaveWindow)

            engine.setWindowFrom("06:30")
            engine.setWindowTo("21:00")

            assertEquals(NotificationWindow("06:30", "21:00"), engine.ready().draft)
            assertEquals(NotificationWindow("07:00", "22:00"), engine.ready().window)
            assertTrue(engine.ready().windowDirty)
            assertTrue(engine.ready().canSaveWindow)
            assertTrue(api.patches.isEmpty())

            engine.saveWindow()
            runCurrent()

            assertEquals(listOf(window("06:30", "21:00")), api.patches)
            val ready = engine.ready()
            assertEquals(NotificationWindow("06:30", "21:00"), ready.window)
            assertEquals(ready.window, ready.draft)
            assertTrue(ready.windowSaved)
            assertFalse(ready.windowDirty)
            assertFalse(ready.windowWorking)
        }

    @Test
    fun uxDr47WhileSavingTheWindowIsWorkingAndASecondSaveIsIgnored() =
        runTest {
            val engine = signedIn()
            api.patches.clear()
            api.writeGate = CompletableDeferred()
            engine.setWindowFrom("06:00")

            engine.saveWindow()
            runCurrent()
            engine.saveWindow()
            engine.setWindowFrom("05:00")
            runCurrent()

            assertTrue(engine.ready().windowWorking)
            assertEquals("06:00", engine.ready().draft.from)
            assertEquals(1, api.patches.size)
        }

    @Test
    fun uxDr47AWindowThatDoesNotStartBeforeItEndsCannotBeSaved() =
        runTest {
            val engine = signedIn()
            api.patches.clear()

            engine.setWindowFrom("22:00")

            assertTrue(engine.ready().windowOutOfOrder)
            assertFalse(engine.ready().canSaveWindow)
            engine.saveWindow()
            runCurrent()
            assertTrue(api.patches.isEmpty())

            engine.setWindowFrom("23:30")
            assertTrue(engine.ready().windowOutOfOrder)
        }

    @Test
    fun uxDr47TextThatIsNoTimeLeavesTheDraftAsItIs() =
        runTest {
            val engine = signedIn()

            for (text in listOf("7:00", "24:00", "07:60", "", "0700", "ab:cd")) engine.setWindowFrom(text)

            assertEquals("07:00", engine.ready().draft.from)
        }

    @Test
    fun uxDr47TheWindowKnowsItsMinutesForTheBar() {
        val window = NotificationWindow("07:00", "22:30")

        assertEquals(420, window.fromMinutes)
        assertEquals(1350, window.toMinutes)
        assertTrue(window.inOrder)
        assertFalse(NotificationWindow("22:00", "22:00").inOrder)
        assertNull(NotificationWindow.minutesOf("24:00"))
        assertEquals(1439, NotificationWindow.minutesOf("23:59"))
        assertEquals("06:05", NotificationWindow.timeOf(6, 5))
    }

    @Test
    fun uxDr47AFailedSaveReturnsTheWindowToTheServersValueAndTryAgainSendsItAgain() =
        runTest {
            val engine = signedIn()
            api.patches.clear()
            api.writeFailures += ApiFailure.Unreachable
            engine.setWindowFrom("06:30")

            engine.saveWindow()
            runCurrent()

            val failed = engine.ready()
            assertEquals(NotificationWindow("07:00", "22:00"), failed.draft)
            assertEquals(NotificationSettingsNotice.NotSaved, failed.notice)
            assertEquals(NotificationControl.Window, failed.noticeControl)
            assertTrue(failed.notice!!.tryAgain)
            assertFalse(failed.windowSaved)

            engine.retry()
            runCurrent()

            assertEquals(listOf(window("06:30", "22:00"), window("06:30", "22:00")), api.patches)
            assertEquals(NotificationWindow("06:30", "22:00"), engine.ready().window)
            assertNull(engine.ready().notice)
        }

    @Test
    fun uxDr47AWindowTheServerRefusesSaysSoWithoutTryAgain() =
        runTest {
            val engine = signedIn()
            api.writeFailures += ApiFailure.Validation
            engine.setWindowFrom("06:30")

            engine.saveWindow()
            runCurrent()

            assertEquals(NotificationSettingsNotice.Invalid, engine.ready().notice)
            assertEquals(NotificationControl.Window, engine.ready().noticeControl)
            assertEquals(NotificationWindow("07:00", "22:00"), engine.ready().draft)
        }

    @Test
    fun uxDr47AnEditClearsTheNoticeAndTheSavedMark() =
        runTest {
            val engine = signedIn()
            engine.setWindowFrom("06:30")
            engine.saveWindow()
            runCurrent()
            assertTrue(engine.ready().windowSaved)

            engine.setWindowTo("21:00")

            assertFalse(engine.ready().windowSaved)
        }

    // Mute (UX-DR49)

    @Test
    fun uxDr49MutingTheSiteAppliesAtOnceAndSendsTheCurrentCadenceWithIt() =
        runTest {
            api.sites["a"] = SiteNotificationSettingsDto(false, "daily", "every2Days")
            val engine = signedIn()
            api.writeGate = CompletableDeferred()

            engine.setMuted(true)
            runCurrent()

            assertTrue(engine.ready().site!!.muted)
            assertTrue(engine.ready().site!!.working)
            api.writeGate?.complete(Unit)
            runCurrent()

            assertEquals(listOf("a" to SetSiteNotificationSettingsRequestDto(true, "every2Days")), api.puts)
            assertTrue(engine.ready().site!!.muted)
            assertFalse(engine.ready().site!!.working)
            assertEquals(ReminderCadence.Every2Days, engine.ready().site!!.reminderCadence)
        }

    @Test
    fun uxDr49AMuteThatWasNotSavedReturnsToTheServersValueWithTryAgain() =
        runTest {
            val engine = signedIn()
            api.writeFailures += ApiFailure.Unreachable

            engine.setMuted(true)
            runCurrent()

            assertFalse(engine.ready().site!!.muted)
            assertEquals(NotificationSettingsNotice.NotSaved, engine.ready().notice)
            assertEquals(NotificationControl.Mute, engine.ready().noticeControl)

            engine.retry()
            runCurrent()

            assertTrue(engine.ready().site!!.muted)
            assertEquals(2, api.puts.size)
        }

    @Test
    fun uxDr49WithoutACurrentSiteNothingIsMuted() =
        runTest {
            val engine = signedIn(*emptyArray())

            engine.setMuted(true)
            engine.setReminderCadence(ReminderCadence.Daily)
            runCurrent()

            assertTrue(api.puts.isEmpty())
        }

    @Test
    fun uxDr49ASiteThatIsNoLongerMineSaysSoWithoutTryAgain() =
        runTest {
            val engine = signedIn()
            for (failure in listOf(ApiFailure.Forbidden, ApiFailure.NotFound)) {
                api.writeFailures += failure

                engine.setMuted(true)
                runCurrent()

                assertEquals(NotificationSettingsNotice.SiteRefused, engine.ready().notice, failure.name)
                assertFalse(engine.ready().notice!!.tryAgain)
                assertFalse(engine.ready().site!!.muted)
            }
        }

    // My Reminder cadence (UX-DR50)

    @Test
    fun uxDr50MyCadenceAppliesAtOnceSendsTheCurrentMuteAndUseSiteSettingSendsNone() =
        runTest {
            api.sites["a"] = SiteNotificationSettingsDto(true, "daily")
            val engine = signedIn()

            engine.setReminderCadence(ReminderCadence.Every2Days)
            runCurrent()

            assertEquals(ReminderCadence.Every2Days, engine.ready().site!!.reminderCadence)

            engine.setReminderCadence(null)
            runCurrent()

            assertEquals(
                listOf(
                    "a" to SetSiteNotificationSettingsRequestDto(true, "every2Days"),
                    "a" to SetSiteNotificationSettingsRequestDto(true, null),
                ),
                api.puts,
            )
            assertNull(engine.ready().site!!.reminderCadence)
            assertEquals(ReminderCadence.Daily, engine.ready().site!!.siteReminderCadence)
        }

    @Test
    fun uxDr50ACadenceThatWasNotSavedReturnsToTheServersValueWithTryAgain() =
        runTest {
            val engine = signedIn()
            api.writeFailures += ApiFailure.Unexpected

            engine.setReminderCadence(ReminderCadence.Every2Days)
            runCurrent()

            assertNull(engine.ready().site!!.reminderCadence)
            assertEquals(NotificationSettingsNotice.NotSaved, engine.ready().notice)
            assertEquals(NotificationControl.ReminderCadence, engine.ready().noticeControl)
        }

    @Test
    fun uxDr50TheCadencesAreTheContractValuesAndThereIsNoNever() {
        assertEquals(listOf("daily", "every2Days"), ReminderCadence.entries.map { it.key })
        assertEquals(ReminderCadence.Every2Days, ReminderCadence.fromServer("every2Days"))
        assertNull(ReminderCadence.fromServer("never"))
        assertNull(ReminderCadence.fromServer(null))
    }

    @Test
    fun uxDr93A401OnAWriteEndsTheSession() =
        runTest {
            val engine = signedIn()
            api.writeFailures += ApiFailure.Unauthorized

            engine.setMuted(true)
            runCurrent()

            assertEquals(NotificationSettingsState.Idle, engine.state.value)
        }

    @Test
    fun uxDr49AMuteNotSavedOnTheSiteLeftBehindShowsNoNoticeOnTheNewSiteAndTryAgainWritesNothing() =
        runTest {
            val engine = signedIn(home, allotment)
            api.writeFailures += ApiFailure.Unreachable
            engine.setMuted(true)
            runCurrent()
            assertEquals(NotificationSettingsNotice.NotSaved, engine.ready().notice)

            sites.select("b")
            runCurrent()

            assertEquals(
                "Allotment",
                engine
                    .ready()
                    .site!!
                    .site.name,
            )
            assertNull(engine.ready().notice)
            assertNull(engine.ready().noticeControl)

            engine.retry()
            runCurrent()

            assertEquals(listOf("a"), api.puts.map { it.first })
            assertFalse(engine.ready().site!!.muted)
            assertNull(api.sites["a"])
        }

    @Test
    fun anAnswerForTheSiteLeftBehindIsDropped() =
        runTest {
            val engine = signedIn(home, allotment)
            api.writeGate = CompletableDeferred()
            engine.setMuted(true)
            runCurrent()

            sites.select("b")
            runCurrent()
            api.writeGate?.complete(Unit)
            runCurrent()

            val site = engine.ready().site!!
            assertEquals("Allotment", site.site.name)
            assertFalse(site.muted)
            assertFalse(site.working)
        }
}
