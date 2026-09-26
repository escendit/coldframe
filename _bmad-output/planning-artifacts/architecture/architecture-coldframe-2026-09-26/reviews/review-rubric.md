---
title: 'Rubric review: Architecture Spine, Coldframe V1'
reviewed: ARCHITECTURE-SPINE.md (status draft, updated 2026-09-26, 16 ADs)
against: prd-coldframe-2026-09-25/prd.md, .memlog.md
date: 2026-09-26
reviewer: rubric (good-spine checklist, 8 criteria)
---

# Rubric Review: Architecture Spine, Coldframe V1

## Verdict

**Revise before handing to epics.** The domain core is strong: single-writer grains, the infrastructure lanes, persisted deadlines, the ack-after-commit rule and the Device-grain gate are well-formed, enforceable rules that close real divergence points. But there is one internal contradiction that breaks the ingestion path (AD-12 replay rejection versus AD-9 resend and acknowledge). The Server-to-Node downlink (ack and time) is unauthenticated even though the spine calls the Hub untrusted. Several cross-epic contracts are left without one source: grain event and stream contracts, the SignalR/push payloads, the AEAD construction, and the BLE setup and ESP-NOW channel protocols. And most of the operational envelope (event and schema migration, backups target and restore, CI/release, firmware update, the secret mechanism, the NFR-6 test strategy) is neither decided, nor deferred, nor listed as an open question.

Counts: **2 critical, 9 high, 13 medium, 7 low.**

## Scorecard

| # | Criterion | Result |
| --- | --- | --- |
| 1 | Fixes the real divergence points, misses none | Partial: core domain yes; event/stream contracts, SignalR/push contracts, Lot status, crypto construction, and the Node/Hub radio protocols are missed |
| 2 | Every Rule is enforceable and prevents its divergence | Mostly: AD-12 ("an AEAD cipher", "mirrored on the Server") and AD-10 ("checked against") are vague; AD-3/AD-4 leave a read-your-writes gap |
| 3 | Nothing Deferred lets two units diverge | Fails on 2 rows: BLE provisioning protocol, ESP-NOW channel following |
| 4 | Named tech looks current | Mostly OK; flags on the ingress controller, missing toolchain pins, and the Phase Two licence |
| 5 | Capability map covers the PRD | Partial: NFR-2, NFR-4, NFR-5, NFR-6, NFR-11 and FR-6 (Lots) are not mapped, although `binds:` claims them |
| 6 | Altitude dimensions decided, deferred, or open | Fails: operations, CI/release, firmware update, secrets mechanism, security model, and test strategy are silent |
| 7 | Diagrams valid and consistent | All 4 parse (validated with mermaid 11.12 in headless Chromium). The dependency diagram contradicts AD-1/AD-7/AD-9 through missing edges |
| 8 | Internal consistency between ADs | Fails: AD-12 vs AD-9 (replay vs resend), the Edge API "no domain logic" vs AD-3 FR-6/7 enforcement, AD-6 invitation expiry vs AD-3 |

---

## Critical

### C1. AD-12 replay rejection contradicts AD-9 resend-and-acknowledge
- **Where:** AD-12 Sealing ("rejects a frame that ... replays an old counter"); AD-9 ("Inserting a duplicate is a no-op that is still acknowledged"); FR-4 (resend after an unacknowledged send).
- **Problem:** When an ack is lost (Hub reboot, Wi-Fi drop after the PostgreSQL commit), the Node resends the same buffered frame. The Server has already seen that counter, so under AD-12 it rejects the frame and never acks it. The Node then keeps the Reading and retries forever. Buffered frames sent after newer ones also fail a strict monotonic check. The Node epic and the Server epic will each "fix" this their own way: re-seal on resend, a sliding window, or skip the check for known frames.
- **Fix (AD-12, replace the Sealing bullet):** "Sealing happens at transmission time, not at measurement time. The Node buffers plaintext Readings (under flash encryption) and seals every transmission as a new frame with a fresh counter value. The Server rejects a frame whose counter is ≤ the last accepted counter for that Device. Deduplication of re-sent Readings relies only on the AD-9 key `(device_id, sensor_id, measured_at)`. The counter is persisted across deep sleep and reboot (RTC memory plus a periodic flash checkpoint with a forward jump of N on cold boot)."

