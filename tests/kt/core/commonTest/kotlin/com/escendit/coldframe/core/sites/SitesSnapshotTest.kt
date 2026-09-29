package com.escendit.coldframe.core.sites

import com.russhwolf.settings.MapSettings
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

class SitesSnapshotTest {
    private val form =
        CreateSiteForm(
            name = "Home",
            nameError = NameError.TooLong,
            working = false,
            notice = SitesNotice.KeyReused,
            idempotencyKey = "secret-key",
            timeZone = TimeZoneProposal("Europe/Zurich", null, true),
            cancellable = true,
        )

    @Test
    fun readyFlattensTheSitesInTheServerOrderAndTheGarden() {
        val sites = listOf(SiteSummary("b", "Allotment", SiteRole.Member), SiteSummary("a", "Home", SiteRole.Owner))

        val snapshot = snapshotOf(SitesState.Ready(sites, sites[0], creating = form))

        assertEquals("ready", snapshot.surface)
        assertEquals(listOf("b", "a"), snapshot.siteIds)
        assertEquals(listOf("Allotment", "Home"), snapshot.siteNames)
        assertEquals(listOf("member", "owner"), snapshot.siteRoles)
        assertEquals("b", snapshot.currentId)
        assertEquals("member", snapshot.currentRole)
        assertEquals(listOf("addHub", "addNode", "calibrate", "setLowThreshold"), snapshot.steps)
        assertEquals(listOf("next", "later", "later", "later"), snapshot.stepStates)
        assertFalse(snapshot.stepsActionable)
        assertTrue(snapshot.memberNotice)
        assertEquals(listOf("siteSettings"), snapshot.menuItems)
        assertTrue(snapshot.menuEnabled)
        assertTrue(snapshot.formShown)
        assertEquals("tooLong", snapshot.formNameError)
        assertEquals("keyReused", snapshot.formNotice)
        assertTrue(snapshot.formNoticeTryAgain)
        assertTrue(snapshot.timeZoneChanging)
    }

    @Test
    fun theKeyNeverCrossesTheBoundary() {
        val snapshot = snapshotOf(SitesState.NeedsSite(form))

        assertFalse(snapshot.toString().contains("secret-key"))
        assertEquals("needsSite", snapshot.surface)
        assertTrue(snapshot.siteIds.isEmpty())
        assertTrue(snapshot.steps.isEmpty())
    }

    @Test
    fun failedCarriesItsNotice() {
        val snapshot = snapshotOf(SitesState.Failed(SitesNotice.Certificate))

        assertEquals("failed", snapshot.surface)
        assertEquals("certificate", snapshot.notice)
        assertFalse(snapshot.noticeTryAgain)
        assertFalse(snapshot.formShown)
        assertNull(snapshot.currentId)
    }

    @Test
    fun deviceChoicesPersistAndClear() {
        val settings = MapSettings()
        val choices = DeviceChoices(settings)
        choices.currentSiteId = "a"
        choices.timeZone = "Europe/Zurich"

        assertEquals("a", DeviceChoices(settings).currentSiteId)
        assertEquals("Europe/Zurich", DeviceChoices(settings).timeZone)
        choices.currentSiteId = null
        assertNull(DeviceChoices(settings).currentSiteId)
    }

    @Test
    fun everyKeyIsARawValueTheSwiftEnumsAccept() {
        // SitesNoticeKind, NameErrorKind, SiteRoleKind, SiteMenuItem, FirstRunStepKind, StepStateKind.
        assertEquals(
            listOf("unreachable", "certificate", "identityProviderUnavailable", "keyReused", "unexpected"),
            SitesNotice.entries.map { it.key() },
        )
        assertEquals(listOf("blank", "tooLong"), NameError.entries.map { it.key() })
        assertEquals(listOf("member", "administrator", "owner"), SiteRole.entries.map { it.key() })
        assertEquals(listOf("pause", "resume", "siteSettings"), SiteMenuAction.entries.map { it.key() })
        assertEquals(
            listOf("addHub", "addNode", "calibrate", "setLowThreshold"),
            FirstRunStep.entries.map { it.key() },
        )
        assertEquals(listOf("next", "later", "done"), StepState.entries.map { it.key() })
    }
}
