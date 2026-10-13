package com.escendit.coldframe.android

import com.escendit.coldframe.android.ui.devices.DevicesActions
import com.escendit.coldframe.android.ui.sites.LotDetailActions
import com.escendit.coldframe.android.ui.sites.LotsActions
import com.escendit.coldframe.core.api.ApiFailure
import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.DeviceDto
import com.escendit.coldframe.core.api.DeviceListDto
import com.escendit.coldframe.core.api.DevicePauseDto
import com.escendit.coldframe.core.api.LotDto
import com.escendit.coldframe.core.api.LotHistoryDto
import com.escendit.coldframe.core.api.LotListDto
import com.escendit.coldframe.core.api.SilenceWindowDto
import com.escendit.coldframe.core.api.SiteDto
import com.escendit.coldframe.core.api.SiteListDto
import com.escendit.coldframe.core.api.SitePauseDto
import com.escendit.coldframe.core.devices.DevicesApi
import com.escendit.coldframe.core.devices.DevicesEngine
import com.escendit.coldframe.core.lots.LotDetailApi
import com.escendit.coldframe.core.lots.LotDetailEngine
import com.escendit.coldframe.core.lots.LotsApi
import com.escendit.coldframe.core.lots.LotsEngine
import com.escendit.coldframe.core.signin.SignInState
import com.escendit.coldframe.core.sites.DeviceChoices
import com.escendit.coldframe.core.sites.PauseApi
import com.escendit.coldframe.core.sites.PauseScope
import com.escendit.coldframe.core.sites.SitesApi
import com.escendit.coldframe.core.sites.SitesEngine
import com.russhwolf.settings.MapSettings
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

/**
 * The shells' Pause actions reach the Pause engine of their own surface (Story 8.2): what
 * `LotsActions.of`, `LotDetailActions.of` and `DevicesActions.of` hand to the screens opens that
 * engine's sheet and sends the Pause and the Resume to the Server.
 */
@OptIn(ExperimentalCoroutinesApi::class)
class PauseActionsTest {
    private val node = "7c19aaaaaaaaaaaa"
    private val calls = mutableListOf<String>()

    private val pauseApi =
        object : PauseApi {
            override suspend fun pauseSite(
                siteId: String,
                until: String?,
            ): ApiResult<SitePauseDto> {
                calls += "pauseSite $siteId ${until ?: "open"}"
                return ApiResult.Ok(SitePauseDto(paused = true))
            }

            override suspend fun resumeSite(siteId: String): ApiResult<SitePauseDto> {
                calls += "resumeSite $siteId"
                return ApiResult.Ok(SitePauseDto(paused = false))
            }

            override suspend fun pauseDevice(
                siteId: String,
                deviceId: String,
                until: String?,
            ): ApiResult<DevicePauseDto> {
                calls += "pauseDevice $siteId $deviceId ${until ?: "open"}"
                return ApiResult.Ok(DevicePauseDto(listOf("device")))
            }

            override suspend fun resumeDevice(
                siteId: String,
                deviceId: String,
            ): ApiResult<DevicePauseDto> {
                calls += "resumeDevice $siteId $deviceId"
                return ApiResult.Ok(DevicePauseDto(emptyList()))
            }
        }

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
            override suspend fun listLots(siteId: String): ApiResult<LotListDto> = ApiResult.Ok(LotListDto(emptyList()))

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

    private val lotDetailApi =
        object : LotDetailApi {
            override suspend fun getLot(
                siteId: String,
                lotId: String,
            ): ApiResult<LotDto> = ApiResult.Failed(ApiFailure.Unreachable)

            override suspend fun getLotHistory(
                siteId: String,
                lotId: String,
                quantity: String,
                cursor: String?,
            ): ApiResult<LotHistoryDto> = ApiResult.Failed(ApiFailure.Unreachable)
        }

    private val devicesApi =
        object : DevicesApi {
            override suspend fun listDevices(siteId: String): ApiResult<DeviceListDto> =
                ApiResult.Ok(DeviceListDto(emptyList()))

            override suspend fun listLots(siteId: String): ApiResult<LotListDto> = ApiResult.Ok(LotListDto(emptyList()))

            override suspend fun moveDevice(
                siteId: String,
                deviceId: String,
                lotId: String,
            ): ApiResult<DeviceDto> = ApiResult.Failed(ApiFailure.Unexpected)

            override suspend fun unassignDevice(
                siteId: String,
                deviceId: String,
            ): ApiResult<DeviceDto> = ApiResult.Failed(ApiFailure.Unexpected)

            override suspend fun setSilenceWindow(
                siteId: String,
                deviceId: String,
                windowSeconds: Int?,
            ): ApiResult<SilenceWindowDto> = ApiResult.Failed(ApiFailure.Unexpected)
        }

    private val settings = MapSettings()

    private fun TestScope.sites(): SitesEngine =
        SitesEngine(
            sitesApi,
            DeviceChoices(settings),
            backgroundScope,
            MutableStateFlow<SignInState>(SignInState.SignedIn("Simon")),
        )

    @Test
    fun `UX-DR22 the Site menu's Pause and Resume reach the overview's Pause engine and the Server`() =
        runTest {
            val engine = LotsEngine(lotsApi, sites(), settings, backgroundScope, pauseApi = pauseApi)
            runCurrent()
            val pause = LotsActions.of(engine).pause

            pause.openForSite()

            val sheet = engine.pause.state.value.sheet!!
            assertEquals(listOf(PauseScope.Site), sheet.scopes)
            assertEquals("Home garden", sheet.target)

            pause.setUntil("2027-03-01")
            pause.save()
            runCurrent()

            assertEquals(listOf("pauseSite a 2027-03-01"), calls)
            assertNull(engine.pause.state.value.sheet)

            pause.resumeSite()
            runCurrent()

            assertEquals(listOf("pauseSite a 2027-03-01", "resumeSite a"), calls)
        }

    @Test
    fun `UX-DR70 Pause and Resume on Lot detail reach its own Pause engine and the Server`() =
        runTest {
            val engine = LotDetailEngine(lotDetailApi, sites(), settings, backgroundScope, pauseApi = pauseApi)
            runCurrent()
            val pause = LotDetailActions.of(engine).pause

            pause.openForDevice(node)

            val sheet = engine.pause.state.value.sheet!!
            assertEquals(listOf(PauseScope.Device, PauseScope.Site), sheet.scopes)
            assertEquals(node, sheet.deviceId)

            pause.save()
            runCurrent()
            pause.resumeDevice(node)
            runCurrent()

            assertEquals(listOf("pauseDevice a $node open", "resumeDevice a $node"), calls)
        }

    @Test
    fun `UX-DR70 Pause and Resume on a Devices row reach the Devices' Pause engine and the Server`() =
        runTest {
            val engine = DevicesEngine(devicesApi, sites(), backgroundScope, pauseApi = pauseApi)
            runCurrent()
            val pause = DevicesActions.of(engine).pause

            pause.openForDevice(node)

            assertEquals(
                node,
                engine.pause.state.value.sheet!!
                    .deviceId,
            )

            pause.setScope(PauseScope.Site)
            pause.save()
            runCurrent()
            pause.resumeDevice(node)
            runCurrent()
            pause.resumeSite()
            runCurrent()

            assertEquals(listOf("pauseSite a open", "resumeDevice a $node", "resumeSite a"), calls)
        }
}