### C2. Server-to-Node downlink (ack and Server time) is unauthenticated through an untrusted Hub
- **Where:** AD-9 (the Node deletes on ack), AD-11 (the Node sets its RTC from relayed Server time), AD-12 ("a Hub that can forge or read Readings" is prevented only on uplink), AD-16 (future commands are sealed, but acks are not).
- **Problem:** A compromised or buggy Hub can forge acks, which makes Nodes delete Readings the Server never stored (this breaks NFR-3). It can also forge time, which shifts `measured_at` and poisons the dedupe keys and Alert timing. The Hub and Node epics have no rule telling them the ack must be verified.
- **Fix (AD-12, add a bullet):** "**Downlink:** every Server→Node message (Reading ack list, Server time, future commands) is sealed or MACed by the Server under the Node's derived key and bound to the counter of the frame it answers. A Node deletes buffered Readings and adjusts its RTC only after verifying that seal. The Hub relays the downlink opaquely." Amend AD-9's ack bullet and AD-11 to reference it.

---

## High

### H1. Grain event transport, projection feed, and stream contracts are undefined
- **Where:** AD-1 ("projectors that consume grain events"), AD-5 (Orleans streams on JetStream), AD-7 ("User grain consumes the Alert events for its Sites"), AD-9/AD-15 ("JetStream is disposable").
- **Problem:** The spine never says *how* events leave a JournaledGrain: an Orleans stream, a poll of the journal table, or an outbox. It also leaves open the delivery guarantee, the stream namespace and key per event type, and how a projector recovers after JetStream is wiped (which AD-15 explicitly allows). The Alert, notification, and projection epics will each invent their own, and a disposable JetStream combined with stream-fed projectors silently loses read-model updates.
- **Fix (new AD "Event distribution"):** "The PostgreSQL event journal is the only durable event log. Grain events are published to Orleans streams from the journal (transactional outbox: append and outbox row in one transaction). Delivery is at-least-once, and every consumer is idempotent by `(streamId, sequence)`. Stream namespaces are `<entity>-events` keyed by the entity ID, plus `site-alerts` keyed by Site ID for fan-out. Projectors keep a per-projection checkpoint in PostgreSQL and can rebuild from the journal; losing JetStream costs only latency, never events."

### H2. Event serialization and event schema migration are not fixed
- **Where:** AD-2 ("Events are immutable, versioned contracts; ... superseded by a new type").
- **Problem:** Nothing fixes the serializer used for journaled events and grain state (System.Text.Json vs Orleans binary), how old event types are still read (upcasters vs keeping every version deserializable forever), or the snapshot policy. Orleans types also need stable `[Alias]` names so they survive renames. Two epics picking different serializers corrupt a shared journal table; an unconstrained rename breaks replay after upgrade.
- **Fix (AD-2, append):** "Events and snapshots are stored as System.Text.Json with an explicit `type` discriminator and `v` field; event contracts live in `packages/cs` and carry `[GenerateSerializer]` + `[Alias("<stable-name>")]`. An old event version is never deleted from code: it is either still handled by `Apply`, or mapped by an upcaster registered in one place. Journaled grains snapshot every N events (N fixed in `packages/cs`). A replay-all-events test over a fixture journal runs in CI."

### H3. Relational schema migration and Readings partition management have no owner
- **Where:** AD-9 (monthly partitions), AD-15, the Orleans ADO.NET scripts (the memlog notes manual SQL scripts).
- **Problem:** Read-model tables, the Readings table, the journal/outbox tables, and the Orleans ADO.NET schema all live in one database, but no migration tool, ordering, or runner is chosen, and nothing says who creates next month's partition. Epics will mix EF migrations, raw scripts, and startup DDL. A missing future partition makes ingestion inserts fail, which halts acks.
- **Fix (AD-15, add a bullet):** "All Server database schema, including the Orleans ADO.NET scripts, is versioned in one migration set in `apps/cs/server` (<tool, e.g. DbUp or EF Core migrations>), applied by a Kubernetes Job/init step before the silo starts, forward-only. Readings partitions are created ahead by <pg_partman | a scheduled grain/Job> at least 2 months in advance, with a default partition as a safety net."

### H4. Browser live updates: the BFF model and a direct SignalR connection conflict
- **Where:** AD-14 ("the browser holds only a session cookie"; "Live updates reach the browser through the Server's SignalR hub"); dependency diagram `WEB -->|REST + SignalR| EDGE`; containers `BROWSER --> ING --> SRV`.
- **Problem:** A browser with only a BFF session cookie cannot authenticate to the Server's SignalR hub, which validates Keycloak tokens (AD-4). The web epic and the server epic will diverge: proxy SignalR through SvelteKit, hand the browser a token (which breaks the BFF), or have the Server accept the BFF cookie.
- **Fix (AD-14, replace the last sentence):** "The browser connects to SignalR only through the SvelteKit BFF, which proxies the hub under the same origin and attaches the user's access token server-side; the Server's SignalR hub accepts only Keycloak tokens (AD-4)." (Or name the alternative explicitly. The point is to pick one.)

