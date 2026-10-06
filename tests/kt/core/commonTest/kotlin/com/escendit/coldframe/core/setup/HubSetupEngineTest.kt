package com.escendit.coldframe.core.setup

import coldframe.setup.v1.WifiStatus
import com.escendit.coldframe.core.api.ColdframeApi
import com.escendit.coldframe.core.signin.mockEngine
import com.escendit.coldframe.core.sites.SiteRole
import com.escendit.coldframe.core.sites.SiteSummary
import com.escendit.coldframe.core.sites.SitesState
import io.ktor.client.engine.mock.MockRequestHandleScope
import io.ktor.client.engine.mock.respond
import io.ktor.client.request.HttpRequestData
import io.ktor.client.request.HttpResponseData
import io.ktor.http.HttpHeaders
import io.ktor.http.HttpMethod
import io.ktor.http.HttpStatusCode
import io.ktor.http.content.OutgoingContent
import io.ktor.http.headersOf
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlin.io.encoding.Base64
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlin.time.Duration.Companion.seconds

/**
 * The Add a Hub engine with a fake radio and a fake Hub running the real session crypto, and the
 * real API client over MockEngine: one test per I/O matrix row and step transition (AD-24).
 */
class HubSetupEngineTest {
    private val base64url = Base64.UrlSafe.withPadding(Base64.PaddingOption.ABSENT)
    private val json = headersOf(HttpHeaders.ContentType, "application/json")
    private val problem = headersOf(HttpHeaders.ContentType, "application/problem+json")

    private val home = SiteSummary("site-home", "Home garden", SiteRole.Owner)
    private val allotment = SiteSummary("site-allotment", "Allotment", SiteRole.Member)
    private val sites = MutableStateFlow<SitesState>(SitesState.Ready(listOf(home, allotment), home, creating = null))

    private val requests = mutableListOf<HttpRequestData>()
    private val bodies = mutableListOf<String>()
    private var keyAnswer: MockRequestHandleScope.() -> HttpResponseData = {
        respond(
            """{"publicKey":"${base64url.encode(SERVER_KEY)}","fingerprint":"$SERVER_FINGERPRINT"}""",
            HttpStatusCode.OK,
            json,
        )
    }
    private val enrolAnswers = ArrayDeque<MockRequestHandleScope.() -> HttpResponseData>()
    private var keys = 0
    private val addNodeSites = mutableListOf<String>()

    private fun created(): MockRequestHandleScope.() -> HttpResponseData =
        { respond("""{"id":"3f2a9c01b2d4e6f8","kind":"hub","siteId":"site-home"}""", HttpStatusCode.Created, json) }

    private fun TestScope.engine(radio: FakeSetupRadio): HubSetupEngine {
        val api =
            ColdframeApi(
                http =
                    ColdframeApi.httpClient(
                        mockEngine { request ->
                            requests += request
                            if (request.url.encodedPath == "/enrolment-key") {
                                keyAnswer()
                            } else {
                                bodies += (request.body as OutgoingContent.ByteArrayContent).bytes().decodeToString()
                                (enrolAnswers.removeFirstOrNull() ?: created())()
                            }
                        },
                    ),
                serverUrl = "https://coldframe.example.org/",
                accessToken = { "access-1" },
                onUnauthorized = {},
            )
        return HubSetupEngine(
            radio = radio,
            api = api,
            sites = sites,
            serverUrl = "https://coldframe.example.org/",
            scope = backgroundScope,
            newKey = { "key-${++keys}" },
            onAddNode = { addNodeSites += it },
        )
    }

    private val HubSetupEngine.now: HubSetupState get() = state.value

    private fun TestScope.advert(
        radio: FakeSetupRadio,
        id: String = "peripheral-1",
        name: String? = "Coldframe Hub 3F2A",
        rssi: Int = -55,
    ) {
        radio.adverts.tryEmit(SetupAdvert(id, name, rssi))
        runCurrent()
    }

    /** Opens the flow, finds and selects Hub 3F2A and enters [code]. */
    private fun TestScope.toCode(
        engine: HubSetupEngine,
        radio: FakeSetupRadio,
        code: String = "k7m2-q9xp",
    ) {
        engine.open()
        runCurrent()
        advert(radio)
        engine.select("peripheral-1")
        engine.continueFromScan()
        engine.setCode(code)
    }

    /** Through an accepted code to the Wi-Fi list. */
    private fun TestScope.toWifi(
        engine: HubSetupEngine,
        radio: FakeSetupRadio,
    ) {
        toCode(engine, radio)
        engine.submitCode()
        runCurrent()
        engine.continueFromCode()
        runCurrent()
    }

