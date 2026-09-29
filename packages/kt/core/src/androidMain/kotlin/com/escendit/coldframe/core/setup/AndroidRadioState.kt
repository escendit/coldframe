package com.escendit.coldframe.core.setup

import android.Manifest
import android.bluetooth.BluetoothAdapter
import android.bluetooth.BluetoothManager
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.PackageManager
import android.os.Build
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/**
 * The Bluetooth state on Android: the adapter's on/off broadcasts plus the runtime permissions,
 * `BLUETOOTH_SCAN` and `BLUETOOTH_CONNECT` from Android 12, fine location before. The shell
 * calls [recheck] after its permission prompt and on every return to the foreground.
 */
public class AndroidRadioState(
    context: Context,
) : RadioStateSource {
    private val context = context.applicationContext
    private val mutableState = MutableStateFlow(read())

    override val state: StateFlow<RadioState> = mutableState.asStateFlow()

    init {
        val receiver =
            object : BroadcastReceiver() {
                override fun onReceive(
                    context: Context,
                    intent: Intent,
                ) {
                    recheck()
                }
            }
        val filter = IntentFilter(BluetoothAdapter.ACTION_STATE_CHANGED)
        // A system broadcast: it arrives even though no other app may send to this receiver.
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            this.context.registerReceiver(receiver, filter, Context.RECEIVER_NOT_EXPORTED)
        } else {
            this.context.registerReceiver(receiver, filter)
        }
    }

    override fun recheck() {
        mutableState.value = read()
    }

    private fun read(): RadioState {
        val adapter = context.getSystemService(BluetoothManager::class.java)?.adapter ?: return RadioState.Unsupported
        if (!context.packageManager.hasSystemFeature(PackageManager.FEATURE_BLUETOOTH_LE)) return RadioState.Unsupported
        if (REQUIRED_PERMISSIONS.any { context.checkSelfPermission(it) != PackageManager.PERMISSION_GRANTED }) {
            return RadioState.Unauthorized
        }
        return if (adapter.isEnabled) RadioState.Ready else RadioState.Off
    }

    public companion object {
        /** What the shell asks for before scanning. */
        public val REQUIRED_PERMISSIONS: List<String> =
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                listOf(Manifest.permission.BLUETOOTH_SCAN, Manifest.permission.BLUETOOTH_CONNECT)
            } else {
                listOf(Manifest.permission.ACCESS_FINE_LOCATION)
            }
    }
}
