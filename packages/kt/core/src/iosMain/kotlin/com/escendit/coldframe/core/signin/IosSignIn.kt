package com.escendit.coldframe.core.signin

import com.escendit.coldframe.core.Watch
import com.escendit.coldframe.core.lots.IosLots
import com.escendit.coldframe.core.sites.IosSites
import com.escendit.coldframe.core.sites.SitesWiring
import com.escendit.coldframe.core.watch
import com.russhwolf.settings.NSUserDefaultsSettings
import io.ktor.client.engine.darwin.Darwin
import kotlinx.coroutines.MainScope
import org.publicvalue.multiplatform.oidc.appsupport.IosCodeAuthFlowFactory
import org.publicvalue.multiplatform.oidc.tokenstore.IosKeychainTokenStore
import platform.Foundation.NSUserDefaults

/**
 * The iOS side of the core: `ASWebAuthenticationSession` (not ephemeral) for the browser flow,
 * the Keychain for the tokens. Swift observes it with [watch].
 */
public class IosSignIn private constructor(
    config: CoreConfig,
) {
    private val scope = MainScope()
    private val authFlowFactory = IosCodeAuthFlowFactory(ephemeralBrowserSession = false)

    private val engine: SignInEngine =
        SignInClients.engine(
            config = config,
            httpEngine = Darwin.create(),
            store = IosKeychainTokenStore(),
            authFlowFactory = { authFlowFactory },
            scope = scope,
        )

    private val api = SitesWiring.api(config, Darwin.create(), engine)

    private val sitesEngine =
        SitesWiring.engine(
            api = api,
            signIn = engine,
            settings = NSUserDefaultsSettings(NSUserDefaults.standardUserDefaults),
            scope = scope,
        )

    /** The Sites of the signed-in user; loads whenever the session becomes signed in. */
    public val sites: IosSites = IosSites(sitesEngine, scope)

    /** The Lots of the current Site and Site settings; reloads whenever the current Site changes. */
    public val lots: IosLots = IosLots(SitesWiring.lots(api, sitesEngine, scope), scope)

    /** Calls [onEach] on the main thread with the current snapshot and every change. */
    public fun watch(onEach: (SignInSnapshot) -> Unit): Watch = engine.state.watch(scope) { onEach(snapshotOf(it)) }

    public fun signIn() {
        engine.signIn()
    }

    public fun resume() {
        engine.resume()
    }

    public fun signOut() {
        engine.signOut()
    }

    public companion object {
        /** Unset or blank values (an unconfigured xcconfig) fall back to the `.invalid` defaults. */
        public fun create(
            serverUrl: String?,
            keycloakIssuer: String?,
            clientId: String?,
        ): IosSignIn = IosSignIn(CoreConfig.of(serverUrl, keycloakIssuer, clientId))
    }
}
