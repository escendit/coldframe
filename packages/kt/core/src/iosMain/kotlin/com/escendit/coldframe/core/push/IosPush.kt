package com.escendit.coldframe.core.push

import com.escendit.coldframe.core.Watch
import com.escendit.coldframe.core.watch
import kotlinx.coroutines.CoroutineScope

/**
 * Push for Swift (Story 6.5), which observes flat [PushSnapshot]s and holds the OS calls: `UNUserNotificationCenter`
 * and `registerForRemoteNotifications`. Swift never calls the Server; the core registers the token after sign-in
 * and removes the registration at sign-out.
 */
public class IosPush internal constructor(
    private val engine: PushEngine,
    private val scope: CoroutineScope,
) {
    /** Calls [onEach] on the main thread with the current snapshot and every change. */
    public fun watch(onEach: (PushSnapshot) -> Unit): Watch = engine.state.watch(scope) { onEach(snapshotOf(it)) }

    /**
     * What the OS says, on start and on every foreground: `notDetermined` (`UNAuthorizationStatus.notDetermined`),
     * `denied` (`.denied`) or `granted` (`.authorized`, `.provisional`, `.ephemeral`). Any other text is ignored.
     */
    public fun reportPermission(status: String) {
        OsPermission.fromKey(status)?.let { engine.reportPermission(it) }
    }

    /**
     * `requestAuthorization` has answered: [status] is `granted` or `denied`. The why-line and the prompt are not
     * shown again on this device.
     */
    public fun promptAnswered(status: String) {
        OsPermission.fromKey(status)?.let { engine.promptAnswered(it) }
    }

    /**
     * The APNs device token of `didRegisterForRemoteNotificationsWithDeviceToken` as lowercase hex, with the
     * [environment] of the build's `aps-environment`: `production`, or `sandbox` for a development build. Any other
     * environment, and an empty token, is ignored.
     */
    public fun deviceToken(
        tokenHex: String,
        environment: String,
    ) {
        ApnsEnvironment.fromKey(environment)?.let { engine.tokenReceived(tokenHex, it) }
    }

    /**
     * A notification was tapped: [coldframe] is the `coldframe` dictionary of its `userInfo` (`kind`, `siteId`,
     * `lotId`, `alertId`, `collapseId`), every value a string. The route arrives in the snapshot once the Sites,
     * and for an Alert the Lots, are loaded.
     */
    public fun opened(coldframe: Map<String, String>) {
        engine.opened(coldframe)
    }

    /** Swift has shown the snapshot's route. */
    public fun routeHandled() {
        engine.routeHandled()
    }
}
