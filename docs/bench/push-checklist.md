# Bench checklist: push notifications on real phones (Story 6.5)

This checklist is the acceptance test for a real push on an iPhone and on an Android phone
(UX-DR115 to UX-DR122, UX-DR88). CI sends every push to a stub provider and builds both apps
without credentials. What Apple, Google and a real phone do is checked here, by hand, with your own
accounts.

> [!NOTE]
> Coldframe ships no push credentials. You need an Apple Developer Program membership for the
> iPhone part and a Firebase project for the Android part, and you build both apps yourself with
> them. Do either part alone if you have only one phone: the Server runs with one provider.

## What you need

- A running stack: the Aspire AppHost ([`docs/quickstart.md`](../quickstart.md)) or a cluster
  installed from the charts ([`docs/operations/install.md`](../operations/install.md)). The phones
  must reach the Server and Keycloak.
- A Site with one Lot, a Node on it whose soil Sensor is calibrated, and its low Threshold known
  (the default is 30 %). Two Users with a Membership on the Site help with Part F.
- For the iPhone: a Mac with Xcode, an iPhone (the simulator gets no remote push with this set-up),
  and in the Apple Developer account an **APNs auth key** (Certificates, IDs & Profiles → Keys → +,
  *Apple Push Notifications service*). Write down the **Key ID** and your **Team ID**, and keep the
  downloaded `AuthKey_<KeyID>.p8`.
- For the Android phone: a Firebase project with an **Android app** whose package name is
  `com.escendit.coldframe`, and a **service-account key** (Project settings → Service accounts →
  Generate new private key). The service account needs the role *Firebase Cloud Messaging API
  Admin*, and the *Firebase Cloud Messaging API (V1)* must be enabled. From Project settings →
  General write down the **Project ID**, the **App ID** (`1:…:android:…`), the **Web API key** and,
  from Cloud Messaging, the **Sender ID**.

Keep every key out of the repository and out of shell history. Record the date, the phone models
and OS versions, and the Server version for each run.

## A. The Server gets the credentials

Local stack: set the variables in the shell that starts the AppHost. The AppHost hands on only what
is set.

```sh
export Push__Apns__KeyId='<key id>'
export Push__Apns__TeamId='<team id>'
export Push__Apns__PrivateKeyPem="$(cat /path/to/AuthKey_<KeyID>.p8)"
export Push__Fcm__ServiceAccountJson="$(cat /path/to/service-account.json)"
dotnet run --project aspire/Coldframe.AppHost
```

