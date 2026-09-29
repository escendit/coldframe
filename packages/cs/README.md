# packages/cs

.NET libraries the Server references.

| Folder | What |
| --- | --- |
| `contracts/` | `Coldframe.Contracts`: event contracts, `EventTypeAttribute`, `IEventUpcaster<TFrom, TTo>`, and the Site and User grain interfaces with their results |
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