### H5. Cross-unit contracts with no single source: SignalR messages, push payloads, Hub auth scheme
- **Where:** AD-10 covers only Protobuf and REST/OpenAPI.
- **Problem:** SignalR hub method names and payloads (Server ↔ web), the APNs/FCM payload schema and deep-link format (Server ↔ KMP core), and the Hub request authentication scheme (AD-12 "authenticates its requests with its own derived key", undefined format) are each shared by two or more epics and have no defined source.
- **Fix (AD-10, append):** "SignalR messages and push-notification payloads (including deep-link routes) are defined as schemas in `packages/openapi` (components under `x-signalr` / `x-push`) and generated like the REST client. The Hub request-auth scheme (header name, MAC input: method, path, body hash, timestamp and nonce; clock skew window) is specified in `packages/openapi` and implemented against shared test vectors."

### H6. AEAD construction and key derivation are not pinned; "mirrored on the Server" is hand-duplication
- **Where:** AD-12 ("an AEAD cipher", "fixed, versioned labels defined in `packages/rs/crypto` and mirrored on the Server").
- **Problem:** Cipher, key size, nonce construction, AAD contents, and the frame header layout are exactly where firmware and Server diverge. "Mirrored" means two hand-written copies, which contradicts AD-10's single-source intent.
- **Fix (AD-12, replace in the Sealing bullet):** "AEAD = AES-128-CCM (S3 AES peripheral) [or name the chosen one]; nonce = device_id(6) ‖ counter(6) ‖ 0x00…; AAD = protocol version ‖ device_id ‖ counter. Labels, algorithm IDs, and **known-answer test vectors** live in one language-neutral file under `packages/proto/crypto/` (or `packages/crypto-vectors/`); both `packages/rs/crypto` and the .NET implementation must pass the same vectors in CI."

### H7. Read-your-writes gap: Role and Site changes are invisible until the Keycloak → Temporal round trip completes
- **Where:** AD-3 (only the event path writes the identity projection), AD-4 (authorization reads the projection).
- **Problem:** After "Create Site", the creator's first follow-up request (for example, create a Lot) is checked against a projection that has not yet received the event, and returns 403. The same happens after invite acceptance and Role changes. Epics will add ad-hoc retries, client sleeps, or token-claim fallbacks, and AD-4 forbids the last of these. There is also no rule for recovering from lost Keycloak events (Temporal down, listener failure): the projection drifts, and a resync would need a second writer, which AD-3 forbids.
- **Fix (AD-3, append two bullets):** "A Server command that writes to Keycloak completes only after the corresponding projection update is observed (awaited on the owning grain with a bounded timeout; on timeout it returns 202 with a status link). **Reconciliation:** a periodic and on-demand full resync reads Keycloak and emits the same synthetic events into the same Temporal→grain path, so the single-writer rule holds."

### H8. NFR-6 test strategy is absent although `binds:` claims NFR-6
- **Where:** frontmatter `binds`, AD-3, AD-4; the Capability map has no NFR-6 row.
- **Problem:** NFR-6 exists because multi-Site behavior cannot be field-tested. Without a spine-level rule, each epic tests authorization its own way (or not at all), and the Keycloak projection path, the part most likely to break, is left untested.
- **Fix (new AD "Test strategy" or AD-4 addendum):** "Every endpoint's minimum Role is declared as metadata and exercised by one generated authorization matrix test (Owner/Administrator/Member/none × Site A/Site B) that fails when an endpoint lacks a declaration. Multi-Site integration tests run the real stack slice (Orleans TestCluster + Testcontainers PostgreSQL + Keycloak with Phase Two + Temporal dev server), including the FR-7 last-Owner rule and break-glass admin-console edits. Contract tests: OpenAPI conformance for the Server, Protobuf and crypto known-answer vectors for Rust and .NET, and an event replay test (H2)." Add NFR-6 to the Capability map.

