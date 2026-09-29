---
title: 'Story 3.3: Server-side Device enrolment'
type: 'feature'
created: '2026-09-29'
baseline_revision: 'a39908e3208f31ad083f29f44fb80711673fcc44'
status: 'awaiting-operator'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-3-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-coldframe-2026-09-26/ARCHITECTURE-SPINE.md'
  - '{project-root}/apps/cs/README.md'
warnings: ['oversized']
operator_actions:
  - "Before upgrading the live cluster to the first release that contains Story 3.3, check that the Secret coldframe-enrolment-key exists in namespace coldframe with key private-key.pem; if it does not, create it as deploy/SECRETS.md describes (openssl genpkey -algorithm X25519)."
  - "Before that upgrade, run openssl rand -base64 32, record the value in a password manager or offline store kept apart from the database backups and the coldframe-backup-s3 credentials, then create the Secret coldframe-device-kek in namespace coldframe with key kek set to that value (deploy/SECRETS.md); the Server refuses to start without it."
  - "After the upgrade, confirm the server pod is Ready and that GET /enrolment-key, called with a signed-in token, returns a publicKey and a fingerprint."
deferred:
  - summary: >-
      A Device enrolled on a Site that is later deleted can never be enrolled again: re-enrolling on that Site answers 404, and every other Site answers 409.
    evidence: |-
      DeviceGrain.Enrol refuses any Site other than State.SiteId, and nothing un-enrols a Device or releases it when its Site is deleted. This follows the story's "Device already enrolled on another Site → 409" rule literally. Releasing or moving a Device (AD-2 "moved, unassigned") belongs to the later Device lifecycle and Site-deletion work.
    location: >-
      apps/cs/server/Devices/DeviceGrain.cs (Enrol, step 1)
    severity: medium
---

<intent-contract>

## Intent

**Problem:** The Server cannot enrol a Device. `GET /enrolment-key` and `POST /sites/{siteId}/devices` exist only as `x-coldframe-planned: "3.3"` contract operations. No Device grain exists, the Site grain has no Device roster, and the Server consumes neither the `coldframe-enrolment-key` Secret nor any key that encrypts `K_dev` at rest (AD-12, AD-18).

**Approach:** Serve both operations in the Edge API:
- Load the X25519 enrolment private key from a PEM.
- Open the HPKE-sealed `K_dev` with `Coldframe.Crypto.Enrolment.Open`, check that it derives the claimed Device ID, and wrap it under a separate key-encryption key (KEK).
- Hand only the wrapped key to a new journaled Device grain. It calls `Site.RegisterDevice` and then persists `DeviceEnrolled`.
- Wire both secrets through the AppHost, the Helm chart, the smokes and `deploy/SECRETS.md`.

## Boundaries & Constraints

**Always:**
- Follow the "Add an endpoint" checklist in `apps/cs/README.md:181-196`:
  - Remove `x-coldframe-planned` from both operations.
  - Map `GET /enrolment-key` with `.RequireAuthenticatedCaller()` (the contract says `Authenticated`) and `POST /sites/{siteId}/devices` with `.RequireSiteRole(SiteRole.Administrator)`.
  - Add both samples to the authorization matrix.
- Plaintext `K_dev` exists only inside the request handler. It never crosses a grain boundary, is never journaled, and is never logged. Key material appears in no log, exception message or Problem Details `detail`.
- `IDeviceGrain` (`[GrainType("device")]`, key = the 16-hex Device ID, stream `device/{id}`) takes these steps in order:
  1. Reject a Device already enrolled on a different Site.
  2. Call `ISiteGrain.RegisterDevice`.
  3. Only after a successful reply, `RaiseEvent(DeviceEnrolled)` + `ConfirmEvents()`.
- `RegisterDevice` enforces the Site-side rules without persisting anything on refusal:
  - It returns `NotFound` unless the Site is Active.
  - It enforces the per-caller Idempotency-Key rule (`{sub}:{key}`, 24 h, same as `CreateLot`). A key reused for a different Device gives `IdempotencyKeyReused`.
  - It journals `site.device-registered` only when the Device is not already on the roster.
  - Its reply carries the Site's Pause state, always "not paused" until Epic 8.
