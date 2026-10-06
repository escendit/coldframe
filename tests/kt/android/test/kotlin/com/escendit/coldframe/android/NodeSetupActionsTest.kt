package com.escendit.coldframe.android

import com.escendit.coldframe.android.ui.setup.NodeSetupActions
import com.escendit.coldframe.core.api.ApiFailure
import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.DeviceDto
import com.escendit.coldframe.core.api.EnrolDeviceRequestDto
import com.escendit.coldframe.core.api.EnrolmentKeyDto
import com.escendit.coldframe.core.api.LotDto
import com.escendit.coldframe.core.api.LotListDto
import com.escendit.coldframe.core.lots.LotsApi
import com.escendit.coldframe.core.setup.EnrolmentApi
import com.escendit.coldframe.core.setup.NodeSetupEngine
import com.escendit.coldframe.core.setup.NodeSetupStep
import com.escendit.coldframe.core.setup.RadioState
import com.escendit.coldframe.core.setup.SetupAdvert
import com.escendit.coldframe.core.setup.SetupLink
import com.escendit.coldframe.core.setup.SetupLinkException
import com.escendit.coldframe.core.setup.SetupRadio
import com.escendit.coldframe.core.sites.SitesState
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.emptyFlow
import org.junit.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

/** [NodeSetupActions.of] over a real engine: the wiring a *no Node* tile and Devices depend on. */
class NodeSetupActionsTest {
    private val failed = ApiResult.Failed(ApiFailure.Unreachable)

    private val radio =
        object : SetupRadio {
            override val state: StateFlow<RadioState> = MutableStateFlow(RadioState.Ready)

            override fun recheck() = Unit

            override fun scan(): Flow<SetupAdvert> = emptyFlow()

            override suspend fun connect(id: String): SetupLink = throw SetupLinkException("no radio")
        }

    private val enrolment =
        object : EnrolmentApi {
            override suspend fun enrolmentKey(): ApiResult<EnrolmentKeyDto> = failed

            override suspend fun enrolDevice(
                siteId: String,
                request: EnrolDeviceRequestDto,
                idempotencyKey: String,
            ): ApiResult<DeviceDto> = failed
        }

    private val lots =
        object : LotsApi {
            override suspend fun listLots(siteId: String): ApiResult<LotListDto> = failed

            override suspend fun createLot(
                siteId: String,
                name: String,
                idempotencyKey: String,
            ): ApiResult<LotDto> = failed

            override suspend fun renameLot(
                siteId: String,
                lotId: String,
                name: String,
            ): ApiResult<LotDto> = failed

            override suspend fun removeLot(
                siteId: String,
                lotId: String,
            ): ApiResult<Unit> = failed
        }

    @Test
    fun `UX-DR67 the actions open the core's flow with the tile's Lot on the current Site and drive it`() {
        val scope = CoroutineScope(Dispatchers.Unconfined)
        try {
            val engine =
                NodeSetupEngine(
                    radio = radio,
                    api = enrolment,
                    lots = lots,
                    sites = MutableStateFlow<SitesState>(readySites()),
                    scope = scope,
                )
            val actions = NodeSetupActions.of(engine)

            // The Lot is a Lot, never the Site: a tile's id must not be read as a Site id.
            actions.open("lot-t")
            assertTrue(engine.state.value.open)
            assertEquals("site-home", engine.state.value.siteId)
            assertEquals(NodeSetupStep.Press, engine.state.value.step)

            actions.continueFromPress()
            assertEquals(NodeSetupStep.Scan, engine.state.value.step)
            actions.back()
            assertEquals(NodeSetupStep.Press, engine.state.value.step)
            actions.close()
            assertFalse(engine.state.value.open)

            // From Devices there is no Lot.
            actions.open(null)
            assertTrue(engine.state.value.open)
        } finally {
            scope.cancel()
        }
    }
}
