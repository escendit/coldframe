package com.escendit.coldframe.core.push

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

/** Story 6.5: the flat snapshot Swift observes. */
class PushSnapshotTest {
    @Test
    fun uxDr88ADeniedPermissionShowsTheNotice() {
        val snapshot = snapshotOf(PushState(PushPermission.Denied, promptDue = false, route = null))

        assertEquals(
            PushSnapshot(
                permission = "denied",
                noticeVisible = true,
                promptDue = false,
                routeTarget = null,
                routeSiteId = null,
                routeLotId = null,
                routeLotName = null,
            ),
            snapshot,
        )
    }

    @Test
    fun uxDr122ThePromptIsDueWhileThePermissionIsUnknown() {
        val snapshot = snapshotOf(PushState(PushPermission.Unknown, promptDue = true, route = null))

        assertEquals("unknown", snapshot.permission)
        assertEquals(true, snapshot.promptDue)
        assertEquals(false, snapshot.noticeVisible)
    }

    @Test
    fun uxDr120ARouteToALotCarriesTheSiteTheLotAndItsName() {
        val state = PushState(PushPermission.Granted, false, PushRoute.LotDetail("a", "lot-1", "Tomatoes"))

        val snapshot = snapshotOf(state)

        assertEquals("granted", snapshot.permission)
        assertEquals("lotDetail", snapshot.routeTarget)
        assertEquals("a", snapshot.routeSiteId)
        assertEquals("lot-1", snapshot.routeLotId)
        assertEquals("Tomatoes", snapshot.routeLotName)
    }

    @Test
    fun uxDr120ARouteToTheOverviewCarriesOnlyTheSite() {
        val snapshot = snapshotOf(PushState(PushPermission.Granted, false, PushRoute.Overview("b")))

        assertEquals("overview", snapshot.routeTarget)
        assertEquals("b", snapshot.routeSiteId)
        assertNull(snapshot.routeLotId)
        assertNull(snapshot.routeLotName)
    }

    @Test
    fun theStatusesOfTheOsCrossAsStrings() {
        assertEquals(OsPermission.NotDetermined, OsPermission.fromKey("notDetermined"))
        assertEquals(OsPermission.Granted, OsPermission.fromKey("granted"))
        assertEquals(OsPermission.Denied, OsPermission.fromKey("denied"))
        assertNull(OsPermission.fromKey("provisional"))
        assertEquals(ApnsEnvironment.Production, ApnsEnvironment.fromKey("production"))
        assertEquals(ApnsEnvironment.Sandbox, ApnsEnvironment.fromKey("sandbox"))
        assertNull(ApnsEnvironment.fromKey("development"))
    }
}
