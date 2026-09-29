package com.escendit.coldframe.core

import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch

/** A running observation; [close] stops it. */
public class Watch internal constructor(
    private val job: Job,
) {
    public fun close() {
        job.cancel()
    }
}

/**
 * Calls [onEach] with the current value and every change, on [scope]. Swift cannot collect a
 * flow, so the iOS shell observes state through this callback.
 */
public fun <T> StateFlow<T>.watch(
    scope: CoroutineScope,
    onEach: (T) -> Unit,
): Watch = Watch(scope.launch { collect { onEach(it) } })
