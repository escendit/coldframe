package com.escendit.coldframe.core.signin

import kotlin.test.Test
import kotlin.test.assertEquals

class CoreConfigTest {
    @Test
    fun unsetOrBlankValuesUseTheInvalidDefaults() {
        val unset = CoreConfig.of(null, null, null)
        val blank = CoreConfig.of("", "  ", "")

        for (config in listOf(unset, blank)) {
            assertEquals("https://server.coldframe.invalid", config.serverUrl)
            assertEquals("https://keycloak.coldframe.invalid/realms/coldframe", config.keycloakIssuer)
            assertEquals("coldframe-mobile", config.clientId)
        }
    }

    @Test
    fun configuredValuesAreKept() {
        val config = CoreConfig.of("https://server.example", "https://id.example/realms/coldframe", "other")

        assertEquals("https://server.example", config.serverUrl)
        assertEquals("https://id.example/realms/coldframe", config.keycloakIssuer)
        assertEquals("other", config.clientId)
    }

    @Test
    fun redirectUrisUseTheReversedDomainScheme() {
        assertEquals("com.escendit.coldframe:/signin/callback", testConfig.redirectUri)
        assertEquals("com.escendit.coldframe:/signout/callback", testConfig.postLogoutRedirectUri)
    }

    @Test
    fun theHealthUrlIsAtTheServerRoot() {
        assertEquals("https://server.example/.well-known/healthz", serverHealthUrl("https://server.example"))
        assertEquals("https://server.example/.well-known/healthz", serverHealthUrl("https://server.example/"))
        assertEquals(
            "https://server.example:8443/.well-known/healthz",
            serverHealthUrl("https://server.example:8443/app?x=1"),
        )
    }

    @Test
    fun theDiscoveryUrlIsRelativeToTheRealm() {
        val expected = "https://id.example/realms/coldframe/.well-known/openid-configuration"

        assertEquals(expected, discoveryUrl("https://id.example/realms/coldframe"))
        assertEquals(expected, discoveryUrl("https://id.example/realms/coldframe/"))
        assertEquals(expected, testConfig.discoveryUri)
    }
}