- Re-enrolling the same Device on the same Site succeeds without new events and answers 201 with the same body.
- Grain boundary types follow the Contracts conventions (`[GenerateSerializer]`, `[Alias]`, `[Id]`, outcome enums, no throwing for business refusals). New events get rows in `tests/cs/server.tests/Fixtures/journal.json`, `Apply` overloads, and the `device` prefix in `FixtureJournalReplayTests`.
- Server-side Device-path tests use `tests/cs/device-simulator` (`SimulatedDevice`) and never hand-built sealed payloads.
- The Secrets come from the fixed contract:
  - `coldframe-enrolment-key/private-key.pem` becomes env `Enrolment__PrivateKeyPem`.
  - The new `coldframe-device-kek/kek` becomes env `Enrolment__DeviceKeyEncryptionKey`.
  - Both are required, have no fallback, and are validated on start.
- Pin and lock everything. Regenerate each `packages.lock.json` that a new ProjectReference changes. `dotnet build -warnaserror`, `dotnet format --verify-no-changes`, the chart tests and all existing suites stay green.

**Never:**
- No heartbeat or HMAC verification, `/device/ingest`, Devices read-model projection or migration (Stories 3.5 and 3.7), and no Pause semantics (Epic 8).
- No key rotation or re-wrap tooling (see Design Notes).
- Don't bind siteId or kind into the HPKE AAD (rejected in 3.1).
- Don't edit `_bmad-output/implementation-artifacts/sprint-status.yaml`.
- Never commit a real private key or KEK. The AppHost derives its dev keys from generated parameters, and gitleaks must stay clean.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Get key | any signed-in caller | 200 `{publicKey: base64url(32 B, no padding), fingerprint: Enrolment.Fingerprint(publicKey)}` | no token → 401 |
| Enrol | Administrator or Owner of the Site, valid sealed body, fresh Idempotency-Key | 201 `{id, kind, siteId}`. The Site stream gets `site.device-registered` and the Device stream gets `device.enrolled` with the wrapped key | none |
| Retry | same caller, key and body | 201 with the same body, no new events | none |
| Key reused | same caller and key, a different Device | 422 `idempotency-key-reused`, nothing persisted | Problem Details |
| Same Device, same Site, new key | already enrolled here | 201 with the same body, no new events | none |
| Other Site | Device enrolled on Site A, Administrator of Site B enrols it on B | 409 `device-on-another-site`, nothing persisted on either stream | Problem Details |
| Malformed | missing field, wrong JSON, bad base64url or length, unknown `kind`, non-hex or uppercase `deviceId` | 400 `validation`, nothing persisted | Problem Details |
| Wrongly sealed | tampered `ciphertext`/`enc`, sealed to another server key, or `deviceId` ≠ ID derived from the opened `K_dev` | 400 `validation`, nothing persisted | `CryptoFailureException` is caught and never echoed |
| Missing key header | no `Idempotency-Key` | 400 `idempotency-key-missing` | Problem Details |
| Member / other Site / unknown Site | per matrix | 403 / 403 / 404 before the body is read | existing policy |

</intent-contract>

## Code Map

