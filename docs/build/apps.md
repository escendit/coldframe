# Build your own apps

Coldframe publishes no mobile store binaries. Each adopter builds the iOS and Android apps from
source, for their own deployment and with their own push credentials. The Server URL and the
Keycloak issuer are fixed at build time: the apps have no field to enter or change them, so every
build is for one deployment.

This page is step 6 of the [build guide](README.md). The Node and Hub hardware is in
[`hardware/`](../../hardware/README.md); the deployment the apps talk to is set up with the
[deploy guide](../deploy/README.md).

CI does not run the command blocks here (`<!-- ci:manual -->` in the source): most need a phone,
Xcode, the Android SDK or a cluster.

## What you need

- **Your deployment**, from the [deploy guide](../deploy/README.md), and its two URLs: the Server
  `https://api.<domain>` and the Keycloak issuer `https://auth.<domain>/realms/coldframe`. Release
  builds trust public certificates only, which is what the deployment has.
- **For Android**: JDK 25 and the Android SDK, a phone with USB debugging on, and `adb`. For
  pushes, a **Firebase project** (free).
- **For iOS**: a Mac with Xcode 26 and [XcodeGen](https://github.com/yonaskolb/XcodeGen), JDK 25
  and the Android SDK (the build runs Gradle to build the shared Kotlin core, and Gradle configures
  every project), and an iPhone. For pushes, membership of the **Apple Developer Program** (paid,
  yearly); a free personal team can install the app on your own iPhone, without pushes.
- **No credential in the repository.** No key, team ID, provisioning profile, keystore or
  `google-services.json` belongs in it; the files below that hold your values are git-ignored or
  live outside the repository.

Point Gradle at the Android SDK once, in `local.properties` in the repository root (git ignores it)
or through `ANDROID_HOME`, as in the [quickstart](../quickstart.md#run-the-android-app).

## Which version

Build the apps from the same release tag as the Server you run
([`docs/operations/release.md`](../operations/release.md)), and rebuild them after every upgrade:

<!-- ci:manual -->

```bash
git checkout vX.Y.Z
```

## Choose your ids

| What | Default | Where | May you change it? |
| --- | --- | --- | --- |
| Android application ID | `com.escendit.coldframe` | `applicationId` in [`apps/kt/android/build.gradle.kts`](../../apps/kt/android/build.gradle.kts) | Yes, alone. The Firebase Android app must use the application ID you build. |
| iOS bundle ID | `com.escendit.coldframe` | `PRODUCT_BUNDLE_IDENTIFIER` in [`apps/swift/ios/project.yml`](../../apps/swift/ios/project.yml) | Yes, alone. Your App ID and the Server's `push.apns.topic` must equal it. |
| OIDC redirect scheme | `com.escendit.coldframe` | `REDIRECT_SCHEME` in `CoreConfig.kt`, the `oidcRedirectScheme` manifest placeholder in `build.gradle.kts` and the `coldframe-mobile` client of the realm | Keep it, or change all three together. |

An Apple bundle ID is unique across all Apple developers, so `com.escendit.coldframe` may already
be taken for your team: pick your own reverse domain, for example `org.example.coldframe`. The
redirect scheme is only the app's sign-in callback; it does not have to match your ids. It is a
constant in [`CoreConfig.kt`](../../packages/kt/core/src/commonMain/kotlin/com/escendit/coldframe/core/signin/CoreConfig.kt)
(both apps), the `oidcRedirectScheme` manifest placeholder of the Android build, and the
`redirectUris` and `post.logout.redirect.uris` of the `coldframe-mobile` client in the realm
([`aspire/keycloak/realms/coldframe-realm.json`](../../aspire/keycloak/realms/coldframe-realm.json)).
Change one without the others and sign-in fails with a redirect error.

## Android

### Configure the build

The configuration is a set of Gradle properties (`coldframeProperty` in
[`build.gradle.kts`](../../apps/kt/android/build.gradle.kts)). Put them in
`~/.gradle/gradle.properties`, outside the repository, or pass each as `-P<key>=<value>`:

```properties
coldframe.serverUrl=https://api.coldframe.example.org
coldframe.keycloakIssuer=https://auth.coldframe.example.org/realms/coldframe
# The public client of the realm; this is the default.
coldframe.keycloakClientId=coldframe-mobile
# Push (optional): the Android app of your Firebase project.
coldframe.firebaseProjectId=<project id>
coldframe.firebaseApplicationId=<app id>
coldframe.firebaseApiKey=<web api key>
coldframe.firebaseSenderId=<sender id>
```

Unset values fall back to never-resolvable `.invalid` addresses, so a build without them shows
the Unreachable notice.

### Push with your Firebase project

There is no `google-services.json` and no google-services plugin: the four `coldframe.firebase*`
properties carry the same values, and the app starts Firebase from them by hand. Without all four
it starts no Firebase and registers no device; everything else works.

1. In the [Firebase console](https://console.firebase.google.com), create a project and add an
   **Android app** whose package name is the application ID you build (`com.escendit.coldframe`
   unless you changed it).
2. From Project settings → General take the **Project ID**, the Android app's **App ID** and the
   **Web API key**; from Project settings → Cloud Messaging the **Sender ID**. Those are the four
   properties above.
3. For the Server: Project settings → Service accounts → generate a private key for a service
   account with the role *Firebase Cloud Messaging API Admin*. That JSON file goes into the
   Secret `coldframe-push` ([Server push](#server-push)).

### Install a debug build (the quick path)

With the phone connected by USB and USB debugging allowed:

<!-- ci:manual -->

```bash
./gradlew :android:installDebug
```

### Install a signed release build

The release build carries no signing configuration, so you sign it with your own key after the
build, with `zipalign` and `apksigner` from the Android SDK build-tools. Create the keystore once,
**outside the repository**, and keep it and its password safe: an update must be signed with the
same key.

<!-- ci:manual -->

```bash
keytool -genkeypair -v -keystore ~/keys/coldframe-release.jks -alias coldframe \
  -keyalg RSA -keysize 4096 -validity 10000
```

Then build, align, sign and install. This block needs `ANDROID_HOME` exported to the SDK path, also
when Gradle finds the SDK through `sdk.dir` in `local.properties`; `<version>` is a folder in
`$ANDROID_HOME/build-tools`:

<!-- ci:manual -->

```bash
./gradlew :android:assembleRelease
BT="$ANDROID_HOME/build-tools/<version>"
"$BT/zipalign" -p -f 4 apps/kt/android/build/outputs/apk/release/android-release-unsigned.apk coldframe-aligned.apk
"$BT/apksigner" sign --ks ~/keys/coldframe-release.jks --ks-key-alias coldframe --out coldframe.apk coldframe-aligned.apk
"$BT/apksigner" verify coldframe.apk
adb install coldframe.apk
```

A phone that has the debug build refuses the release build (another signature): uninstall the
app first (`adb uninstall com.escendit.coldframe`, or your application ID).

## iOS

### Configure the build

Put your values in `apps/swift/ios/Config/Coldframe.local.xcconfig` (git ignores it), next to
[`Coldframe.xcconfig`](../../apps/swift/ios/Config/Coldframe.xcconfig), which documents every key.
`//` starts a comment in an xcconfig, so write every URL as `https:/$()/host`:

```text
COLDFRAME_SERVER_URL = https:/$()/api.coldframe.example.org
COLDFRAME_KEYCLOAK_ISSUER = https:/$()/auth.coldframe.example.org/realms/coldframe
// The public client of the realm; this is the default.
COLDFRAME_KEYCLOAK_CLIENT_ID = coldframe-mobile
// Signing and push.
DEVELOPMENT_TEAM = <your Apple Developer team ID>
COLDFRAME_PUSH = YES
```

Empty values fall back to never-resolvable defaults, so a build without them shows the Unreachable
notice. If you chose your own bundle ID, change `PRODUCT_BUNDLE_IDENTIFIER` in
[`project.yml`](../../apps/swift/ios/project.yml) locally.

### Push with your APNs key

- **The entitlement.** `COLDFRAME_PUSH = YES` signs the app with
  [`Config/Coldframe.entitlements`](../../apps/swift/ios/Config/Coldframe.entitlements), whose
  `aps-environment` is `development` (APNs sandbox) for Debug and `production` for Release. The
  team must be in the Apple Developer Program, and the App ID of your bundle ID needs the Push
  Notifications capability; automatic signing adds it. A Release build signed with a development
  profile needs `COLDFRAME_APS_ENVIRONMENT[config=Release] = development`. With `COLDFRAME_PUSH = NO`
  (the default) the app builds with any team, gets no device token and registers nothing.
- **The key, for the Server.** In the Apple Developer account, Certificates, Identifiers & Profiles
  → Keys, create a key with the Apple Push Notifications service. Download the `.p8` (only once),
  and note its Key ID and your Team ID. They go into the Secret `coldframe-push`
  ([Server push](#server-push)).

### Install on an iPhone

<!-- ci:manual -->

```bash
cd apps/swift/ios
xcodegen generate
open Coldframe.xcodeproj
```

In Xcode, connect the iPhone (with Developer Mode on), choose it as the run destination and Run.
The build first runs `./gradlew :core:embedAndSignAppleFrameworkForXcode`, which needs JDK 25 and
the Android SDK. Run the Debug configuration for the APNs sandbox, or archive the Release
configuration for production pushes. More on the iOS project: [`apps/swift/README.md`](../../apps/swift/README.md).

## Server push

The Server sends a push only with the credentials of each provider, from the optional Secret
`coldframe-push`: `apns-key.p8`, `apns-key-id` and `apns-team-id` for APNs, and
`fcm-service-account.json` for FCM, of the providers you use
([`deploy/SECRETS.md`](../../deploy/SECRETS.md#push-notifications)). Then enable the providers
under the `server` key of your site values, with `push.apns.topic` equal to your iOS bundle ID:

```yaml
  server: |
    identity:
      authority: https://auth.coldframe.example.org/realms/coldframe
    keycloak:
      invitationRedirectUri: https://coldframe.example.org/garden
    push:
      apns:
        enabled: true
        topic: com.escendit.coldframe
      fcm:
        enabled: true
```

Apply the values and redeploy ([`install.md`](../operations/install.md#changing-the-site-values)).
At start the Server logs `PushChannelConfigured` for each provider it can use. The end-to-end
check, with an Alert on both phones, is [`docs/bench/push-checklist.md`](../bench/push-checklist.md).

## Troubleshooting

- **The Unreachable notice** ("Can't reach your Coldframe Server") right after install: the build
  has no URLs, so it uses the `.invalid` defaults. Rebuild with the properties or the local
  xcconfig above. If the URLs are right, the phone is not on the home Wi-Fi or bypasses the LAN
  resolver ([`split-dns.md`](../operations/split-dns.md#phones-must-not-bypass-the-lan-resolver)).
- **A redirect error from Keycloak at sign-in** ("Invalid parameter: redirect_uri"): the app's
  redirect scheme and the realm's `coldframe-mobile` client do not match ([Choose your ids](#choose-your-ids)),
  or `coldframe.keycloakClientId` / `COLDFRAME_KEYCLOAK_CLIENT_ID` names another client. In the web
  app the same error means the realm was written for another origin
  ([deploy guide, section 4](../deploy/README.md#4-keycloak-realm-and-phase-two)); the realm is
  imported once, so fix an installed realm in the Keycloak admin console.
- **No push**: the Server log names the provider without credentials (`PushChannelNotConfigured`)
  or with unusable ones (`PushChannelCredentialsUnusable`); the Firebase app's package name or the
  APNs topic differs from the id you built; or the iOS build has `COLDFRAME_PUSH = NO`.
