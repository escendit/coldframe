package com.escendit.coldframe.core.setup

import com.escendit.coldframe.core.sites.NameError
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

/** The raw values Swift's `NodeSetupPresentation` parses; renaming one breaks the iOS shell. */
class NodeSetupSnapshotTest {
    private val open =
        NodeSetupState.CLOSED.copy(
            open = true,
            step = NodeSetupStep.Lot,
            radio = RadioState.Unauthorized,
            candidates = listOf(NodeCandidate("p-1", "7C19", -50, true), NodeCandidate("p-2", "11C0", -90, false)),
            selected = NodeCandidate("p-1", "7C19", -50, true),
            code = CodeForm("n4d3", CodeError.WrongCode, working = false, accepted = true),
            deviceId = "7c19aa01b2d4e6f8",
            siteId = "s",
            siteName = "Home garden",
            lots =
                LotForm(
                    choices = listOf(LotChoice("l-1", "Tomatoes", true), LotChoice("l-2", "Beans", false)),
                    selectedId = "l-1",
                    newLotOpen = true,
                    newLotName = "Cucumbers",
                    newLotError = NameError.TooLong,
                    creating = true,
                    assigning = false,
                    notice = LotPickerNotice(LotPickerNoticeKind.LotTaken, "Beans"),
                ),
            outcome = NodeOutcome(NodeOutcomeKind.StoppedListening, 3),
            announcement =
                NodeAnnouncement(
                    7,
                    NodeAnnouncementKind.CandidateFound,
                    false,
                    "7C19",
                    null,
                    SignalStrength.Strong,
                    null,
                ),
        )

    @Test
    fun uxDr67TheSnapshotCarriesTheSwiftRawValues() {
        val snapshot = snapshotOf(open)

        assertTrue(snapshot.open)
        assertEquals(4, snapshot.step)
        assertTrue(snapshot.keepAwake)
        assertFalse(snapshot.showsCancel)
        assertEquals("unauthorized", snapshot.radio)
        assertEquals(listOf("p-1", "p-2"), snapshot.candidateIds)
        assertEquals(listOf("7C19", "11C0"), snapshot.candidateNames)
        assertEquals(listOf("strong", "weak"), snapshot.candidateSignals)
        assertEquals(listOf(true, false), snapshot.candidatePressed)
        assertEquals("p-1", snapshot.selectedId)
        assertEquals("7C19", snapshot.node)
        assertEquals("wrongCode", snapshot.codeError)
        assertTrue(snapshot.codeAccepted)
        assertEquals("7c19aa01b2d4e6f8", snapshot.deviceId)
        assertEquals("Home garden", snapshot.siteName)
        assertTrue(snapshot.lotsLoaded)
        assertEquals(listOf("l-1", "l-2"), snapshot.lotIds)
        assertEquals(listOf("Tomatoes", "Beans"), snapshot.lotNames)
        assertEquals(listOf(true, false), snapshot.lotSelectable)
        assertEquals("l-1", snapshot.selectedLotId)
        assertEquals("Tomatoes", snapshot.lotName)
        assertTrue(snapshot.newLotOpen)
        assertEquals("Cucumbers", snapshot.newLotName)
        assertEquals("tooLong", snapshot.newLotError)
        assertTrue(snapshot.creatingLot)
        assertFalse(snapshot.assigning)
        assertEquals("lotTaken", snapshot.lotNotice)
        assertEquals("Beans", snapshot.lotNoticeLot)
        assertFalse(snapshot.lotsRetryable)
        assertEquals("stoppedListening", snapshot.outcome)
        assertEquals("startOver", snapshot.outcomePrimary)
        assertEquals(3, snapshot.stoppedStep)
        assertEquals(7, snapshot.announcementId)
        assertEquals("candidateFound", snapshot.announcementKind)
        assertEquals("7C19", snapshot.announcementNode)
        assertEquals("strong", snapshot.announcementSignal)
    }

