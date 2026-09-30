package com.escendit.coldframe.core.setup

import com.juul.kable.PeripheralBuilder

/** The JVM target only runs the tests; the desktop negotiates the MTU on connect. */
internal actual fun PeripheralBuilder.requestLargerMtu(mtu: Int) = Unit
