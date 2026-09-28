package com.escendit.coldframe.core.sites

import platform.Foundation.NSTimeZone
import platform.Foundation.knownTimeZoneNames
import platform.Foundation.localTimeZone

public actual fun detectedTimeZone(): String = NSTimeZone.localTimeZone.name

public actual fun availableTimeZones(): List<String> = NSTimeZone.knownTimeZoneNames.filterIsInstance<String>().sorted()
