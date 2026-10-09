# packages/asyncapi

The contract for push payloads (AD-10, AD-14), written before the code that sends and reads them:
[`coldframe.asyncapi.json`](coldframe.asyncapi.json), AsyncAPI 3.0. SignalR messages for the web arrive
with Story 6.6.

| Message | Sent by | Read by | Since |
| --- | --- | --- | --- |
| `ApnsPush` | the Server's APNs channel, one request per iPhone registration | iOS, and the Kotlin core for tap routing | Story 6.5 |
| `FcmPush` | the Server's FCM channel, one request per Android registration | the Android messaging service, and the Kotlin core | Story 6.5 |

## What a push carries

- **Text** is written on the Server and is self-contained: `title` is the condition ("Tomatoes needs
  water"), `body` is value and context ("~20 % in the soil, your low is 30 %."). A Reminder has the Alert
  text with "Still". A summary is one notification with one line per Alert and a footer naming the held
  hours. A client shows the text as it is and never writes its own.
- **Routing** is the same five keys on both platforms (`PushRoute`): `kind` (`alert`, `reminder`,
  `summary`), `siteId`, `lotId` and `alertId` (both absent in a summary) and `collapseId`. On APNs they
  are the object `coldframe` next to `aps`; on FCM they are keys of `data`, where every value is a string.
  `alert` and `reminder` open Lot detail after switching to the Site, `summary` opens that Site's
  overview. `kind` is extensible: a client opens the overview for a kind it does not know.
- **Grouping** is per Site: `aps.thread-id` is the Site ID; on Android the app builds the notification
  from `data` with the Site as its group, because an FCM display notification cannot set one. `siteName`
  names the group.
- **No badge.** `aps` has no `badge` key, and the Android channel shows none. The interruption level is
  `active`, never time-sensitive or critical.
- **Collapse identity.** `collapseId` is the first 32 hex digits of SHA-256 over
  `<kind>:<subject>:<dueAt in Unix ms>`, where the subject is the Alert ID (the Site ID for a summary). A
  send that is repeated carries the same value and replaces the earlier one: `apns-collapse-id` on iOS,
  the notification tag on Android. No FCM `collapse_key` is set (a device keeps only four of them).
- **Lifetime.** A push that cannot be delivered is dropped by the provider after 24 h
  (`apns-expiration`, `android.ttl`).

## Fixtures

[`fixtures/`](fixtures) holds one example per kind (`push.alert.json`, `push.reminder.json`,
`push.summary.json`). Each has the request the Server sends to APNs (`apns.path`, `apns.headers`,
`apns.payload`) and to FCM (`fcm.message`), the `notification` it was written for, and `route`, where a
tap must lead. The Server's channel tests (`tests/cs/server.tests/Notifications`) require that what they
send equals the fixture, and the Kotlin core's push tests (`tests/kt/core`) require that parsing
`fcm.message.data` and `apns.payload.coldframe` gives `route`. A change to a payload therefore starts in
this folder.

CI's `contracts` job runs for a change in this folder.
