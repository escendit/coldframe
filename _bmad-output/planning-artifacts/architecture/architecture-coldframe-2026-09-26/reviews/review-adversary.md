---
review: adversary
target: ARCHITECTURE-SPINE.md (Coldframe V1, draft 2026-09-26)
context: prds/prd-coldframe-2026-09-25/prd.md (+ addendum.md)
reviewed: 2026-09-26
lens: "Build two units one level down that obey every AD word for word and still do not fit together. Each such pair is a hole to close with a new or tightened AD."
---

# Adversarial Review: Coldframe Architecture Spine

## Verdict

**Not ready to build from.** The paradigm (one writer per grain, event-sourced aggregates, sealed frames) is sound. But the spine names owners by *entity* and never by *relationship, invariant, or cross-grain protocol*. Almost every hole below sits where two grains share one fact: Lot and Device, Site and Device, Sensor and Device and Alert, User and Site and Membership. The Device trust chain (AD-9, AD-11, AD-12) also contains two outright contradictions: counter replay rejection versus resend-and-dedupe, and an "untrusted relay" that carries unauthenticated acknowledgements and time. Either one alone can make a compliant Node firmware and a compliant Server lose data silently. **9 Critical, 10 High, 6 Medium.** Closing the Critical ones needs about 8 new or amended ADs. None of them changes the paradigm.

## Method

Each unit is an epic one level down: Node firmware (NF), Hub firmware (HF), Ingestion (ING), Sensors/Calibration/Thresholds (SCT), Alerts (AL), Notifications (NT), Device Health (DH), Pause (PA), Sites/Memberships/Identity (SMI), KMP core (KMP), iOS UI, Android UI, Web app (WEB), Deployment (DEP). For each hole, the entry gives two implementations **A** and **B**. Each one satisfies every AD as written, yet together they produce a wrong or broken system. Each entry ends with AD wording that makes one of the two illegal.

Severity:
- **Critical:** silent data loss, security break, or an invariant violated in normal operation.
- **High:** the units fail to integrate or users see wrong behavior; the fix means rework across epics.
- **Medium:** divergence that is cheap to fix early and expensive later.

## Summary

| # | Hole | Units in conflict | Severity | Closes with |
|---|------|-------------------|----------|-------------|
| H1 | AEAD counter: replay rejection vs resend; counter reset on reboot reuses nonces | NF ↔ ING | Critical | New AD-17 (frame sequence and replay) |
| H2 | Acknowledgements and Server time go through the untrusted Hub unauthenticated | HF/NF ↔ ING | Critical | Amend AD-9, AD-11, AD-12 |
| H3 | Ack granularity and partial batch failure undefined | HF ↔ ING ↔ NF | Critical | Amend AD-9 |
| H4 | Lot↔Node assignment: two grains claim the one-Node-per-Lot invariant | SCT/ING (Device) ↔ SMI (Lot) | Critical | New AD-18 (relationship ownership) |
| H5 | Alert identity: no deterministic key, so duplicate open Alerts | SCT/DH ↔ AL | Critical | Amend AD-7 |
| H6 | Alert closure has two mutation paths (Pause closes Alerts the Sensor grain owns) | PA ↔ SCT ↔ AL | Critical | Amend AD-7, AD-8 |
| H7 | Site Pause vs Device Pause: two stores of Pause state and deadlines | PA (Site) ↔ PA (Device) | Critical | Amend AD-8 |
| H8 | Create Site and the FR-7 Owner invariant race Keycloak's async event path | SMI ↔ WEB/KMP | Critical | Amend AD-3 |
| H9 | Key derivation: the Server cannot compute HMAC(eFuse root, label); "mirrored" labels | NF/HF ↔ ING ↔ KMP | Critical | Amend AD-12 |
| H10 | Node time before its first ack; the dedupe key depends on a clock that can jump | NF ↔ ING | High | Amend AD-9, AD-11 |
| H11 | Silent Hub reported as N+1 Alerts (Hub plus every Node behind it) | DH (Hub) ↔ DH (Node) | High | Amend AD-7 |
| H12 | User grain fan-out: how it learns its Sites and open Alerts (grains cannot read the projection) | NT ↔ SMI ↔ AL | High | Amend AD-3, AD-7 |
| H13 | Sensor identity and Specification (re)declaration overwrite Threshold overrides | NF ↔ SCT | High | New AD-19 (Sensor identity and Specification) |
| H14 | Calibration ID stamped by the Device grain, but Calibration owned by the Sensor grain | ING ↔ SCT | High | Amend AD-9 |
| H15 | Evaluation order: backlog and out-of-order Readings vs the three-consecutive rule | ING ↔ SCT ↔ DH | High | Amend AD-7 |
| H16 | Reminder ownership: the Health Alert once-a-day cap vs cadence; where the Site cadence comes from | NT ↔ SMI ↔ DH | High | Amend AD-7 |
| H17 | Lot status and staleness computed separately by the web and KMP clients | WEB ↔ KMP | High | Amend AD-14 |
| H18 | SignalR contract not in any schema; a browser with only a cookie cannot authenticate to the Server's SignalR | WEB ↔ Server | High | Amend AD-10, AD-14 |
| H19 | Event store, serialization, and projector feed unspecified; projections fed by disposable JetStream | AL/SCT ↔ projectors | High | Amend AD-2, AD-5 |
| H20 | Site→Device roster and Hub↔Site binding: who owns them; does a Hub's Site gate Node frames? | HF ↔ ING ↔ PA | Medium | New AD-18 (with H4) |
| H21 | Hub JSON envelope "generated from OpenAPI" is not feasible for no_std; the heartbeat contract is unowned | HF ↔ ING | Medium | Amend AD-10 |
| H22 | Where the Server enrolment public key comes from | KMP ↔ NF/HF | Medium | Amend AD-12 |
| H23 | Invitations: Keycloak/Phase Two native vs Server grain with an AD-6 deadline | SMI ↔ SMI | Medium | Amend AD-3, AD-6 |
| H24 | Entity removal and lifecycle (Lot, Device, Site, Membership) unspecified | SMI ↔ PA ↔ AL | Medium | New AD-20 |
| H25 | Client command idempotency (retried POST creates two Lots or two Sites) | WEB/KMP ↔ Edge | Medium | Conventions |

