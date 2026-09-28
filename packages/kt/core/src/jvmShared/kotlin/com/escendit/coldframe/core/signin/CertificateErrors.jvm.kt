package com.escendit.coldframe.core.signin

import java.security.cert.CertPathValidatorException
import java.security.cert.CertificateException
import javax.net.ssl.SSLHandshakeException
import javax.net.ssl.SSLPeerUnverifiedException

public actual fun isCertificateError(error: Throwable): Boolean =
    anyCause(error) {
        it is SSLHandshakeException ||
            it is SSLPeerUnverifiedException ||
            it is CertificateException ||
            it is CertPathValidatorException
    }