    /** Through Novak-Home and its password to step 4 with the key read. */
    private fun TestScope.toSite(
        engine: HubSetupEngine,
        radio: FakeSetupRadio,
    ) {
        toWifi(engine, radio)
        engine.chooseNetwork("Novak-Home")
        engine.setPassword("correct horse")
        engine.continueFromWifi()
        runCurrent()
    }

    private fun TestScope.toProgress(
        engine: HubSetupEngine,
        radio: FakeSetupRadio,
    ) {
        toSite(engine, radio)
        engine.start()
        runCurrent()
    }

    // Opening, AC 1

    @Test
    fun uxDr66UxDr39OpensOnStep1WithCancelForAnOwner() =
        runTest {
            val radio = FakeSetupRadio()
            val engine = engine(radio)

            engine.open()
            runCurrent()

            assertTrue(engine.now.open)
            assertEquals(SetupStep.Scan, engine.now.step)
            assertEquals(1, engine.now.step.number)
            assertTrue(engine.now.showsCancel)
            assertTrue(engine.now.keepAwake)
            assertEquals(1, radio.scans)
            assertEquals(listOf(SiteChoice("site-home", "Home garden")), engine.now.site.sites)
        }

    @Test
    fun uxDr66AMemberCannotOpenTheFlow() =
        runTest {
            sites.value = SitesState.Ready(listOf(allotment), allotment, creating = null)
            val engine = engine(FakeSetupRadio())

            engine.open()

            assertFalse(engine.now.open)
        }

    // Happy path, AC 2 and AC 3

    @Test
    fun uxDr66UxDr40HappyPathBindsEnrolsThenSendsWifiAndEndsOnline() =
        runTest {
            val hub = FakeHub()
            hub.wifiResults += WifiStatus.WIFI_STATUS_CONNECTED
            val radio = FakeSetupRadio(hub)
            val engine = engine(radio)

            toSite(engine, radio)
            assertEquals(SetupStep.Site, engine.now.step)
            assertEquals(SERVER_FINGERPRINT, engine.now.site.fingerprint)
            engine.start()
            runCurrent()

            assertEquals(
                listOf("IdentityRequest", "WifiScanRequest", "SiteBinding", "EnrolmentRequest", "WifiConfig"),
                hub.received,
            )
            val binding = hub.messages.first { it.site_binding != null }.site_binding!!
            assertEquals("site-home", binding.site_id)
            assertEquals("https://coldframe.example.org", binding.server_url)
            assertNull(binding.lot_id)
            val config = hub.messages.first { it.wifi_config != null }.wifi_config!!
            assertEquals("Novak-Home", config.ssid)
            assertEquals("correct horse", config.password)

            val post = requests.single { it.method == HttpMethod.Post }
            assertEquals("https://coldframe.example.org/sites/site-home/devices", post.url.toString())
            assertEquals("key-1", post.headers[ColdframeApi.IDEMPOTENCY_KEY])
            val body = Json.parseToJsonElement(bodies.single()).jsonObject
            assertEquals("3f2a9c01b2d4e6f8", body["deviceId"]!!.jsonPrimitive.content)
            assertEquals("hub", body["kind"]!!.jsonPrimitive.content)
            // Relayed unread: byte-identical to what the Hub sealed.
            assertEquals(base64url.encode(hub.enc), body["enc"]!!.jsonPrimitive.content)
            assertEquals(base64url.encode(hub.ciphertext), body["ciphertext"]!!.jsonPrimitive.content)

            val outcome = assertNotNull(engine.now.outcome)
            assertEquals(OutcomeKind.Online, outcome.kind)
            assertEquals(OutcomeAction.AddNode, outcome.primary)
            assertEquals(ProgressSegment.entries.size, engine.now.progress.reached)
            assertEquals(AnnouncementKind.ServerSees, engine.now.announcement?.kind)
            assertEquals("", engine.now.wifi.password)
            assertTrue(radio.links.single().closed)

            // Add a Node closes this flow first, then opens the Node flow for the Hub's Site.
            assertTrue(addNodeSites.isEmpty())
            engine.outcomeAction(OutcomeAction.AddNode)
            assertFalse(engine.now.open)
            assertEquals(listOf("site-home"), addNodeSites)
        }

