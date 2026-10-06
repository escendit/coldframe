package com.escendit.coldframe.core.setup

import coldframe.setup.v1.DeviceKind
import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.ColdframeApi
import com.escendit.coldframe.core.api.DeviceDto
import com.escendit.coldframe.core.api.DeviceListItemDto
import com.escendit.coldframe.core.api.EnrolDeviceRequestDto
import com.escendit.coldframe.core.api.EnrolmentKeyDto
import com.escendit.coldframe.core.api.LotDto
import com.escendit.coldframe.core.api.SiteDto
import com.escendit.coldframe.core.devices.DevicesEngine
import com.escendit.coldframe.core.devices.DevicesState
import com.escendit.coldframe.core.devices.FakeDevicesApi
import com.escendit.coldframe.core.lots.FakeLotsApi
import com.escendit.coldframe.core.lots.LotStatus
import com.escendit.coldframe.core.lots.LotsEngine
import com.escendit.coldframe.core.lots.LotsState
import com.escendit.coldframe.core.signin.SignInState
import com.escendit.coldframe.core.signin.mockEngine
import com.escendit.coldframe.core.sites.DeviceChoices
import com.escendit.coldframe.core.sites.FakeSitesApi
import com.escendit.coldframe.core.sites.NameError
import com.escendit.coldframe.core.sites.SiteRole
import com.escendit.coldframe.core.sites.SiteSummary
import com.escendit.coldframe.core.sites.SitesEngine
import com.escendit.coldframe.core.sites.SitesState
import com.escendit.coldframe.core.sites.SitesWiring
import com.russhwolf.settings.MapSettings
import io.ktor.client.engine.mock.MockRequestHandleScope
import io.ktor.client.engine.mock.respond
import io.ktor.client.request.HttpRequestData
import io.ktor.client.request.HttpResponseData
import io.ktor.http.HttpHeaders
import io.ktor.http.HttpMethod
import io.ktor.http.HttpStatusCode
import io.ktor.http.content.OutgoingContent
import io.ktor.http.headersOf
import kotlinx.coroutines.CompletableDeferred
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
import kotlin.test.assertIs
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlin.time.Duration.Companion.seconds

/**
 * The Add a Node engine with a fake radio and a fake Node running the real session crypto, and
 * the real API client over MockEngine: one test per I/O matrix row and step transition (AD-24).
 */
class NodeSetupEngineTest {
    private val base64url = Base64.UrlSafe.withPadding(Base64.PaddingOption.ABSENT)
    private val json = headersOf(HttpHeaders.ContentType, "application/json")
    private val problem = headersOf(HttpHeaders.ContentType, "application/problem+json")

    private val home = SiteSummary("site-home", "Home garden", SiteRole.Owner)
    private val shed = SiteSummary("site-shed", "Shed", SiteRole.Administrator)
    private val allotment = SiteSummary("site-allotment", "Allotment", SiteRole.Member)
    private val sites =
        MutableStateFlow<SitesState>(SitesState.Ready(listOf(home, shed, allotment), home, creating = null))

    private val requests = mutableListOf<HttpRequestData>()
    private val enrolBodies = mutableListOf<String>()
    private val createBodies = mutableListOf<String>()
    private var keyAnswer: MockRequestHandleScope.() -> HttpResponseData = {
        respond(
            """{"publicKey":"${base64url.encode(SERVER_KEY)}","fingerprint":"$SERVER_FINGERPRINT"}""",
            HttpStatusCode.OK,
            json,
        )
    }

    /** The Lots the Server lists, in its order: Tomatoes and Peppers are free, Beans has a Node. */
    private var lotsJson =
        """{"lots":[""" +
            """{"id":"lot-tomatoes","name":"Tomatoes","status":"noNode"},""" +
            """{"id":"lot-beans","name":"Beans","status":"ok"},""" +
            """{"id":"lot-peppers","name":"Peppers","status":"noNode"},""" +
            """{"id":"lot-old","name":"Old","status":"noNode","removed":true}]}"""
    private val lotsAnswers = ArrayDeque<MockRequestHandleScope.() -> HttpResponseData>()
    private val createAnswers = ArrayDeque<MockRequestHandleScope.() -> HttpResponseData>()
    private val enrolAnswers = ArrayDeque<MockRequestHandleScope.() -> HttpResponseData>()
    private var keys = 0
    private var assigned = 0

    /** Holds every read of the Lots until it completes. */
    private var lotsGate: CompletableDeferred<Unit>? = null

    private fun created(): MockRequestHandleScope.() -> HttpResponseData =
        {
            respond(
                """{"id":"7c19aa01b2d4e6f8","kind":"node","siteId":"site-home","lotId":"lot-tomatoes"}""",
                HttpStatusCode.Created,
                json,
            )
        }

    private fun refusal(
        status: HttpStatusCode,
        type: String,
    ): MockRequestHandleScope.() -> HttpResponseData =
        { respond("""{"type":"urn:coldframe:problem:$type","title":"t","status":${status.value}}""", status, problem) }

    private fun TestScope.engine(radio: FakeSetupRadio): NodeSetupEngine {
        val api =
            ColdframeApi(
                http =
                    ColdframeApi.httpClient(
                        mockEngine { request ->
                            requests += request
                            val path = request.url.encodedPath
                            when {
                                path == "/enrolment-key" -> {
                                    keyAnswer()
                                }

                                path.endsWith("/lots") && request.method == HttpMethod.Get -> {
                                    lotsGate?.await()
                                    (
                                        lotsAnswers.removeFirstOrNull() ?: {
                                            respond(
                                                lotsJson,
                                                HttpStatusCode.OK,
                                                json,
                                            )
                                        }
                                    )()
                                }

                                path.endsWith("/lots") -> {
                                    createBodies += request.text()
                                    createAnswers.removeFirst()()
                                }

                                else -> {
                                    enrolBodies += request.text()
                                    (enrolAnswers.removeFirstOrNull() ?: created())()
                                }
                            }
                        },
                    ),
                serverUrl = "https://coldframe.example.org/",
                accessToken = { "access-1" },
                onUnauthorized = {},
            )
        return NodeSetupEngine(
            radio = radio,
            api = api,
            lots = api,
            sites = sites,
            scope = backgroundScope,
            onAssigned = { assigned++ },
            newKey = { "key-${++keys}" },
        )
    }

