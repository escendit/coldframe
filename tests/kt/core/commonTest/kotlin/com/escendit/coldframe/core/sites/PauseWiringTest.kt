package com.escendit.coldframe.core.sites

import com.escendit.coldframe.core.api.SiteDto
import com.escendit.coldframe.core.devices.FakeDevicesApi
import com.escendit.coldframe.core.lots.FakeLotDetailApi
import com.escendit.coldframe.core.lots.FakeLotsApi
import com.escendit.coldframe.core.signin.SignInState
import com.russhwolf.settings.MapSettings
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNotNull
import kotlin.test.assertNull

/**
 * The engines the apps build through [SitesWiring] can pause (Story 8.2): the `pause` argument of
 * `lots`, `lotDetail` and `devices` is the API their Pause engine calls. Without it nothing opens.
 */
class PauseWiringTest {
    private val sitesApi = FakeSitesApi()
    private val pauseApi = FakePauseApi(sitesApi)
    private val settings = MapSettings()
    private val node = "7c19aaaaaaaaaaaa"

    private fun TestScope.sites(): SitesEngine {
        sitesApi.sites += SiteDto("a", "Home garden", "Owner")
        return SitesEngine(
            api = sitesApi,
            choices = DeviceChoices(settings),
            scope = backgroundScope,
            signIn = MutableStateFlow<SignInState>(SignInState.SignedIn("Simon")),
            detectTimeZone = { "Europe/Zurich" },
            newKey = { "k" },
            zones = { emptyList() },
        )
    }

    @Test
    fun uxDr22TheWiredLotsEnginePausesAndResumesTheSiteFromItsMenu() =
        runTest {
            val engine =
                SitesWiring.lots(
                    FakeLotsApi(),
                    sites(),
                    settings,
                    backgroundScope,
                    cadence = null,
                    pause = pauseApi,
                )
            runCurrent()

            engine.pause.openForSite()

            assertEquals("Home garden", assertNotNull(engine.pause.state.value.sheet).target)
            engine.pause.save()
            runCurrent()
            engine.pause.resumeSite()
            runCurrent()
            assertEquals(listOf("pauseSite a open", "resumeSite a"), pauseApi.calls)
        }

    @Test
    fun uxDr70TheWiredLotDetailAndDevicesEnginesPauseADevice() =
        runTest {
            val sites = sites()
            val detail = SitesWiring.lotDetail(FakeLotDetailApi(), sites, settings, backgroundScope, pause = pauseApi)
            val devices = SitesWiring.devices(FakeDevicesApi(), sites, backgroundScope, pause = pauseApi)
            runCurrent()

            detail.pause.openForDevice(node)
            assertEquals(node, assertNotNull(detail.pause.state.value.sheet).deviceId)
            detail.pause.save()
            runCurrent()

            devices.pause.openForDevice(node)
            assertEquals(node, assertNotNull(devices.pause.state.value.sheet).deviceId)
            devices.pause.cancel()
            devices.pause.resumeDevice(node)
            runCurrent()

            assertEquals(listOf("pauseDevice a $node open", "resumeDevice a $node"), pauseApi.calls)
        }

    @Test
    fun anEngineWiredWithoutThePauseApiOpensNothing() =
        runTest {
            val engine =
                SitesWiring.lots(
                    FakeLotsApi(),
                    sites(),
                    settings,
                    backgroundScope,
                    cadence = null,
                    pause = null,
                )
            runCurrent()

            engine.pause.openForSite()

            assertNull(engine.pause.state.value.sheet)
        }
}
