# packages/cs

.NET libraries the Server references.

| Folder | What |
| --- | --- |
| `contracts/` | `Coldframe.Contracts`: event contracts, `EventTypeAttribute`, `IEventUpcaster<TFrom, TTo>`, the Site, User and Device grain interfaces with their results, and the notification model |
| `crypto/` | `Coldframe.Crypto`: the Device crypto contract of [`packages/crypto-spec`](../crypto-spec) (key hierarchy, frame sealing and `ReplayWindow`, HPKE `Enrolment`, `SetupSession`, `Heartbeat`); `Generated/CryptoSpec.g.cs` is generated. X25519 comes from BouncyCastle, everything else from .NET |
| `protocol/` | `Coldframe.Protocol`: Google.Protobuf types compiled at build time from [`packages/proto`](../proto) |

Tests live in [`tests/cs`](../../tests/cs). Server and end-to-end tests drive the Device path through
the Device simulator in [`tests/cs/device-simulator`](../../tests/cs/device-simulator), never through
hand-built payloads.

## Event contracts

Every journaled event is a public record in `contracts/` with a stable alias and a schema version:

```csharp
[EventType("lot.renamed")]
public sealed record LotRenamed(string Name);
```

- The alias is `<entity>.<verb-ed>` in lowercase, past tense (`site.created`, `lot.removed`). It is
  written to every journal row and never changes; the record may be renamed or moved freely.
