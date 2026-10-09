package com.escendit.coldframe.android

import com.escendit.coldframe.android.ui.notifications.NotificationSettingsActions
import com.escendit.coldframe.core.api.ApiFailure
import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.NotificationSettingsDto
import com.escendit.coldframe.core.api.NotificationWindowDto
import com.escendit.coldframe.core.api.NotificationWindowRequestDto
import com.escendit.coldframe.core.api.SetSiteNotificationSettingsRequestDto
import com.escendit.coldframe.core.api.SiteDto
import com.escendit.coldframe.core.api.SiteListDto
import com.escendit.coldframe.core.api.SiteNotificationSettingsDto
import com.escendit.coldframe.core.api.UpdateNotificationSettingsRequestDto
import com.escendit.coldframe.core.notifications.NotificationSettingsApi
import com.escendit.coldframe.core.notifications.NotificationSettingsEngine
import com.escendit.coldframe.core.notifications.NotificationSettingsState
import com.escendit.coldframe.core.notifications.ReminderCadence
import com.escendit.coldframe.core.signin.SignInState
import com.escendit.coldframe.core.sites.DeviceChoices
import com.escendit.coldframe.core.sites.SitesApi
import com.escendit.coldframe.core.sites.SitesEngine
import com.russhwolf.settings.MapSettings
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** Every action of My notifications reaches its own method of the core's engine (Story 6.3, AD-14). */
@OptIn(ExperimentalCoroutinesApi::class)
class NotificationSettingsActionsTest {
    private val zones = listOf("Europe/Vienna", "Europe/Zurich")
    private var reads = 0
    private val patches = mutableListOf<UpdateNotificationSettingsRequestDto>()
    private val puts = mutableListOf<Pair<String, SetSiteNotificationSettingsRequestDto>>()
    private var failNextWrite = false
    private var me = NotificationSettingsDto(NotificationWindowDto("07:00", "22:00"), timeZoneConfirmed = false)
    private var site = SiteNotificationSettingsDto(muted = false, siteReminderCadence = "daily")

    private val sitesApi =
        object : SitesApi {
            override suspend fun listSites(): ApiResult<SiteListDto> =
                ApiResult.Ok(SiteListDto(listOf(SiteDto("a", "Home garden", "Owner"))))

            override suspend fun createSite(
                name: String,
                idempotencyKey: String,
            ): ApiResult<SiteDto> = ApiResult.Failed(ApiFailure.Unexpected)

            override suspend fun renameSite(
                siteId: String,
                name: String,
            ): ApiResult<SiteDto> = ApiResult.Failed(ApiFailure.Unexpected)
        }

    private val api =
        object : NotificationSettingsApi {
            override suspend fun getMyNotificationSettings(): ApiResult<NotificationSettingsDto> {
                reads++
                return ApiResult.Ok(me)
            }

            override suspend fun updateMyNotificationSettings(
                request: UpdateNotificationSettingsRequestDto,
            ): ApiResult<NotificationSettingsDto> {
                patches += request
                request.window?.let { me = me.copy(window = NotificationWindowDto(it.from, it.to ?: "22:00")) }
                request.timeZone?.let { me = me.copy(timeZone = it, timeZoneConfirmed = true) }
                request.detectedTimeZone?.let { me = me.copy(timeZone = it) }
                return ApiResult.Ok(me)
            }

            override suspend fun getSiteNotificationSettings(siteId: String): ApiResult<SiteNotificationSettingsDto> =
                ApiResult.Ok(site)

            override suspend fun setSiteNotificationSettings(
                siteId: String,
                request: SetSiteNotificationSettingsRequestDto,
            ): ApiResult<SiteNotificationSettingsDto> {
                puts += siteId to request
                if (failNextWrite) {
                    failNextWrite = false
                    return ApiResult.Failed(ApiFailure.Unreachable)
                }
                site = site.copy(muted = request.muted, reminderCadence = request.reminderCadence)
                return ApiResult.Ok(site)
            }
        }

    private fun TestScope.engine(): NotificationSettingsEngine {
        val choices = DeviceChoices(MapSettings())
        val signIn = MutableStateFlow<SignInState>(SignInState.SignedIn("Simon"))
        val sites = SitesEngine(sitesApi, choices, backgroundScope, signIn)
        return NotificationSettingsEngine(
            api = api,
            sites = sites,
            choices = choices,
            scope = backgroundScope,
            signIn = signIn,
            detectTimeZone = { "Europe/Zurich" },
            zones = { zones },
        ).also {
            runCurrent()
            assertTrue(it.state.value is NotificationSettingsState.Ready)
            patches.clear()
        }
    }

    private fun NotificationSettingsEngine.ready() = state.value as NotificationSettingsState.Ready

    @Test
    fun `UX-DR72 load reads the settings again and timeZones lists the core's zones`() =
        runTest {
            val actions = NotificationSettingsActions.of(engine())
            val before = reads

            actions.load()
            runCurrent()

            assertEquals(before + 1, reads)
            assertEquals(zones, actions.timeZones())
        }

    @Test
    fun `UX-DR47 from, to and Save reach the window, each its own side`() =
        runTest {
            val engine = engine()
            val actions = NotificationSettingsActions.of(engine)

            actions.setWindowFrom("06:30")
            actions.setWindowTo("21:00")
            assertEquals("06:30" to "21:00", engine.ready().draft.from to engine.ready().draft.to)
            assertTrue(patches.isEmpty())

            actions.saveWindow()
            runCurrent()

            assertEquals(
                listOf(UpdateNotificationSettingsRequestDto(window = NotificationWindowRequestDto("06:30", "21:00"))),
                patches,
            )
        }

    @Test
    fun `UX-DR48 Confirm sends the proposed zone, Change opens the list and a pick sends that zone`() =
        runTest {
            val engine = engine()
            val actions = NotificationSettingsActions.of(engine)

            actions.changeTimeZone()
            assertTrue(engine.ready().timeZone.changing)
            assertTrue(patches.isEmpty())

            actions.pickTimeZone("Europe/Vienna")
            runCurrent()
            assertEquals(listOf(UpdateNotificationSettingsRequestDto(timeZone = "Europe/Vienna")), patches)
        }

    @Test
    fun `UX-DR48 Confirm sends the proposed zone as the User's choice`() =
        runTest {
            val actions = NotificationSettingsActions.of(engine())

            actions.confirmTimeZone()
            runCurrent()

            assertEquals(listOf(UpdateNotificationSettingsRequestDto(timeZone = "Europe/Zurich")), patches)
        }

    @Test
    fun `UX-DR49 UX-DR50 the switch sets the mute, the choice my cadence, and Try again sends what was not saved`() =
        runTest {
            val engine = engine()
            val actions = NotificationSettingsActions.of(engine)

            actions.setMuted(true)
            runCurrent()
            assertEquals(listOf("a" to SetSiteNotificationSettingsRequestDto(muted = true)), puts)

            actions.setReminderCadence(ReminderCadence.Every2Days)
            runCurrent()
            assertEquals(
                "a" to SetSiteNotificationSettingsRequestDto(muted = true, reminderCadence = "every2Days"),
                puts.last(),
            )

            failNextWrite = true
            actions.setReminderCadence(null)
            runCurrent()
            assertEquals(ReminderCadence.Every2Days, engine.ready().site!!.reminderCadence)

            actions.retry()
            runCurrent()

            assertEquals(4, puts.size)
            assertEquals("a" to SetSiteNotificationSettingsRequestDto(muted = true), puts.last())
            assertFalse(engine.ready().site!!.reminderCadence != null)
        }
}