---

## Critical

### H1. AEAD counter: replay rejection vs resend; counter reset reuses nonces

- **Unit A (NF):** a Reading that is not acknowledged stays buffered. On the next wake the Node **resends the same sealed frame bytes** (cheap, and the Reading is unchanged). The counter lives in RTC RAM, so it survives deep sleep but resets on brownout or battery swap.
- **Unit B (ING):** as AD-12 says, the Server "rejects a frame that … replays an old counter". It keeps a per-Device high-water mark and rejects any counter ≤ HWM.
- **Both comply:** AD-12 says only "monotonic counter" and "reject replays". AD-9 says "duplicate insert is a no-op that is still acknowledged". A resend of the same frame is a replay by AD-12 and a duplicate by AD-9.
- **Incompatible:**
  1. A frame that was stored but whose ack was lost (the Hub's HTTP response dropped) is resent with its old counter. It is rejected and never acked, so the Node keeps it until the buffer overflows. That is silent data loss in the other direction.
  2. After a brownout the counter restarts at 0. Every frame is rejected forever, so the Node is bricked from the Server's point of view. And because the key is fixed in eFuse, **AEAD nonce reuse** under AES-GCM or ChaCha20 exposes plaintext and allows forgery.
  3. If the Node sends a live frame before its backlog, strict ordering rejects the backlog.
- **Proposed AD-17: Frame sequence, sealing parameters, and replay**
  > The sealed Node frame uses **one AEAD algorithm, fixed per key-label version** (V1: `<AES-128-CCM | ChaCha20-Poly1305>`, chosen once), with a 96-bit nonce = `device_id_hash(32) ‖ counter(64)`. The **counter never repeats for the life of the key.** The Node persists a counter reservation in flash in blocks: on boot it reads the stored ceiling, sets `counter = ceiling`, and writes `ceiling + N` before sealing any frame. So a reset skips at most N values and never reuses one. **Every transmission is freshly sealed with a new counter**, including a resend of a buffered Reading. A buffered Reading keeps its original payload (`measured_at`, value, `reading_seq`), not its sealed bytes. The Server accepts any authentic frame whose counter is above the Device's high-water mark **or** inside a 64-entry sliding window below it that has not been seen. It rejects the rest as `replayed`. Duplicate *Readings* are removed by AD-9's dedupe key, not by the counter. The counter protects the channel; the dedupe key protects the data.

### H2. Acknowledgements and Server time go through the untrusted Hub unauthenticated

- **Unit A (HF):** the Hub parses the Server's ack list and time, then sends an ESP-NOW ack `{counter, server_time}` to each Node, as AD-11 says ("the Hub passes it to the Node").
- **Unit B (NF):** on receiving an ack the Node deletes the matching buffered Readings and sets its RTC from `server_time`, as AD-9 and AD-11 say.
- **Both comply**, and the paradigm line says "The Hub is a relay that cannot read or forge **what it carries**". But AD-12 seals only Node→Server frames. The ack and the time are generated by the Hub from plaintext JSON, so the Hub *can* forge both.
- **Incompatible:** a buggy or compromised Hub (or anything that can spoof ESP-NOW from the Hub's MAC) can:
  - ack unstored Readings, so the Node deletes them. That breaks NFR-3 and the first "Prevents" of AD-9.
  - shift the Node's clock, so `measured_at` becomes wrong. That defeats AD-11, collides dedupe keys (H10), and hides Silence.

  Firmware teams following the spine will ship exactly this.
- **Proposed amendment (AD-9, AD-11, AD-12):**
  > **Downlink sealing.** The Server seals every Node-bound acknowledgement as a **Server→Node frame** defined in `packages/proto`: `{device_id, acked: [reading_seq ranges], server_time, commands: []}`. It is sealed under the Node's derived *downlink* key (AD-12 label `ack/v1`) with the Server's own counter. The Hub relays these bytes opaquely inside its HTTP response and its ESP-NOW ack. A Node **deletes buffered Readings and adjusts its RTC only on an authenticated Server→Node frame.** It ignores time and acks the Hub produces itself. The Hub's own time comes from the plaintext `serverTime` in its HTTP response (TLS-authenticated) and is used only for Hub-local timing, never stamped on data. The AD-16 commands field lives inside this sealed frame.

### H3. Ack granularity and partial batch failure undefined

- **Unit A (HF):** the Hub batches frames from several Nodes into one POST. It treats any non-2xx as "nothing stored" and a 200 as "everything stored", and acks every Node in the batch.
- **Unit B (ING):** the Server fans the envelope out to one Device grain per Node. When one grain times out it still returns 200 with a per-frame list (AD-9's diagram says "200 ack list"), and marks that frame `retry`.
- **Both comply:** AD-9 says the Server acks "a Reading" and the diagram says "200 ack list". It defines no unit of ack (Reading, frame, or envelope), no status vocabulary, and no rule for how the Hub maps HTTP status to Node acks.
- **Incompatible:** A acks the Node whose grain failed, so its Readings are lost. The reverse pairing (Server returns 500 for one bad frame) makes every Node in the batch resend forever, because one frame that fails authentication poisons the whole batch.
- **Proposed amendment to AD-9:**
  > **The unit of acknowledgement is the Reading, identified by `(device_id, reading_seq)`.** It is carried per sealed frame in a Server→Node frame (H2). The ingestion response returns HTTP 200 whenever the envelope parses. For each input frame it carries `{frameIndex, status ∈ {stored, duplicate, rejected_auth, rejected_replay, unknown_device, retry}, downlink?: base64}`. Only `stored` and `duplicate` carry a downlink ack. `rejected_*` and `unknown_device` are never retried by the Node for that sealed frame. The Node reseals the payload (H1) and, after K consecutive rejections, raises a local fault flag reported in its next frame. `retry` and transport errors are retried. HTTP 4xx is used only for an unparseable envelope or failed Hub authentication, and HTTP 5xx only when nothing was processed. The Hub never synthesizes an ack.

### H4. Lot↔Node assignment: two grains claim the one-Node-per-Lot invariant

- **Unit A (Device epic):** the Device grain owns `assigned` and `moved` (AD-2 lists them as Device events). To enforce "a Lot takes at most one Node" (FR-2), it queries the Lots read model for occupancy before emitting `DeviceAssigned`. Reading read models from the Edge is allowed (AD-1).
- **Unit B (SMI, Lot epic):** the Lot grain (AD-1 lists Lot as a grain, and AD-3 says the Site grain owns Lots) stores `nodeId` and refuses removal while it is set (FR-6). It learns `nodeId` by projecting `DeviceAssigned`.
- **Both comply.** Neither AD names the owner of the *relationship* or of the *invariant*.
- **Incompatible:**
  - Two concurrent assignments of different Nodes to one Lot both pass the read-model check, so the Lot holds two Nodes.
  - A Lot removal racing an assignment leaves a Node on a deleted Lot.
  - A "move" is one Device event but touches two Lots, so neither Lot grain transitions atomically.
  - A third reading, "Lots are just rows inside the Site grain", is also compliant and gives a third owner.
- **Proposed AD-18: Relationship and invariant ownership**
  > Every cross-entity invariant has **exactly one enforcing grain**, and the enforcement is a synchronous grain call, never a read-model check.
  > - **Lot occupancy is owned by the Lot grain** (a grain keyed by Lot ID; the Site grain holds only the Lot roster). Assigning Node *n* to Lot *L*: the Device grain calls `L.Claim(n)`. That call succeeds only if *L* exists, is not removed, and is free or already held by *n*. The Device grain then persists `DeviceAssigned{siteId, lotId}`.
  > - **Move:** `newLot.Claim(n)`, then persist `DeviceMoved`, then `oldLot.Release(n)`. Release is idempotent and retried from the Device grain's persisted pending-release until it succeeds (AD-6).
  > - **Lot removal:** the Lot grain refuses while claimed.
  > - The Device grain is the **only source** of "which Lot am I on". The Lot grain's claim is the **only source** of "is this Lot free".

### H5. Alert identity: no deterministic key, so duplicate open Alerts

- **Unit A (SCT):** on the third consecutive low Reading, the Sensor grain generates a UUIDv7 (the Conventions say Server-created entities use UUIDv7), calls `AlertGrain(id).Open(...)`, and then persists `ThresholdCrossed`.
- **Unit B (AL):** the Alert grain treats each ID as a new Alert. Downstream processing is at-least-once (AD-9: "everything downstream is idempotent").
- **Both comply.** AD-1 says grains are "keyed by their stable ID". It never says what makes an Alert ID stable.
- **Incompatible:**
  - If the Sensor grain crashes between the Alert call and its own persist, or the async Device→Sensor call is redelivered, the evaluation runs again and a fresh UUID opens a **second open Alert** for the same condition. Users get two notifications and two summary entries, which breaks FR-16's "one entry per open Alert".
  - Low and high on the same Sensor, or battery and silent on the same Device, have no rule for whether they coexist.
- **Proposed amendment to AD-7:**
  > **Alert identity is deterministic.** `alertId = UUIDv5(ns, "{subjectKind}:{subjectId}:{alertKind}:{episode}")`, where `alertKind ∈ {threshold, uncalibrated, silent, battery}` and `episode` is a counter in the evaluating grain's event-sourced state. The evaluating grain persists `…EpisodeStarted{episode}` **before** calling `Alert.Open`, and `Open` is idempotent per ID. There is at most one open Alert per `(subject, alertKind)`. A Threshold Alert records its `side`. If a Sensor crosses from low to high directly, the low Alert closes and a new episode opens. The Alert grain stores `siteId`, `lotId?`, `deviceId`, `sensorId?` as of opening; these are the fan-out keys (H12).

### H6. Alert closure has two mutation paths

- **Unit A (PA):** AD-8 says "Pausing closes the Device's open Alerts". The Device grain looks up its open Alerts, including the Sensor grains' Threshold and Uncalibrated Alerts, and calls `Alert.Close` on each.
- **Unit B (SCT):** the Sensor grain keeps its own `openAlertId` and streak counters (AD-7: it "evaluates Threshold crossings … to open and to close").
- **Both comply.** AD-7 gives open/close *evaluation* to the Sensor grain, and AD-8 gives close-on-pause to the Device grain.
- **Incompatible:**
  - After a resume the Sensor grain still believes its Alert is open, so it never reopens it. Or it tries to close an already-closed Alert and eventually leaves a zombie `openAlertId`.
  - Its streak counter carries three pre-Pause Readings into post-Pause evaluation.
  - The same problem hits FR-21: the Uncalibrated Alert opens "when a Node is assigned", which is a Device fact, but the Sensor grain evaluates it. Unassigning has no defined effect on open Alerts.
- **Proposed amendment to AD-7 and AD-8:**
  > **Only the grain that opened an Alert closes it.** The Device grain never touches Sensor-owned Alerts. It sends its Sensor grains an **evaluation context** `{assigned, lotId, paused, epoch}` on every change of assignment or Pause. `epoch` increases monotonically, and Readings forwarded to Sensor grains carry it. When `epoch` changes, a Sensor grain resets all streaks. If the new context is paused or unassigned, it closes its own open Alerts with reason `paused` or `unassigned`. If the new context is assigned and not paused, it re-evaluates Uncalibrated (FR-21). The Device grain closes only its own Silent and Battery Alerts. Close reasons are an enum on `AlertClosed`.

### H7. Site Pause vs Device Pause

- **Unit A (PA, Site side):** the Site aggregate is event-sourced for Pause (AD-2). `SitePaused{until?}` is stored on the Site grain. At ingestion the Device grain calls `Site.IsPaused()`: AD-8 still holds, because the Device grain is the gate and only reads an input.
- **Unit B (PA, Device side):** Site Pause fans out. The Site grain calls `Device.Pause(until)` on every Device, and each Device stores its own `paused` event and its own end-date deadline (AD-6).
- **Both comply.**
- **Incompatible:**
  - A: Device views and read models show "not paused" during a Site Pause. Silence Windows still run. FR-18's "open Alerts close" never happens. There is a per-Reading cross-grain call.
  - B: take a Device paused individually until May, then a Site paused until April. On the Site resume in April, is the Device resumed? B says yes (it overwrites), but intent says no. A says no. Two end-date deadlines exist for one fact.
  - "Devices added to a paused Site start paused": A needs nothing. B needs the Device to ask the Site at pairing time, which is a third path.
- **Proposed amendment to AD-8:**
  > **Pause is a set of sources on the Device grain.** `pausedBy ⊆ {device, site}`, each with its own optional `until`. A Device is paused iff `pausedBy ≠ ∅`. The **Site grain owns Site Pause** (state and `until` deadline, AD-6) and propagates it by calling `Device.SetSitePause(on/off, siteEpoch)` on every Device in its roster (H20). The call is idempotent and redelivered from a persisted pending set until acknowledged. A Device resumes, restarting its Silence Window, only when its last source clears. A Device joining a Site (H20) receives the Site's current Pause in the join reply. Only the Device grain decides "is paused". Only the Site grain decides "is the Site paused".

### H8. Create Site and the FR-7 Owner invariant race the async identity path

- **Unit A (SMI):** `POST /sites` calls Phase Two to create the Organization and add the caller as Owner, then returns `201 {siteId}`. The Site grain activates lazily on first use (virtual actor). The identity projection updates when the Keycloak → Temporal → grain event arrives (AD-3), typically seconds later.
- **Unit B (WEB/KMP):** after 201 the client immediately calls `POST /sites/{id}/lots`. The Edge checks the Role in the identity projection (AD-4), finds no Membership, and returns **403**.
- **Both comply.**
- **Incompatible:**
  - UJ-1 breaks on first use.
  - If the Keycloak call succeeds and the process dies before 201, there is an orphan Organization and the user retries, so there are two Sites.
  - **The FR-7 race:** two Owners each demote the other concurrently. Both Edge handlers read two Owners from the projection, both write to Keycloak, and zero Owners remain. AD-3 enforces FR-7 "before writing" but serializes nothing.
  - A virtual Site grain "exists" for any GUID, so the spine cannot tell a non-existent Site from an empty one.
- **Proposed amendment to AD-3:**
  > **Membership and Site writes are serialized through the Site grain.** The Edge never calls Keycloak directly for Site-scoped writes. It calls the Site grain, which checks FR-6 and FR-7 against **its own persisted Owner set**, performs the Keycloak/Phase Two call, and persists the outcome.
  > - The Site grain has a lifecycle `{Uncreated, Active, Deleted}`, and every Site-scoped call on an `Uncreated` grain fails with 404.
  > - **Create Site:** the Edge calls `UserGrain(sub).CreateSite(idempotencyKey)`. That persists `SiteCreationRequested` and creates the Organization (keyed by the idempotency key via an Organization attribute, so retries find it instead of duplicating). It then activates `SiteGrain(orgId).Initialize(owner)`, which persists `SiteCreated` and `MembershipGranted(owner)`.
  > - **Read-your-writes:** Site-grain events write the identity projection immediately. Keycloak events arriving later through Temporal are idempotent upserts that reconcile, and a disagreement raises the operator-visible error AD-3 already requires.
  >
  > (This replaces "no component writes the identity projection except that path" with "only identity events from the Site grain or the Temporal pipeline write it".)

### H9. Key derivation: the Server cannot compute HMAC(root, label)

- **Unit A (NF/HF):** following AD-12, each purpose key (seal, Hub auth) = `HMAC_eFuse(root, label)`. During enrolment it encrypts "its enrolment key" = `HMAC(root, "enrol/v1")` to the Server public key.
- **Unit B (ING):** it receives the enrolment key and derives the seal key as `HKDF(enrolKey, "seal/v1")` using the "mirrored" labels.
- **Both comply.** AD-12 says purpose keys come from the HMAC peripheral with mirrored labels, and that the Device sends "its enrolment key". It never says whether the Server derives purpose keys from the enrolment key or receives them. The Server *cannot* compute HMAC over an eFuse-held root.
- **Incompatible:**
  - A's seal key ≠ B's seal key, so every frame fails authentication.
  - "Mirrored" means hand-copied constants in Rust and C#, which AD-10's own principle forbids.
  - The KMP core relays the ciphertext but no schema for the enrolment message is named.
  - The AEAD algorithm, nonce layout (H1), and HMAC output handling are all unstated.
- **Proposed amendment to AD-12:**
  > **Key hierarchy.** The eFuse root derives exactly one key: `K_dev = HMAC-SHA256_eFuse(root, "coldframe/device/v1")`. All purpose keys are `HKDF-SHA256(K_dev, info = label)` and are computed identically by Device and Server. V1 labels: `seal/v1` (Node uplink), `ack/v1` (downlink, H2), `hub-auth/v1`. Enrolment transmits `K_dev` sealed with HPKE (RFC 9180, X25519-HKDF-SHA256-ChaCha20Poly1305) to the Server enrolment key. Labels, algorithms, and the enrolment message are defined **once** in `packages/proto` (enrolment message) and `packages/crypto-spec/` (a labels and parameters file generated into Rust and C#, plus **shared test vectors** that both CI pipelines must pass). Hub request authentication is HMAC over `method ‖ path ‖ body-hash ‖ timestamp ‖ nonce` with the `hub-auth/v1` key. The Server rejects timestamps more than ±5 min from its clock.

---

## High

### H10. Node time before its first ack; dedupe key tied to a clock that can jump

- **Unit A (NF):** from cold boot the Node measures immediately and stamps `measured_at` from an RTC reading 1970 (AD-11: set "at measurement"). It corrects itself after the first ack.
- **Unit B (ING):** it dedupes on `(device_id, sensor_id, measured_at)` (AD-9). It also rejects future-dated Readings, which is sensible.
- **Both comply**, because nothing covers a Node that has never been acked.
- **Incompatible:**
  - Pre-sync Readings are stored at 1970, falling into a missing partition or polluting history.
  - A clock step backward after sync (drift correction) makes two different Readings share `measured_at`, and the second is silently dropped as a "duplicate".
  - 24 h of deep-sleep drift is unbounded.
- **Proposed amendment to AD-9 and AD-11:**
  > **Readings carry `reading_seq`**, a per-Device 64-bit counter persisted like the frame counter (H1). The **dedupe key is `(device_id, sensor_id, reading_seq)`**. `measured_at` is data, not identity. A Node that has never received an authenticated Server time (H2) marks Readings `time_unsynced`, with `boot_id` and uptime at measurement. It sends them in the same way, and the Server rebases them using the frame's `(boot_id, uptime_now)` and arrival time. The Server rejects (`status: rejected_time`) a `measured_at` more than 5 min in the future relative to Server time. The Node slews its RTC toward Server time and never steps it backward by more than 1 s per correction.

### H11. Silent Hub reported as N+1 Alerts

- **Unit A (DH, Hub):** the Hub's Device grain opens `silent` after 5 min without a heartbeat (FR-13, AD-7).
- **Unit B (DH, Node):** each Node's Device grain opens `silent` after 6 h without Readings (AD-7: the Device grain evaluates its own Silence Window).
- **Both comply.** FR-13 says "a silent Hub is reported as the Hub, not as every Node behind it", but no AD gives any grain the knowledge or the duty to suppress. A Node grain does not even know which Hub relays it, because the Hub is an interchangeable relay (H20).
- **Proposed amendment to AD-7:**
  > On every accepted frame, a Node's Device grain records `lastRelayHubId` from the envelope's authenticated Hub. **The Node's Silence evaluation is suppressed while `lastRelayHubId` has an open `silent` Alert**. The Node grain subscribes to that Hub grain's silent/recovered notifications, and the Silence Window restarts when the Hub recovers. A Node with no relay Hub yet uses its own window.

### H12. User grain fan-out: how it learns Sites and Alerts

- **Unit A (NT):** the User grain queries the identity projection for its Sites on activation. It subscribes to an Orleans stream `alerts/{siteId}` for each.
- **Unit B (AL):** the Alert grain publishes to `alerts/{deviceId}`, because it knows its Device, not its Site. Membership events from Temporal go to "an Orleans grain" (AD-3), which here is the Site grain.
- **Both comply**, except that A reads a read model from a grain, which the diagram forbids. So a compliant A must get membership from events, and the spine never says which grain receives them.
- **Incompatible:**
  - The stream keys differ, so nothing is delivered.
  - A new Member (UJ-5) joins while Alerts are open and never hears about them.
  - A removed Member keeps its subscription.
  - A Device moved to another Site keeps notifying the old Site's Users.
  - AD-2 says the identity projection is not event-sourced, yet AD-1 says projectors consume grain events. Which grain emits the events that write the identity projection?
- **Proposed amendment to AD-3 and AD-7:**
  > Identity events from the Site grain (H8) or from the Temporal pipeline are delivered to **both** `SiteGrain(orgId)` and `UserGrain(sub)`, as `MembershipGranted/RoleChanged/MembershipRevoked`. The **User grain keeps its Site set in its own persisted state.**
  > Alert events are published on **one stream per Site, `alerts/{siteId}`**, using the Alert's `siteId` at opening (H5). The **Site grain keeps an index of open Alert IDs** by consuming that stream. On `MembershipGranted`, the User grain subscribes and pulls the open set from the Site grain. On revoke, it unsubscribes and drops held entries for that Site.
  > Stream delivery is at-least-once and handled idempotently by `alertId + eventVersion`. On activation the User grain reconciles its held set against `SiteGrain.OpenAlerts()`, because JetStream is disposable (AD-9) and cannot be the only feed.

### H13. Sensor identity and Specification (re)declaration

- **Unit A (NF):** the Node declares Specifications in the first frame after every boot ("when it first reports", FR-3). Sensors are identified by slot index 0..n.
- **Unit B (SCT):** Sensor grains use UUIDv7 IDs (the Conventions: Server-created). `SpecificationDeclared` resets thresholds to the defaults (FR-10: "a new Sensor starts with the default").
- **Both comply.**
- **Incompatible:**
  - The Node cannot put a Server-generated UUID into frames, so `sensor_id` in AD-9's dedupe key is undefined on the wire.
  - Redeclaring after each reboot, or after a firmware update that changes a default, wipes the Administrator's overrides.
  - FR-10's "cleared the default high Threshold" and "never overridden" look the same unless the override is stored as three states.
  - Readings that arrive before the declaration (it was lost, or it sits behind a backlog) reference unknown Sensors.
- **Proposed AD-19: Sensor identity and Specification**
  > `sensorId = UUIDv5(ns, "{deviceId}:{slot}")`, where `slot` is the u8 index in the Protobuf Specification. It is deterministic on both sides. Every frame carries a `spec_hash`. The full Specification set is sent when the Server's downlink (H2) reports `spec_hash` unknown. It is idempotent, and redeclaring the same hash is a no-op. A **changed** Specification on an existing slot emits `SensorSpecificationChanged`. It updates **defaults only**. Thresholds are stored as `{low: Default|Override(v)|Cleared, high: …}`, and an override is never replaced by a declaration. A change of measured quantity at a slot retires the Sensor and creates a new one with a new ID (`"{deviceId}:{slot}:{quantity}"`). Readings for an undeclared slot are stored and acked but not evaluated until the declaration arrives.

### H14. Calibration ID stamped by the Device grain, owned by the Sensor grain

- **Unit A (ING):** the Device grain stamps each Reading with its cached `calibrationId` per Sensor, loaded on activation by calling the Sensor grains.
- **Unit B (SCT):** `SensorCalibrated` is persisted in the Sensor grain and returned to the user. The Device grain learns of it through a stream event.
- **Both comply.** AD-9 says the Device grain writes Readings with "the Calibration in force"; AD-2 puts Calibration on the Sensor. The copy protocol is unspecified.
- **Incompatible:**
  - For a window after recalibration, new Readings carry the old Calibration, so a user sees values that are not what was just calibrated.
  - If the stream event is lost (disposable JetStream), the old Calibration is used forever.
  - A third compliant choice, capturing the "dry" and "wet" points from a live BLE raw read in KMP rather than from the latest stored Reading, disagrees with both on what raw value a Calibration point is.
- **Proposed amendment to AD-9:**
  > **The Sensor grain is the only writer of Calibration.** `Sensor.Calibrate` synchronously calls `Device.SetCalibrationInForce(sensorId, calibrationId)` **before** acknowledging the command, and redelivers it from persisted pending state on failure. The Device grain treats that value as a cache it may not change. Calibration reference points are **raw values of stored Readings chosen by the user** ("use latest Reading", with its `readingSeq`) and are sent over REST. No Calibration input comes over BLE.

### H15. Evaluation order: backlog and out-of-order Readings

- **Unit A (ING):** after an outage the Node uploads 24 h of backlog newest-first, which is sensible for freshness. The Device grain forwards Readings to the Sensor grain in arrival order.
- **Unit B (SCT):** it counts three consecutive Readings in the order they arrive.
- **Both comply.**
- **Incompatible:**
  - The streak interleaves old and new Readings, so Alerts flap or open for conditions that ended hours ago.
  - A backlog of 96 low Readings from yesterday arriving now opens an Alert and schedules a notification about the past.
  - Battery streaks (FR-14) have the same defect.
  - Silence liveness is ambiguous: should a replayed or backlog frame close a Silent Alert?
- **Proposed amendment to AD-7:**
  > Evaluation is **in `measured_at` order per Sensor or Device, and monotonic**. The evaluating grain keeps `lastEvaluatedAt`. A Reading older than that is stored (AD-9) but **not** evaluated. Backlog is therefore evaluated only where it extends the timeline. The Node sends backlog **oldest-first**. An Alert opened from Readings older than 2× the Silence Window is recorded with `openedAt = measured_at` and delivered like any other. Liveness for Silence is `max(measured_at)` of accepted Readings, never arrival time.

### H16. Reminder ownership and Health Alert cadence

- **Unit A (NT):** the User grain resolves cadence (User → Site → default) for *every* Alert kind. It reads the Site cadence with a direct call to the Site grain on every scheduling decision.
- **Unit B (SMI):** the Site grain owns the Site cadence (AD-2). DH assumes the "at most once per day" for Health Alerts is applied by the Alert grain, which throttles its own `AlertReminderDue` events.
- **Both comply.** AD-6 says a "next Reminder" deadline lives "in the owning grain", without naming it.
- **Incompatible:**
  - Two Reminder schedulers. A User with a 4 h cadence gets Health Reminders every 4 h (A), or both mechanisms fire.
  - "Once per day" means a 24 h interval in one unit and "next Notification Window opening" in another (UJ-2 implies the latter).
- **Proposed amendment to AD-7:**
  > **Reminder deadlines live only in the User grain**, one per `(alertId)`. Effective interval = the resolved cadence, where Site cadence is **cached in the User grain from `SiteReminderCadenceChanged`**, received with Membership events (H12). For `alertKind ≠ threshold`, the effective interval is `max(resolved, 24 h)`. The first Reminder is due `openedAt + interval`. A due-time outside the Notification Window collapses into the next opening (FR-16). The Alert grain emits no timing events.

### H17. Lot status and staleness computed separately by clients

- **Unit A (KMP):** AD-14 gives "staleness and 'Server unreachable' logic" to the KMP core. It computes Lot status: *unknown* when the last Reading is older than 2× the interval, and *needs water* when the latest value is below the low Threshold.
- **Unit B (WEB):** it has its own rule: *needs water* when a low Threshold Alert is open, and *unknown* when the Device has a Silent Alert.
- **Both comply.**
- **Incompatible:** the same Lot shows *needs water* on the phone and *OK* on the web. The latest-value rule has no hysteresis, but the Alert does (FR-11). The "Lots that need water first" ordering (FR-8) differs between the two.
- **Proposed amendment to AD-14:**
  > **Domain status is computed server-side, once**, in a projection exposed by OpenAPI: `LotStatus ∈ {needsWater, ok, unknown, paused, noNode}`, with `statusSince`, `lastReadingAt`, and sort order. `needsWater` ⇔ an open low-side Threshold Alert on a soil-moisture Sensor of the Lot's Node. `unknown` ⇔ an open Silent Alert on the Node or its relay Hub. `paused` ⇔ the Node is paused. Clients only render. Client-side logic is limited to transport staleness: the age of the last successful fetch, and Server unreachable.

### H18. SignalR contract and browser authentication

- **Unit A (WEB):** the browser connects to the Server's SignalR hub, as AD-14 says ("Live updates reach the browser through the Server's SignalR hub"). It holds only a session cookie (AD-14).
- **Unit B (Server):** the SignalR hub, like every non-Device endpoint, requires a Keycloak access token (AD-4, NFR-2).
- **Both comply**, and cannot connect.
- **Also:** message shapes are not in OpenAPI (AD-10 covers REST only). Web and Server hand-write `AlertOpened` payloads and drift.
- **Also:** SignalR has two roles: the Notifier adapter (windowed and muted, AD-7) and live read-model updates (unwindowed). One team uses the adapter's messages for live UI and hides Alerts outside the window.
- **Proposed amendment to AD-10 and AD-14:**
  > The browser connects to SignalR **through the BFF**, which proxies the WebSocket and attaches the session's access token. The Server groups connections by `sub`. SignalR messages are defined only in `packages/asyncapi/` (AsyncAPI 3), and the TS client types are generated from it. There are exactly two message families:
  > - `notification.delivered`: emitted only by the Notifier adapter, already windowed and muted.
  > - `readmodel.changed {resource, id, version}`: invalidation hints, sent to every connected Member of the Site. Clients refetch over REST, and hints carry no domain data.

### H19. Event store, serialization, and projector feed

- **Unit A (AL):** it uses `JournaledGrain` with CustomStorage (AD-2), stores events with the Orleans binary serializer (`[GenerateSerializer]`), and publishes them on an Orleans stream for projectors.
- **Unit B (projectors):** they subscribe to Orleans streams backed by JetStream, which AD-9 calls disposable. One projector epic reads JSON.
- **Both comply.**
- **Incompatible:**
  - Events are not readable outside .NET, and the serializer changes version to version.
  - If JetStream is wiped, projections diverge permanently and cannot be rebuilt.
  - "Versioned; superseded by a new type" says nothing about how old types are read (upcasting) or where schemas live.
- **Proposed amendment to AD-2 and AD-5:**
  > All journaled events go to **one PostgreSQL event table** `(stream_id, version, type, schema_version, payload jsonb, recorded_at, global_position)`, serialized as System.Text.Json from contracts in `packages/cs/events`. **Projectors read the event table by `global_position` checkpoint.** Orleans streams (NATS) are only a low-latency wake hint, and a projector must converge with streams disabled. Old event types are read through registered upcasters and never deleted. Every projection can be rebuilt from position 0.

---

## Medium

### H20. Site→Device roster and Hub↔Site binding

- **Unit A (HF/ING):** a Node's frames are accepted only through a Hub bound to the same Site ("reports through that Site's Hub", FR-2).
- **Unit B (ING, second epic):** the Hub is an untrusted, interchangeable relay (paradigm), so any enrolled Hub may carry any frame.
- **Also:** the Site grain needs a Device roster for Pause fan-out (H7) and deletion (H24). No AD says who owns the roster or how a Device joins it. Enrolment of a Hub to a Site via the app (FR-1) has no named endpoint or owner.
- **Proposed addition to AD-18:**
  > The Device grain owns `siteId`. It joins the Site by calling `Site.RegisterDevice(deviceId, kind)` before persisting `DeviceEnrolled`, and the reply carries the Site's Pause state. The Site grain owns the roster. **Any enrolled Hub may relay any Node's frames.** Hub↔Site binding is used for display and Silence suppression (H11) only, never for acceptance.

### H21. Hub envelope and heartbeat contract

AD-10 says the Hub's JSON types are "generated from or checked against" OpenAPI, but there is no no_std OpenAPI generator. The Hub epic will hand-write `serde-json-core` structs and "check" them by eye. The heartbeat is not named as an endpoint.

- **Proposed wording:** the Hub endpoints (`POST /device/ingest`, `POST /device/heartbeat`) are in OpenAPI under a `device` tag. The Hub's Rust structs are hand-written but **validated in CI by round-tripping golden JSON fixtures generated from the OpenAPI schema**. A heartbeat response has the same shape as an ingest response with no frames.

### H22. Source of the Server enrolment public key

The Device firmware could embed the Server key, which is impossible because each adopter's Server has its own. Or the app fetches it and passes it over BLE. The KMP team and the firmware team will each assume the other side supplies it.

- **Proposed wording:** the KMP core fetches `GET /enrolment-key` (authenticated, TLS to a public certificate, AD-13) and writes it to the Device over the BLE setup channel. The Device returns the HPKE ciphertext (H9). The key fingerprint is shown in the app and the web settings. Key rotation invalidates only pending enrolments.

### H23. Invitation ownership

AD-6 lists invitation expiry as a grain deadline. Phase Two also has native Organization invitations with their own expiry and email. An SMI epic could use either, and both are compliant.

- **Proposed wording:** invitations use Phase Two's native invitation flow, and the Server keeps no invitation deadline. Remove "invitation expiry" from AD-6. Or the reverse, but state which one.

### H24. Entity removal and lifecycle

Removing a Lot (FR-6), unassigning or unenrolling a Device, revoking a Membership, and deleting a Site through Keycloak break-glass have no specified effect on open Alerts, Reminders, the roster, or historical Readings. One epic hard-deletes and another tombstones.

- **Proposed AD-20:** entities are **never hard-deleted**. Removal is a `…Removed` event and a tombstone. Removal first closes the entity's open Alerts with reason `removed`, through the owning grains (H6). History stays queryable by ID. Site deletion arriving from Keycloak moves the Site grain to `Deleted` and cascades Pause-like suspension to its roster.

### H25. Client command idempotency

A mobile retry over flaky Wi-Fi creates two Lots or two Sites (worse with H8).

- **Proposed convention:** every creating `POST` accepts an `Idempotency-Key` header, which the owning grain stores for 24 h and uses to return the original result.

---

## Checklist for the spine author

1. Add **AD-17** (frame sequence, sealing parameters, replay window), **AD-18** (relationship and invariant ownership: Lot occupancy, roster, Hub relay), **AD-19** (Sensor identity and Specification), **AD-20** (removal and tombstones).
2. Amend **AD-9**: the Reading ack unit, the status vocabulary, `reading_seq` as the dedupe key, the Calibration-in-force protocol.
3. Amend **AD-11** and **AD-12**: downlink sealing (acks, time, commands), key hierarchy, HPKE enrolment, and a language-neutral crypto spec with test vectors instead of "mirrored".
4. Amend **AD-7**: deterministic Alert IDs, close-by-opener only, evaluation epoch and order, Hub-silence suppression, Reminder deadlines only in the User grain with the Health cap.
5. Amend **AD-8**: Pause as a set of sources, and Site-grain propagation.
6. Amend **AD-3**: Site-scoped writes serialized through the Site grain, read-your-writes on Create Site, identity events delivered to both Site and User grains.
7. Amend **AD-2** and **AD-5**: a JSON event table in PostgreSQL as the projector feed, and streams as hints only.
8. Amend **AD-10** and **AD-14**: an AsyncAPI source for SignalR, a BFF-proxied SignalR connection, server-computed `LotStatus`, and golden fixtures for the Hub JSON.
