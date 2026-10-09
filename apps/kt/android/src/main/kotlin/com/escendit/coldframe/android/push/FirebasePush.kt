package com.escendit.coldframe.android.push

import android.content.Context
import com.escendit.coldframe.BuildConfig
import com.google.firebase.FirebaseApp
import com.google.firebase.FirebaseOptions
import com.google.firebase.messaging.FirebaseMessaging

/**
 * The Firebase project of this build, from the Gradle properties `coldframe.firebaseProjectId`,
 * `coldframe.firebaseApplicationId`, `coldframe.firebaseApiKey` and `coldframe.firebaseSenderId` (Story 6.5). An
 * adopter builds with their own project; the repository holds no `google-services.json` and no credential.
 */
data class FirebaseConfig(
    val projectId: String,
    val applicationId: String,
    val apiKey: String,
    val senderId: String,
) {
    /** What `google-services.json` would have given the default Firebase app. */
    fun options(): FirebaseOptions =
        FirebaseOptions
            .Builder()
            .setProjectId(projectId)
            .setApplicationId(applicationId)
            .setApiKey(apiKey)
            .setGcmSenderId(senderId)
            .build()

    companion object {
        /** `null` unless all four values are set: a build without them has no Firebase. */
        fun of(
            projectId: String?,
            applicationId: String?,
            apiKey: String?,
            senderId: String?,
        ): FirebaseConfig? {
            val values = listOf(projectId, applicationId, apiKey, senderId).map { it?.trim().orEmpty() }
            if (values.any { it.isEmpty() }) return null
            return FirebaseConfig(values[0], values[1], values[2], values[3])
        }

        /** The values this app was built with. */
        fun fromBuild(): FirebaseConfig? =
            of(
                BuildConfig.FIREBASE_PROJECT_ID,
                BuildConfig.FIREBASE_APPLICATION_ID,
                BuildConfig.FIREBASE_API_KEY,
                BuildConfig.FIREBASE_SENDER_ID,
            )
    }
}

/** Firebase Cloud Messaging, started by hand: the manifest removes Firebase's own start-up provider. */
object FirebasePush {
    /**
     * With a [config]: initialises the default Firebase app once and hands the device's FCM registration to
     * [onRegistration], now and (through [ColdframeMessagingService]) whenever Firebase replaces it. Without one
     * nothing is touched: the app works and registers nothing. Returns whether Firebase was started.
     */
    fun start(
        context: Context,
        config: FirebaseConfig?,
        onRegistration: (String) -> Unit,
    ): Boolean {
        if (config == null) return false
        if (FirebaseApp.getApps(context).isEmpty()) FirebaseApp.initializeApp(context, config.options())
        FirebaseMessaging.getInstance().token.addOnSuccessListener { onRegistration(it) }
        return true
    }
}
