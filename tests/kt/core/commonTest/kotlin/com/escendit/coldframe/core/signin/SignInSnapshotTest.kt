package com.escendit.coldframe.core.signin

import com.escendit.coldframe.core.watch
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.test.advanceUntilIdle
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals

class SignInSnapshotTest {
    @Test
    fun uxDr92EveryNoticeReachesSwiftAsItsCatalogueKeyAndAction() {
        assertEquals(
            SignInSnapshot(false, false, false, "unreachable", "tryAgain", null),
            snapshotOf(SignInState.SignedOut(Notice.Unreachable)),
        )
        assertEquals(
            SignInSnapshot(false, false, false, "certificate", null, null),
            snapshotOf(SignInState.SignedOut(Notice.Certificate)),
        )
        assertEquals(
            SignInSnapshot(false, false, false, "keycloak", "tryAgain", null),
            snapshotOf(SignInState.SignedOut(Notice.Keycloak)),
        )
        assertEquals(
            SignInSnapshot(false, false, false, "signedOut", "signIn", null),
            snapshotOf(SignInState.SignedOut(Notice.SignedOut)),
        )
    }

    @Test
    fun theOtherStatesAreFlags() {
        assertEquals(SignInSnapshot(true, false, false, null, null, null), snapshotOf(SignInState.Restoring))
        assertEquals(SignInSnapshot(false, true, false, null, null, null), snapshotOf(SignInState.Working))
        assertEquals(SignInSnapshot(false, false, false, null, null, null), snapshotOf(SignInState.SignedOut(null)))
        assertEquals(SignInSnapshot(false, false, true, null, null, "Simon"), snapshotOf(SignInState.SignedIn("Simon")))
    }

    @Test
    fun aWatchReportsTheCurrentValueAndEveryChangeUntilClosed() =
        runTest {
            val flow = MutableStateFlow(1)
            val seen = mutableListOf<Int>()
            val watch = flow.watch(this) { seen += it }
            advanceUntilIdle()

            flow.value = 2
            advanceUntilIdle()
            watch.close()
            flow.value = 3
            advanceUntilIdle()

            assertEquals(listOf(1, 2), seen)
        }
}