### H9. Node radio-on time depends on a Server round trip, with no latency budget or ack timeout
- **Where:** AD-9 (ack only after the PostgreSQL commit, relayed Hub→Node), NFR-4 (a ~100 µA average budget per the addendum).
- **Problem:** The Node must keep ESP-NOW listening while the Hub does TLS, HTTP, grain calls, and a PostgreSQL commit. Hub batching or a slow commit makes this seconds long. The Node epic will choose a timeout, the Hub epic a batching delay, and the Server epic a latency target, and nothing makes them agree. The result is either blown energy budgets or spurious resends.
- **Fix (AD-9, add a bullet):** "End-to-end ack budget: the Hub forwards each frame immediately (no batching delay > 200 ms) and the Server responds within 1 s p99. The Node waits at most T_ack (e.g. 2 s) and otherwise keeps the Readings for its next wake. Acks not received in time may be delivered on the next exchange (the ack list names Readings by dedupe key)." Record the numbers as the contract between the Node, Hub, and Server epics.

---

## Medium

### M1. Edge API "no domain logic" contradicts it writing to Keycloak with FR-6/FR-7 enforcement
- **Where:** layer table (Edge API: "no domain logic"), dependency diagram `EDGE -->|Admin API writes| KC`, AD-3 ("It enforces FR-6 ... FR-7 ... before writing").
- **Fix:** In AD-3, state "The **Site grain** enforces FR-6/FR-7 and performs the Keycloak Admin API write (through one `IIdentityDirectory` seam)." Redraw the arrow as `GRAINS -->|Admin API writes| KC`.

### M2. Dependency diagram omits edges the ADs require, although "anything not drawn is forbidden"
- **Missing:** `GRAINS --> Readings table / PG` (AD-9: the Device grain writes Readings), `GRAINS --> event journal` (AD-2), `GRAINS --> NATS` via the stream seam (AD-5), `GRAINS --> Notifier --> APNs/FCM/SignalR` (AD-7), `EDGE -.conforms to.-> OAPI` and `HUB -.checked against.-> OAPI` (AD-10). `READ` should read "Read models + Readings".
- **Fix:** Add these edges, or change the caption to "Arrows are allowed dependencies between layers; storage and infrastructure access follows AD-5 and AD-9."

### M3. OpenAPI direction (contract-first vs code-first) is undecided
- **Where:** AD-10 ("defined only by the OpenAPI document").
- **Fix:** Add "The OpenAPI document is hand-authored (contract-first); the Server is validated against it in CI (for example, generated endpoint stubs or a conformance test); a PR that changes the API without changing `packages/openapi` fails."

### M4. Lot status and staleness logic duplicated in KMP core and web
- **Where:** AD-14 (the KMP core implements "staleness"), FR-8 (Lot status: needs water / OK / unknown / paused).
- **Problem:** Two independent implementations of the core product decision.
- **Fix (AD-14 or AD-7):** "Lot status is computed by the Server into the Lot read model (`needs water` = an open low-side Threshold Alert on the Lot's soil-moisture Sensor; `unknown` = an open Silent Alert or no Reading; `paused` = Device paused). Clients render it; they compute only 'Server unreachable' and data age."

### M5. Lot (and the rest of Site) persistence model not decided
- **Where:** AD-2 lists Alert, Device, Sensor, User prefs, and Site "Server-owned settings"; Lot is not mentioned; the User grain's held-notification queue and deadlines are not mentioned.
- **Fix (AD-2):** State explicitly "Lot: event-sourced (created, renamed, removed); Site grain: event-sourced for Lots index, cadence, Pause; User grain: the held queue and due-at deadlines are part of its journaled state."

### M6. Site Pause vs Device Pause precedence is undefined
- **Where:** AD-8, AD-2 (Pause on both the Site and Device aggregates), FR-18.
- **Fix (AD-8, append):** "Effective pause = Site paused OR Device paused. The Site grain fans out `SitePaused/SiteResumed` to its Device grains, and each Device grain records the cause. A Device cannot be individually resumed while its Site is paused. A Site Pause end date resumes only Devices paused by the Site."

### M7. Invitation ownership conflicts between AD-3 and AD-6
- **Where:** AD-6 lists "invitation expiry" as a grain deadline; AD-3 puts Memberships in Keycloak/Phase Two, which has its own invitation entity and emails (containers: KC → SMTP).
- **Fix:** Pick one: "Invitations are Phase Two invitations; expiry is Keycloak's" (remove it from AD-6), or "Invitations are Server-owned in the Site grain; Keycloak is written only on acceptance." Say which component sends the email.

