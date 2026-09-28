package com.escendit.coldframe.core.sites

import java.time.ZoneId
import java.util.TimeZone

public actual fun detectedTimeZone(): String = TimeZone.getDefault().id

public actual fun availableTimeZones(): List<String> = ZoneId.getAvailableZoneIds().sorted()
