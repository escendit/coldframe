package com.escendit.coldframe.core.setup

import com.juul.kable.PeripheralBuilder

/** Core Bluetooth negotiates the largest MTU itself; the app cannot ask. */
internal actual fun PeripheralBuilder.requestLargerMtu(mtu: Int) = Unit
