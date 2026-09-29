package com.escendit.coldframe.core.crypto

import java.security.SecureRandom

private val random = SecureRandom()

internal actual fun secureRandom(size: Int): ByteArray = ByteArray(size).also { random.nextBytes(it) }
