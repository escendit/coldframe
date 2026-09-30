package com.escendit.coldframe.core.setup

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

/** The raw values Swift's `HubSetupPresentation` parses; renaming one breaks the iOS shell. */
class HubSetupSnapshotTest {
    private val open =
        HubSetupState.CLOSED.copy(
            open = true,
            step = SetupStep.Wifi,
            radio = RadioState.Unauthorized,
            candidates = listOf(HubCandidate("p-1", "3F2A", -50), HubCandidate("p-2", "11C0", -90)),
            selected = HubCandidate("p-1", "3F2A", -50),
            code = CodeForm("k7m2", CodeError.WrongCode, working = false, accepted = true),
            identity = HubIdentity("3f2a9c01b2d4e6f8", "0.1.0"),
            wifi =
                WifiForm(
                    networks =
                        listOf(
                            WifiNetworkRow("Novak-Home", NetworkSecurity.Wpa2, -40),
                            WifiNetworkRow("Guest", NetworkSecurity.Open, -60),
                            WifiNetworkRow("Mixed", NetworkSecurity.Wpa3Transition, -61),
                            WifiNetworkRow("Neighbour", NetworkSecurity.Wpa3Only, -70),
                            WifiNetworkRow("Office", NetworkSecurity.Other, -80),
                        ),
                    ssid = "Novak-Home",
                    other = false,
                    password = "pw",
                    error = WifiError.PasswordTooLong,
                ),
            site = SiteForm(listOf(SiteChoice("s", "Home")), "s", "ab".repeat(32), false, KeyNotice.Certificate),
            progress = ProgressState(2, 17),
            outcome = SetupOutcome(OutcomeKind.NoServer),
            announcement =
                SetupAnnouncement(7, AnnouncementKind.CandidateFound, false, "3F2A", null, SignalStrength.Strong, null),
        )

    @Test
    fun uxDr66TheSnapshotCarriesTheSwiftRawValues() {
        val snapshot = snapshotOf(open)

        assertTrue(snapshot.open)
        assertEquals(3, snapshot.step)
        assertTrue(snapshot.keepAwake)
        assertFalse(snapshot.showsCancel)
        assertEquals("unauthorized", snapshot.radio)
        assertEquals(listOf("p-1", "p-2"), snapshot.candidateIds)
        assertEquals(listOf("3F2A", "11C0"), snapshot.candidateNames)
        assertEquals(listOf("strong", "weak"), snapshot.candidateSignals)
        assertEquals("3F2A", snapshot.hub)
        assertEquals("wrongCode", snapshot.codeError)
        assertTrue(snapshot.codeAccepted)
        assertEquals("3f2a9c01b2d4e6f8", snapshot.deviceId)
        assertEquals(listOf("wpa2", "open", "wpa3Transition", "wpa3Only", "other"), snapshot.networkSecurities)
        assertEquals(listOf(true, true, true, false, false), snapshot.networkSupported)
        assertEquals("passwordTooLong", snapshot.wifiError)
        assertEquals("certificate", snapshot.keyNotice)
        assertEquals(2, snapshot.progressReached)
        assertEquals(17, snapshot.elapsedSeconds)
        assertEquals("noServer", snapshot.outcome)
        assertEquals("retryWifi", snapshot.outcomePrimary)
        assertEquals("help", snapshot.outcomeSecondary)
        assertEquals(7, snapshot.announcementId)
        assertEquals("candidateFound", snapshot.announcementKind)
        assertEquals("strong", snapshot.announcementSignal)
    }

    @Test
    fun uxDr55EveryOutcomeAndActionHasAStableKey() {
        val kinds = OutcomeKind.entries.map { snapshotOf(open.copy(outcome = SetupOutcome(it))).outcome }
        assertEquals(
            listOf(
                "online",
                "wrongPassword",
                "networkNotFound",
                "unsupportedSecurity",
                "noServer",
                "timeout",
                "lostConnection",
                "onAnotherSite",
                "notAllowed",
                "siteGone",
                "serverUnreachable",
                "fingerprintMismatch",
                "hubRefused",
            ),
            kinds,
        )
        assertEquals(
            listOf(
                "addNode",
                "reenterPassword",
                "otherNetwork",
                "chooseNetwork",
                "retryWifi",
                "help",
                "startOver",
                "close",
            ),
            OutcomeAction.entries.map { it.name.replaceFirstChar { c -> c.lowercase() } },
        )
        assertEquals(
            listOf("candidateFound", "wifiSent", "serverSees", "wrongCode", "error"),
            AnnouncementKind.entries.map { it.name.replaceFirstChar { c -> c.lowercase() } },
        )
    }

    @Test
    fun theClosedFlowIsEmpty() {
        val snapshot = snapshotOf(HubSetupState.CLOSED)
        assertFalse(snapshot.open)
        assertFalse(snapshot.keepAwake)
        assertEquals(0, snapshot.announcementId)
    }

    @Test
    fun uxDr55EveryOutcomeActionKeyParsesBackToItself() {
        val sent =
            OutcomeKind.entries.flatMap { kind ->
                val outcome = SetupOutcome(kind)
                val snapshot = snapshotOf(open.copy(outcome = outcome))
                listOfNotNull(
                    outcome.primary to snapshot.outcomePrimary,
                    outcome.secondary?.let {
                        it to
                            snapshot.outcomeSecondary
                    },
                )
            }
        assertEquals(OutcomeAction.entries.toSet(), sent.map { it.first }.toSet())
        for ((action, key) in sent) assertEquals(action, outcomeActionOf(key!!), key)
        assertEquals(null, outcomeActionOf("unknown"))
    }
}
