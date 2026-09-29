package com.escendit.coldframe.core.signin

/**
 * Build-time configuration of a mobile app (AD-23). Users never enter or change it.
 *
 * Android reads it from the Gradle properties `coldframe.serverUrl`, `coldframe.keycloakIssuer`
 * and `coldframe.keycloakClientId` (through `BuildConfig`); iOS from `Config/Coldframe.xcconfig`
 * (through Info.plist).
 */
public data class CoreConfig(
    val serverUrl: String,
    val keycloakIssuer: String,
    val clientId: String,
) {
    /** Where Keycloak sends the browser back after sign-in. */
    val redirectUri: String get() = REDIRECT_URI

    /** Where Keycloak sends the browser back after sign-out. */
    val postLogoutRedirectUri: String get() = POST_LOGOUT_REDIRECT_URI

    /** The issuer's discovery document. */
    val discoveryUri: String get() = discoveryUrl(keycloakIssuer)

    public companion object {
        /** Never resolvable, so an unconfigured build shows the Unreachable notice. */
        public const val DEFAULT_SERVER_URL: String = "https://server.coldframe.invalid"

        /** Never resolvable, like [DEFAULT_SERVER_URL]. */
        public const val DEFAULT_KEYCLOAK_ISSUER: String = "https://keycloak.coldframe.invalid/realms/coldframe"

        /** The public client of the Coldframe realm. */
        public const val DEFAULT_CLIENT_ID: String = "coldframe-mobile"

        /** The custom scheme is the reversed domain of the application id. */
        public const val REDIRECT_SCHEME: String = "com.escendit.coldframe"
        public const val REDIRECT_URI: String = "$REDIRECT_SCHEME:/signin/callback"
        public const val POST_LOGOUT_REDIRECT_URI: String = "$REDIRECT_SCHEME:/signout/callback"

        /** Unset or blank values fall back to the defaults. */
        public fun of(
            serverUrl: String?,
            keycloakIssuer: String?,
            clientId: String?,
        ): CoreConfig =
            CoreConfig(
                serverUrl = serverUrl.orDefault(DEFAULT_SERVER_URL),
                keycloakIssuer = keycloakIssuer.orDefault(DEFAULT_KEYCLOAK_ISSUER),
                clientId = clientId.orDefault(DEFAULT_CLIENT_ID),
            )

        private fun String?.orDefault(default: String): String = this?.trim()?.takeIf { it.isNotEmpty() } ?: default
    }
}

/** The Server answers `/.well-known/healthz` at its root; any HTTP response means it is reachable. */
public fun serverHealthUrl(serverUrl: String): String {
    val schemeEnd = serverUrl.indexOf("://")
    require(schemeEnd > 0) { "Not an absolute URL: $serverUrl" }
    val authorityStart = schemeEnd + 3
    val authorityEnd =
        serverUrl
            .indexOfAny(charArrayOf('/', '?', '#'), startIndex = authorityStart)
            .let { if (it < 0) serverUrl.length else it }
    require(authorityEnd > authorityStart) { "No host in: $serverUrl" }
    return serverUrl.substring(0, authorityEnd) + "/.well-known/healthz"
}

/** The issuer's discovery document, relative to the realm path. */
public fun discoveryUrl(issuer: String): String = issuer.trimEnd('/') + "/.well-known/openid-configuration"