    @Test
    fun uxDr40SegmentsAdvanceOnlyOnRealEvents() =
        runTest {
            val hub = FakeHub()
            val radio = FakeSetupRadio(hub)
            val engine = engine(radio)
            toSite(engine, radio)

            engine.start()
            assertEquals(SetupStep.Progress, engine.now.step)
            assertEquals(ProgressSegment.Bluetooth, engine.now.progress.active)
            runCurrent()

            // 201 marks Bluetooth, the WifiConfig write marks Wi-Fi sent; Joining waits.
            assertEquals(2, engine.now.progress.reached)
            assertEquals(SegmentState.Done, engine.now.progress.stateOf(ProgressSegment.WifiSent))
            assertEquals(SegmentState.Active, engine.now.progress.stateOf(ProgressSegment.Joining))
            assertEquals(SegmentState.Pending, engine.now.progress.stateOf(ProgressSegment.Server))
            assertEquals(AnnouncementKind.WifiSent, engine.now.announcement?.kind)
            assertEquals("Novak-Home", engine.now.announcement?.ssid)
            assertNull(engine.now.outcome)

            advanceTimeBy(12.seconds)
            runCurrent()
            assertEquals(12, engine.now.progress.elapsedSeconds)
        }

    @Test
    fun uxDr66NoWifiConfigUnlessEnrolmentAnswered201() =
        runTest {
            for ((status, expected) in listOf(
                HttpStatusCode.Conflict to OutcomeKind.OnAnotherSite,
                HttpStatusCode.Forbidden to OutcomeKind.NotAllowed,
                HttpStatusCode.NotFound to OutcomeKind.SiteGone,
                HttpStatusCode.InternalServerError to OutcomeKind.ServerUnreachable,
            )) {
                val hub = FakeHub()
                val radio = FakeSetupRadio(hub)
                val engine = engine(radio)
                enrolAnswers +=
                    {
                        respond(
                            """{"type":"urn:coldframe:problem:device-on-another-site","title":"t","status":${status.value}}""",
                            status,
                            problem,
                        )
                    }

                toProgress(engine, radio)

                assertEquals(expected, engine.now.outcome?.kind, "$status")
                assertFalse("WifiConfig" in hub.received, "$status")
                assertEquals(0, engine.now.progress.reached)
                assertTrue(radio.links.last().closed)
                assertEquals(AnnouncementKind.Error, engine.now.announcement?.kind)
                assertTrue(engine.now.announcement!!.assertive)
                engine.close()
            }
        }

    @Test
    fun uxDr94AnUnreachableServerDuringEnrolmentSendsNoWifiAndTryAgainReusesTheKey() =
        runTest {
            val hub = FakeHub()
            hub.wifiResults += WifiStatus.WIFI_STATUS_CONNECTED
            val radio = FakeSetupRadio(hub)
            val engine = engine(radio)
            enrolAnswers += { throw IllegalStateException("connection refused") }

            toProgress(engine, radio)
            assertEquals(OutcomeKind.ServerUnreachable, engine.now.outcome?.kind)
            assertEquals(OutcomeAction.StartOver, engine.now.outcome?.primary)
            assertFalse("WifiConfig" in hub.received)

            engine.outcomeAction(OutcomeAction.StartOver)
            runCurrent()
            assertEquals(SetupStep.Scan, engine.now.step)
            advert(radio)
            engine.select("peripheral-1")
            engine.continueFromScan()
            engine.setCode(HUB_CODE)
            engine.submitCode()
            runCurrent()
            engine.continueFromCode()
            runCurrent()
            engine.chooseNetwork("Novak-Home")
            engine.setPassword("correct horse")
            engine.continueFromWifi()
            runCurrent()
            engine.start()
            runCurrent()

            val keysSent =
                requests
                    .filter {
                        it.method == HttpMethod.Post
                    }.map { it.headers[ColdframeApi.IDEMPOTENCY_KEY] }
            assertEquals(listOf("key-1", "key-1"), keysSent)
            assertEquals(OutcomeKind.Online, engine.now.outcome?.kind)
        }

    // Step 1

    @Test
    fun uxDr94BluetoothOffOrDeniedShowsTheNoticeAndScanningResumesWhenReady() =
        runTest {
            val radio = FakeSetupRadio(initial = RadioState.Off)
            val engine = engine(radio)

            engine.open()
            runCurrent()
            assertEquals(RadioState.Off, engine.now.radio)
            assertEquals(0, radio.scans)

            radio.mutableState.value = RadioState.Unauthorized
            runCurrent()
            assertEquals(RadioState.Unauthorized, engine.now.radio)
            engine.recheckRadio()
            assertEquals(1, radio.rechecks)

            radio.mutableState.value = RadioState.Ready
            runCurrent()
            assertEquals(RadioState.Ready, engine.now.radio)
            assertEquals(1, radio.scans)
        }

