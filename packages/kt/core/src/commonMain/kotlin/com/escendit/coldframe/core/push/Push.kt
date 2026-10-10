package com.escendit.coldframe.core.push

import com.escendit.coldframe.core.api.ApiResult
import com.escendit.coldframe.core.api.RegisterPushDeviceRequestDto

/** The Server calls of the push registration; [com.escendit.coldframe.core.api.ColdframeApi] in the apps. */
public interface PushApi {
    /** `registerPushDevice`: this installation's token; the same registration again changes nothing. */
    public suspend fun registerPushDevice(
        installationId: String,
        request: RegisterPushDeviceRequestDto,
    ): ApiResult<Unit>

    /** `removePushDevice`: succeeds also when the caller held no registration for the installation. */
    public suspend fun removePushDevice(installationId: String): ApiResult<Unit>
}

/** The push provider of this app: [key] is the contract's `PushPlatform`. */
public enum class PushPlatform(
    public val key: String,
) {
    /** An iPhone. Its registration names the [ApnsEnvironment] of the token. */
    Apns("apns"),

    /** An Android phone. Its registration carries no environment. */
    Fcm("fcm"),
}

/** The APNs environment a device token belongs to: [key] is the contract's `ApnsEnvironment`. */
public enum class ApnsEnvironment(
    public val key: String,
) {
    Production("production"),

    /** A development build (`aps-environment` = `development`). */
    Sandbox("sandbox"),
    ;

    public companion object {
        /** `production` or `sandbox`; anything else is `null`. */
        public fun fromKey(key: String): ApnsEnvironment? = entries.firstOrNull { it.key == key }
    }
}

/**
 * What the OS says about notifications, as the shell reads it on start and on every foreground.
 *
 * - iOS: `UNAuthorizationStatus.notDetermined` is [NotDetermined], `.denied` is [Denied], and `.authorized`,
 *   `.provisional` and `.ephemeral` are [Granted].
 * - Android 13 and later: [Granted] when `POST_NOTIFICATIONS` is held and the app's notifications are on,
 *   [NotDetermined] when the permission is not held (Android cannot tell "never asked" from "denied"; the core
 *   can), [Denied] when it is held but the app's notifications are turned off.
 * - Android 12 and earlier has no prompt: [Granted] while the app's notifications are on, else [Denied].
 */
public enum class OsPermission(
    public val key: String,
) {
    NotDetermined("notDetermined"),
    Granted("granted"),
    Denied("denied"),
    ;

    public companion object {
        /** `notDetermined`, `granted` or `denied`; anything else is `null`. */
        public fun fromKey(key: String): OsPermission? = entries.firstOrNull { it.key == key }
    }
}

/** The notification permission as the surfaces need it. */
public enum class PushPermission {
    /** Not reported yet, or never asked: no notice. */
    Unknown,
    Granted,

    /** Denied or revoked: the notice of UX-DR88 shows in My notifications and on the overview. */
    Denied,
}

/**
 * Where a tap on a notification must lead, as the payload says it (the `route` of the fixtures in
 * `packages/asyncapi`): [kind] is `alert`, `reminder`, `summary` or a kind this app does not know yet.
 */
public data class PushTap(
    val kind: String,
    val siteId: String,
    val lotId: String?,
) {
    /** An Alert and its Reminder open Lot detail; a summary and any other kind open the Site overview. */
    val opensLot: Boolean get() = lotId != null && (kind == PushPayload.KIND_ALERT || kind == PushPayload.KIND_REMINDER)
}

/**
 * A push as the Server sends it (`packages/asyncapi`, `PushRoute`): on FCM the keys of `message.data`, with
 * [title], [body] and [siteName]; on APNs the object `coldframe` beside `aps`, which carries the routing keys only.
 * The text is the Server's and is shown as it is.
 */
public data class PushPayload(
    val kind: String,
    val siteId: String,
    val lotId: String?,
    val alertId: String?,
    val collapseId: String?,
    val title: String?,
    val body: String?,
    val siteName: String?,
) {
    /** Where a tap leads. */
    val tap: PushTap get() = PushTap(kind, siteId, lotId)

    public companion object {
        public const val KIND: String = "kind"
        public const val SITE_ID: String = "siteId"
        public const val LOT_ID: String = "lotId"
        public const val ALERT_ID: String = "alertId"
        public const val COLLAPSE_ID: String = "collapseId"
        public const val TITLE: String = "title"
        public const val BODY: String = "body"
        public const val SITE_NAME: String = "siteName"

        public const val KIND_ALERT: String = "alert"
        public const val KIND_REMINDER: String = "reminder"
        public const val KIND_SUMMARY: String = "summary"

        /** The five keys both platforms carry; a tap needs no other. */
        public val ROUTING_KEYS: Set<String> = setOf(KIND, SITE_ID, LOT_ID, ALERT_ID, COLLAPSE_ID)

        /** Reads [data]; `null` when it names no kind or no Site, so it is not a Coldframe push. */
        public fun parse(data: Map<String, String>): PushPayload? {
            fun value(key: String): String? = data[key]?.takeIf { it.isNotBlank() }
            val kind = value(KIND) ?: return null
            val siteId = value(SITE_ID) ?: return null
            return PushPayload(
                kind = kind,
                siteId = siteId,
                lotId = value(LOT_ID),
                alertId = value(ALERT_ID),
                collapseId = value(COLLAPSE_ID),
                title = value(TITLE),
                body = value(BODY),
                siteName = value(SITE_NAME),
            )
        }
    }
}

/**
 * Where the shell goes after a tap, once the core has switched to the notification's Site and knows its Lots
 * (UX-DR120). The shell shows it and calls [PushEngine.routeHandled].
 */
public sealed interface PushRoute {
    public val siteId: String

    /** Lot detail of [lotId] on the Garden; [lotName] is its name until the Server's answer arrives. */
    public data class LotDetail(
        override val siteId: String,
        val lotId: String,
        val lotName: String,
    ) : PushRoute

    /** The Site overview, with no Lot detail over it. */
    public data class Overview(
        override val siteId: String,
    ) : PushRoute
}

/**
 * What the shells render of push.
 *
 * [promptDue]: the why-line of UX-DR122 shows on the Site overview, and its action opens the OS prompt; true only
 * while a Site is current, the OS has not been asked and this device never asked. [route] is the tap waiting to be
 * shown.
 */
public data class PushState(
    val permission: PushPermission,
    val promptDue: Boolean,
    val route: PushRoute?,
) {
    /** The notice "Notifications are off for Coldframe on this phone." shows (UX-DR88). */
    val noticeVisible: Boolean get() = permission == PushPermission.Denied

    public companion object {
        /** Before the shell reported anything: no notice, no prompt, no route. */
        public val None: PushState = PushState(PushPermission.Unknown, promptDue = false, route = null)
    }
}