    private fun HttpRequestData.text(): String = (body as OutgoingContent.ByteArrayContent).bytes().decodeToString()

    private val NodeSetupEngine.now: NodeSetupState get() = state.value

    private val posts: List<HttpRequestData>
        get() = requests.filter { it.method == HttpMethod.Post && it.url.encodedPath.endsWith("/devices") }

    private fun radioFor(node: FakeNode): FakeSetupRadio = FakeSetupRadio(device = node)

    private fun TestScope.advert(
        radio: FakeSetupRadio,
        id: String = "peripheral-1",
        name: String? = "Coldframe Node 7C19",
        rssi: Int = -55,
    ) {
        radio.adverts.tryEmit(SetupAdvert(id, name, rssi))
        runCurrent()
    }

    /** Opens the flow, continues to the list, finds and selects Node 7C19 and enters [code]. */
    private fun TestScope.toCode(
        engine: NodeSetupEngine,
        radio: FakeSetupRadio,
        code: String = "n4d3-c0de",
        lotId: String? = null,
    ) {
        engine.open(lotId = lotId)
        runCurrent()
        engine.continueFromPress()
        runCurrent()
        advert(radio)
        engine.select("peripheral-1")
        engine.continueFromScan()
        engine.setCode(code)
    }

    /** Through an accepted code to the Lot picker with the Lots read. */
    private fun TestScope.toLots(
        engine: NodeSetupEngine,
        radio: FakeSetupRadio,
        lotId: String? = null,
    ) {
        toCode(engine, radio, lotId = lotId)
        engine.submitCode()
        runCurrent()
        engine.continueFromCode()
        runCurrent()
    }

    // Opening

    @Test
    fun uxDr67UxDr39OpensOnStep1WithCancelAndDoesNotScanYet() =
        runTest {
            val radio = FakeSetupRadio()
            val engine = engine(radio)

            engine.open()
            runCurrent()

            assertTrue(engine.now.open)
            assertEquals(NodeSetupStep.Press, engine.now.step)
            assertEquals(1, engine.now.step.number)
            assertEquals(5, NodeSetupStep.COUNT)
            assertTrue(engine.now.showsCancel)
            assertTrue(engine.now.keepAwake)
            assertEquals("site-home", engine.now.siteId)
            assertEquals("Home garden", engine.now.siteName)
            assertEquals(0, radio.scans)
        }

    @Test
    fun uxDr67OnlyAnAdministratorOrOwnerOfTheSiteCanOpenTheFlow() =
        runTest {
            val engine = engine(FakeSetupRadio())

            engine.open("site-allotment")
            assertFalse(engine.now.open)
            engine.open("site-unknown")
            assertFalse(engine.now.open)

            sites.value = SitesState.Ready(listOf(allotment), allotment, creating = null)
            engine.open()
            assertFalse(engine.now.open)

            sites.value = SitesState.Idle
            engine.open()
            assertFalse(engine.now.open)
        }

    @Test
    fun uxDr67TheHubOutcomeOpensTheFlowOnStep1ForTheHubsSite() =
        runTest {
            val engine = engine(FakeSetupRadio())

            engine.open("site-shed")
            runCurrent()

            assertTrue(engine.now.open)
            assertEquals(NodeSetupStep.Press, engine.now.step)
            assertEquals("site-shed", engine.now.siteId)
            assertEquals("Shed", engine.now.siteName)
        }

    // Happy path

    @Test
    fun uxDr67HappyPathEnrolsOverBleThenAssignsTheLotOnlyAfterTheUserConfirmed() =
        runTest {
            val node = FakeNode()
            val radio = radioFor(node)
            val engine = engine(radio)

            engine.open()
            runCurrent()
            engine.continueFromPress()
            runCurrent()
            assertEquals(NodeSetupStep.Scan, engine.now.step)
            assertEquals(2, engine.now.step.number)
            assertFalse(engine.now.showsCancel)
            assertEquals(1, radio.scans)

            advert(radio)
            engine.select("peripheral-1")
            assertEquals("7C19", engine.now.nodeId)
            engine.continueFromScan()
            assertEquals(NodeSetupStep.Code, engine.now.step)
            assertEquals(0, radio.connects)

            engine.setCode("n4d3-c0de")
            engine.submitCode()
            assertTrue(engine.now.code.working)
            runCurrent()

            // The chip shows only once the sealed key is held and the Node is hung up on.
            assertTrue(engine.now.code.accepted)
            assertFalse(engine.now.code.working)
            assertEquals("7c19aa01b2d4e6f8", engine.now.deviceId)
            assertTrue(radio.links.single().closed)
            // A Node never gets a binding or a Wi-Fi message.
            assertEquals(listOf("IdentityRequest", "EnrolmentRequest"), node.received)
            assertTrue(posts.isEmpty())

            engine.continueFromCode()
            runCurrent()
            assertEquals(NodeSetupStep.Lot, engine.now.step)
            assertEquals(4, engine.now.step.number)
            assertEquals(
                listOf(
                    LotChoice("lot-tomatoes", "Tomatoes", selectable = true),
                    LotChoice("lot-beans", "Beans", selectable = false),
                    LotChoice("lot-peppers", "Peppers", selectable = true),
                ),
                engine.now.lots.choices,
            )
            assertNull(engine.now.lots.selectedId)
            engine.assign()
            assertTrue(posts.isEmpty())

            engine.chooseLot("lot-tomatoes")
            assertEquals("Tomatoes", engine.now.lotName)
            assertTrue(posts.isEmpty())
            assertEquals(0, assigned)
            engine.assign()
            assertTrue(engine.now.lots.assigning)
            runCurrent()

            val post = posts.single()
            assertEquals("https://coldframe.example.org/sites/site-home/devices", post.url.toString())
            assertEquals("key-1", post.headers[ColdframeApi.IDEMPOTENCY_KEY])
            val body = Json.parseToJsonElement(enrolBodies.single()).jsonObject
            assertEquals("7c19aa01b2d4e6f8", body["deviceId"]!!.jsonPrimitive.content)
            assertEquals("node", body["kind"]!!.jsonPrimitive.content)
            assertEquals("lot-tomatoes", body["lotId"]!!.jsonPrimitive.content)
            // Relayed unread: byte-identical to what the Node sealed.
            assertEquals(base64url.encode(node.enc), body["enc"]!!.jsonPrimitive.content)
            assertEquals(base64url.encode(node.ciphertext), body["ciphertext"]!!.jsonPrimitive.content)

            val outcome = assertNotNull(engine.now.outcome)
            assertEquals(NodeOutcomeKind.Assigned, outcome.kind)
            assertTrue(outcome.success)
            assertEquals(NodeOutcomeAction.Done, outcome.primary)
            assertEquals(NodeSetupStep.Outcome, engine.now.step)
            assertEquals(5, engine.now.step.number)
            assertEquals("Tomatoes", engine.now.lotName)
            val announcement = assertNotNull(engine.now.announcement)
            assertEquals(NodeAnnouncementKind.Assigned, announcement.kind)
            assertFalse(announcement.assertive)
            assertEquals("Tomatoes", announcement.lot)
            assertEquals(1, assigned)
            assertEquals(1, radio.connects)

            engine.outcomeAction(NodeOutcomeAction.Done)
            assertFalse(engine.now.open)
        }

