package com.escendit.coldframe.android.push

import android.Manifest
import android.app.NotificationManager
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import android.provider.Settings
import com.escendit.coldframe.core.push.OsPermission

/**
 * What Android says about this app's notifications. From Android 13 the permission is asked at runtime: while it is
 * not held this is [OsPermission.NotDetermined], and the core knows whether the prompt was answered before. Held but
 * turned off in the system settings is [OsPermission.Denied]. Before Android 13 there is nothing to ask: on unless
 * the User turned them off.
 */
fun Context.notificationPermission(): OsPermission {
    val enabled = getSystemService(NotificationManager::class.java)?.areNotificationsEnabled() != false
    if (Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU) {
        return if (enabled) OsPermission.Granted else OsPermission.Denied
    }
    val held = checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED
    return when {
        !held -> OsPermission.NotDetermined
        enabled -> OsPermission.Granted
        else -> OsPermission.Denied
    }
}

/** Open Settings on the notice (UX-DR88): this app's notification settings in the system settings. */
fun Context.openNotificationSettings() {
    val intent =
        Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS)
            .putExtra(Settings.EXTRA_APP_PACKAGE, packageName)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
    startActivity(intent)
}
