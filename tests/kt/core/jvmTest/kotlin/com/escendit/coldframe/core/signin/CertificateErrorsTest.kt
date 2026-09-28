package com.escendit.coldframe.core.signin

import kotlinx.coroutines.test.runTest
import java.io.IOException
import java.security.cert.CertPathValidatorException
import java.security.cert.CertificateExpiredException
import javax.net.ssl.SSLHandshakeException
import javax.net.ssl.SSLPeerUnverifiedException
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class CertificateErrorsTest {
    @Test
    fun `UX-DR92 a TLS failure nested in the cause chain is a certificate error`() {
        val handshake = SSLHandshakeException("PKIX path building failed")
        handshake.initCause(CertPathValidatorException("untrusted"))

        assertTrue(isCertificateError(IOException("request failed", handshake)))
        assertTrue(isCertificateError(RuntimeException(IOException(CertPathValidatorException("untrusted")))))
        assertTrue(isCertificateError(SSLPeerUnverifiedException("hostname mismatch")))
        assertTrue(isCertificateError(IllegalStateException(CertificateExpiredException("expired"))))
    }

    @Test
    fun `UX-DR92 other failures are not certificate errors`() {
        assertFalse(isCertificateError(IOException("connection refused")))
        assertFalse(isCertificateError(IllegalStateException("timeout", IOException("reset"))))
    }

    @Test
    fun `a cause chain deeper than the limit is not followed`() {
        var error: Throwable = SSLHandshakeException("deep")
        repeat(MAX_CAUSE_DEPTH) { error = IOException(error) }

        assertFalse(isCertificateError(error))
    }

    @Test
    fun `UX-DR92 the JVM probe maps a handshake failure to the certificate notice`() =
        runTest {
            val engine = mockEngine { throw SSLHandshakeException("PKIX path building failed") }
            val probe = HttpProbe(SignInClients.probeHttpClient(engine), testConfig)

            assertEquals(Failure.Certificate, probe.probeSignIn())
        }
}
