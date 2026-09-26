# Review: versions and reality check, Coldframe V1 Architecture Spine

- **Lens:** every committed decision must be web-researched or reality-checked, not asserted from training data.
- **Reviewed:** `ARCHITECTURE-SPINE.md` (Stack table, AD-1…AD-16, diagrams) against `.memlog.md` `(version)` entries
- **Date:** 2026-09-26. All checks ran live against registries today: crates.io API, nuget.org v3 registration and flatcontainer, npm registry, GitHub releases/tags API, quay.io, postgresql.org, the dotnet release index, and update.rke2.io.

## Verdict

Most of the Stack table holds up against the live registries. Three items are wrong or incompatible and must be fixed before stories are cut:

- the Temporalio .NET SDK version,
- the trouble-host/esp-radio pairing,
- the Keycloak / keycloak-temporal-extensions compatibility claim.

Several more are stale or missing (Phase Two image patch, Temporal Server, the ingress default, and the unpinned cert-manager, Fleet, Google.Protobuf and AEAD crate).

---

## Blocking findings

### F1: Temporalio .NET SDK "1.2.0" is a 2024 release; the current version is 1.19.0

- **Where:** Stack row `Temporal Server / Temporalio .NET SDK | 1.31.2 / 1.2.0`. The error came from memlog line 28, which was not checked against the registry.
- **Evidence:**
  - `Temporalio` 1.2.0 was published 2024-06-27.
  - The latest is **1.19.0**, published 2026-09-14.
  - Source: https://api.nuget.org/v3-flatcontainer/temporalio/index.json and https://www.nuget.org/packages/Temporalio/1.19.0
  - GitHub latest release: https://github.com/temporalio/sdk-dotnet/releases/tag/1.19.0
  - `Escendit.Extensions.Hosting.Temporalio` 0.1.0-rc.4 already floors `Temporalio.Extensions.Hosting >= 1.14.0`, so 1.2.0 would not even satisfy the Escendit package (https://www.nuget.org/packages/Escendit.Extensions.Hosting.Temporalio/0.1.0-rc.4).
- **Correction:** `Temporalio .NET SDK | 1.19.0`. Also fix memlog line 28 with a new `(version)` entry.

### F2: trouble-host 0.8.0 is incompatible with esp-radio 1.0.0-beta.1 (bt-hci major mismatch)

- **Where:** Stack rows `esp-radio 1.0.0-beta.1` and `trouble-host 0.8.0`.
- **Evidence:**
  - esp-radio 1.0.0-beta.1 depends on `bt-hci ^0.9.0` (https://crates.io/api/v1/crates/esp-radio/1.0.0-beta.1/dependencies).
  - trouble-host 0.8.0 depends on `bt-hci ^0.10` (https://crates.io/api/v1/crates/trouble-host/0.8.0/dependencies).
  - trouble-host 0.7.0 depends on `bt-hci ^0.9`.
  - The official esp-hal BLE example at tag `esp-radio-v1.0.0-beta.1`, and on `main`, uses `trouble-host = "0.7.0"`: https://github.com/esp-rs/esp-hal/blob/esp-radio-v1.0.0-beta.1/examples/ble/bas_peripheral/Cargo.toml
  - The two bt-hci versions are distinct crates, so esp-radio's `BleConnector` controller will not satisfy trouble-host 0.8's `Controller` bound.
- **Also noted:**
  - esp-radio 1.0.0-beta.2 was published and then **yanked** (2026-09-17).
  - The latest non-prerelease esp-radio is 0.18.0, which targets esp-hal ~1.1.
  - The beta line is the only one that fits esp-hal 1.2.2. Correct, but it is a pre-release on the critical radio path.
- **Correction:** `trouble-host (BLE host) | 0.7.0 (bt-hci 0.9, matches esp-radio 1.0.0-beta.1; move to 0.8 only when esp-radio moves to bt-hci 0.10)`.

### F3: keycloak-temporal-extensions does not match the Phase Two Keycloak line as claimed

- **Where:**
  - Stack rows `Keycloak (Phase Two image) + keycloak-orgs | 26.6.3 + v0.182` and `keycloak-temporal-extensions | Keycloak 26.6.x line`.
  - AD-3.
  - Memlog line 63 ("requires Keycloak 26.6.x, Temporal SDK 1.35.0, Java 25 — matches Phase Two's Keycloak 26.6.3").
- **Evidence (the README says 26.6.x, but the build files say otherwise):**

  | Ref | `keycloak.version` | `temporal-sdk` | Java target |
  | --- | --- | --- | --- |
  | `v0.0.1-rc.0` | 26.6.4 | 1.36.1 | |
  | `v0.0.1-rc.1` | **26.7.0** | | `maven.compiler.target 21` (not Java 25) |
  | `v0.0.1-rc.2` (latest tag) | **26.7.0** | 1.37.0 | |
  | `main` (2026-08-19) | **26.7.2** | 1.38.0 | |

  - Poms: https://raw.githubusercontent.com/escendit/keycloak-temporal-extensions/v0.0.1-rc.2/pom.xml and https://raw.githubusercontent.com/escendit/keycloak-temporal-extensions/main/pom.xml
  - README "Requirements: Java 25+, Keycloak 26.6.x, Temporal SDK 1.35.0" is stale compared with the pom.
  - GitHub releases carry **no binary assets**, so adopters must build the JAR from source. That matters for NFR-7 and AD-15 ("obtainable from a public registry or repository").
  - The extension compiles against `keycloak-services`, which is not a stable public SPI across minor versions. A JAR built for 26.7 and loaded into a 26.6 server is unsupported.
- **Phase Two side:**
  - The current Phase Two image is **26.6.7**, built 2026-09-21 (https://quay.io/repository/phasetwo/phasetwo-keycloak?tab=tags).
  - `phasetwo-containers` `libs/pom.xml` pins `keycloak.version 26.6.7` and `keycloak-orgs.version 0.182` (https://github.com/p2-inc/phasetwo-containers/blob/main/libs/pom.xml).
  - keycloak-orgs v0.182 itself compiles against 26.6.3 (https://github.com/p2-inc/keycloak-orgs/blob/v0.182/pom.xml).
  - Tag `26.6.3` exists but dates from 2026-06-16.
  - Upstream community Keycloak's last 26.6 release is 26.6.4 (2026-06-26); upstream is on **26.7.4** (https://github.com/keycloak/keycloak/releases/tag/26.7.4). Phase Two builds 26.6.x patches from its own fork (`p2-inc/keycloak` `<version>_crdb`), so the "Phase Two image pins Keycloak" decision means following Phase Two's patch cadence, not upstream's.
- **Correction:**
  - `Keycloak (Phase Two image) + keycloak-orgs | quay.io/phasetwo/phasetwo-keycloak:26.6.7 + keycloak-orgs 0.182`
  - `keycloak-temporal-extensions | v0.0.1-rc.0 (built against KC 26.6.4), or a build of main with -Dkeycloak.version=26.6.7; the published rc.1/rc.2 target KC 26.7 and are NOT compatible`
  - Add a follow-up: fix the README and publish a release JAR per supported Keycloak line.

---

## Stale or missing (should fix)

### F4: Temporal Server 1.31.2 is superseded by 1.31.3 (security) and 1.32.0

- **Evidence:**
  - v1.32.0 is GitHub "latest", released 2026-09-11 (https://github.com/temporalio/temporal/releases/tag/v1.32.0).
  - v1.31.3 was released 2026-09-18 with a Go 1.26.8 security bump and security fixes (https://github.com/temporalio/temporal/releases/tag/v1.31.3).
  - 1.32 turns on the unified visibility query converter by default (breaking) and is the last release with deprecated Worker Versioning. Neither affects a Keycloak-event pipeline.
- **Correction:** `Temporal Server | 1.31.3 (or 1.32.0)`. At minimum take the 1.31.3 patch.

### F5: The ingress controller is unnamed; the RKE2 v1.36 default changed to Traefik

- **Where:** Containers diagram "Ingress + cert-manager DNS-01". AD-13 and AD-15 do not name the controller.
- **Evidence:**
  - RKE2 v1.36.4+rke2r1 release notes: "Because ingress-nginx was retired upstream as of March 2026, Traefik is now the default for new clusters starting in v1.36 … completely removed in v1.37" (https://github.com/rancher/rke2/releases/tag/v1.36.4%2Brke2r1).
  - RKE2 networking docs say the same (https://docs.rke2.io/networking/networking_services).
- **Correction:** add the Stack row `Ingress | RKE2-bundled Traefik (rke2-traefik chart)`. Charts in `deploy/` must use Traefik-compatible Ingress and IngressRoute annotations, and SignalR WebSockets must work through Traefik. Do not target ingress-nginx.

### F6: cert-manager is named but has no version, and 1.20 does not support Kubernetes 1.36

- **Evidence:**
  - Latest releases are v1.21.2 (2026-09-11) and v1.20.4 (https://github.com/cert-manager/cert-manager/releases).
  - The supported-releases table lists 1.21 for Kubernetes 1.33 → 1.36 and **1.20 for 1.32 → 1.35 only** (https://cert-manager.io/docs/releases/).
- **Correction:** add the Stack row `cert-manager | 1.21.x (1.21.2)`.

### F7: The Phase Two image patch level is stale (26.6.3, should be 26.6.7)

This is covered in F3. Listed separately so the Stack row is not missed.

### F8: Fleet is named in AD-15 but has no version (memlog records CVE-2026-41050 with a fix at 0.14.5 or later)

- **Evidence:**
  - Current Fleet releases are v0.16.2, v0.15.7 and v0.14.11, all dated 2026-09-18 (https://github.com/rancher/fleet/releases).
  - Compatibility with RKE2 v1.36 was not confirmed in this review.
- **Correction:** add the Stack row `Fleet | v0.16.2 (>= 0.14.5 for CVE-2026-41050); confirm K8s 1.36 support`.

### F9: The Escendit Orleans packages float to the lowest Orleans; the NATS.Net range is open-ended across a major version

This question was asked explicitly in the brief.

- **Escendit.Extensions.Hosting.Orleans 0.1.0-rc.4** (published 2026-06-10) targets **net10.0 only** (https://www.nuget.org/packages/Escendit.Extensions.Hosting.Orleans/0.1.0-rc.4).
  - It depends on `Microsoft.Orleans.{Server,Client,Clustering.AdoNet,Persistence.AdoNet,Reminders.AdoNet,EventSourcing,Hosting.Kubernetes} >= 10.1.0`.
  - It also depends on `Microsoft.Orleans.Streaming.NATS >= 10.1.0-alpha.1`.
  - It **also pulls `Clustering.Redis` and `Reminders.Redis`**, and so StackExchange.Redis, which Coldframe does not use.
  - It has **no direct NATS.Net dependency**. NATS.Net arrives transitively through Streaming.NATS.
  - Answer: yes, it targets Orleans 10.x (floor 10.1.0) and is compatible with the pinned 10.3.1.
- **Escendit.AspNetCore.Builder.Orleans 0.1.0-rc.0** has the same shape (Orleans >= 10.1.0, Streaming.NATS >= 10.1.0-alpha.1, Redis clustering).
- **Microsoft.Orleans.Streaming.NATS 10.3.1-alpha.1** depends on `NATS.Net >= 2.7.2`. That is a floor, not an exact pin, as is the 10.1.0-alpha.1 dependency (https://www.nuget.org/packages/Microsoft.Orleans.Streaming.NATS/10.3.1-alpha.1).
  - The latest NATS.Net 2.x is 2.8.2.
  - NATS.Net **3.2.0** is the latest stable; 3.0.0 was published 2026-07-10.
  - NuGet resolves the lowest applicable version (2.7.2) unless something else in the graph references 3.x. If it does, the graph silently unifies to 3.x under an alpha provider compiled against 2.x.
- **Correction:** use Central Package Management with explicit pins:
  - `Microsoft.Orleans.* 10.3.1`, so Escendit's `>= 10.1.0` floor does not resolve to 10.1.0
  - `Microsoft.Orleans.Streaming.NATS 10.3.1-alpha.1`
  - `NATS.Net 2.8.2`, or 2.7.2, but never 3.x until the provider moves

  Add the Stack row `NATS.Net (transitive, pinned) | 2.x (2.7.2–2.8.2)`. Consider asking the Escendit packages to drop their hard Redis provider dependencies.

### F10: Google.Protobuf is committed in AD-10 but missing from the Stack table

- **Evidence:** the latest stable is 3.36.2. **4.0.0-rc2** is in pre-release (https://www.nuget.org/packages/Google.Protobuf).
- **Correction:** add the Stack row `Google.Protobuf | 3.36.2 (stay on 3.x until 4.0 GA)`.

### F11: SvelteKit patch lag (minor)

- **Evidence:** `@sveltejs/kit` latest is **2.70.3** (2026-08-18). Svelte latest is 5.57.1 (https://www.npmjs.com/package/@sveltejs/kit).
- `@escendit/sveltekit-auth-keycloak` and `@escendit/sveltekit-session` are at **0.1.0-rc.12** (2026-08-25). They are named in AD-14 but have no Stack version.
- **Correction:** `SvelteKit / Svelte | 2.70.3 / 5.57.1`. Add `@escendit/sveltekit-auth-keycloak + -session | 0.1.0-rc.12`.

### F12: Aspire patch lag (minor)

- **Evidence:** Aspire 13.5.4 (2026-09-15; https://github.com/microsoft/aspire/releases). `Aspire.Hosting.AppHost` 13.5.4 is on nuget.
- **Correction:** `Aspire (dev AppHost) | 13.5.4`.

### F13: The deferred licence item is already resolved

- **Where:** the Deferred row "Licence files for keycloak-temporal-extensions and extensions-workflows" and memlog line 437.
- **Evidence:** the GitHub API now reports **Apache-2.0** for both repositories, and a LICENSE file exists in keycloak-temporal-extensions (https://api.github.com/repos/escendit/keycloak-temporal-extensions, https://api.github.com/repos/escendit/extensions-workflows).
- **Correction:** remove the row from Deferred and record a new memlog entry.

---

## Fit checks asked in the brief

### micropb and Google.Protobuf (proto3): fits

- micropb 0.6.0 (with micropb-gen 0.6.0) generates code from `.proto`, is no_std and no_alloc, and handles proto3 and Editions (`field_presence`, `repeated_field_encoding`) (https://github.com/YuhanLiin/micropb).
- Limitations that matter here:
  - no extensions,
  - no closed enums (proto3 enums are open, so this is fine),
  - `string`, `bytes` and `repeated` need configured fixed capacities (heapless or arrayvec),
  - needs `protoc` at build time,
  - MSRV 1.88.
- The wire format is standard, so frames interoperate with Google.Protobuf.
- **Advice:** add a round-trip conformance test (Rust encode → .NET decode) to `packages/proto` CI. Declare capacity annotations in a shared micropb config next to the `.proto` files, so Node and Hub cannot disagree on them.

### no_std AEAD for ESP32-S3 sealing: available, not pinned

- RustCrypto `aes-gcm` 0.11.1, `chacha20poly1305` 0.11.0 and `ccm` 0.6.1 are all `#![no_std]`. Build them with `default-features = false`, because the defaults enable `alloc` and `getrandom` (https://crates.io/crates/aes-gcm, https://crates.io/crates/chacha20poly1305).
- On the Server, .NET 10 `System.Security.Cryptography.AesGcm` and `ChaCha20Poly1305` cover the other end.
- AD-12 says only "an AEAD cipher". Leaving the cipher open is a cross-unit consistency risk, because firmware and Server must agree.
- **Correction:**
  - Name the cipher in AD-12, for example AES-128-GCM or ChaCha20-Poly1305, with a nonce built from the counter.
  - Add the Stack row `AEAD (firmware) | aes-gcm 0.11.1 or chacha20poly1305 0.11.0, default-features=false`.
  - esp-hal's hardware AES sits behind `unstable` and is not a RustCrypto `BlockCipher` drop-in. Software AES or ChaCha is the safe default.

### Hub HTTP and TLS stack: glue required

- reqwless 0.14.0's built-in TLS is **embedded-tls ^0.18** (the default feature), not mbedtls-rs and not embedded-tls 0.19 (https://crates.io/api/v1/crates/reqwless/0.14.0/dependencies).
- Using mbedtls-rs 0.3.0, which the Stack names as the Hub TLS client, means disabling reqwless's `embedded-tls` feature. It also means supplying an `embedded-nal-async` `TcpConnect` whose connection is an mbedtls-rs TLS session.
- Both sides use embedded-io 0.7, so this is feasible. It is still unowned glue.
- **Correction:** add to the Hub epic, or as a note in the Stack row, "reqwless with `default-features = false` over an mbedtls-rs session (custom TcpConnect)". Alternatively choose embedded-tls 0.18 and accept its certificate-verification limits against AD-13's public-roots requirement.

### Sealed Secrets: consistent

- AD-15 creates secrets out of band and names no Sealed Secrets or External Secrets.
- The memlog mention (Sealed Secrets >= 0.36.0) is research only and does not leak into the spine. No action.

---

## Confirmed as written (live-verified today)

| Item | Spine | Live evidence |
| --- | --- | --- |
| .NET 10 LTS | 10 (LTS) | release index: 10.0 `lts`, `active`, latest 10.0.12, EOL 2028-11-14. 11.0 is an STS RC (`go-live`, 11.0.0-rc.1). Staying on 10 is right. https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json |
| PostgreSQL 18 | 18 | 18.6 current, EOL 2030-11-14. 19 is at **Beta 4**. CNPG 1.30.1 default image `postgresql:18.6-system-trixie`. Keycloak 26.6.3 CI tests on `postgres:18`. Temporal docs say "PostgreSQL 12+". https://www.postgresql.org/versions.json |
| CloudNativePG | 1.30.1 | v1.30.1 (2026-09-23). 1.30.x supports K8s 1.34–1.36 and PG 14–18. 1.29 EOL 2026-09-29. |
| RKE2 | v1.36 line | `stable` channel = v1.36.4+rke2r1. `latest` = v1.37.0. https://update.rke2.io/v1-release/channels |
| Orleans | 10.3.1 | nuget latest 10.3.1 |
| Streaming.NATS | 10.3.1-alpha.1 | nuget latest, published 2026-08-28 |
| NATS Server | 2.15.0 | v2.15.0 (2026-09-17) |
| Escendit.Extensions.Hosting.* / AspNetCore.Builder.* | 0.1.0-rc.4 / 0.1.0-rc.0 | nuget latest, all net10.0 (see F9) |
| esp-hal | 1.2.2 | crates.io max 1.2.2 (2026-09-18) |
| mbedtls-rs | 0.3.0 | crates.io 0.3.0 (2026-09-14) |
| micropb | 0.6.0 | crates.io 0.6.0 (2026-01-19) |
| reqwless | 0.14.0 | crates.io 0.14.0 (2026-01-12) |
| Kotlin | 2.4.20 | GitHub latest stable v2.4.20 (2026-09-07) |
| Kable | 0.45.0 | v0.45.0 (2026-09-15) |
| kotlin-multiplatform-oidc | 0.18.3 | 0.18.3 (2026-09-23) |
| keycloak-orgs | v0.182 | tag v0.182 (2026-09-21), KC 26.6.3 in pom |

## Suggested Stack table (corrected)

| Name | Version |
| --- | --- |
| Rust firmware HAL: esp-hal (ESP32-S3; `unstable` for HMAC/AES/SHA) | 1.2.2 |
| esp-radio (Wi-Fi, BLE, ESP-NOW, coex) | 1.0.0-beta.1 (pre-release; beta.2 yanked) |
| trouble-host (BLE host) | **0.7.0** (bt-hci 0.9, matches esp-radio) |
| mbedtls-rs (Hub TLS client) | 0.3.0 |
| reqwless (Hub HTTP client) | 0.14.0, `default-features = false`, over mbedtls-rs |
| micropb / micropb-gen (Protobuf, no_std) | 0.6.0 |
| AEAD (firmware) | **aes-gcm 0.11.1** or chacha20poly1305 0.11.0 (no default features); cipher fixed in AD-12 |
| .NET | 10 (LTS; 10.0.12) |
| Microsoft Orleans (+ AdoNet, Hosting.Kubernetes, EventSourcing) | 10.3.1 (CPM-pinned) |
| Microsoft.Orleans.Streaming.NATS (pinned) | 10.3.1-alpha.1 |
| NATS.Net (transitive, pinned) | **2.8.2** (not 3.x) |
| Google.Protobuf | **3.36.2** |
| NATS Server (JetStream) | 2.15.0 |
| Temporal Server / Temporalio .NET SDK | **1.31.3** (or 1.32.0) / **1.19.0** |
| Escendit.Extensions.Hosting.* | 0.1.0-rc.4 |
| Escendit.AspNetCore.Builder.* | 0.1.0-rc.0 |
| PostgreSQL | 18 (18.6) |
| CloudNativePG | 1.30.1 |
| Keycloak (Phase Two image) + keycloak-orgs | **phasetwo-keycloak:26.6.7** + 0.182 |
| keycloak-temporal-extensions | **v0.0.1-rc.0, or a source build against KC 26.6.7** (rc.1/rc.2 target KC 26.7) |
| SvelteKit / Svelte | **2.70.3 / 5.57.1** |
| @escendit/sveltekit-auth-keycloak + -session | **0.1.0-rc.12** |
| Kotlin / Kable / kotlin-multiplatform-oidc | 2.4.20 / 0.45.0 / 0.18.3 |
| Aspire (dev AppHost) | **13.5.4** |
| RKE2 | v1.36 line (stable channel, v1.36.4+rke2r1) |
| Ingress | **RKE2-bundled Traefik** |
| cert-manager | **1.21.2** |
| Fleet | **v0.16.2** (>= 0.14.5) |