- `packages/openapi/coldframe.openapi.json:73-93` (getEnrolmentKey) and `:198-236` (enrolDevice) -- operations, schemas and `DeviceOnAnotherSite` (:658-661). `problem-type` `x-extensible-enum` already lists `device-on-another-site` (:603-617). `packages/openapi/README.md:17-18` has the operation table.
- `apps/cs/server/Program.cs:9-24` -- host chain. Add `AddDevices()` modelled on `apps/cs/server/Lots/LotsHostingExtensions.cs:13-34`.
- `apps/cs/server/Edge/EdgeApi.cs` -- request and response records (:16-63), `MapEdgeApi` (:88-129) with the `createLot` pattern (:112-114), `CheckIdempotencyKey` (:403-417), lenient JSON body parsing (:433-452).
- `apps/cs/server/Edge/EdgeProblems.cs` -- add the `DeviceOnAnotherSite` constant and result. `Edge/EdgeValidation.cs` holds the normalizers that return null on invalid input. `Edge/EdgeAccessRule.cs:47-64` holds the rules. `Edge/SiteAccessAuthorization.cs:29-73` canonicalizes siteId.
- `apps/cs/server/Identity/SiteGrain.cs:169-212` -- `CreateLot` is the idempotent-creation template (Active check, `{caller}:{key}`, `FindLiveLotCreation`). `Identity/SiteState.cs` has `Apply` overloads (:87-151); the next free `[Id]` is 6. `Identity/UserState.cs:31` defines `IdempotencyKeyLifetime`.
- `packages/cs/contracts/Sites/SiteGrains.cs:36-88`, `Sites/SiteEvents.cs:79-95` -- grain interface and event conventions. Put the new `Devices/` folder here (IDeviceGrain, DeviceKind, results, events).
- `apps/cs/server/Journal/JournaledStreamGrain.cs:17-52` -- base class; read time through `Clock`. `Lots/LotGrain.cs` is a child-grain example.
- `packages/cs/crypto/Hpke.cs:151` `Enrolment.Open(priv, DeviceId, enc, ct)` returns 32-byte `K_dev` or throws `CryptoFailureException`. Also `:165` `Enrolment.Fingerprint`, `X25519.cs:18` `PublicKey`, `DeviceKeys.cs:30` `DeviceId.Parse`, `:84` `KeyHierarchy.DeriveDeviceId`, and `Aead.cs:13,25` ChaCha20-Poly1305. All keys are raw 32 bytes; there is no PEM support.
- `apps/cs/server/Coldframe.Server.csproj:25-27` -- references only Contracts. Add `packages/cs/crypto`.
- `tests/cs/device-simulator/SimulatedDevice.cs:20,55,77` -- `SimulatedEnrolment(SiteId, DeviceId, Kind, Enc, Ciphertext, …)` maps onto the request body. `Create(rootKey, kind)` and `Keys.DeviceKey` give the expected `K_dev`.
- `packages/crypto-spec/vectors.json` `enrolment[]` has `rootKey`, `deviceId`, `deviceKey`. `tests/cs/crypto.tests/Vectors.cs` is internal, so copy the tiny loader or link the file.
- `tests/cs/server.tests/Edge/EdgeEndpointDiscoveryTests.cs:52-72`, `Edge/EdgeEndpointCatalog.cs` -- mapped endpoints must equal the non-planned operations. `Fixtures/journal.json`, `Journal/FixtureJournalReplayTests.cs:22-28` hold the event fixtures and the prefix map.
- `tests/cs/server.integration/Edge/AuthorizationMatrixTests.cs:31-78` -- samples; samples that need per-call setup go in the constructor. `Edge/EdgeApiFixture.cs` provides `CreateUserAsync` (:96), seeding through `AppendAsync` (:192-213), `GetParameterValueAsync` (:355), `Database`/`AliasesAsync` (:251). `Edge/LotsTests.cs` and `EdgeApiTests.AssertProblemAsync` (:217) are the HTTP test templates. `Identity/IdentityCluster.cs:112-160` is the grain test cluster and `Identity/SiteLotsAndRenameTests.cs` its example.
- `aspire/Coldframe.AppHost/AppHost.cs:35-52,98-118` -- generated secret parameters and server env.
- `deploy/SECRETS.md:24,91-92` -- contract row and create commands. `deploy/charts/server/templates/deployment.yaml:49-83` gets env from `secretKeyRef`. `deploy/charts/server/tests/secrets_test.yaml`, `deploy/charts/check-manifests.py` (via `deploy/charts/test.sh`) hold the chart checks. `deploy/charts/smoke.sh` and `deploy/fleet/smoke.sh:297-309` create the Secrets for the smokes. `docs/operations/install.md` is the install runbook.

## Tasks & Acceptance

**Execution:**
- `packages/cs/contracts/Devices/*` -- add:
  - `DeviceKind { Hub, Node }`.
  - `IDeviceGrain.Enrol(EnrolDevice)`, where `EnrolDevice` carries siteId, kind, the wrapped key, callerId and idempotencyKey. It returns `DeviceEnrolmentResult(Outcome, Device?)` with the outcomes `Enrolled`, `SiteNotFound`, `OnAnotherSite`, `IdempotencyKeyReused`.
  - `[EventType("device.enrolled")] DeviceEnrolled(SiteId, Kind, WrappedKey, EnrolledAt)`, with the wrapped key as its own serializable record.
  - In `Sites/`: `ISiteGrain.RegisterDevice(deviceId, kind, idempotencyKey)` returning `DeviceRegistrationResult(Outcome, SitePause Pause)`, and `[EventType("site.device-registered")] DeviceRegistered(DeviceId, Kind, IdempotencyKey)`. Update the event table in `packages/cs/README.md`.
