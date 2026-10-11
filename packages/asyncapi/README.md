# packages/asyncapi

The contract for push payloads and for the SignalR messages of the open web app (AD-10, AD-14), written
before the code that sends and reads them: [`coldframe.asyncapi.json`](coldframe.asyncapi.json),
AsyncAPI 3.0.

| Message | Sent by | Read by | Since |
| --- | --- | --- | --- |
| `ApnsPush` | the Server's APNs channel, one request per iPhone registration | iOS, and the Kotlin core for tap routing | Story 6.5 |
| `FcmPush` | the Server's FCM channel, one request per Android registration | the Android messaging service, and the Kotlin core | Story 6.5 |
| `notification.delivered` | the Server's SignalR channel, to every open hub connection of the User | the web app, which shows it as a browser notification | Story 6.6 |
| `readmodel.changed` | the Server's hub, after a read model changed | the web app, which reads the open page again over REST | Story 6.6 |

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

## What the hub carries

The channel `liveHub` (`/live`) is the Server's one SignalR hub: the JSON hub protocol over Server-Sent
Events or long polling, reached by the browser only through the web app's own origin. It has exactly two
messages, both from the Server to the client, each with one argument; the message name is the SignalR
invocation target.

- **`notification.delivered`** (`NotificationDelivered`): `kind`, `siteId`, `siteName`, `lotId` and `alertId`
  (both absent in a summary), `collapseId`, `title` and `body`. The text and the collapse identity are those
  of the push for the same notification. `siteName` and `body` are always present and are empty when the
  Server found none. The web app uses `collapseId` as the notification's `tag` and routes a click like a
  tap: `alert` and `reminder` to Lot detail on that Site, `summary` and an unknown kind to that Site's
  overview. No other field may be added without changing this contract (`additionalProperties: false`).
- **`readmodel.changed`** (`ReadModelChanged`): `resource` (`sites`, `lots`, `readings`, `devices` or
  `alerts`), `id` and `version`, and nothing else: a hint never carries domain data. `id` is the Site, the
  Lot (for `lots` and `readings`), the Device or the Alert. `version` is a positive integer that never
  decreases for the same `resource` and `id`: the journal position of the event, or for `readings` the
  newest `measured_at` in Unix milliseconds.

## Fixtures

[`fixtures/`](fixtures) holds one example per kind (`push.alert.json`, `push.reminder.json`,
`push.summary.json`). Each has the request the Server sends to APNs (`apns.path`, `apns.headers`,
`apns.payload`) and to FCM (`fcm.message`), the `notification` it was written for, and `route`, where a
tap must lead. The Server's channel tests (`tests/cs/server.tests/Notifications`) require that what they
send equals the fixture, and the Kotlin core's push tests (`tests/kt/core`) require that parsing
`fcm.message.data` and `apns.payload.coldframe` gives `route`. A change to a payload therefore starts in
this folder.

The hub has its own: `live.notification.alert.json`, `live.notification.reminder.json` and
`live.notification.summary.json` (the notifications of the three push fixtures) and
`live.readmodel.changed.json`. Each has `target`, the message name, and `message`, its one argument; a
notification also has the `notification` it was written for and `route`, the path a click opens on the web.
The Server's tests (`tests/cs/server.tests/Live`) require that what the hub serialises equals `message`,
the web app's tests (`tests/ts/web`) that a click leads to `route`.

## Generated types

`pnpm --filter @coldframe/api-client generate` writes
[`packages/ts/api-client/src/live.ts`](../ts/api-client/src/live.ts) from the schemas of this contract
(openapi-typescript), with the hub's path and message names as constants. `tests/ts/api-client` fails when
that file is stale, and when a `live.*.json` fixture does not fit the schema of its message.

A change in this folder runs CI's `contracts`, `.NET`, `TypeScript` and `Web end-to-end` jobs (and the
other language jobs, as for every shared contract).