    @Test
    fun uxDr67AnAssignedNodeReloadsTheLotsAndTheDevicesSoTheTileIsNoLongerNoNode() =
        runTest {
            val sitesApi = FakeSitesApi()
            sitesApi.sites += SiteDto("a", "Home garden", "Owner")
            val lotsApi = FakeLotsApi()
            lotsApi.lots["a"] = mutableListOf(LotDto("lot-tomatoes", "Tomatoes", "noNode"))
            val devicesApi = FakeDevicesApi()
            val signIn = MutableStateFlow<SignInState>(SignInState.Restoring)
            val sitesEngine =
                SitesEngine(
                    api = sitesApi,
                    choices = DeviceChoices(MapSettings()),
                    scope = backgroundScope,
                    signIn = signIn,
                    detectTimeZone = { "Europe/Zurich" },
                    newKey = { "site-key" },
                    zones = { emptyList() },
                )
            val lots = LotsEngine(lotsApi, sitesEngine, MapSettings(), backgroundScope)
            val devices = DevicesEngine(devicesApi, sitesEngine, backgroundScope)
            signIn.value = SignInState.SignedIn("Simon")
            runCurrent()
            // A Server that assigns: the Lot stops being noNode and the Node is listed.
            val enrolment =
                object : EnrolmentApi {
                    override suspend fun enrolmentKey(): ApiResult<EnrolmentKeyDto> =
                        ApiResult.Ok(EnrolmentKeyDto(base64url.encode(SERVER_KEY), SERVER_FINGERPRINT))

                    override suspend fun enrolDevice(
                        siteId: String,
                        request: EnrolDeviceRequestDto,
                        idempotencyKey: String,
                    ): ApiResult<DeviceDto> {
                        lotsApi.lots[siteId] = mutableListOf(LotDto(request.lotId!!, "Tomatoes", "needsCalibration"))
                        devicesApi.devices[siteId] =
                            listOf(DeviceListItemDto(request.deviceId, "node", online = false, lotId = request.lotId))
                        return ApiResult.Ok(DeviceDto(request.deviceId, request.kind, siteId, request.lotId))
                    }
                }
            val node = FakeNode()
            val radio = radioFor(node)
            val engine = SitesWiring.nodeSetup(enrolment, lotsApi, radio, sitesEngine, lots, devices, backgroundScope)

            engine.open(lotId = "lot-tomatoes")
            runCurrent()
            engine.continueFromPress()
            runCurrent()
            advert(radio)
            engine.select("peripheral-1")
            engine.continueFromScan()
            engine.setCode(NODE_CODE)
            engine.submitCode()
            runCurrent()
            engine.continueFromCode()
            runCurrent()
            assertEquals("lot-tomatoes", engine.state.value.lots.selectedId)
            val devicesReads = devicesApi.calls.size
            engine.assign()
            runCurrent()

            assertEquals(
                NodeOutcomeKind.Assigned,
                engine.state.value.outcome
                    ?.kind,
            )
            val ready = assertIs<LotsState.Ready>(lots.state.value)
            assertEquals(LotStatus.NeedsCalibration, ready.lots.single().status)
            assertEquals(devicesReads + 1, devicesApi.calls.size)
            assertIs<DevicesState.Ready>(devices.state.value)
        }

    // Step 2: scan