- `apps/cs/server/Identity/SiteGrain.cs`, `SiteState.cs` -- `RegisterDevice` plus a roster (`[Id(6)]`) and idempotency records (`[Id(7)]`) with Apply overloads, following the `CreateLot` pattern.
- `apps/cs/server/Devices/*` -- add:
  - `DeviceGrain` + `DeviceState`.
  - `EnrolmentOptions` (section `Enrolment`: `PrivateKeyPem`, `DeviceKeyEncryptionKey`), validated on start.
  - `EnrolmentKeyring`: parses the PKCS#8 X25519 PEM and exposes the public key, the fingerprint and `Open`.
  - `DeviceKeyVault`: `Wrap(DeviceId, kDev)` and `Unwrap(DeviceId, WrappedDeviceKey)`.
  - `DevicesHostingExtensions.AddDevices()`.
- `apps/cs/server/Edge/EdgeApi.cs`, `EdgeProblems.cs`, `Program.cs`, `Coldframe.Server.csproj` -- map `getEnrolmentKey` and `enrolDevice` with the handlers described in the I/O matrix. Add the crypto reference and regenerate the lock files.
- `packages/openapi/coldframe.openapi.json`, `packages/openapi/README.md`, `apps/cs/README.md` -- remove the planned marks and update both endpoint tables. Run `pnpm --filter @coldframe/api-client generate` and commit any diff.
- `aspire/Coldframe.AppHost/AppHost.cs` -- add the generated secret parameters `enrolment-key-seed` and `device-kek` (`GenerateParameterDefault`, MinLength 32). Pass `Enrolment__DeviceKeyEncryptionKey` = device-kek, and `Enrolment__PrivateKeyPem` = a PKCS#8 PEM whose private key is SHA-256(seed), built in an async environment callback.
- `deploy/charts/server/templates/deployment.yaml`, `values.yaml` comment, `tests/secrets_test.yaml` -- add the two `secretKeyRef` env vars. `deploy/SECRETS.md` gets the new `coldframe-device-kek` row, the consumer of `coldframe-enrolment-key` changed to the `server` chart, create commands (`openssl genpkey -algorithm X25519`, `openssl rand -base64 32`) and rotation warnings. `deploy/charts/smoke.sh` and `deploy/fleet/smoke.sh` create both Secrets. `docs/operations/install.md` lists them.
- `tests/cs/server.tests/**` -- unit tests:
  - keyring PEM parsing: valid, wrong OID or length, not PEM.
  - vault round trip, and tamper or wrong Device ID failing.
  - journal fixtures for both events plus the `device` prefix.
  - discovery test passing.
- `tests/cs/server.integration/**` (add references to `device-simulator` and `crypto`, copy `vectors.json` to output) -- `Devices/EnrolmentTests.cs` covers every row of the I/O matrix over HTTP, asserting "nothing persisted" through `journal_events` of the Site and Device streams. Add both matrix samples, each with a fresh `SimulatedDevice`. Add grain tests in `IdentityCluster` for `RegisterDevice`: roster, empty Pause, idempotency, deleted Site. Register the new services in the test silo.