### M8. Alert identity and uniqueness not fixed
- **Where:** AD-7.
- **Fix:** "Alert ID is deterministic: `hash(subject, kind, side, openedSequence)`. The evaluating grain (Sensor or Device) holds at most one open Alert per (subject, kind, side) and is the only caller that opens or closes it, so retries cannot create duplicates." Also add "Health Alert Reminders are capped at once per day regardless of cadence (FR-13 preamble)."

### M9. Push token registration has no owner
- **Where:** AD-7 (Notifier seam), AD-14.
- **Fix:** "Push tokens (per install: platform, token, app version) are registered by the KMP core through the REST API and owned by the User grain (event-sourced); the Notifier removes tokens the provider reports as invalid via a User-grain command."

### M10. Deferred rows that span two epics: BLE setup protocol and ESP-NOW channel following / reach
- **Where:** Deferred rows 2 and 8; NFR-11 (no map row).
- **Problem:** The BLE protocol is used by the Hub *and* the Node (FR-2) *and* the KMP core; channel following requires coordinated Node and Hub behavior (beacon, re-scan, PHY/LR mode for NFR-11). "Decide in the Hub/mobile epic" or "inside the Node/Hub epics" lets two epics decide differently.
- **Fix:** Keep them deferred, but add the binding: "One BLE setup protocol serves both Hub and Node; it is decided by the first epic that needs it and its messages go in `packages/proto`. Channel discovery messages, PHY rate/LR mode, and the ESP-NOW peer/broadcast scheme are part of the `packages/proto` Node⇄Hub contract, owned by the Hub epic." Add rows for NFR-4, NFR-5, NFR-11 to the Capability map (Node firmware, `hardware/`, addendum).

### M11. Hub TLS needs wall-clock time before it can reach the time authority
- **Where:** AD-11 (Server is the time authority, time arrives in acks over TLS), AD-13 (Hub validates public certificates).
- **Problem:** Certificate validity checks need the current time, but the Hub gets time only from a TLS response. The Node's time also depends on this (and on C2). First boot and power loss hit this every time.
- **Fix (AD-11):** "The Hub obtains initial time via SNTP (router or pool) before its first TLS connection, then tracks Server time from responses; the Node does not buffer Readings before its first verified time sync (or stores a monotonic offset re-based on sync)."

### M12. Operational envelope incomplete: backups target and restore, secret mechanism, environments
- **Where:** AD-15.
- **Problem:** "backed up by CNPG" does not say where to. CNPG needs an object store (Barman Cloud plugin) or volume snapshots, and on a single node the backup must go off-node. There is no restore drill and no statement of the loss window: after a restore, Readings acked after the backup are lost, because Nodes already deleted them. And device counters rolled back by a restore reopen replay (C1). Fleet cannot do SOPS natively (memlog), so secrets being "out of band" still needs a mechanism and fixed Secret names that the charts reference.
- **Fix (AD-15, add bullets):** "Backups: CNPG Barman Cloud to an S3-compatible target off the node (adopter-provided), WAL archiving on, a documented and CI-tested restore; the stated RPO is the WAL archive interval. After a restore, Device counters are advanced by a safety margin. Secrets: Kubernetes Secrets with fixed names and keys listed in `deploy/`, created by the adopter (optionally via Sealed Secrets ≥0.36); charts never template secret values. Environments: local (Aspire), reference (RKE2); no staging." Add "Server upgrade = stop-the-world rollout (single silo) with migrations first (H3)."

### M13. CI/release, versioning, and firmware update are silent
- **Where:** none (altitude-owned dimension).
- **Problem:** No CI platform, no image registry, no version scheme across the monorepo (one version vs per-app), no firmware release artifacts, no mobile distribution model (adopters build their own apps, given push credentials), and no firmware update path (USB reflash vs OTA). The firmware update path matters because AD-10's "backward compatible within a major version" needs a version carried on the wire and a way to move Devices to a new major. Let's Encrypt root changes also require Hub firmware updates.
- **Fix:** Add AD "Build and release": "GitHub Actions; images on `ghcr.io/escendit/coldframe-*`; one SemVer per release tag across the monorepo, Helm chart appVersion = that tag; firmware released as signed binaries per tag; mobile apps built by adopters from source. Every Protobuf frame and Hub envelope carries `protocol_version`; the Server supports the current and previous major." Put the firmware update mechanism (USB-only in V1 vs OTA) in Deferred or Open Questions, noting that OTA touches AD-12 (secure boot) and AD-16 (the command path).

---

## Low

