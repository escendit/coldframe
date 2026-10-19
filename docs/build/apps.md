# Build your own apps

Coldframe publishes no mobile store binaries. Each adopter builds the iOS and Android apps from
source for their own Server, with their own push credentials. This page is a short guide; Story 10.3
expands it.

## What you need

- **Your own push credentials.** iOS pushes go through the Apple Push Notification service with
  your APNs key: the Apple Developer Program, an App ID with the Push Notifications capability, and
  the same key on the Server. Android pushes go through your own Firebase Cloud Messaging project.
  No key, team ID, provisioning profile or `google-services.json` belongs in this repository.
- **The URLs of your deployment.** The Server URL and the Keycloak issuer
  (`https://<auth host>/realms/coldframe`) are build-time configuration. The apps have no field to
  enter or change them, so every build is for one deployment. An unconfigured build starts and shows
  the Unreachable notice.

## iOS

Put your values in `apps/swift/ios/Config/Coldframe.local.xcconfig` (not committed), next to
[`Coldframe.xcconfig`](../../apps/swift/ios/Config/Coldframe.xcconfig), which documents every key:
`COLDFRAME_SERVER_URL`, `COLDFRAME_KEYCLOAK_ISSUER`, and for pushes `COLDFRAME_PUSH = YES` and your
`DEVELOPMENT_TEAM`. Building, signing and push setup: [`apps/swift/README.md`](../../apps/swift/README.md).

## Android

Pass the configuration as Gradle properties, on the command line or in `~/.gradle/gradle.properties`:
`coldframe.serverUrl`, `coldframe.keycloakIssuer`, and for pushes `coldframe.firebaseProjectId`,
`coldframe.firebaseApplicationId`, `coldframe.firebaseApiKey` and `coldframe.firebaseSenderId`
(see [`apps/kt/android/build.gradle.kts`](../../apps/kt/android/build.gradle.kts)). Building and the
Firebase setup: [`apps/kt/README.md`](../../apps/kt/README.md).

## Which version

Build the apps from the same release tag as the Server you run
([`docs/operations/release.md`](../operations/release.md)).
