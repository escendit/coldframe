package com.escendit.coldframe.core.setup

import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import platform.CoreBluetooth.CBCentralManager
import platform.CoreBluetooth.CBCentralManagerDelegateProtocol
import platform.CoreBluetooth.CBManagerStatePoweredOff
import platform.CoreBluetooth.CBManagerStatePoweredOn
import platform.CoreBluetooth.CBManagerStateUnauthorized
import platform.CoreBluetooth.CBManagerStateUnsupported
import platform.darwin.NSObject

/**
 * The Bluetooth state on iOS from a `CBCentralManager`, created on first use so the system asks
 * for Bluetooth permission only when Add a Hub opens (`NSBluetoothAlwaysUsageDescription`).
 */
public class IosRadioState : RadioStateSource {
    private val mutableState = MutableStateFlow(RadioState.Off)
    private var manager: CBCentralManager? = null
    private val delegate =
        object : NSObject(), CBCentralManagerDelegateProtocol {
            override fun centralManagerDidUpdateState(central: CBCentralManager) {
                mutableState.value =
                    when (central.state) {
                        CBManagerStatePoweredOn -> RadioState.Ready
                        CBManagerStatePoweredOff -> RadioState.Off
                        CBManagerStateUnauthorized -> RadioState.Unauthorized
                        CBManagerStateUnsupported -> RadioState.Unsupported
                        else -> RadioState.Off
                    }
            }
        }

    override val state: StateFlow<RadioState>
        get() {
            if (manager == null) recheck()
            return mutableState
        }

    override fun recheck() {
        val current = manager
        if (current == null) {
            manager = CBCentralManager(delegate = delegate, queue = null)
        } else {
            delegate.centralManagerDidUpdateState(current)
        }
    }
}
