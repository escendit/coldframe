# packages/kt/core

The shared Kotlin Multiplatform core (Gradle project `:core`). The overview of the whole module is
in [`packages/kt`](../README.md); this file covers Lot status and stale mode (Story 4.7). Tests
live in [`tests/kt/core`](../../../tests/kt/core).

## Lot status

The Server computes a Lot's status once (AD-14). The core mirrors the `Lot` schema of
`packages/openapi/coldframe.openapi.json` in `api/LotDto` (`OpenApiContractTest` holds the two
together) and maps it one to one to `lots/LotSummary`: `status`, `statusSince`, `lastReadingAt`,
`unknownCause` (`node`, `hub`), `pausedBy` (`device`, `site`), `pausedUntil`, `moisturePercent`
and `lowThresholdPercent`. Times become epoch milliseconds; a value this client does not know is
left out, and a status it does not know reads as `unknown`, never as fine.

| The Server decides | The core decides |
| --- | --- |
| The status of every Lot and its precedence | Whether the shown Lots are live or stale (transport only) |
| The order of the Lots; the core never re-sorts | One retry of a failed read |
| `statusSince`, `lastReadingAt`, `unknownCause`, `pausedBy`, `pausedUntil` | The last good Lots per Site, kept on the device |
| The percentages (`moisturePercent`, `lowThresholdPercent`) | How they are written: `~` and the nearest 5 % for soil moisture |
| | Which headline, counts and tile a set of statuses gets, and durations measured from the Server's timestamps |

The core counts statuses and measures time. It never derives a status, and it has no age
threshold: old data from a Server that answers is live.

### The overview model

`lots/LotsOverview.of(ready, nowEpochMs)` turns a `LotsState.Ready` into what both shells draw.
It is structured data; the strings are in the shells' catalogues.

- `LotsHeadline` — the first case that applies: `NoReadings` (no Lot has a Node), `NeedsWater`
  (with the Lot's name for one Lot, else the count), `CantBeRead` (the count of unknown and
  needs-calibration Lots), `Paused` (every Lot with a Node is paused by the Site; with the end
  only when they all share it; paused ink), `NothingNeedsWater`.
- `LotCounts` — the non-zero counts in the Server's order without needs water: needs calibration,
  unknown, OK, paused, without Node. Empty when no Lot has a Node.
- `LotTile` — one per Lot, in the Server's order: the variant (six statuses and `Stale`), the
  status label (`Silent` / `HubSilent` from `unknownCause`, `PausedBySite` when `pausedBy` has the
  Site), the value, the foot line, which spoken label applies, and their parts. The silence of an
  unknown Lot is measured from the last Reading for a silent Node and from `statusSince` for a
  silent Hub or a Lot without a Reading. A needs-calibration tile never carries a percentage. A
  field the variant does not show is `null`.
- `SoilMoisture`, `LotDuration` ("min" under 1 h, "h" under 24 h, "d" from 24 h) and `StaleAge`
  (days, hours, minutes for the stale header) are the shared formatters.

The shells build the overview again on their minute tick, so the stale age and the durations
move. The core has no timer.

### Stale mode

`lots/LotsEngine` decides it, for the Site overview only. Devices and Site settings have no stale
mode.

- **Transport failures only.** `ApiFailure.transport` is true for no answer (unreachable,
  timeout), a 5xx and an answer that is not the expected one. Nothing else is served stale.
- **Retry.** A read of the Lots, and of the Sites, that fails for a transport reason is tried once
  more, at once.
- **Entering.** When a read and its retry fail over shown Lots, they stay, with
  `staleReason = Unreachable`. This holds for `refresh()` (pull-to-refresh, coming to the front)
  and for the reload after a change.
- **403 and 404** on the Lots read drop the Site's stored Lots, and the load notice replaces the
  Lots that were showing, live or not.
- **Certificate failure** always shows its notice (`LotsState.Failed`, `SitesState.Failed`), with
  or without stored data, and is not retried.
- **401** ends the session and clears everything stored (below).
- **Leaving.** The first read that succeeds clears `staleReason`.
- **`fetchedAtEpochMs`** is the time of the last successful read, from the engine's clock
  (`now`, injected in tests). It is the "as of" of stale tiles and the "Last data" of the stale
  header, and it does not move while reads fail.
- **Cache.** Every successful read stores the Server's rows and their time under
  `lots.lastGood.‹siteId›` in the core's key-value `Settings` (the app's SharedPreferences on
  Android, `NSUserDefaults` on iOS), and every successful read of the Sites stores them under
  `sites.lastGood`. An entry that cannot be read is dropped.
- **One user's data is never shown to the next.** The core does not know a user ID (only a display
  name), so the keys are not per user. Instead the stored Sites and the stored Lots of every Site
  are removed when the session ends: on `SignInState.SignedOut` and on a 401 from any Sites or
  Lots call. A cold start that is still restoring its session removes nothing.
- **Cold start.** `sites/SitesEngine` shows the stored Sites at once
  (`SitesState.Ready.fromCache`) while it reads them, and keeps them when the read and its retry
  fail for a transport reason, so there is a current Site without the Server. A Site with stored
  Lots shows them at once with `staleReason = Cached` and `refreshing`, until the first read lands;
  when that read fails they turn `Unreachable`. Without stored Lots the state is `Loading`
  (skeleton tiles, "Loading ‹Site›"), and `Failed` with the load notice when the Server does not
  answer. Without stored Sites the Sites load notice shows, as before.
- **Back home.** `LotsEngine.refresh()` also reads the Sites again while they are `fromCache`.
- **Events.** `LotsEngine.events` sends `EnteredStale(siteId, fetchedAtEpochMs)` once when a read
  and its retry fail, and `LeftStale(siteId)` on the first success after that. Showing stored Lots
  on a cold start, a first load, a refresh that changes nothing and a further failed refresh send
  nothing. Events are not replayed.
- **In stale mode** every tile is `Stale` (no value, "as of"), a *no Node* tile does not start Add
  a Node, and `LotsState.siteMenu()` returns every Site menu item disabled ("Needs your Server").

`fromCache` is not a stale mode: the Site switcher and Create Site work on the stored list, the
Devices list shows its own notice, and Site settings reads `LotsState.stale` if it wants to say
that its Lots are not live.

iOS gets all of it through the flat `LotsSnapshot` (`IosLots.watch`, `IosLots.current()` for the
minute tick, `IosLots.watchEvents`, `IosLots.refresh()`); Android reads `AndroidSignIn.lots`.
