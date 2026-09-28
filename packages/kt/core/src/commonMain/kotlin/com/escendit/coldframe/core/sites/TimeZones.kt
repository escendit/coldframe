package com.escendit.coldframe.core.sites

/** The OS time zone's IANA ID, proposed on the time-zone confirm panel. */
public expect fun detectedTimeZone(): String

/** Every IANA zone ID the OS knows, sorted, for Change. */
public expect fun availableTimeZones(): List<String>
