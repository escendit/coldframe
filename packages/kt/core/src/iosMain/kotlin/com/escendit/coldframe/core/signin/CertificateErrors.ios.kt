package com.escendit.coldframe.core.signin

import io.ktor.client.engine.darwin.DarwinHttpRequestException
import platform.Foundation.NSURLErrorDomain

/**
 * `NSURLErrorSecureConnectionFailed` (-1200) through `NSURLErrorClientCertificateRequired`
 * (-1206): bad date, untrusted, unknown root, not yet valid, rejected, required.
 */
private val certificateErrorCodes: LongRange = -1206L..-1200L

public actual fun isCertificateError(error: Throwable): Boolean =
    anyCause(error) {
        it is DarwinHttpRequestException &&
            it.origin.domain == NSURLErrorDomain &&
            it.origin.code in certificateErrorCodes
    }