- The schema version starts at 1. A breaking payload change adds a new record with the next version and
  an `IEventUpcaster<TOld, TNew>`; the old record stays. See
  [`apps/cs/README.md`](../../apps/cs/README.md#add-an-upcaster).
- Payloads are System.Text.Json, camelCase, enums as strings, absent optional fields omitted.

Nothing here reads the clock: `DateTime.UtcNow` and its relatives fail the build.

## Sites and Users

`Sites/` holds what the Server's identity pipeline shares (Story 1.6):

| Contract | Alias | Meaning |
| --- | --- | --- |
| `SiteCreated(Name, CreatedBy)` | `site.created` | The Site exists; `CreatedBy` is a User ID (`sub`) |
| `MembershipGranted(UserId, Role)` | `site.membership-granted` | The User holds the Role on the Site from now on |
| `SiteCreationRequested(IdempotencyKey, SiteId, Name, RequestedAt)` | `user.site-creation-requested` | A Create Site request, persisted before any Keycloak call |
| `SiteCreationCompleted(IdempotencyKey, SiteId)` | `user.site-creation-completed` | The requested Site exists with the User as its Owner |
| `MembershipRevoked(UserId)` | `site.membership-revoked` | The User no longer holds any Role on the Site (Story 1.7) |
| `SiteRenamed(Name)` | `site.renamed` | The Site was renamed in Keycloak |
| `SiteDeleted()` | `site.deleted` | The Organization is gone from Keycloak; the Site is `Deleted` for good |
| `SiteOwnerlessEditRefused(KeptOwners)` | `site.ownerless-edit-refused` | Keycloak shows no Owner; the listed Owners keep Owner (once per episode) |
| `SiteOwnerlessEditResolved()` | `site.ownerless-edit-resolved` | Keycloak shows an Owner again; the episode is over |
| `SiteMembershipChanged(SiteId, Role)` | `user.site-membership-changed` | The User's Role on a Site; `null` when the User left it or it was deleted |
| `NotificationWindowChanged(FromMinutes, ToMinutes, ChangedAt)` | `user.notification-window-changed` | The User's Notification Window, wall-clock minutes since midnight (Story 6.3) |
| `TimeZoneDetected(TimeZone, DetectedAt)` | `user.time-zone-detected` | A device or browser reported a zone while the User had chosen none |
| `TimeZoneChosen(TimeZone, ChosenAt)` | `user.time-zone-chosen` | The User chose a zone; a detected one never replaces it |
| `SiteMuteChanged(SiteId, Muted, ChangedAt)` | `user.site-mute-changed` | The User muted a Site or ended the mute; only this User is affected |
| `PersonalReminderCadenceChanged(SiteId, Cadence, ChangedAt)` | `user.site-reminder-cadence-changed` | The User's own Reminder cadence for a Site; `null` uses the Site setting |
| `SiteReminderCadenceSynced(SiteId, Cadence)` | `user.site-reminder-cadence-synced` | The User grain's copy of a Site's Reminder cadence changed |
| `SiteReminderCadenceChanged(Cadence, ChangedAt)` | `site.reminder-cadence-changed` | The Site's Reminder cadence, `Daily` until changed |
| `AlertTracked(SiteId, Alert, Told, RemindFrom, TrackedAt)` | `user.alert-tracked` | The User grain learned of an open Alert on one of its Sites: told by the Alert grain (an opening notification is due) or pulled from the Site grain (Story 6.4) |
| `AlertDropped(AlertId, DroppedAt)` | `user.alert-dropped` | The User grain stopped tracking an Alert: it closed, or its Site no longer lists it |
| `DeliveryHeld(SiteId, AlertId, DueAt, WindowOpensAt)` | `user.delivery-held` | A delivery fell due outside the Notification Window and waits for its Site's summary |
| `NotificationSent(SiteId, Kind, AlertIds, DueAt, SentAt)` | `user.notification-sent` | The Notifier returned for an Alert, Reminder or Summary notification |
| `SiteAlertsPulled(SiteId, PulledAt)` | `user.site-alerts-pulled` | The User grain pulled the open Alerts of a Site it joined |

`SiteRole` is ordered `Owner > Administrator > Member` (compare with `>=`); `SiteLifecycle` is
`Uncreated`, `Active`, `Deleted`. `IUserGrain` (key: `sub`) and `ISiteGrain` (key: Site ID) return
result records for expected outcomes (created, key reused, Keycloak unavailable, conflict) instead of
throwing.

The project references `Microsoft.Orleans.Sdk`, which generates their serializers; every type that
crosses the grain boundary carries `[GenerateSerializer]` and a stable `[Alias]`.

Reconciliation (Story 1.7) adds `ISiteGrain.Reconcile(RosterExpectation?, acceptUnconfirmed)`, which
returns `SiteReconciliationResult(Outcome, Lifecycle, Members, FormerMembers)` with `Outcome` one of
`Ignored`, `Unchanged`, `Changed`, `NotYetVisible` and `IdentityProviderUnavailable`, and
`IUserGrain.SyncSiteMembership(siteId, role)`, which journals only a change. A `RosterExpectation` is
what a Keycloak event says the roster now shows: `OrganizationAbsent`, `MemberPresent`,
`MemberAbsent`, `RoleHeld` or `RoleNotHeld`, with the User and the Role it is about.

Notification settings (Story 6.3) add `ReminderCadence` (`Daily`, `Every2Days`) and `NotificationWindow`
(wall-clock minutes of one day, `Default` 07:00 to 22:00). `IUserGrain` has `GetNotificationSettings`,
`UpdateNotificationSettings(UpdateNotificationSettings)` (window, chosen zone, detected zone; outcome
`Changed`, `Unchanged`, `InvalidWindow` or `InvalidTimeZone`), `GetSiteNotificationSettings(siteId)`,
`SetSiteNotificationSettings(siteId, muted, cadence?)` and `SyncSiteReminderCadence(siteId, cadence)`.
`ISiteGrain` has `GetReminderCadence()` and `SetReminderCadence(cadence)`, whose
`SiteReminderCadenceResult(Outcome, Cadence, Members)` names the members to hand the cadence to, as
`SiteReconciliationResult.ReminderCadence` does for a reconciliation.

Delivery timing (Story 6.4) adds `IUserGrain.AlertOpened(siteId, alert)` and `AlertClosed(siteId, alertId)`,
which only the Alert grain calls. `ISiteGrain.AlertOpened` and `AlertClosed` answer
`SiteAlertReportResult(Members)`, the members the Alert grain is to tell, because the Site grain never calls
User grains. `Notifications/` holds what the User grain hands to the Server's Notifier:
`Notification(UserId, SiteId, Kind, DueAt, HeldFrom, Entries)` with `NotificationKind` `Alert`, `Reminder` or
`Summary`, and one `NotificationEntry(AlertId, Kind, Side, LotId, SensorId, DeviceId, Quantity, OpenedAt)` per
Alert. It carries no text.

## Devices

`Devices/` holds Device enrolment (Story 3.3). `IDeviceGrain` (key: the Device ID, 16 lowercase hex
digits) has `Enrol(EnrolDevice)`, which returns `DeviceEnrolmentResult(Outcome, Device)` with `Outcome`
one of `Enrolled`, `SiteNotFound`, `OnAnotherSite` and `IdempotencyKeyReused`. `EnrolDevice` carries only
the wrapped `K_dev` (`WrappedDeviceKey(KekId, Nonce, Sealed)`), never the plaintext. The Device grain calls
`ISiteGrain.RegisterDevice(deviceId, kind, idempotencyKey)`, which returns
`DeviceRegistrationResult(Outcome, Pause)` with `Outcome` one of `Registered`, `NotFound` and
`IdempotencyKeyReused`, and `SitePause` not paused until Epic 8. `DeviceKind` is `Hub` or `Node`.

`IDeviceGrain.Heartbeat(DeviceHeartbeat)` (Story 3.5) takes a Hub's heartbeat as the Edge API parsed
it (`Method`, `Path`, the raw `Body`, `TimestampMs`, `Nonce`, `Signature`, `UptimeMs?`), verifies it
inside the grain and returns `DeviceHeartbeatResult(Outcome)` with `Outcome` one of `Accepted` and
`Unauthorized` (the reason is not told).

| Contract | Alias | Meaning |
| --- | --- | --- |
| `DeviceRegistered(DeviceId, Kind, IdempotencyKey, RegisteredAt)` | `site.device-registered` | The Device joined the Site's roster; the caller-scoped key expires 24 h after `RegisteredAt` |
| `DeviceEnrolled(SiteId, Kind, WrappedKey, EnrolledAt)` | `device.enrolled` | The Device is enrolled on the Site; the Server holds its `K_dev` wrapped |
| `DeviceSeen(SeenAt, DeviceTimestampMs, UptimeMs?)` | `device.seen` | The Server accepted an authentic, new heartbeat: the Device's last-seen time; the next heartbeat must be stamped above `DeviceTimestampMs` |
