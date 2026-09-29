package com.escendit.coldframe.core.sites

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class FirstRunStepsTest {
    @Test
    fun uxDr54FourStepsWithAddAHubNextAndTheRestLater() {
        val steps = FirstRunSteps.of(SiteRole.Owner)

        assertEquals(
            listOf(
                FirstRunTile(FirstRunStep.AddHub, 1, StepState.Next),
                FirstRunTile(FirstRunStep.AddNode, 2, StepState.Later),
                FirstRunTile(FirstRunStep.Calibrate, 3, StepState.Later),
                FirstRunTile(FirstRunStep.SetLowThreshold, 4, StepState.Later),
            ),
            steps.tiles,
        )
    }

    @Test
    fun uxDr66TheAddAHubTileStartsTheFlowForAdministratorsAndOwnersOnMobile() {
        assertTrue(FirstRunSteps.of(SiteRole.Owner).actionable)
        assertTrue(FirstRunSteps.of(SiteRole.Administrator).actionable)
        assertFalse(FirstRunSteps.of(SiteRole.Member).actionable)
        assertEquals(
            FirstRunStep.AddHub,
            FirstRunSteps
                .of(SiteRole.Owner)
                .tiles
                .first { it.state == StepState.Next }
                .step,
        )
    }

    @Test
    fun uxDr54WithoutAFlowNoTileActs() {
        assertFalse(FirstRunSteps.of(SiteRole.Owner, flowAvailable = false).actionable)
    }

    @Test
    fun uxDr54OnceTheFlowExistsOnlyAdministratorsAndOwnersCanAct() {
        assertTrue(FirstRunSteps.of(SiteRole.Owner, flowAvailable = true).actionable)
        assertTrue(FirstRunSteps.of(SiteRole.Administrator, flowAvailable = true).actionable)
        assertFalse(FirstRunSteps.of(SiteRole.Member, flowAvailable = true).actionable)
    }

    @Test
    fun uxDr54OnlyMembersGetTheReadOnlyNotice() {
        assertTrue(FirstRunSteps.of(SiteRole.Member).memberNotice)
        assertFalse(FirstRunSteps.of(SiteRole.Administrator).memberNotice)
        assertFalse(FirstRunSteps.of(SiteRole.Owner).memberNotice)
    }

    @Test
    fun rolesAreOrderedOwnerOverAdministratorOverMember() {
        assertTrue(SiteRole.Owner > SiteRole.Administrator)
        assertTrue(SiteRole.Administrator > SiteRole.Member)
        assertEquals(SiteRole.Member, SiteRole.fromServer("Unknown"))
        assertEquals(SiteRole.Administrator, SiteRole.fromServer("Administrator"))
    }
}