    @Test
    fun uxDr94NoHubIn30SecondsShowsTheNoticeWhileScanningContinues() =
        runTest {
            val radio = FakeSetupRadio()
            val engine = engine(radio)
            engine.open()
            runCurrent()

            advanceTimeBy(29.seconds)
            runCurrent()
            assertFalse(engine.now.noHubYet)
            advanceTimeBy(2.seconds)
            runCurrent()
            assertTrue(engine.now.noHubYet)

            advert(radio)
            assertFalse(engine.now.noHubYet)
            assertEquals(1, engine.now.candidates.size)
        }

    @Test
    fun uxDr37UxDr105CandidatesAreSortedStrongestFirstAndAnnouncedOncePerDevice() =
        runTest {
            val radio = FakeSetupRadio()
            val engine = engine(radio)
            engine.open()
            runCurrent()

            advert(radio, "a", "Coldframe Hub 3F2A", -70)
            val first = assertNotNull(engine.now.announcement)
            assertEquals(AnnouncementKind.CandidateFound, first.kind)
            assertEquals("3F2A", first.hub)
            assertEquals(SignalStrength.Medium, first.signal)
            assertFalse(first.assertive)

            advert(radio, "b", "Coldframe Hub 11C0", -50)
            val second = assertNotNull(engine.now.announcement)
            assertEquals("11C0", second.hub)
            assertEquals(SignalStrength.Strong, second.signal)
            assertEquals(listOf("11C0", "3F2A"), engine.now.candidates.map { it.shortId })

            // Signal and order changes are not announced.
            advert(radio, "a", "Coldframe Hub 3F2A", -40)
            assertEquals(second, engine.now.announcement)
            assertEquals(listOf("3F2A", "11C0"), engine.now.candidates.map { it.shortId })

            // Other Devices and nameless adverts are not Hubs.
            advert(radio, "c", "Coldframe Node 7C19", -30)
            advert(radio, "d", null, -30)
            assertEquals(2, engine.now.candidates.size)
        }

    @Test
    fun uxDr37SelectingAHubMarksItAndContinueGoesToTheCode() =
        runTest {
            val radio = FakeSetupRadio()
            val engine = engine(radio)
            engine.open()
            runCurrent()
            engine.continueFromScan()
            assertEquals(SetupStep.Scan, engine.now.step)

            advert(radio)
            engine.select("peripheral-1")
            assertEquals("3F2A", engine.now.hubId)
            engine.continueFromScan()

            assertEquals(SetupStep.Code, engine.now.step)
            assertFalse(engine.now.showsCancel)
            assertEquals(0, radio.connects)
        }

    // Step 2

    @Test
    fun uxDr41UxDr95AcceptedCodeShowsTheChipAndKeepsTheFullDeviceId() =
        runTest {
            val hub = FakeHub()
            val radio = FakeSetupRadio(hub)
            val engine = engine(radio)
            toCode(engine, radio)

            engine.submitCode()
            assertTrue(engine.now.code.working)
            runCurrent()

            assertTrue(engine.now.code.accepted)
            assertFalse(engine.now.code.working)
            assertEquals(HubIdentity("3f2a9c01b2d4e6f8", "0.1.0"), engine.now.identity)
            assertEquals(listOf("IdentityRequest"), hub.received)

            engine.continueFromCode()
            runCurrent()
            assertEquals(SetupStep.Wifi, engine.now.step)
            assertEquals(listOf("IdentityRequest", "WifiScanRequest"), hub.received)
        }

    @Test
    fun uxDr95WrongSetupCodeKeepsTheFieldAndTheNextContinueReconnectsWithANewHello() =
        runTest {
            val hub = FakeHub()
            val radio = FakeSetupRadio(hub)
            val engine = engine(radio)
            toCode(engine, radio, code = "K7M2Q9XQ")

            engine.submitCode()
            runCurrent()

            assertEquals(CodeError.WrongCode, engine.now.code.error)
            assertEquals("K7M2Q9XQ", engine.now.code.text)
            assertFalse(engine.now.code.accepted)
            assertNull(engine.now.outcome)
            assertEquals(SetupStep.Code, engine.now.step)
            val announcement = assertNotNull(engine.now.announcement)
            assertEquals(AnnouncementKind.WrongCode, announcement.kind)
            assertTrue(announcement.assertive)
            assertEquals("3F2A", announcement.hub)
            assertTrue(radio.links.single().closed)

            engine.setCode("k7m2 q9xp")
            engine.submitCode()
            runCurrent()

            assertEquals(2, radio.connects)
            assertEquals(2, hub.hellos)
            assertTrue(engine.now.code.accepted)
        }

