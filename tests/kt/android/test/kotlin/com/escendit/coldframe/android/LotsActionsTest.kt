package com.escendit.coldframe.android

import com.escendit.coldframe.android.ui.sites.LotsActions
import com.escendit.coldframe.core.api.ApiFailure
import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.LotDto
import com.escendit.coldframe.core.api.LotListDto
import com.escendit.coldframe.core.api.SiteDto
import com.escendit.coldframe.core.api.SiteListDto
import com.escendit.coldframe.core.api.SiteReminderCadenceDto
import com.escendit.coldframe.core.lots.LotsApi
import com.escendit.coldframe.core.lots.LotsEngine
import com.escendit.coldframe.core.lots.LotsState
import com.escendit.coldframe.core.notifications.ReminderCadence
import com.escendit.coldframe.core.notifications.SiteReminderCadenceApi
import com.escendit.coldframe.core.signin.SignInState
import com.escendit.coldframe.core.sites.DeviceChoices
import com.escendit.coldframe.core.sites.SitesApi
import com.escendit.coldframe.core.sites.SitesEngine
import com.russhwolf.settings.MapSettings
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/** The shell's Lot actions reach the core's engine: a refresh reads the Lots again (UX-DR112). */
@OptIn(ExperimentalCoroutinesApi::class)
class LotsActionsTest {
    private val listed = mutableListOf<String>()

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

    private val lotsApi =
        object : LotsApi {
            override suspend fun listLots(siteId: String): ApiResult<LotListDto> {
                listed += siteId
                return ApiResult.Ok(LotListDto(emptyList()))
            }

            override suspend fun createLot(
                siteId: String,
                name: String,
                idempotencyKey: String,
            ): ApiResult<LotDto> = ApiResult.Failed(ApiFailure.Unexpected)

            override suspend fun renameLot(
                siteId: String,
                lotId: String,
                name: String,
            ): ApiResult<LotDto> = ApiResult.Failed(ApiFailure.Unexpected)

            override suspend fun removeLot(
                siteId: String,
                lotId: String,
            ): ApiResult<Unit> = ApiResult.Failed(ApiFailure.Unexpected)
        }

    @Test
    fun `UX-DR112 the refresh action reads the Lots of the current Site again`() =
        runTest {
            val settings = MapSettings()
            val signIn = MutableStateFlow<SignInState>(SignInState.SignedIn("Simon"))
            val sites = SitesEngine(sitesApi, DeviceChoices(settings), backgroundScope, signIn)
            val engine = LotsEngine(lotsApi, sites, settings, backgroundScope)
            runCurrent()
            assertTrue(engine.state.value is LotsState.Ready)
            assertEquals(listOf("a"), listed)

            LotsActions.of(engine).refresh()
            runCurrent()

            assertEquals(listOf("a", "a"), listed)
        }

    @Test
    fun `UX-DR50 the Reminder cadence actions set the Site cadence and send a pick that was not saved again`() =
        runTest {
            val puts = mutableListOf<Pair<String, String>>()
            var failNext = true
            val cadenceApi =
                object : SiteReminderCadenceApi {
                    override suspend fun getSiteReminderCadence(siteId: String): ApiResult<SiteReminderCadenceDto> =
                        ApiResult.Ok(SiteReminderCadenceDto("daily"))

                    override suspend fun setSiteReminderCadence(
                        siteId: String,
                        cadence: String,
                    ): ApiResult<SiteReminderCadenceDto> {
                        puts += siteId to cadence
                        if (failNext) {
                            failNext = false
                            return ApiResult.Failed(ApiFailure.Unreachable)
                        }
                        return ApiResult.Ok(SiteReminderCadenceDto(cadence))
                    }
                }
            val settings = MapSettings()
            val signIn = MutableStateFlow<SignInState>(SignInState.SignedIn("Simon"))
            val sites = SitesEngine(sitesApi, DeviceChoices(settings), backgroundScope, signIn)
            val engine = LotsEngine(lotsApi, sites, settings, backgroundScope, cadenceApi = cadenceApi)
            runCurrent()
            val actions = LotsActions.of(engine)

            actions.setReminderCadence(ReminderCadence.Every2Days)
            runCurrent()

            assertEquals(listOf("a" to "every2Days"), puts)
            assertEquals(ReminderCadence.Daily, (engine.state.value as LotsState.Ready).reminderCadence.shown)

            actions.retryReminderCadence()
            runCurrent()

            assertEquals(listOf("a" to "every2Days", "a" to "every2Days"), puts)
            assertEquals(ReminderCadence.Every2Days, (engine.state.value as LotsState.Ready).reminderCadence.value)
        }
}
