package com.escendit.coldframe.android.push

import android.os.Handler
import android.os.Looper
import com.escendit.coldframe.android.ColdframeApplication
import com.google.firebase.messaging.FirebaseMessagingService
import com.google.firebase.messaging.RemoteMessage

/**
 * Receives the Server's FCM data messages (Story 6.5). The Server sends title, body and routing as data, because an
 * FCM display notification cannot set a notification group; [PushNotifications] builds the notification from them.
 * A replaced registration goes to the core, which sends it to the Server while signed in.
 */
class ColdframeMessagingService : FirebaseMessagingService() {
    override fun onMessageReceived(message: RemoteMessage) {
        PushNotifications.show(this, message.data)
    }

    /** Firebase calls this on a worker thread; the core's engines are touched on the main thread only. */
    override fun onNewToken(token: String) {
        val push = (application as? ColdframeApplication)?.signIn?.push ?: return
        Handler(Looper.getMainLooper()).post { push.tokenReceived(token) }
    }
}