**Acceptance Criteria:**
- Given the Server enrolment key from the Secret, when a signed-in caller calls `GET /enrolment-key`, then it returns the X25519 public key and a fingerprint equal to `Enrolment.Fingerprint(publicKey)`.
- Given an Administrator of a Site and a `SimulatedDevice` created from the vector `enrolment[0].rootKey`, sealed to the key from `GET /enrolment-key`, when it is posted, then:
  - it answers 201;
  - the Site stream holds `site.device-registered` for `enrolment[0].deviceId`;
  - the Device stream holds one `device.enrolled` whose wrapped key unwraps (with the Server's `DeviceKeyVault` and the AppHost `device-kek` value) to `enrolment[0].deviceKey`.
- Given `ISiteGrain.RegisterDevice` on an Active Site, when it is called, then the reply is `Registered` with a not-paused Pause state, and the Site's roster, after replay, contains the Device.
- Given the edge cases in the I/O matrix, when they are submitted, then each gets the listed status and problem type, and neither stream gains an event.
- Given the discovery and matrix tests, when they run, then both operations are mapped with their contract roles, and every role and Site combination gets the expected status.

## Spec Change Log

## Review Triage Log

### 2026-09-29 — Review pass
- verdicts: 26 findings — high 1, medium 8, low 15, false 2, maybe-false 0
- findings:
  - `[low]` `[reject]` (blind) An Idempotency-Key used to re-enrol a Device already on the roster is never recorded, so reusing it for another Device within 24 h gives 201, not 422. Real, but it needs a client that reuses a key for a second Device after a same-Device retry. Recording the key needs a new event, or a change to the matrix row "Same Device, same Site, new key → no new events".
  - `[medium]` `[patch]` (blind) If the Device's write fails after the Site has journaled, the Device can end up on two Sites. Grouped as "split Site/Device write" with the edge and verification-gap rows below. The reachable trigger, a client abort cancelling the grain call, is closed: DeviceGrain now passes CancellationToken.None to RegisterDevice. The residual silo-crash or DB-failure window is recorded under Residual risks.
  - `[medium]` `[defer]` (blind) A Device whose Site was deleted can never be enrolled again. Deferred: the story's 409 rule is applied literally, and Device release or move is later lifecycle work.
  - `[low]` `[reject]` (blind) Re-enrolling with a different `kind` is silently ignored: the 201 truthfully returns the stored kind. Refusing it would add a new outcome and branch for a client error with no harm shown.
  - `[false]` `[reject]` (blind) The Site's Pause is read and then dropped. The acceptance criterion requires only that the reply carries Pause; AD-8 semantics arrive in Epic 8, and the grain test asserts the reply.
  - `[low]` `[reject]` (blind) Expired Device idempotency records are never pruned. This follows the existing `_lotCreations` pattern, costs one small entry per enrolment, and pruning would add new state logic.
  - `[medium]` `[patch]` (blind) SECRETS.md told operators to keep the KEK "with the database backups' credentials", next to the data it protects. Grouped with the next row as "KEK backup guidance". SECRETS.md and install.md now say to keep it apart from the backups and coldframe-backup-s3.
  - `[low]` `[patch]` (blind) The KEK was created inline, so the operator never saw the value they were told to back up, and the password-manager advice sat on the freely rotatable enrolment key. The operator now generates and records the value first, a kubectl read-back command is given, and the misplaced remark is removed.
  - `[low]` `[reject]` (blind) The AppHost duplicates the PKCS#8 prefix. The prefix is the fixed RFC 8410 constant, and any drift stops the Server at start, which the integration suite catches.
  - `[low]` `[reject]` (blind) Enrolment attempts are not logged. Nothing requires logging; adding it is new surface and needs a review of what is safe to log.
  - `[low]` `[reject]` (blind) The "never echoed" test only checks for the word "authenticate". All refusals share one `NotSealedToThisServer` result in code, so a stronger test adds no protection now.
  - `[low]` `[reject]` (blind) EnrolmentKeyring keeps the private key unzeroed for the process lifetime. It is a singleton that must hold the key, and zeroing at shutdown is defence in depth only (the same rejection as 3.1).
  - `[low]` `[reject]` (blind) Vectors.cs is copied into two test projects. The copies are two tiny test-only loaders, and drift would fail a test.
  - `[low]` `[reject]` (blind) DecodeBase64Url may accept non-canonical trailing bits. Any accepted form decodes to bytes that HPKE then authenticates, so nothing is gained by it.
  - `[medium]` `[patch]` (edge) A client abort or a ConfirmEvents failure leaves a split Site/Device state. Grouped with the split-write row; fixed with the CancellationToken.None patch.
  - `[medium]` `[patch]` (edge) A split state lets a Device be enrolled on a second Site. Grouped with the split-write row.
  - `[low]` `[reject]` (edge) The re-enrol key k2 is not recorded. Same as the first blind row.
  - `[low]` `[reject]` (edge) A different kind on re-enrolment is ignored. Same as the blind kind row.
  - `[low]` `[patch]` (edge) A split state followed by a retry with another kind makes the roster kind and the Device kind diverge. Grouped with the split-write row; the abort trigger is closed.
  - `[medium]` `[defer]` (edge) A Device on a deleted Site is stuck. Grouped with the blind deleted-Site row.
  - `[high]` `[patch]` (verification-gap) deploy/images/smoke.sh started the server without either Enrolment setting, so ValidateOnStart would stop it and fail the Images CI job; deploy/images/README.md did not list them. The smoke now generates a throwaway X25519 PEM and KEK and passes both, and the README has the required row.
  - `[low]` `[reject]` (verification-gap) The unrecorded re-enrol key. Same as the first blind row.
  - `[medium]` `[patch]` (verification-gap) A partial failure leaves a Device on two rosters. Grouped with the split-write row.
  - `[medium]` `[reject]` (intent) The Server now needs two Secrets that only an operator can create in the live cluster. The fix is not code: the run finalizes as awaiting-operator with operator_actions, per the invocation's rule for human-only actions.
  - `[false]` `[reject]` (intent) The epic says GET /enrolment-key is for an "authenticated Administrator", but the route is Authenticated. The route has no siteId, so no Site role can apply; the reviewed 3.1 contract and adversarial review H22 fix it as Authenticated (Design Notes).
  - `[low]` `[reject]` (intent) The end-to-end unwrap uses the Server's own DeviceKeyVault. The wrap format is Server-private by design, and the independent check is that the unwrapped key equals the vector `deviceKey`.

## Design Notes

- **Roles.** Epic text says "an authenticated Administrator" for `GET /enrolment-key`, but the reviewed 3.1 contract and the adversarial review (H22) settled on `Authenticated`. The route has no `{siteId}`, so no Site role can be enforced, and the public key is not secret. The contract governs.
- **At-rest KEK.** The KEK is a separate Secret, not derived from the enrolment key. H22 requires that rotating the enrolment key invalidates only *pending* enrolments, so stored keys must survive that rotation.
  - `kek32 = HKDF-SHA256(ikm=UTF-8(secret), salt=∅, info="coldframe/device-kek/v1", L=32)`. The secret must be at least 32 characters.
  - The wrapped key is `{kekId, nonce(12, random), sealed(48)}`, where `sealed = ChaCha20-Poly1305(kek32, nonce, aad="coldframe/device-key/v1" ‖ deviceId(8), K_dev)` and `kekId` is the first 16 hex characters of SHA-256(kek32).
  - The label is Server-private (not wire), so it lives in the Server code, not in crypto-spec.
  - KEK rotation (re-wrap events) is deferred work.
- **PEM.** `openssl genpkey -algorithm X25519` writes PKCS#8: DER prefix `302e020100300506032b656e04220420` followed by 32 key bytes. Use `PemEncoding.Find` with label `PRIVATE KEY` and reject anything else.
- **Wrong seal → 400 `validation`** (3.1 decision). An ID mismatch is also 400, because the Device ID must be the one derived from `K_dev` (AD-12 "ID derived from the eFuse-bound identity").

## Verification

**Commands:**
- `dotnet restore --locked-mode && dotnet build --no-restore -warnaserror && dotnet format --verify-no-changes` -- expected: pass
- `dotnet test --project tests/cs/server.tests && dotnet test --project tests/cs/server.integration` -- expected: pass (integration needs Docker/podman)
- `deploy/charts/test.sh` -- expected: pass (helm unittest, check-manifests)
- `pnpm --filter @coldframe/api-client generate && pnpm -r test` and `./gradlew check` -- expected: pass, no uncommitted diff

## Auto Run Result

Status: awaiting-operator

**Summary.** Story 3.3 is implemented: the Server enrols Devices.
- `GET /enrolment-key`: any signed-in caller gets the X25519 enrolment public key (base64url) and its fingerprint.
- `POST /sites/{siteId}/devices` (Administrator):
  - opens the HPKE-sealed `K_dev` with the enrolment private key;
  - checks that `K_dev` derives the claimed Device ID;
  - wraps it at once under a separate KEK (ChaCha20-Poly1305);
  - hands only the wrapped key to the new journaled Device grain (`device/{id}`).
- The Device grain refuses a Device on another Site (409). It then calls `ISiteGrain.RegisterDevice`, which owns the roster and the per-caller 24 h Idempotency-Key rule (422 on reuse) and replies with the Site's Pause (never paused until Epic 8). Only then does it journal `device.enrolled`.
- Refusals persist nothing. Plaintext `K_dev` never leaves the handler.
- Both secrets are required and validated at start, and wired through the AppHost, Helm, three smokes and `deploy/SECRETS.md`.

**Files changed (main groups).**
- `packages/cs/contracts/Devices/*` (new): `IDeviceGrain`, `DeviceKind`, `WrappedDeviceKey`, `device.enrolled`.
- `packages/cs/contracts/Sites/*`: `RegisterDevice`, `SitePause`, `site.device-registered`.
- `apps/cs/server/Devices/*` (new): Device grain and state, `EnrolmentOptions`, `EnrolmentKeyring` (PKCS#8 X25519 PEM), `DeviceKeyVault`, `AddDevices`.
- `apps/cs/server/Identity/SiteGrain.cs`, `SiteState.cs`: the roster and idempotency records.
- `apps/cs/server/Edge/EdgeApi.cs`, `EdgeProblems.cs`, `EdgeValidation.cs`: both endpoints, validation and `device-on-another-site`. `Program.cs`, the csproj and `Dockerfile` carry the Coldframe.Crypto reference; three `packages.lock.json` files are regenerated.
- `packages/openapi/coldframe.openapi.json`: planned marks removed. The READMEs (`apps/cs`, `packages/cs`, `packages/openapi`) are updated.
- `aspire/Coldframe.AppHost/AppHost.cs`: generated `enrolment-key-seed` and `device-kek` parameters.
- Deploy: `deploy/charts/server` (deployment env, values comment, `secrets_test`), `deploy/SECRETS.md`, `deploy/charts/smoke.sh`, `deploy/fleet/smoke.sh`, `deploy/images/smoke.sh` and `README.md`, `docs/operations/install.md`.
- Tests: `tests/cs/server.tests` (keyring, vault, options, response mapping, roster state, journal fixtures) and `tests/cs/server.integration` (`Devices/EnrolmentTests` covering every I/O-matrix row, `SiteDeviceRegistrationTests`, authorization-matrix samples).

**Review findings.** 26 findings: high 1, medium 8, low 15, false 2.
- **Patched (3 entries):**
  - high: the image smoke lacked the enrolment settings.
  - medium: the split Site/Device write on client abort (`CancellationToken.None`).
  - medium: the KEK backup guidance.
- **Deferred (1):** a Device on a deleted Site is stuck (medium).
- **Rejected:** everything else, with its reason in the Review Triage Log:
  - the unrecorded re-enrol key;
  - a different kind ignored on re-enrolment;
  - unpruned idempotency records;
  - the PKCS#8 prefix duplicated in the AppHost;
  - no enrolment logging;
  - the weak "never echoed" test;
  - the unzeroed singleton key;
  - duplicated Vectors.cs;
  - non-canonical base64url;
  - the self-referential unwrap check;
  - the Pause dropped (false);
  - the enrolment-key role (false);
  - the operator Secrets, handled here as operator_actions.

**Follow-up review recommended: true.** A high entry was patched. The named unverified risk is that the patched `deploy/images/smoke.sh` has only been syntax-checked (`bash -n`). Its multi-line PEM `--env` will first run in the CI Images job.

**Verification.**
- `dotnet restore --locked-mode`, `dotnet build -warnaserror` (0 warnings) and `dotnet format --verify-no-changes`: pass.
- `dotnet test --project tests/cs/server.tests`: 242/242.
- `dotnet test --project tests/cs/server.integration` (podman): 129/129, before and after the patches. The listing confirms that EnrolmentTests (9), SiteDeviceRegistrationTests (6) and the AuthorizationMatrix tests ran.
- `deploy/charts/test.sh`: 163 passed, 0 failed, with the pinned kubeconform and fleet CLI on PATH.
- `pnpm --filter @coldframe/api-client generate`: no diff, and the api-client tests pass (5).
- `./gradlew :core:jvmTest`: pass. The full `./gradlew check` cannot run here because there is no Android SDK; no Kotlin changed.

**Residual risks.**
- A silo crash or database failure between the Site's `site.device-registered` and the Device's `device.enrolled` still leaves an orphan roster entry. A retry on the same Site heals it; enrolling on another Site would double-register the Device.
- The KEK cannot be rotated: no re-wrap tooling exists, and this is documented as "do not rotate".
- Live deployments need both Secrets before upgrading (operator_actions).