    @Test
    fun uxDr94BluetoothOffOrDeniedShowsTheNoticeAndScanningResumesWhenReady() =
        runTest {
            val radio = FakeSetupRadio(initial = RadioState.Off)
            val engine = engine(radio)
            engine.open()
            runCurrent()
            engine.continueFromPress()
            runCurrent()

            assertEquals(NodeSetupStep.Scan, engine.now.step)
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
    fun uxDr94NoNodeIn30SecondsShowsTheNoticeWhileScanningContinues() =
        runTest {
            val radio = FakeSetupRadio()
            val engine = engine(radio)
            engine.open()
            runCurrent()
            engine.continueFromPress()
            runCurrent()

            advanceTimeBy(29.seconds)
            runCurrent()
            assertFalse(engine.now.noNodeYet)
            // A Hub in range is not a Node.
            advert(radio, "hub", "Coldframe Hub 3F2A", -40)
            advanceTimeBy(2.seconds)
            runCurrent()
            assertTrue(engine.now.noNodeYet)
            assertTrue(engine.now.candidates.isEmpty())

            advert(radio)
            assertFalse(engine.now.noNodeYet)
            assertEquals(1, engine.now.candidates.size)
        }

    @Test
    fun uxDr37UxDr105TwoNodesAreListedStrongestFirstAndOnlyTheLastHeardIsPressedJustNow() =
        runTest {
            val radio = FakeSetupRadio()
            val engine = engine(radio)
            engine.open()
            runCurrent()
            engine.continueFromPress()
            runCurrent()

            advert(radio, "a", "Coldframe Node 7C19", -50)
            val first = assertNotNull(engine.now.announcement)
            assertEquals(NodeAnnouncementKind.CandidateFound, first.kind)
            assertEquals("7C19", first.node)
            assertEquals(SignalStrength.Strong, first.signal)
            assertFalse(first.assertive)
            assertEquals(listOf(true), engine.now.candidates.map { it.pressedJustNow })

            advert(radio, "b", "Coldframe Node 11C0", -70)
            val second = assertNotNull(engine.now.announcement)
            assertEquals("11C0", second.node)
            assertEquals(SignalStrength.Medium, second.signal)
            assertEquals(listOf("7C19", "11C0"), engine.now.candidates.map { it.shortId })
            assertEquals(listOf(false, true), engine.now.candidates.map { it.pressedJustNow })

            // A stronger signal changes the order, not the badge, and is not announced.
            advert(radio, "b", "Coldframe Node 11C0", -40)
            assertEquals(second, engine.now.announcement)
            assertEquals(listOf("11C0", "7C19"), engine.now.candidates.map { it.shortId })
            assertEquals(listOf(true, false), engine.now.candidates.map { it.pressedJustNow })
            advert(radio, "a", "Coldframe Node 7C19", -45)
            assertEquals(second, engine.now.announcement)
            assertEquals(listOf(true, false), engine.now.candidates.map { it.pressedJustNow })

            // Hubs and nameless adverts are not Nodes.
            advert(radio, "c", "Coldframe Hub 3F2A", -30)
            advert(radio, "d", null, -30)
            advert(radio, "e", "Coldframe Node ", -30)
            assertEquals(2, engine.now.candidates.size)
        }

    @Test
    fun uxDr37ContinueNeedsASelectedNode() =
        runTest {
            val radio = FakeSetupRadio()
            val engine = engine(radio)
            engine.open()
            runCurrent()
            engine.continueFromPress()
            runCurrent()

            engine.continueFromScan()
            assertEquals(NodeSetupStep.Scan, engine.now.step)
            engine.select("unknown")
            assertNull(engine.now.selected)
        }

    // Step 3: the code and the BLE session

    @Test
    fun uxDr94WrongSetupCodeKeepsTheFieldAndTheNextContinueReconnects() =
        runTest {
            val node = FakeNode()
            val radio = radioFor(node)
            val engine = engine(radio)
            toCode(engine, radio, code = "N4D3C0DF")

            engine.submitCode()
            runCurrent()

            assertEquals(CodeError.WrongCode, engine.now.code.error)
            assertEquals("N4D3C0DF", engine.now.code.text)
            assertFalse(engine.now.code.accepted)
            assertFalse(engine.now.code.working)
            assertNull(engine.now.outcome)
            assertEquals(NodeSetupStep.Code, engine.now.step)
            val announcement = assertNotNull(engine.now.announcement)
            assertEquals(NodeAnnouncementKind.WrongCode, announcement.kind)
            assertTrue(announcement.assertive)
            assertEquals("7C19", announcement.node)
            assertTrue(radio.links.single().closed)
            assertTrue(requests.isEmpty())

            engine.setCode("n4d3 c0de")
            engine.submitCode()
            runCurrent()

            assertEquals(2, radio.connects)
            assertEquals(2, node.hellos)
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
    fun uxDr94ANodeThatCannotBeConnectedToStoppedListening() =
        runTest {
            val radio = radioFor(FakeNode())
            radio.connectFails = true
            val engine = engine(radio)
            toCode(engine, radio)

            engine.submitCode()
            runCurrent()

            val outcome = assertNotNull(engine.now.outcome)
            assertEquals(NodeOutcomeKind.StoppedListening, outcome.kind)
            assertEquals(3, outcome.stoppedStep)
            assertEquals(NodeOutcomeAction.StartOver, outcome.primary)
            val announcement = assertNotNull(engine.now.announcement)
            assertEquals(NodeAnnouncementKind.Error, announcement.kind)
            assertEquals(NodeOutcomeKind.StoppedListening, announcement.outcome)
            assertTrue(announcement.assertive)

            radio.connectFails = false
            engine.outcomeAction(NodeOutcomeAction.StartOver)
            runCurrent()
            assertEquals(NodeSetupStep.Press, engine.now.step)
            assertNull(engine.now.outcome)
            assertNull(engine.now.selected)
            assertEquals("", engine.now.code.text)
            assertEquals("site-home", engine.now.siteId)
        }

    @Test
    fun uxDr94ADroppedLinkOrAMissingReplyMidSessionIsALostConnection() =
        runTest {
            val dropping = FakeNode()
            dropping.dropOnEnrolment = true
            val silent = FakeNode()
            silent.holdEnrolment = true
            for (node in listOf(dropping, silent)) {
                val radio = radioFor(node)
                val engine = engine(radio)
                toCode(engine, radio)

                engine.submitCode()
                runCurrent()
                advanceTimeBy(31.seconds)
                runCurrent()

                val outcome = assertNotNull(engine.now.outcome)
                assertEquals(NodeOutcomeKind.LostConnection, outcome.kind)
                assertEquals(NodeOutcomeAction.StartOver, outcome.primary)
                assertTrue(engine.now.announcement!!.assertive)
                assertFalse(engine.now.code.accepted)
                assertTrue(radio.links.single().closed)
                assertTrue(posts.isEmpty())

                engine.outcomeAction(NodeOutcomeAction.StartOver)
                runCurrent()
                assertEquals(NodeSetupStep.Press, engine.now.step)
                engine.close()
            }
        }

    @Test
    fun uxDr94ADeviceThatIsNotANodeOrAnswersAnErrorRefusedTheSetup() =
        runTest {
            val hub = FakeNode()
            hub.kind = DeviceKind.DEVICE_KIND_HUB
            val erring = FakeNode()
            erring.refuseIdentity = true
            val other = FakeNode()
            other.responseDeviceId = HUB_DEVICE_ID
            for (node in listOf(hub, erring, other)) {
                val radio = radioFor(node)
                val engine = engine(radio)
                toCode(engine, radio)

                engine.submitCode()
                runCurrent()

                val outcome = assertNotNull(engine.now.outcome)
                assertEquals(NodeOutcomeKind.NodeRefused, outcome.kind)
                assertEquals(NodeOutcomeAction.Close, outcome.primary)
                assertTrue(engine.now.announcement!!.assertive)
                assertFalse(engine.now.code.accepted)
                assertTrue(radio.links.single().closed)
                engine.continueFromCode()
                assertEquals(NodeSetupStep.Code, engine.now.step)

                engine.outcomeAction(NodeOutcomeAction.Close)
                assertFalse(engine.now.open)
            }
            assertTrue(posts.isEmpty())
        }

    @Test
    fun uxDr94AnUnreadableEnrolmentKeyCannotReachTheServerAndDisconnects() =
        runTest {
            val node = FakeNode()
            val radio = radioFor(node)
            val engine = engine(radio)
            keyAnswer = { throw IllegalStateException("connection refused") }
            toCode(engine, radio)

            engine.submitCode()
            runCurrent()

            val outcome = assertNotNull(engine.now.outcome)
            assertEquals(NodeOutcomeKind.ServerUnreachable, outcome.kind)
            assertEquals(NodeOutcomeAction.StartOver, outcome.primary)
            assertTrue(engine.now.announcement!!.assertive)
            assertEquals(listOf("IdentityRequest"), node.received)
            assertTrue(radio.links.single().closed)
        }

    @Test
    fun uxDr94AFingerprintThatDoesNotCheckOutEnrolsNothing() =
        runTest {
            // The Server's key does not hash to its fingerprint.
            val node = FakeNode()
            val radio = radioFor(node)
            val engine = engine(radio)
            keyAnswer = {
                respond(
                    """{"publicKey":"${base64url.encode(SERVER_KEY)}","fingerprint":"${"0".repeat(64)}"}""",
                    HttpStatusCode.OK,
                    json,
                )
            }
            toCode(engine, radio)
            engine.submitCode()
            runCurrent()

            assertEquals(NodeOutcomeKind.FingerprintMismatch, engine.now.outcome?.kind)
            assertEquals(NodeOutcomeAction.Close, engine.now.outcome?.primary)
            assertFalse("EnrolmentRequest" in node.received)
            assertTrue(radio.links.single().closed)
            engine.close()

            // The Node says so itself.
            val refusing = FakeNode()
            refusing.refuseFingerprint = true
            val second = radioFor(refusing)
            val again = engine(second)
            keyAnswer = {
                respond(
                    """{"publicKey":"${base64url.encode(SERVER_KEY)}","fingerprint":"$SERVER_FINGERPRINT"}""",
                    HttpStatusCode.OK,
                    json,
                )
            }
            toCode(again, second)
            again.submitCode()
            runCurrent()

            assertEquals(NodeOutcomeKind.FingerprintMismatch, again.now.outcome?.kind)
            assertTrue(again.now.announcement!!.assertive)
            assertFalse(again.now.code.accepted)
            assertTrue(second.links.single().closed)
            assertTrue(posts.isEmpty())
        }

    @Test
    fun uxDr41TheSetupCodeIsNeverSentOrPrinted() =
        runTest {
            val node = FakeNode()
            val radio = radioFor(node)
            val engine = engine(radio)
            toLots(engine, radio)
            engine.chooseLot("lot-tomatoes")
            engine.assign()
            runCurrent()

            val written = radio.links.flatMap { it.writes }.joinToString("") { it.decodeToString() }
            assertFalse(written.contains("N4D3C0DE", ignoreCase = true))
            assertTrue(enrolBodies.none { it.contains("N4D3", ignoreCase = true) })
            assertTrue(requests.none { it.url.toString().contains("N4D3", ignoreCase = true) })
        }

    @Test
    fun theSetupCodeNeverAppearsInToString() =
        runTest {
            val radio = radioFor(FakeNode())
            val engine = engine(radio)
            toLots(engine, radio)

            for (text in listOf(engine.now.toString(), snapshotOf(engine.now).toString())) {
                assertFalse(text.contains("n4d3", ignoreCase = true), text)
                assertFalse(text.contains("c0de", ignoreCase = true), text)
            }
        }

    // Step 4: the Lot

    @Test
    fun uxDr38OnlyALotWithoutANodeCanBePickedAndTheTilesLotIsPreselected() =
        runTest {
            val radio = radioFor(FakeNode())
            val engine = engine(radio)
            toLots(engine, radio, lotId = "lot-peppers")

            assertEquals("lot-peppers", engine.now.lots.selectedId)
            assertEquals("Peppers", engine.now.lotName)

            engine.chooseLot("lot-beans")
            assertEquals("lot-peppers", engine.now.lots.selectedId)
            engine.chooseLot("lot-old")
            assertEquals("lot-peppers", engine.now.lots.selectedId)
            engine.chooseLot("lot-tomatoes")
            assertEquals("lot-tomatoes", engine.now.lots.selectedId)
        }

    @Test
    fun uxDr38ATilesLotThatHasANodeMeanwhileIsNotPreselected() =
        runTest {
            val radio = radioFor(FakeNode())
            val engine = engine(radio)
            toLots(engine, radio, lotId = "lot-beans")

            assertNull(engine.now.lots.selectedId)
            assertNull(engine.now.lots.selected)
            engine.assign()
            runCurrent()
            assertTrue(posts.isEmpty())
        }

    @Test
    fun uxDr38UnreadableLotsShowTryAgainAndKeepTheSealedKey() =
        runTest {
            val node = FakeNode()
            val radio = radioFor(node)
            val engine = engine(radio)
            lotsAnswers += { throw IllegalStateException("connection refused") }
            toLots(engine, radio)

            assertEquals(NodeSetupStep.Lot, engine.now.step)
            assertNull(engine.now.lots.choices)
            assertEquals(LotPickerNotice(LotPickerNoticeKind.Unreachable), engine.now.lots.notice)
            assertTrue(engine.now.lots.retryable)
            assertNull(engine.now.outcome)

            engine.retryLots()
            runCurrent()
            assertEquals(
                3,
                engine.now.lots.choices
                    ?.size,
            )
            assertNull(engine.now.lots.notice)

            // No second BLE session: the key from the first one is still held.
            engine.chooseLot("lot-tomatoes")
            engine.assign()
            runCurrent()
            assertEquals(NodeOutcomeKind.Assigned, engine.now.outcome?.kind)
            assertEquals(1, radio.connects)
            assertEquals(1, node.hellos)
        }

    @Test
    fun uxDr38NewLotCreatesItInlineReloadsTheListAndSelectsIt() =
        runTest {
            val radio = radioFor(FakeNode())
            val engine = engine(radio)
            toLots(engine, radio)

            engine.chooseLot("lot-tomatoes")
            engine.openNewLot()
            assertTrue(engine.now.lots.newLotOpen)
            // The field replaces the pick: only Create Lot is a primary action now.
            assertNull(engine.now.lots.selectedId)
            assertNull(engine.now.lots.selected)
            engine.createLot()
            assertEquals(NameError.Blank, engine.now.lots.newLotError)
            engine.setNewLotName("x".repeat(101))
            assertNull(engine.now.lots.newLotError)
            engine.createLot()
            assertEquals(NameError.TooLong, engine.now.lots.newLotError)
            assertTrue(createBodies.isEmpty())

            createAnswers += {
                lotsJson =
                    """{"lots":[{"id":"lot-tomatoes","name":"Tomatoes","status":"noNode"},""" +
                    """{"id":"lot-cucumbers","name":"Cucumbers","status":"noNode"}]}"""
                respond("""{"id":"lot-cucumbers","name":"Cucumbers","status":"noNode"}""", HttpStatusCode.Created, json)
            }
            engine.setNewLotName(" Cucumbers ")
            engine.createLot()
            assertTrue(engine.now.lots.creating)
            runCurrent()

            assertEquals("""{"name":"Cucumbers"}""", createBodies.single())
            val create = requests.single { it.method == HttpMethod.Post }
            assertEquals("https://coldframe.example.org/sites/site-home/lots", create.url.toString())
            assertEquals("key-1", create.headers[ColdframeApi.IDEMPOTENCY_KEY])
            assertFalse(engine.now.lots.creating)
            assertFalse(engine.now.lots.newLotOpen)
            assertEquals(
                listOf("Tomatoes", "Cucumbers"),
                engine.now.lots.choices
                    ?.map { it.name },
            )
            assertEquals("lot-cucumbers", engine.now.lots.selectedId)
            assertEquals("Cucumbers", engine.now.lotName)
        }

    @Test
    fun uxDr38ANewLotThatCouldNotBeCreatedKeepsTheFieldAndItsKey() =
        runTest {
            val radio = radioFor(FakeNode())
            val engine = engine(radio)
            toLots(engine, radio)
            engine.openNewLot()
            engine.setNewLotName("Cucumbers")

            createAnswers += { throw IllegalStateException("connection refused") }
            engine.createLot()
            runCurrent()
            assertEquals(LotPickerNotice(LotPickerNoticeKind.Unreachable), engine.now.lots.notice)
            assertFalse(engine.now.lots.retryable)
            assertTrue(engine.now.lots.newLotOpen)
            assertEquals("Cucumbers", engine.now.lots.newLotName)
            assertEquals(
                3,
                engine.now.lots.choices
                    ?.size,
            )

            createAnswers += refusal(HttpStatusCode.BadRequest, "validation")
            engine.createLot()
            runCurrent()
            assertEquals(NameError.Blank, engine.now.lots.newLotError)
            assertNull(engine.now.lots.notice)

            val sent = requests.filter { it.method == HttpMethod.Post }.map { it.headers[ColdframeApi.IDEMPOTENCY_KEY] }
            assertEquals(listOf("key-1", "key-1"), sent)
        }

    @Test
    fun uxDr38ASecondNewLotAndACreateAfterA422SendAFreshKeyAndA422ReadsTheLotsAgain() =
        runTest {
            val radio = radioFor(FakeNode())
            val engine = engine(radio)
            toLots(engine, radio)
            val made: (String) -> MockRequestHandleScope.() -> HttpResponseData = { name ->
                { respond("""{"id":"lot-$name","name":"$name","status":"noNode"}""", HttpStatusCode.Created, json) }
            }

            fun createKeys() =
                requests
                    .filter { it.method == HttpMethod.Post && it.url.encodedPath.endsWith("/lots") }
                    .map { it.headers[ColdframeApi.IDEMPOTENCY_KEY] }

            fun lotReads() = requests.count { it.method == HttpMethod.Get && it.url.encodedPath.endsWith("/lots") }

            createAnswers += made("Cucumbers")
            engine.openNewLot()
            engine.setNewLotName("Cucumbers")
            engine.createLot()
            runCurrent()
            createAnswers += made("Herbs")
            engine.openNewLot()
            engine.setNewLotName("Herbs")
            engine.createLot()
            runCurrent()
            assertEquals(listOf("key-1", "key-2"), createKeys())

            // A 422: the key was used before, perhaps for a Lot the picker does not show.
            createAnswers += refusal(HttpStatusCode.UnprocessableEntity, "idempotency-key-reused")
            lotsJson = """{"lots":[{"id":"lot-leeks","name":"Leeks","status":"noNode"}]}"""
            val reads = lotReads()
            engine.openNewLot()
            engine.setNewLotName("Leeks")
            engine.createLot()
            runCurrent()
            assertEquals(LotPickerNotice(LotPickerNoticeKind.Unexpected), engine.now.lots.notice)
            assertEquals(reads + 1, lotReads())
            assertEquals(
                listOf("Leeks"),
                engine.now.lots.choices
                    ?.map { it.name },
            )
            assertTrue(engine.now.lots.newLotOpen)

            createAnswers += made("Leeks")
            engine.createLot()
            runCurrent()
            assertEquals(listOf("key-1", "key-2", "key-3", "key-4"), createKeys())
        }

    @Test
    fun uxDr67ALotsReadThatLandsDuringOrAfterTheAssignmentKeepsTheLotsNameOnTheOutcome() =
        runTest {
            val radio = radioFor(FakeNode())
            val engine = engine(radio)
            toLots(engine, radio)
            // The create's reload is still on its way when the user assigns the new Lot.
            val gate = CompletableDeferred<Unit>()
            createAnswers += {
                respond("""{"id":"lot-cucumbers","name":"Cucumbers","status":"noNode"}""", HttpStatusCode.Created, json)
            }
            // By the time it is answered the Server lists the Lot with its Node.
            lotsGate = gate
            lotsAnswers += {
                respond(
                    """{"lots":[{"id":"lot-cucumbers","name":"Cucumbers","status":"needsCalibration"}]}""",
                    HttpStatusCode.OK,
                    json,
                )
            }
            engine.openNewLot()
            engine.setNewLotName("Cucumbers")
            engine.createLot()
            runCurrent()
            assertEquals("lot-cucumbers", engine.now.lots.selectedId)

            engine.assign()
            runCurrent()
            assertEquals(NodeOutcomeKind.Assigned, engine.now.outcome?.kind)
            gate.complete(Unit)
            runCurrent()

            assertEquals("Cucumbers", engine.now.lotName)
            assertEquals("lot-cucumbers", engine.now.lots.selectedId)
        }

    @Test
    fun uxDr94ALotTakenMeanwhileStaysOnStep4AndAnotherLotNeedsNoNewBleSession() =
        runTest {
            val node = FakeNode()
            val radio = radioFor(node)
            val engine = engine(radio)
            toLots(engine, radio)
            engine.chooseLot("lot-tomatoes")
            enrolAnswers += refusal(HttpStatusCode.Conflict, "lot-claimed")
            lotsJson =
                """{"lots":[{"id":"lot-tomatoes","name":"Tomatoes","status":"needsCalibration"},""" +
                """{"id":"lot-peppers","name":"Peppers","status":"noNode"}]}"""

            engine.assign()
            runCurrent()

            assertEquals(NodeSetupStep.Lot, engine.now.step)
            assertNull(engine.now.outcome)
            assertEquals(LotPickerNotice(LotPickerNoticeKind.LotTaken, "Tomatoes"), engine.now.lots.notice)
            assertNull(engine.now.lots.selectedId)
            assertFalse(engine.now.lots.assigning)
            // The list was read again: Tomatoes now has a Node.
            assertEquals(
                listOf(LotChoice("lot-tomatoes", "Tomatoes", false), LotChoice("lot-peppers", "Peppers", true)),
                engine.now.lots.choices,
            )
            val announcement = assertNotNull(engine.now.announcement)
            assertEquals(NodeAnnouncementKind.LotTaken, announcement.kind)
            assertTrue(announcement.assertive)
            assertEquals("Tomatoes", announcement.lot)
            assertEquals(0, assigned)

            engine.chooseLot("lot-peppers")
            assertNull(engine.now.lots.notice)
            engine.assign()
            runCurrent()

            assertEquals(NodeOutcomeKind.Assigned, engine.now.outcome?.kind)
            assertEquals("Peppers", engine.now.lotName)
            assertEquals(1, radio.connects)
            assertEquals(1, node.hellos)
            val lotsSent =
                enrolBodies.map {
                    Json
                        .parseToJsonElement(it)
                        .jsonObject["lotId"]!!
                        .jsonPrimitive.content
                }
            assertEquals(listOf("lot-tomatoes", "lot-peppers"), lotsSent)
            assertEquals(listOf("key-1", "key-1"), posts.map { it.headers[ColdframeApi.IDEMPOTENCY_KEY] })
        }

    @Test
    fun uxDr94ALotThatIsGoneStaysOnStep4AndTheListReloads() =
        runTest {
            val radio = radioFor(FakeNode())
            val engine = engine(radio)
            toLots(engine, radio)
            engine.chooseLot("lot-tomatoes")
            enrolAnswers += refusal(HttpStatusCode.NotFound, "lot-not-found")
            lotsJson = """{"lots":[{"id":"lot-peppers","name":"Peppers","status":"noNode"}]}"""

            engine.assign()
            runCurrent()

            assertEquals(NodeSetupStep.Lot, engine.now.step)
            assertNull(engine.now.outcome)
            assertEquals(LotPickerNotice(LotPickerNoticeKind.LotGone), engine.now.lots.notice)
            assertNull(engine.now.lots.selectedId)
            assertEquals(
                listOf("Peppers"),
                engine.now.lots.choices
                    ?.map { it.name },
            )
            assertEquals(0, assigned)
        }

    @Test
    fun uxDr94AnUnreachableServerOnAssignStaysOnStep4AndTheButtonRetriesWithTheSameKey() =
        runTest {
            val node = FakeNode()
            val radio = radioFor(node)
            val engine = engine(radio)
            toLots(engine, radio)
            engine.chooseLot("lot-tomatoes")
            enrolAnswers += { throw IllegalStateException("connection refused") }
            enrolAnswers += refusal(HttpStatusCode.InternalServerError, "internal")
            enrolAnswers += refusal(HttpStatusCode.UnprocessableEntity, "idempotency-key-reused")
            enrolAnswers += refusal(HttpStatusCode.BadRequest, "validation")

            repeat(4) {
                engine.assign()
                runCurrent()

                assertEquals(NodeSetupStep.Lot, engine.now.step)
                assertNull(engine.now.outcome)
                assertEquals(LotPickerNotice(LotPickerNoticeKind.AssignFailed), engine.now.lots.notice)
                assertEquals("lot-tomatoes", engine.now.lots.selectedId)
                assertFalse(engine.now.lots.assigning)
                assertEquals(0, assigned)
            }

            engine.assign()
            runCurrent()

            assertEquals(NodeOutcomeKind.Assigned, engine.now.outcome?.kind)
            assertEquals(List(5) { "key-1" }, posts.map { it.headers[ColdframeApi.IDEMPOTENCY_KEY] })
            assertEquals(1, enrolBodies.toSet().size)
            assertEquals(1, node.hellos)
        }

    @Test
    fun uxDr94ANodeAlreadyAssignedOnAnotherSiteOrNotAllowedEndsOnItsOutcome() =
        runTest {
            for ((answer, expected) in listOf(
                refusal(HttpStatusCode.Conflict, "device-assigned") to NodeOutcomeKind.AlreadyAssigned,
                refusal(HttpStatusCode.Conflict, "device-on-another-site") to NodeOutcomeKind.OnAnotherSite,
                refusal(HttpStatusCode.Forbidden, "forbidden") to NodeOutcomeKind.NotAllowed,
            )) {
                val radio = radioFor(FakeNode())
                val engine = engine(radio)
                toLots(engine, radio)
                engine.chooseLot("lot-tomatoes")
                enrolAnswers += answer

                engine.assign()
                runCurrent()

                val outcome = assertNotNull(engine.now.outcome, "$expected")
                assertEquals(expected, outcome.kind)
                assertEquals(4, outcome.stoppedStep)
                assertEquals(NodeOutcomeAction.Close, outcome.primary)
                val announcement = assertNotNull(engine.now.announcement)
                assertEquals(NodeAnnouncementKind.Error, announcement.kind)
                assertTrue(announcement.assertive)
                assertEquals("Tomatoes", announcement.lot)
                assertEquals(0, assigned)

                engine.outcomeAction(NodeOutcomeAction.StartOver)
                assertTrue(engine.now.open)
                engine.outcomeAction(NodeOutcomeAction.Close)
                assertFalse(engine.now.open)
            }
        }

    // Leaving and navigation

    @Test
    fun uxDr39BackOnStep4AsksAndConfirmingClosesWithNothingSentToTheServer() =
        runTest {
            val radio = radioFor(FakeNode())
            val engine = engine(radio)
            toLots(engine, radio)
            engine.chooseLot("lot-tomatoes")

            engine.back()
            assertTrue(engine.now.confirmingLeave)
            assertEquals(NodeSetupStep.Lot, engine.now.step)
            engine.stayInFlow()
            assertFalse(engine.now.confirmingLeave)

            engine.back()
            engine.confirmLeave()
            runCurrent()

            assertFalse(engine.now.open)
            assertTrue(posts.isEmpty())
            assertTrue(radio.links.all { it.closed })
            assertEquals(0, assigned)
        }

    @Test
    fun uxDr39SystemBackAsksOnceANodeIsSelectedAndGoesBackBeforeThat() =
        runTest {
            val radio = radioFor(FakeNode())
            val engine = engine(radio)
            engine.open()
            runCurrent()
            engine.continueFromPress()
            runCurrent()

            engine.leave()
            assertEquals(NodeSetupStep.Press, engine.now.step)
            assertFalse(engine.now.confirmingLeave)

            engine.continueFromPress()
            runCurrent()
            advert(radio)
            engine.select("peripheral-1")
            engine.leave()
            assertTrue(engine.now.confirmingLeave)
            engine.back()
            assertEquals(NodeSetupStep.Scan, engine.now.step)
            engine.confirmLeave()
            assertFalse(engine.now.open)

            engine.open()
            runCurrent()
            engine.leave()
            assertFalse(engine.now.open)
        }

    @Test
    fun uxDr39CancelOnStep1ClosesAtOnce() =
        runTest {
            val engine = engine(FakeSetupRadio())
            engine.open()
            runCurrent()

            engine.back()

            assertFalse(engine.now.open)
        }

    @Test
    fun uxDr39BackGoesOneStepBackAndLeavingStep3DisconnectsAndRescans() =
        runTest {
            val node = FakeNode()
            val radio = radioFor(node)
            val engine = engine(radio)
            toCode(engine, radio)
            engine.submitCode()
            runCurrent()
            assertTrue(engine.now.code.accepted)

            engine.back()
            runCurrent()
            assertEquals(NodeSetupStep.Scan, engine.now.step)
            assertFalse(engine.now.confirmingLeave)
            assertFalse(engine.now.code.accepted)
            assertNull(engine.now.selected)
            assertNull(engine.now.deviceId)
            assertTrue(engine.now.candidates.isEmpty())
            assertTrue(radio.links.all { it.closed })
            assertEquals(2, radio.scans)
            // The sealed key of the session that was left is gone.
            engine.continueFromCode()
            assertEquals(NodeSetupStep.Scan, engine.now.step)

            // The same Node is listed again, with its badge, and is not announced twice.
            val heard = engine.now.announcement
            advert(radio)
            assertEquals(listOf(true), engine.now.candidates.map { it.pressedJustNow })
            assertEquals(heard, engine.now.announcement)

            engine.back()
            assertEquals(NodeSetupStep.Press, engine.now.step)
            assertTrue(engine.now.candidates.isEmpty())
            assertTrue(engine.now.showsCancel)
        }

    @Test
    fun uxDr39AnAssignmentUnderWayIsNotLeft() =
        runTest {
            val radio = radioFor(FakeNode())
            val engine = engine(radio)
            toLots(engine, radio)
            engine.chooseLot("lot-tomatoes")

            engine.assign()
            engine.back()
            engine.leave()
            assertFalse(engine.now.confirmingLeave)
            runCurrent()

            assertEquals(NodeOutcomeKind.Assigned, engine.now.outcome?.kind)
            engine.leave()
            assertFalse(engine.now.open)
        }

    @Test
    fun signingOutClosesTheFlow() =
        runTest {
            val radio = radioFor(FakeNode())
            val engine = engine(radio)
            toLots(engine, radio)

            sites.value = SitesState.Idle
            runCurrent()

            assertFalse(engine.now.open)
            assertEquals(NodeSetupState.CLOSED, engine.now)
        }

    @Test
    fun a401OnAnyServerCallClosesTheFlow() =
        runTest {
            val unauthorized: MockRequestHandleScope.() -> HttpResponseData = {
                respond(
                    "",
                    HttpStatusCode.Unauthorized,
                )
            }

            // The enrolment key.
            var radio = radioFor(FakeNode())
            var engine = engine(radio)
            keyAnswer = unauthorized
            toCode(engine, radio)
            engine.submitCode()
            runCurrent()
            assertFalse(engine.now.open)
            assertTrue(radio.links.single().closed)
            keyAnswer = {
                respond(
                    """{"publicKey":"${base64url.encode(SERVER_KEY)}","fingerprint":"$SERVER_FINGERPRINT"}""",
                    HttpStatusCode.OK,
                    json,
                )
            }

            // The Lots.
            radio = radioFor(FakeNode())
            engine = engine(radio)
            lotsAnswers += unauthorized
            toLots(engine, radio)
            assertFalse(engine.now.open)

            // The new Lot.
            radio = radioFor(FakeNode())
            engine = engine(radio)
            toLots(engine, radio)
            engine.openNewLot()
            engine.setNewLotName("Cucumbers")
            createAnswers += unauthorized
            engine.createLot()
            runCurrent()
            assertFalse(engine.now.open)

            // The assignment.
            radio = radioFor(FakeNode())
            engine = engine(radio)
            toLots(engine, radio)
            engine.chooseLot("lot-tomatoes")
            enrolAnswers += unauthorized
            engine.assign()
            runCurrent()
            assertFalse(engine.now.open)
            assertEquals(0, assigned)
        }
}
