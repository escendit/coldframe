package com.escendit.coldframe.core.setup

import com.juul.kable.PeripheralBuilder

internal actual fun PeripheralBuilder.requestLargerMtu(mtu: Int) {
    onServicesDiscovered { requestMtu(mtu) }
}