    @Test
    fun uxDr41ABlankCodeIsRefusedWithoutConnecting() =
        runTest {
            val radio = FakeSetupRadio()
            val engine = engine(radio)
            toCode(engine, radio, code = " - ")

            engine.submitCode()

            assertEquals(CodeError.Blank, engine.now.code.error)
            assertEquals(0, radio.connects)
        }

    @Test
    fun uxDr41TheCodeIsNormalizedAndNeverSent() =
        runTest {
            assertEquals("K7M2Q9XP", SetupCode.normalize(" k7m2-q9xp "))
            assertEquals("K7M2Q9XP", SetupCode.normalize("k7m2 q9xp"))
            assertEquals("0111", SetupCode.normalize("OIlL"))
            assertEquals("K7M2Q9XP", SetupCode.normalize("k7m2\u00a0q9xp\n"))
            assertEquals("K7M2Q9XP", SetupCode.normalize("k7m2\u2013q9\u2014xp\u2010\u2011"))
            assertFalse(SetupCode.isUsable(SetupCode.normalize(" - ")))

            val hub = FakeHub()
            val radio = FakeSetupRadio(hub)
            val engine = engine(radio)
            toSite(engine, radio)
            engine.start()
            runCurrent()
            val written = radio.links.flatMap { it.writes }.joinToString("") { it.decodeToString() }
            assertFalse(written.contains("K7M2Q9XP", ignoreCase = true))
            assertFalse(written.contains("correct horse"))
        }

    // Step 3

    @Test
    fun uxDr42Wpa3OnlyAndOtherRowsAreShownButCannotBeChosen() =
        runTest {
            val radio = FakeSetupRadio()
            val engine = engine(radio)
            toWifi(engine, radio)

            val rows = assertNotNull(engine.now.wifi.networks)
            assertEquals(listOf("Novak-Home", "Neighbour", "Office"), rows.map { it.ssid })
            assertEquals(
                listOf(NetworkSecurity.Wpa2, NetworkSecurity.Wpa3Only, NetworkSecurity.Other),
                rows.map { it.security },
            )
            assertEquals(listOf(true, false, false), rows.map { it.security.supported })

            engine.chooseNetwork("Neighbour")
            engine.chooseNetwork("Office")
            assertEquals("", engine.now.wifi.ssid)
            engine.continueFromWifi()
            assertEquals(WifiError.SsidBlank, engine.now.wifi.error)
            engine.chooseNetwork("Novak-Home")
            assertEquals("Novak-Home", engine.now.wifi.ssid)
        }

    @Test
    fun uxDr42OtherNetworkSendsTheHiddenSsidAsTyped() =
        runTest {
            val hub = FakeHub()
            hub.wifiResults += WifiStatus.WIFI_STATUS_CONNECTED
            val radio = FakeSetupRadio(hub)
            val engine = engine(radio)
            toWifi(engine, radio)

            engine.chooseOtherNetwork()
            engine.setOtherSsid(" Hidden Shed ")
            engine.setPassword("pw")
            engine.continueFromWifi()
            runCurrent()
            engine.start()
            runCurrent()

            assertEquals(
                " Hidden Shed ",
                hub.messages
                    .last { it.wifi_config != null }
                    .wifi_config!!
                    .ssid,
            )
        }

    @Test
    fun uxDr42OverlongSsidAndPasswordAreRefused() =
        runTest {
            val radio = FakeSetupRadio()
            val engine = engine(radio)
            toWifi(engine, radio)
            engine.chooseOtherNetwork()
            engine.setOtherSsid("x".repeat(33))
            engine.continueFromWifi()
            assertEquals(WifiError.SsidTooLong, engine.now.wifi.error)
            engine.setOtherSsid("Shed")
            engine.setPassword("p".repeat(65))
            engine.continueFromWifi()
            assertEquals(WifiError.PasswordTooLong, engine.now.wifi.error)
            assertEquals(SetupStep.Wifi, engine.now.step)
        }

    // Step 4

    @Test
    fun uxDr66TheSiteStepShowsTheCheckedFingerprintAndOnlyAdminSites() =
        runTest {
            val radio = FakeSetupRadio()
            val engine = engine(radio)
            toSite(engine, radio)

            assertEquals("site-home", engine.now.site.selectedId)
            assertEquals(SERVER_FINGERPRINT, engine.now.site.fingerprint)
            engine.chooseSite("site-allotment")
            assertEquals("site-home", engine.now.site.selectedId)
        }