    @Test
    fun uxDr67EveryStepOutcomeNoticeAndAnnouncementHasAStableKey() {
        assertEquals(listOf(1, 2, 3, 4, 5), NodeSetupStep.entries.map { snapshotOf(open.copy(step = it)).step })
        assertEquals(
            listOf(
                "assigned",
                "stoppedListening",
                "lostConnection",
                "nodeRefused",
                "serverUnreachable",
                "fingerprintMismatch",
                "alreadyAssigned",
                "onAnotherSite",
                "notAllowed",
            ),
            NodeOutcomeKind.entries.map { snapshotOf(open.copy(outcome = NodeOutcome(it, 4))).outcome },
        )
        assertEquals(
            listOf("unreachable", "certificate", "unexpected", "lotTaken", "lotGone", "assignFailed"),
            LotPickerNoticeKind.entries.map {
                snapshotOf(open.copy(lots = open.lots.copy(notice = LotPickerNotice(it)))).lotNotice
            },
        )
        assertEquals(
            listOf("candidateFound", "wrongCode", "lotTaken", "error", "assigned"),
            NodeAnnouncementKind.entries.map {
                snapshotOf(open.copy(announcement = open.announcement!!.copy(kind = it))).announcementKind
            },
        )
        assertEquals(
            listOf("blank", "tooLong"),
            NameError.entries.map { snapshotOf(open.copy(lots = open.lots.copy(newLotError = it))).newLotError },
        )
    }

    @Test
    fun uxDr55EveryOutcomeActionKeyParsesBackToItself() {
        val sent =
            NodeOutcomeKind.entries.map { kind ->
                val outcome = NodeOutcome(kind, 3)
                outcome.primary to snapshotOf(open.copy(outcome = outcome)).outcomePrimary
            }
        // Calibrate is only ever a secondary action (Story 5.2).
        assertEquals((NodeOutcomeAction.entries - NodeOutcomeAction.Calibrate).toSet(), sent.map { it.first }.toSet())
        assertEquals(setOf("done", "startOver", "close"), sent.map { it.second }.toSet())
        for ((action, key) in sent) assertEquals(action, nodeOutcomeActionOf(key!!), key)
        assertNull(nodeOutcomeActionOf("unknown"))
    }

    @Test
    fun story52ASuccessTheCallerMayCalibrateOffersCalibrateAsItsSecondaryAction() {
        val success = NodeOutcome(NodeOutcomeKind.Assigned, 5, calibrateLotId = "t")

        assertEquals("done", snapshotOf(open.copy(outcome = success)).outcomePrimary)
        assertEquals("calibrate", snapshotOf(open.copy(outcome = success)).outcomeSecondary)
        assertEquals(NodeOutcomeAction.Calibrate, nodeOutcomeActionOf("calibrate"))
        // No Role, no secondary; no error outcome has one.
        assertNull(snapshotOf(open.copy(outcome = NodeOutcome(NodeOutcomeKind.Assigned, 5))).outcomeSecondary)
        assertNull(
            snapshotOf(
                open.copy(outcome = NodeOutcome(NodeOutcomeKind.NodeRefused, 3, calibrateLotId = "t")),
            ).outcomeSecondary,
        )
    }

    @Test
    fun uxDr38UnreadLotsAreRetryableExceptForACertificate() {
        val unread = open.copy(lots = LotForm.EMPTY.copy(notice = LotPickerNotice(LotPickerNoticeKind.Unreachable)))
        assertFalse(snapshotOf(unread).lotsLoaded)
        assertTrue(snapshotOf(unread).lotsRetryable)
        val certificate =
            open.copy(
                lots = LotForm.EMPTY.copy(notice = LotPickerNotice(LotPickerNoticeKind.Certificate)),
            )
        assertFalse(snapshotOf(certificate).lotsRetryable)
    }

    @Test
    fun theClosedFlowIsEmpty() {
        val snapshot = snapshotOf(NodeSetupState.CLOSED)
        assertFalse(snapshot.open)
        assertFalse(snapshot.keepAwake)
        assertEquals(0, snapshot.announcementId)
        assertEquals(0, snapshot.stoppedStep)
        assertNull(snapshot.outcome)
    }
}
