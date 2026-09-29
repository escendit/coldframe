package com.escendit.coldframe.core.setup

import com.juul.kable.Advertisement
import com.juul.kable.Peripheral
import com.juul.kable.PeripheralBuilder
import com.juul.kable.Scanner
import com.juul.kable.State
import com.juul.kable.WriteType
import com.juul.kable.characteristicOf
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.TimeoutCancellationException
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.onEach
import kotlinx.coroutines.flow.receiveAsFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.withTimeout
import kotlin.time.Duration.Companion.seconds
import kotlin.uuid.ExperimentalUuidApi
import kotlin.uuid.Uuid

/** Whether Bluetooth is on and permitted, as the platform reports it. */
public interface RadioStateSource {
    public val state: StateFlow<RadioState>

    public fun recheck()
}

/** Asks for a larger ATT MTU where the platform lets the app ask (Android); iOS negotiates itself. */
internal expect fun PeripheralBuilder.requestLargerMtu(mtu: Int)

/**
 * [SetupRadio] over Kable 0.45 (AD-25): scans for the setup service, connects, asks for a larger
 * MTU, subscribes to the notify characteristic before the first write and writes with response.
 * No payload is ever logged.
 */
@OptIn(ExperimentalUuidApi::class)
public class KableSetupRadio(
    private val radioState: RadioStateSource,
) : SetupRadio {
    private val service = Uuid.parse(SetupGatt.SERVICE_UUID)
    private val writeCharacteristic = characteristicOf(service, Uuid.parse(SetupGatt.WRITE_CHARACTERISTIC_UUID))
    private val notifyCharacteristic = characteristicOf(service, Uuid.parse(SetupGatt.NOTIFY_CHARACTERISTIC_UUID))
    private val seen = mutableMapOf<String, Advertisement>()

    override val state: StateFlow<RadioState> get() = radioState.state

    override fun recheck() {
        radioState.recheck()
    }

    override fun scan(): Flow<SetupAdvert> =
        Scanner { filters { match { services = listOf(service) } } }
            .advertisements
            .onEach { seen[it.identifier.toString()] = it }
            .map { SetupAdvert(it.identifier.toString(), it.name ?: it.peripheralName, it.rssi) }

    override suspend fun connect(id: String): SetupLink {
        val advertisement = seen[id] ?: throw SetupLinkException("unknown Device")
        val peripheral = Peripheral(advertisement) { requestLargerMtu(Framing.REQUESTED_MTU) }
        try {
            val connection = withTimeout(CONNECT_TIMEOUT) { peripheral.connect() }
            val incoming = Channel<ByteArray>(Channel.UNLIMITED)
            val subscribed = CompletableDeferred<Unit>()
            connection.launch {
                try {
                    peripheral.observe(notifyCharacteristic) { subscribed.complete(Unit) }.collect { incoming.send(it) }
                } finally {
                    incoming.close()
                }
            }
            connection.launch {
                peripheral.state.first { it is State.Disconnected }
                incoming.close()
            }
            withTimeout(CONNECT_TIMEOUT) { subscribed.await() }
            val maxPayload =
                peripheral
                    .maximumWriteValueLengthForType(WriteType.WithResponse)
                    .coerceIn(Framing.DEFAULT_MAX_PAYLOAD, Framing.REQUESTED_MTU - ATT_HEADER)
            return KableLink(peripheral, maxPayload, incoming)
        } catch (timeout: TimeoutCancellationException) {
            // A timeout is a failed connect, not a cancellation of the caller.
            peripheral.close()
            throw SetupLinkException("connect timed out", timeout)
        } catch (cancellation: CancellationException) {
            peripheral.close()
            throw cancellation
        } catch (
            @Suppress("TooGenericExceptionCaught") failed: Exception,
        ) {
            peripheral.close()
            throw SetupLinkException("connect failed", failed)
        }
    }

    private inner class KableLink(
        private val peripheral: Peripheral,
        override val maxPayload: Int,
        channel: Channel<ByteArray>,
    ) : SetupLink {
        override val incoming: Flow<ByteArray> = channel.receiveAsFlow()

        override suspend fun write(payload: ByteArray) {
            try {
                peripheral.write(writeCharacteristic, payload, WriteType.WithResponse)
            } catch (cancellation: CancellationException) {
                throw cancellation
            } catch (
                @Suppress("TooGenericExceptionCaught") failed: Exception,
            ) {
                throw SetupLinkException("write failed", failed)
            }
        }

        override suspend fun close() {
            try {
                peripheral.disconnect()
            } finally {
                peripheral.close()
            }
        }
    }

    private companion object {
        val CONNECT_TIMEOUT = 20.seconds

        /** The ATT write header: a payload is at most MTU − 3 bytes. */
        const val ATT_HEADER = 3
    }
}