    @Test
    fun uxDr66AnUnreadableKeyShowsTryAgain() =
        runTest {
            val radio = FakeSetupRadio()
            val engine = engine(radio)
            keyAnswer = { throw IllegalStateException("connection refused") }
            toSite(engine, radio)

            assertEquals(KeyNotice.Unreachable, engine.now.site.notice)
            assertNull(engine.now.site.fingerprint)
            engine.start()
            assertEquals(SetupStep.Site, engine.now.step)

            keyAnswer = {
                respond(
                    """{"publicKey":"${base64url.encode(SERVER_KEY)}","fingerprint":"$SERVER_FINGERPRINT"}""",
                    HttpStatusCode.OK,
                    json,
                )
            }
            engine.retryKey()
            runCurrent()
            assertEquals(SERVER_FINGERPRINT, engine.now.site.fingerprint)
        }

    @Test
    fun uxDr95AFingerprintThatDoesNotMatchTheKeyStopsBeforeEnrolment() =
        runTest {
            val hub = FakeHub()
            val radio = FakeSetupRadio(hub)
            val engine = engine(radio)
            keyAnswer = {
                respond(
                    """{"publicKey":"${base64url.encode(SERVER_KEY)}","fingerprint":"${"0".repeat(64)}"}""",
                    HttpStatusCode.OK,
                    json,
                )
            }
            toSite(engine, radio)

            assertEquals(OutcomeKind.FingerprintMismatch, engine.now.outcome?.kind)
            assertFalse("EnrolmentRequest" in hub.received)
            assertTrue(requests.none { it.method == HttpMethod.Post })
            assertTrue(radio.links.single().closed)
        }

    @Test
    fun uxDr95TheHubRefusingTheFingerprintEnrolsNothing() =
        runTest {
            val hub = FakeHub()
            val radio = FakeSetupRadio(hub)
            val engine = engine(radio)
            toSite(engine, radio)
            hub.refuseFingerprint = true

            engine.start()
            runCurrent()

            assertEquals(OutcomeKind.FingerprintMismatch, engine.now.outcome?.kind)
            assertTrue(requests.none { it.method == HttpMethod.Post })
            assertFalse("WifiConfig" in hub.received)
            assertTrue(radio.links.single().closed)
        }

    // Step 5 outcomes

    @Test
    fun uxDr95WrongWifiPasswordReentersOnStep3AndTheRetryResendsOnlyWifiConfig() =
        runTest {
            val hub = FakeHub()
            hub.wifiResults += WifiStatus.WIFI_STATUS_WRONG_PASSWORD
            hub.wifiResults += WifiStatus.WIFI_STATUS_CONNECTED
            val radio = FakeSetupRadio(hub)
            val engine = engine(radio)
            toProgress(engine, radio)

            val outcome = assertNotNull(engine.now.outcome)
            assertEquals(OutcomeKind.WrongPassword, outcome.kind)
            assertEquals(5, outcome.stoppedStep)
            assertEquals(OutcomeAction.ReenterPassword, outcome.primary)
            assertEquals(OutcomeAction.OtherNetwork, outcome.secondary)
            assertEquals(AnnouncementKind.Error, engine.now.announcement?.kind)
            assertTrue(engine.now.announcement!!.assertive)
            assertFalse(radio.links.single().closed)

            engine.outcomeAction(OutcomeAction.ReenterPassword)
            assertEquals(SetupStep.Wifi, engine.now.step)
            assertEquals("Novak-Home", engine.now.wifi.ssid)
            assertEquals("", engine.now.wifi.password)
            engine.setPassword("battery staple")
            engine.continueFromWifi()
            runCurrent()

            assertEquals(
                listOf(
                    "IdentityRequest",
                    "WifiScanRequest",
                    "SiteBinding",
                    "EnrolmentRequest",
                    "WifiConfig",
                    "WifiConfig",
                ),
                hub.received,
            )
            assertEquals(1, requests.count { it.method == HttpMethod.Post })
            assertEquals(OutcomeKind.Online, engine.now.outcome?.kind)
        }

    @Test
    fun uxDr95WrongPasswordOtherNetworkOpensTheSsidField() =
        runTest {
            val hub = FakeHub()
            hub.wifiResults += WifiStatus.WIFI_STATUS_WRONG_PASSWORD
            val radio = FakeSetupRadio(hub)
            val engine = engine(radio)
            toProgress(engine, radio)

            engine.outcomeAction(OutcomeAction.OtherNetwork)

            assertEquals(SetupStep.Wifi, engine.now.step)
            assertTrue(engine.now.wifi.other)
            assertEquals("", engine.now.wifi.ssid)
        }

