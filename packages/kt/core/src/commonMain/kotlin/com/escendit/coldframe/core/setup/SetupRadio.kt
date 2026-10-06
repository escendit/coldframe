package com.escendit.coldframe.core.setup

import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.StateFlow

/** Whether the phone's Bluetooth can scan and connect right now. */
public enum class RadioState {
    /** On and permitted. */
    Ready,

    /** Bluetooth is off. */
    Off,

    /** The app has no Bluetooth permission. */
    Unauthorized,

    /** The phone has no Bluetooth LE. */
    Unsupported,
}

/**
 * One advertisement of an unprovisioned Device with the setup service. [id] is the platform's
 * peripheral identifier (never shown); [name] is the advertised name, `Coldframe Hub 3F2A`.
 */
public data class SetupAdvert(
    val id: String,
    val name: String?,
    val rssi: Int,
)

/** The BLE link failed or closed. */
public class SetupLinkException(
    message: String,
    cause: Throwable? = null,
) : Exception(message, cause)

/**
 * One GATT connection to a Device's setup service, subscribed to the notify characteristic
 * before the first write. Payloads are `header ‖ fragment`, at most [maxPayload] bytes.
 */
public interface SetupLink {
    /** The largest payload one write carries: the negotiated ATT MTU − 3. */
    public val maxPayload: Int

    /** Notifications from the Device, in order. Completes when the link closes. */
    public val incoming: Flow<ByteArray>

    /** Writes one payload with response; throws [SetupLinkException] when the link is gone. */
    public suspend fun write(payload: ByteArray)

    /** Disconnects; safe to call twice. */
    public suspend fun close()
}

/**
 * The BLE radio as the setup flow needs it (AD-25: only the KMP core does BLE). The apps use
 * [KableSetupRadio]; tests use a fake radio with a fake Hub.
 */
public interface SetupRadio {
    public val state: StateFlow<RadioState>

    /** Re-reads [state], after a permission prompt or a return to the foreground. */
    public fun recheck()

    /** Advertisements carrying the setup service UUID, until the collector stops. */
    public fun scan(): Flow<SetupAdvert>

    /**
     * Connects to [id], asks for a larger MTU, subscribes to notifications and returns the link;
     * throws [SetupLinkException] when it cannot.
     */
    public suspend fun connect(id: String): SetupLink
}

/** The GATT service and characteristics of the setup protocol (`packages/rs/setup/src/lib.rs`). */
public object SetupGatt {
    public const val SERVICE_UUID: String = "c01d0001-5e70-4c0d-8f00-00000000c0de"
    public const val WRITE_CHARACTERISTIC_UUID: String = "c01d0002-5e70-4c0d-8f00-00000000c0de"
    public const val NOTIFY_CHARACTERISTIC_UUID: String = "c01d0003-5e70-4c0d-8f00-00000000c0de"

    /** The advertised name of a Hub: this prefix and the first four Device ID digits. */
    public const val HUB_NAME_PREFIX: String = "Coldframe Hub "

    /** The advertised name of a Node in setup mode: this prefix and the first four Device ID digits. */
    public const val NODE_NAME_PREFIX: String = "Coldframe Node "
}
