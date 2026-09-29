package com.escendit.coldframe.core.signin

import org.publicvalue.multiplatform.oidc.tokenstore.TokenStore
import org.publicvalue.multiplatform.oidc.types.remote.AccessTokenResponse

/**
 * Where the tokens live: only the library's token store (`AndroidSettingsTokenStore`,
 * `IosKeychainTokenStore`). Nothing outside the core reads it.
 */
public interface TokenVault {
    public suspend fun read(): AccessTokenResponse?

    public suspend fun save(tokens: AccessTokenResponse)

    public suspend fun clear()
}

/** [TokenVault] over a library [TokenStore]. */
public class StoreTokenVault(
    public val store: TokenStore,
) : TokenVault {
    override suspend fun read(): AccessTokenResponse? = store.getTokenResponse()

    override suspend fun save(tokens: AccessTokenResponse) {
        store.saveTokens(tokens)
    }

    override suspend fun clear() {
        store.removeTokens()
    }
}