    @Test
    fun uxDr95NetworkNotFoundOrUnsupportedGoesBackToStep3() =
        runTest {
            for ((status, kind) in listOf(
                WifiStatus.WIFI_STATUS_NETWORK_NOT_FOUND to OutcomeKind.NetworkNotFound,
                WifiStatus.WIFI_STATUS_UNSUPPORTED_SECURITY to OutcomeKind.UnsupportedSecurity,
            )) {
                val hub = FakeHub()
                hub.wifiResults += status
                val radio = FakeSetupRadio(hub)
                val engine = engine(radio)
                toProgress(engine, radio)

                assertEquals(kind, engine.now.outcome?.kind)
                assertEquals(OutcomeAction.ChooseNetwork, engine.now.outcome?.primary)
                engine.outcomeAction(OutcomeAction.ChooseNetwork)
                assertEquals(SetupStep.Wifi, engine.now.step)
                engine.close()
            }
        }

    @Test
    fun uxDr95NoServerKeepsTheSessionTryAgainResendsWifiConfigAndHelpRevealsText() =
        runTest {
            val hub = FakeHub()
            hub.wifiResults += WifiStatus.WIFI_STATUS_NO_SERVER
            hub.wifiResults += WifiStatus.WIFI_STATUS_CONNECTED
            val radio = FakeSetupRadio(hub)
            val engine = engine(radio)
            toProgress(engine, radio)

            val outcome = assertNotNull(engine.now.outcome)
            assertEquals(OutcomeKind.NoServer, outcome.kind)
            assertEquals(OutcomeAction.RetryWifi, outcome.primary)
            assertEquals(OutcomeAction.Help, outcome.secondary)
            assertFalse(radio.links.single().closed)

            engine.outcomeAction(OutcomeAction.Help)
            assertTrue(engine.now.outcome!!.helpShown)
            assertNull(engine.now.outcome!!.secondary)

            engine.outcomeAction(OutcomeAction.RetryWifi)
            runCurrent()
            assertEquals(2, hub.received.count { it == "WifiConfig" })
            assertEquals(1, hub.received.count { it == "SiteBinding" })
            assertEquals(OutcomeKind.Online, engine.now.outcome?.kind)
        }

    @Test
    fun uxDr40ProgressTimesOutAfter90SecondsWithoutAWifiResult() =
        runTest {
            val radio = FakeSetupRadio()
            val engine = engine(radio)
            toProgress(engine, radio)

            advanceTimeBy(89.seconds)
            runCurrent()
            assertNull(engine.now.outcome)
            advanceTimeBy(2.seconds)
            runCurrent()

            val outcome = assertNotNull(engine.now.outcome)
            assertEquals(OutcomeKind.Timeout, outcome.kind)
            assertEquals(OutcomeAction.StartOver, outcome.primary)
            assertTrue(engine.now.announcement!!.assertive)
            assertTrue(radio.links.single().closed)

            engine.outcomeAction(OutcomeAction.StartOver)
            runCurrent()
            assertEquals(SetupStep.Scan, engine.now.step)
            assertNull(engine.now.outcome)
            assertEquals(2, radio.scans)
        }

    @Test
    fun uxDr103TheTimeoutWaitsWhileAScreenReaderIsAnnouncing() =
        runTest {
            val radio = FakeSetupRadio()
            val engine = engine(radio)
            toProgress(engine, radio)

            engine.announcing(true)
            advanceTimeBy(120.seconds)
            runCurrent()
            assertNull(engine.now.outcome)

            engine.announcing(false)
            runCurrent()
            assertEquals(OutcomeKind.Timeout, engine.now.outcome?.kind)
        }

    @Test
    fun uxDr94LostConnectionBeforeAnOutcomeOffersTryAgainFromStep1() =
        runTest {
            val radio = FakeSetupRadio()
            val engine = engine(radio)
            toWifi(engine, radio)

            radio.links.single().dropFromHub()
            runCurrent()

            val outcome = assertNotNull(engine.now.outcome)
            assertEquals(OutcomeKind.LostConnection, outcome.kind)
            assertEquals(OutcomeAction.StartOver, outcome.primary)
            engine.outcomeAction(OutcomeAction.StartOver)
            runCurrent()
            assertEquals(SetupStep.Scan, engine.now.step)
        }

    @Test
    fun uxDr94LostConnectionWhileJoiningEndsOnTheSameOutcome() =
        runTest {
            val radio = FakeSetupRadio()
            val engine = engine(radio)
            toProgress(engine, radio)

            radio.links.single().dropFromHub()
            runCurrent()

            assertEquals(OutcomeKind.LostConnection, engine.now.outcome?.kind)
        }

    @Test
    fun uxDr94AHubThatCannotBeReachedIsALostConnection() =
        runTest {
            val radio = FakeSetupRadio()
            radio.connectFails = true
            val engine = engine(radio)
            toCode(engine, radio)

            engine.submitCode()
            runCurrent()

            assertEquals(OutcomeKind.LostConnection, engine.now.outcome?.kind)
        }