### L1. Capability map omits NFR-2, FR-6 Lots detail, NFR-4/5/6/11
`binds:` claims every FR and NFR, but the map has no rows for NFR-2 (AD-4, AD-12), NFR-6 (H8), NFR-4/5/11 (M10). Add them, or remove them from `binds:` with a reason ("hardware; covered by addendum").

### L2. Protobuf timestamp unit and dedupe precision unspecified
AD-11 says "Unix time in Protobuf". Specify `fixed64 measured_at_ms` (or `google.protobuf.Timestamp`, whose cost matters on micropb), and state that the AD-9 dedupe key uses the same precision in PostgreSQL (`timestamptz` truncated to ms).

### L3. Secure boot and flash encryption not decided
AD-12 protects the key from being read, but any firmware can *use* the HMAC peripheral. Without secure boot, reflashed firmware can sign as the Device. Add to Deferred ("Secure boot v2 + flash encryption: hardening after V1; V1 threat model accepts physical access") or decide it. This also backs C1's "plaintext buffer under flash encryption".

### L4. Server enrolment public key distribution unspecified
AD-12 has the Device encrypt to "the Server's public enrolment key", but does it arrive compiled into firmware (each adopter builds their own) or delivered by the app from the Server over TLS during BLE setup? Pick one. It affects the firmware build (NFR-7) and the KMP epic.

### L5. REST conventions missing pagination, time-range queries, and API version marker
Add to Consistency Conventions: "cursor pagination (`?cursor=&limit=`); history queries `?from=&to=` (UTC); major API version in the path `/v1/`."

### L6. Stack pins: ingress controller, toolchains, Fleet, cert-manager
The ingress controller is not named. ingress-nginx was retired upstream in March 2026, so name the RKE2 default controller (Traefik) or Gateway API and pin it. Also pin the Rust Xtensa toolchain (espup), Node.js/Svelte, Swift/Xcode minimum, Fleet (≥0.14.5 per memlog CVE note), and cert-manager. The Stack also lists Keycloak 26.6.3 while 26.7.4 is current. That is acceptable only because the Phase Two image pins it; record that as the reason, and track Phase Two security releases. (Version accuracy is for the separate version reviewer; these are only omissions.)

### L7. Phase Two keycloak-orgs is Elastic License 2.0, not open source
The memlog records this as an author decision, but the spine's Deferred row on licences mentions only the Escendit extensions, and NFR-8 promises "open source". Add one line to AD-3 or Deferred: "Phase Two keycloak-orgs is ELv2 (source-available), run as unmodified deployed software; exit path = Keycloak native Organizations (KC 26), since Site ID = Organization ID survives either."

---

## Deferred table audit (criterion 3)

| Deferred item | Can two units diverge? |
| --- | --- |
| Hub radio coexistence | No: spike gate; the target chip changes nothing in the ADs |
| BLE provisioning protocol | **Yes** (M10): spans Hub, Node, KMP core |
| Soil probe / compensation | No: raw value + Calibration ID covers it |
| DS-peripheral identity | No |
| gRPC/WebSocket | No: but a protocol version field is needed (M13) |
| Production telemetry backend | No |
| Multi-arch images | Minor: CI in earlier epics may build amd64 only. Low risk |
| ESP-NOW channel following | **Yes** (M10): Node and Hub must agree |
| Push credentials for adopters | No |
| Keycloak registration policy | No |
| Web session store | No |
| Licence files | No (see L7 for Phase Two) |

## Diagram check (criterion 7)

All four mermaid blocks parse (flowchart ×2, sequence, ER) under mermaid 11.12.0 in headless Chromium. Consistency issues: the dependency diagram has missing edges (M2) and the misplaced Keycloak write edge (M1). The containers diagram shows `BROWSER → ING → SRV` next to a BFF (H4). The Reading-path sequence has an unsealed ack/time (C2) and does not show the Hub's own Device grain heartbeat (cosmetic). The ER diagram is consistent with the Glossary and AD-9.

## What is good (keep)

- AD-5 infrastructure lanes and AD-6 "Reminders only wake": crisp, enforceable, and they head off the most likely Orleans/Temporal/NATS divergence.
- AD-9 ack-after-PostgreSQL-commit with an idempotent dedupe key, and raw value + Calibration ID storage for FR-9.
- AD-8 single gate at the Device grain, including the forward rule for NFR-9 commands.
- AD-4 one ordered Role set with one shared policy and projection-based checks (once H7 closes the lag).
- The Deferred table gives a reason for each row and names the AD that bounds it.
