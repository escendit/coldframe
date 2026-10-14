package com.escendit.coldframe.core.sites

import com.escendit.coldframe.core.Watch
import com.escendit.coldframe.core.watch
import kotlinx.coroutines.CoroutineScope

/**
 * Members for Swift (Story 9.1), which observes flat [MembersSnapshot]s and calls the actions. Only an
 * Owner has the surface; for anyone else the snapshot is `hidden` and every call is ignored.
 */
public class IosMembers internal constructor(
    private val engine: MembersEngine,
    private val scope: CoroutineScope,
) {
    /** Calls [onEach] on the main thread with the current snapshot and every change. */
    public fun watch(onEach: (MembersSnapshot) -> Unit): Watch = engine.state.watch(scope) { onEach(snapshotOf(it)) }

    /** The state as of now. */
    public fun current(): MembersSnapshot = snapshotOf(engine.state.value)

    /** Reads the Memberships; on every entry of Members. */
    public fun load() {
        engine.load()
    }

    /** The email field. */
    public fun setEmail(email: String) {
        engine.setEmail(email)
    }

    /** A segment of the Role choice: `member`, `administrator` or `owner`. Anything else is ignored. */
    public fun setRole(role: String) {
        SiteRole.entries.firstOrNull { it.key() == role }?.let(engine::setRole)
    }

    /** "Send invite". */
    public fun sendInvite() {
        engine.sendInvite()
    }

    /** Try again, where the notice offers it. */
    public fun retry() {
        engine.retry()
    }

    /** Story 9.2: opens the editor of a tile; the last Owner's tile is locked. */
    public fun select(userId: String) {
        engine.select(userId)
    }

    /** Closes the editor. */
    public fun closeEditor() {
        engine.closeEditor()
    }

    /** A segment of the editor's Role choice: `member`, `administrator` or `owner`. Anything else is ignored. */
    public fun setEditRole(role: String) {
        SiteRole.entries.firstOrNull { it.key() == role }?.let(engine::setEditRole)
    }

    /** "Save Role". */
    public fun saveRole() {
        engine.saveRole()
    }

    /** "Remove from Site": shows the confirmation. */
    public fun askRemove() {
        engine.askRemove()
    }

    /** Keeps the member. */
    public fun cancelRemove() {
        engine.cancelRemove()
    }

    /** "Remove". */
    public fun confirmRemove() {
        engine.confirmRemove()
    }
}