    // Leaving and navigation

    @Test
    fun uxDr39LeavingAfterAHubIsSelectedAsksAndConfirmDisconnectsAndCloses() =
        runTest {
            val radio = FakeSetupRadio()
            val engine = engine(radio)
            toWifi(engine, radio)
            engine.setPassword("secret")

            engine.back()
            engine.back()
            runCurrent()
            assertEquals(SetupStep.Scan, engine.now.step)
            assertTrue(radio.links.single().closed)
            advert(radio)
            engine.select("peripheral-1")

            engine.leave()
            assertTrue(engine.now.confirmingLeave)
            engine.stayInFlow()
            assertFalse(engine.now.confirmingLeave)
            engine.leave()
            engine.confirmLeave()

            assertFalse(engine.now.open)
            assertEquals("", engine.now.wifi.password)
        }

    @Test
    fun uxDr39CancelBeforeAHubIsSelectedClosesAtOnce() =
        runTest {
            val engine = engine(FakeSetupRadio())
            engine.open()
            runCurrent()

            engine.leave()

            assertFalse(engine.now.open)
        }

    @Test
    fun uxDr39BackOnARunningStep5Asks() =
        runTest {
            val radio = FakeSetupRadio()
            val engine = engine(radio)
            toProgress(engine, radio)

            engine.back()

            assertTrue(engine.now.confirmingLeave)
            engine.confirmLeave()
            runCurrent()
            assertFalse(engine.now.open)
            assertTrue(radio.links.single().closed)
        }

    @Test
    fun uxDr39BackWalksTheStepsBackward() =
        runTest {
            val radio = FakeSetupRadio()
            val engine = engine(radio)
            toSite(engine, radio)

            engine.back()
            assertEquals(SetupStep.Wifi, engine.now.step)
            assertEquals("Novak-Home", engine.now.wifi.ssid)
            engine.back()
            assertEquals(SetupStep.Code, engine.now.step)
            assertTrue(engine.now.code.accepted)
            engine.back()
            assertEquals(SetupStep.Scan, engine.now.step)
            assertFalse(engine.now.code.accepted)
        }

    @Test
    fun signingOutClosesTheFlow() =
        runTest {
            val engine = engine(FakeSetupRadio())
            engine.open()
            runCurrent()

            sites.value = SitesState.Idle
            runCurrent()

            assertFalse(engine.now.open)
        }

    @Test
    fun signingOutAfterStartingOverStillClosesTheFlow() =
        runTest {
            val radio = FakeSetupRadio()
            val engine = engine(radio)
            toWifi(engine, radio)
            radio.links.single().dropFromHub()
            runCurrent()
            engine.outcomeAction(OutcomeAction.StartOver)
            runCurrent()

            sites.value = SitesState.Idle
            runCurrent()

            assertFalse(engine.now.open)
        }

    @Test
    fun uxDr42GoingBackWhileTheHubScansAsksOnlyOnce() =
        runTest {
            val hub = FakeHub()
            val radio = FakeSetupRadio(hub)
            val engine = engine(radio)
            toCode(engine, radio)
            engine.submitCode()
            runCurrent()

            engine.continueFromCode()
            engine.back()
            engine.continueFromCode()
            runCurrent()

            assertEquals(1, hub.received.count { it == "WifiScanRequest" })
            assertNotNull(engine.now.wifi.networks)
        }

    @Test
    fun uxDr39BackToStep1WhileTheHubScansEndsOnStep1WithoutAnOutcome() =
        runTest {
            val hub = FakeHub()
            hub.holdScan = true
            val radio = FakeSetupRadio(hub)
            val engine = engine(radio)
            toCode(engine, radio)
            engine.submitCode()
            runCurrent()
            engine.continueFromCode()
            runCurrent()
            assertNull(engine.now.wifi.networks)

            engine.back()
            engine.back()
            runCurrent()

            assertEquals(SetupStep.Scan, engine.now.step)
            assertNull(engine.now.outcome)
            assertNull(engine.now.selected)
            assertTrue(radio.links.single().closed)
        }

    @Test
    fun theSetupCodeAndThePasswordNeverAppearInToString() =
        runTest {
            val radio = FakeSetupRadio()
            val engine = engine(radio)
            toWifi(engine, radio)
            engine.chooseNetwork("Novak-Home")
            engine.setPassword("correct horse")

            for (text in listOf(engine.now.toString(), snapshotOf(engine.now).toString())) {
                assertFalse(text.contains("k7m2", ignoreCase = true), text)
                assertFalse(text.contains("correct horse"), text)
            }
        }
}