Cluster: create the Secret `coldframe-push` ([`deploy/SECRETS.md`](../../deploy/SECRETS.md#push-notifications))
and set `push.apns.enabled: true` and/or `push.fcm.enabled: true` in the `server` chart's values,
then roll the Server out.

- [ ] Before the credentials are set, the Server's log has one `PushChannelNotConfigured` entry for
      APNs and one for FCM at start, and the Server is healthy.
- [ ] With the credentials, the log has `PushChannelConfigured` for each provider you set up, and no
      `PushChannelCredentialsUnusable`.
- [ ] No log line contains a key, a service-account field or a device token.

## B. iPhone: build, permission and registration

Put in `apps/swift/ios/Config/Coldframe.local.xcconfig` (git ignores it), next to the Server URL
and the issuer:

```text
COLDFRAME_PUSH = YES
DEVELOPMENT_TEAM = <team id>
```

The bundle identifier is `com.escendit.coldframe` (`apps/swift/ios/project.yml`). If your App ID
differs, change `PRODUCT_BUNDLE_IDENTIFIER` there locally and set the Server's `Push__Apns__Topic`
(chart value `push.apns.topic`) to the same value. Then `xcodegen generate`, open the project, let
automatic signing add the Push Notifications capability, and run the **Debug** configuration on the
iPhone (Debug uses the APNs sandbox).

- [ ] The app builds and signs with the entitlement `aps-environment = development`.
- [ ] Fresh install, sign in, land on the Site overview: one line above the tiles reads "Coldframe
      tells you when a Lot needs water." with Continue. No system prompt has appeared yet.
- [ ] Continue shows the iOS prompt. Allow. The line is gone and does not come back, also not after
      the app is closed and opened again.
- [ ] The User's stream has `user.push-device-registered` with `platform` `Apns` and `environment`
      `Sandbox`:
      `SELECT type_alias, payload FROM journal_events WHERE stream_id = 'user/<sub>' ORDER BY version DESC LIMIT 3;`
- [ ] Closing and opening the app adds no second `user.push-device-registered`.

## C. Android: build, permission and registration

```sh
./gradlew :android:installDebug \
  -Pcoldframe.serverUrl=<server url> -Pcoldframe.keycloakIssuer=<issuer> \
  -Pcoldframe.firebaseProjectId=<project id> \
  -Pcoldframe.firebaseApplicationId=<app id> \
  -Pcoldframe.firebaseApiKey=<web api key> \
  -Pcoldframe.firebaseSenderId=<sender id>
```

- [ ] Fresh install on Android 13 or later, sign in, land on the Site overview: the same line with
      Continue, and no system prompt yet. Continue shows the system prompt. Allow. The line does
      not come back.
- [ ] (If you have one) on Android 12 or earlier there is no line and no prompt.
- [ ] The User's stream has `user.push-device-registered` with `platform` `Fcm` and no environment.
- [ ] System settings → Apps → Coldframe → Notifications shows one category, "Alerts", and its
      notification dot (badge) is off.

## D. An Alert, on both phones

Do this inside your Notification Window (My notifications; the default is 07:00 to 22:00 in your
time zone). Put both apps in the background. Let the Lot's soil Sensor read below its low Threshold
for three consecutive Readings: take the probe out of the soil, or use the Device simulator.

- [ ] Within a minute of the third Reading each phone shows **one** notification. Title:
      "‹Lot› needs water". Body: "~‹value› % in the soil, your low is ‹low› %.", the value a multiple
      of 5 with `~`. The text is the same on both phones.
- [ ] iPhone: the notification makes the default sound, the app icon shows **no badge**, and the
      notification is not marked Time Sensitive.
- [ ] Android: the notification is in the category "Alerts", under a group named after the Site,
      and the app icon shows no dot and no number.
- [ ] Tap the notification on each phone with the app in the background: the app opens on **Lot
      detail** of that Lot. Back leads to the Site overview.
- [ ] Repeat with the app closed (swipe it away first, then wait for the next notification, Part
      E): the cold start also ends on Lot detail, after the Sites have loaded.
- [ ] Sign in to a second Site, make it the current one, and tap a notification of the first Site:
      the app switches to the first Site and opens Lot detail there.
- [ ] The Server's log has one `NotificationSent` entry per User for the Alert, and no
      `NotificationChannelFailed`.

## E. Reminder, summary and grouping

- [ ] Leave the Alert open for a day (Reminder cadence Daily): each phone shows the same title with
      the body "Still ~‹value› % in the soil, your low is ‹low› %."
- [ ] Open a second Alert on another Lot of the same Site while the first is open: on the iPhone
      both notifications stack under one thread; on Android both are under the Site's group.
- [ ] Set the Notification Window to end a few minutes from now, let an Alert open after it ended,
      and set the window to open again a few minutes later (or wait for the morning): when the
      window opens, each phone shows **one** notification titled "‹Site›: 1 needs water" (or
      "‹Site›: 2 need water, 1 to check" with more Alerts), one line per Alert with the needs-water
      ones first, and the last line "Held overnight, ‹to›–‹from›" with your window's times.
- [ ] Tap the summary: the app opens the **Site overview** of that Site, not a Lot.
- [ ] Let the Lot recover (three Readings within the Thresholds): no notification arrives.

## F. Notifications off, sign-out and a dead token

- [ ] Turn notifications off for Coldframe in the phone's settings and bring the app to the front:
      My notifications starts with "Notifications are off for Coldframe on this phone. You won't
      get Alerts." and Open Settings, and the Site overview shows the same notice above the tiles.
      Neither can be dismissed.
- [ ] Open Settings opens the app's notification settings. Turn notifications on, go back to the
      app: both notices are gone without restarting it.
- [ ] Deny the prompt on a fresh install instead of allowing it: the same two notices show, and the
      prompt line does not come back.
- [ ] Sign out on one phone: the User's stream gets `user.push-device-removed` with reason
      `Requested`, and the next Alert reaches only the other phone.
- [ ] Sign in as the second User on that phone: that User's stream gets
      `user.push-device-registered`, and only that User's Alerts arrive there.
- [ ] Delete the app from one phone without signing out, then let an Alert open. The provider may
      need a day and a second push before it reports the token: the User's stream then gets
      `user.push-device-removed` with reason `Invalid`, the log has no failure for it, and later
      notifications are not sent to that token.
- [ ] Stop the Server's network access to one provider (or enter a wrong key for it) and open an
      Alert: the other phone still gets the push, and the log has one `NotificationChannelFailed`
      that names the provider and the reason and contains no token.

## Result

Record for each part: passed or failed, what was seen instead, and the Server log lines around a
failure. A wrong text, a missing group, a badge, or a tap that opens the wrong screen is a defect
of Story 6.5.
