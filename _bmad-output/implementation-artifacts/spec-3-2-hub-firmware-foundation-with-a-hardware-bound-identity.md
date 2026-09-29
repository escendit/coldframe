---
title: 'Story 3.2: Hub firmware foundation with a hardware-bound identity'
type: 'feature'
created: '2026-09-29'
baseline_revision: '1acb3b99eb1c3d5697f67c0619ec1e705856cf9b'
status: 'awaiting-operator'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-3-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-coldframe-2026-09-26/ARCHITECTURE-SPINE.md'
warnings: ['oversized']
operator_actions:
  - "On a spare ESP32-S3 whose eFuse key blocks you accept losing, run the release path of docs/bench/hub-identity-checklist.md: record `espefuse summary` key purposes, flash `cargo run --release` from apps/rs/hub, confirm the first boot logs `source=burned KEYn`, then confirm exactly one block now shows HMAC_UP and is read-protected (espflash 4.5 board-info does not print key purposes, so use espefuse)."
  - "Reboot that board and confirm it logs `source=existing KEYn` with the same device_id, and that `espefuse summary` shows no further key-purpose changes."
  - "On a second ESP32-S3, flash the debug `cargo run --features dev-mode` build twice and confirm it logs `mode=dev source=generated`, then `source=stored` with the same device_id, while `espefuse summary` is unchanged before and after."
  - "Record the results (date, board, Device IDs, pass or fail) in the Result table of docs/bench/hub-identity-checklist.md and commit it."
deferred:
  - summary: >-
      A partial ets_efuse_write_key failure might leave a block that a later boot accepts as the identity.
    evidence: |-
      maybe-false. BoardEfuse::burn_key maps a nonzero ROM return to BurnFailed and halts. If the ROM had already set the HMAC_UP purpose and RD_DIS before failing on the key data, the next boot would take the Existing path with a partial key. Settle it with the ESP32-S3 ROM efuse source or a bench fault test.
    location: >-
      apps/rs/hub/src/board/efuse.rs (burn_key)
    severity: medium (unverified)
  - summary: >-
      The Hub firmware's dev-mode release guard and board eFuse/HMAC index mappings are not checked by any automated test.
    evidence: |-
      apps/rs is excluded from the workspace, and CI installs no esp toolchain. The mocks index blocks directly, so rom_block (4 + n), the RD_DIS bit n and the KeyId mapping are verified only by a local build and the bench checklist. Fix with an esp-toolchain CI job, or by moving the mappings into coldframe-hal where host tests can pin them.
    location: >-
      apps/rs/hub/src/main.rs, apps/rs/hub/src/board/efuse.rs, apps/rs/hub/src/board/hmac.rs
    severity: medium
---

<intent-contract>

## Intent

**Problem:** The Hub has no firmware and no identity. Epic 3 needs a Hub that creates its own secret root on first boot, which no human ever sees, and derives `K_dev`, its purpose keys and its Device ID from it (AD-12). Firmware logic also needs the HAL-trait crate (AD-24) so that it can be tested on the host.

**Approach:**
- Fill `packages/rs/hal` with small `no_std` traits (radio, TRNG, eFuse, HMAC, flash, ADC, RTC, GPIO) and std-only mocks behind a `mock` feature.
- Add a hardware-agnostic identity module to `packages/rs/crypto`. It finds or burns the eFuse root and derives keys through the HMAC trait; dev mode keeps a software root in flash instead.
- Add `apps/rs/hub`, an ESP32-S3 firmware crate. It implements the traits over esp-hal/ROM, turns the radio on, provisions the identity and logs the Device ID.

## Boundaries & Constraints

**Always:**
- Logic in `packages/rs/*` never names esp-hal types. Only `apps/rs/hub/src/board/*` touches esp-hal, esp-radio, esp-storage or the ROM.
- The radio is on before any root-key byte is drawn. The TRNG trait fails while the radio is off, and the mock enforces this.
- Release eFuse path:
  - An existing key block with purpose `HmacUp` (esp-hal `HmacPurpose::ToUser`, value 8) is the identity. Never burn when one exists.
  - Burn only into a block the chip reports unused. The burn sets purpose `HmacUp` together with read and write protection.
  - After burning, re-read the block. It must show `HmacUp` and be read-protected.
- `K_dev` = HMAC-SHA256 computed by the HMAC trait over `DEVICE_KEY_LABEL` from `spec.rs`. Purpose keys and the Device ID come from the existing `DeviceKeys::from_device_key`.
- Dev mode (Cargo feature `dev-mode` on the hub):
  - It never constructs eFuse or HMAC adapters, which the type signatures enforce.
  - Its root is a TRNG-generated software key kept in the flash partition labelled `cf_ident`.
  - `#[cfg(all(feature = "dev-mode", not(debug_assertions)))] compile_error!` makes release builds refuse the feature.
- No key, root or HMAC output is ever logged, formatted with `Debug` or returned in an error.
- Style follows `packages/rs`: `no_std` without `alloc` outside mocks, docs on public items, exact dependency versions, and the workspace lints. Host workspace `unsafe_code = forbid` still holds. `apps/rs/hub` confines `unsafe` to the ROM/eFuse adapter, with `// SAFETY:` comments.

**Never:**
- BLE, Wi-Fi join, SNTP/TLS, heartbeat or PoP code (Stories 3.4 and 3.5).
- Protobuf or micropb generation (`packages/rs/protocol`).
- Secure boot or flash encryption.
- Firmware builds or on-device tests in CI.
- Adding the hub crate to the host workspace.
- Using embedded-hal as a trait dependency.
- Regenerating a corrupt dev identity silently.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| First boot (release) | All key blocks unused, radio off | Radio enabled, 32 TRNG bytes burned once into the first unused block with `HmacUp` + read protection; `Provisioned { source: Burned(block) }`; keys = software derivation of the same root | No error expected |
| Later boot (release) | One block already `HmacUp` + read-protected | That block is used, burn count stays unchanged, keys are identical to first boot, `source: Existing(block)` | No error expected |
| Several identity blocks | Two blocks with `HmacUp` | Nothing burned | `IdentityError::AmbiguousIdentity` |
| Unprotected identity | `HmacUp` block not read-protected | Nothing burned, no HMAC | `IdentityError::IdentityNotProtected` |
| No room | No `HmacUp` block, no unused block | Nothing burned | `IdentityError::NoFreeKeyBlock` |
| Bad entropy | TRNG yields all-zero or all-0xFF 32 bytes | Nothing burned | `IdentityError::BadEntropy` |
| Burn not confirmed | Burn returns Ok but re-read shows no `HmacUp`/read-protection | — | `IdentityError::BurnNotVerified` |
| HAL failure | TRNG/radio/eFuse/HMAC/flash returns an error | Nothing further written | Wrapped `IdentityError::Hal(kind)`, no secret data |
| Dev first boot | `cf_ident` region erased (0xFF) | TRNG root written as `CFID` record, read back and verified; `source: DevGenerated` | No error expected |
| Dev later boot | Valid record | Same root, same keys, `source: DevStored`, no write | No error expected |
| Dev corrupt | Record neither erased nor valid | Nothing written | `IdentityError::CorruptDevIdentity` |

</intent-contract>

## Code Map

- `Cargo.toml` -- host workspace: members list (add nothing for hub; `exclude = ["apps/rs"]`), lints `unsafe_code=forbid`, `missing_docs=warn`, edition 2024, rust 1.97.
- `packages/rs/hal/src/lib.rs` + `Cargo.toml` -- skeleton crate (`NAME`, `VERSION`, one inline test); becomes the trait crate. No deps today.
- `tests/rs/hal/tests/identity.rs` -- existing semver test for the crate; keep, add mock tests beside it.
- `packages/rs/crypto/src/keys.rs` -- `derive_device_key` (software HMAC), `DeviceKeys::{from_device_key, from_root_key}`, `DeviceId::to_hex`; reuse, do not duplicate derivation.
- `packages/rs/crypto/src/lib.rs`, `error.rs` -- module list and the no-secret `Error` style to mirror for `IdentityError`.
- `packages/rs/crypto/src/spec.rs` -- generated constants `DEVICE_KEY_LABEL`, `ROOT_KEY_LENGTH` (32), `DEVICE_KEY_LENGTH`; read-only (generated by `packages/crypto-spec`).
- `tests/rs/crypto/tests/common/mod.rs` -- `vectors()`, `array::<N>()`; `vectors.json` `keyHierarchy[]` has `rootKey`, `deviceKey`, `sealKey`, `ackKey`, `hubAuthKey`, `deviceId`.
- `apps/rs/spike-hub-radio/{Cargo.toml,Cargo.lock,rust-toolchain.toml,.cargo/config.toml,.gitignore}` -- pinned esp stack and build config to copy (esp-hal 1.2.2 `unstable`, esp-radio 1.0.0-beta.1, esp-rtos 0.4.0, esp-bootloader-esp-idf 0.6.0, esp-storage 0.10.0 already in its lock). `src/bin/hub.rs:268-365` shows init: `esp_hal::init`, heap, `esp_rtos::start`, `WifiController::new(peripherals.WIFI, ControllerConfig::default())`.
- esp-hal 1.2.2 facts (registry source): `esp_hal::hmac::Hmac::{new, init, configure(HmacPurpose::ToUser, KeyId::KeyN), update, finalize}` (nb); `esp_hal::efuse::{read_field_le, read_bit}` with `efuse::KEY_PURPOSE_0..5`, `RD_DIS` (bits 4..9 = KEY0..5); `esp_hal::rng::Trng::try_new()` fails unless an entropy source is active — `WifiController::new` increments it (esp-radio wifi/mod.rs:2744). esp-hal has **no eFuse write API**; ESP32-S3 ROM exports `ets_efuse_write_key(block, purpose, data, len) -> i32` (0 = ok; key blocks are 4..=9, purpose `HMAC_UP` = 8), `ets_efuse_key_block_unused(block) -> bool`, `ets_efuse_read()` (esp-rom-sys `ld/esp32s3/rom/esp32s3.rom.ld:572-581`, linked via `linkall.x`).
- esp-bootloader-esp-idf 0.6.0 `partitions::{read_partition_table, PartitionEntry::{label_as_str, as_flash_region}}`, `FlashRegion::{read, write, erase}` over `esp_storage::FlashStorage`.
- `apps/rs/README.md`, `packages/rs/README.md`, `docs/quickstart.md` -- crate tables / command docs to update.
- `.github/workflows/ci.yml:156-184` -- rust job runs `cargo build/clippy/test --workspace`; the new host code rides it unchanged.

## Tasks & Acceptance

**Execution:**
- `packages/rs/hal/src/{lib,radio,rng,efuse,hmac,flash,adc,rtc,gpio}.rs` + `Cargo.toml` -- one module per trait. Each trait has its own small `Copy` error enum. `efuse` defines `KeyBlock` (Key0..Key5 with `index()`), a `KeyPurpose` enum carrying the ESP32-S3 values (`HmacUp = 8`, unknown values preserved) and `trait Efuse { key_purpose, is_read_protected, is_unused, burn_key(block, &[u8;32], purpose) }`. `hmac`: `trait HmacPeripheral { hmac_sha256(block, msg) -> Result<[u8;32], HmacError> }`. `rng`: `trait Trng { fill(&mut [u8]) -> Result<(), TrngError> }`. `radio`: `trait Radio { enable, is_enabled }`, documented as extended in 3.4/3.5. `flash`: `trait Flash { capacity, read, write, erase(offset,len) }`. `adc`/`rtc`/`gpio`: minimal read_millivolts, uptime/unix-time get/set and output/input pin traits. Keep `NAME`/`VERSION`. Crate attribute `#![cfg_attr(not(any(test, feature = "mock")), no_std)]` -- AD-24 trait surface.
- `packages/rs/hal/src/mock.rs` (feature `mock`) -- mocks for every trait:
  - `MockEfuse` models hardware. It tracks 6 blocks with data, purpose and read/write protection, plus a burn counter. A burned block's data is unreadable, and burning a used block fails. Constructors: `new()` and `with_identity(block, root)`.
  - `MockHmac::new(&MockEfuse)` shares its state. It computes real HMAC-SHA256 over the block's key, and fails `KeyPurposeMismatch` unless the purpose is `HmacUp`.
  - `MockRadio` and `MockTrng::new(&MockRadio)`. The TRNG fails `EntropySourceDisabled` while the radio is off, and can be scripted with fixed bytes.
  - `MockFlash`: NOR semantics (erase to 0xFF, write only clears bits), with a write counter.
  - Simple ADC, RTC and GPIO mocks.

  The mocks let host tests run without hardware. `hmac`/`sha2` go in as optional deps behind `mock`.
- `tests/rs/hal/Cargo.toml` + `tests/rs/hal/tests/mocks.rs` -- enable `mock`. Test the contracts: eFuse burn-once and read protection, HMAC purpose mismatch, TRNG refusal while the radio is off, and flash NOR semantics.
- `packages/rs/crypto/Cargo.toml`, `src/identity.rs`, `src/lib.rs` -- depend on `coldframe-hal` (path, no features).
  - `provision_efuse(radio, trng, efuse, hmac) -> Result<Provisioned, IdentityError>` and `provision_dev(radio, trng, flash) -> Result<Provisioned, IdentityError>` per the I/O matrix.
  - `Provisioned { keys: DeviceKeys, source: IdentitySource }`.
  - `device_keys_from_hmac(hmac, block)` gives `K_dev` through the trait, then `DeviceKeys::from_device_key`.
  - Dev record, from region offset 0: `b"CFID" ‖ 0x01 ‖ root[32] ‖ SHA-256(prefix‖root)[0..4]` (41 B).
  - `IdentityError` carries no secrets and implements `Display`.

  This is the AC4 logic behind the hardware trait.
- `tests/rs/crypto/Cargo.toml` + `tests/rs/crypto/tests/identity.rs` -- dev-dep `coldframe-hal` with `mock`.
  - For every `keyHierarchy` vector, a `MockEfuse::with_identity` gives purpose keys and a Device ID equal to the vector.
  - First-boot burn then second-boot reuse shows the same keys and a burn count of 1.
  - Every I/O-matrix error row is covered, and a failure path leaves the burn count at 0.
  - Dev first and later boot give the same keys, with a flash write count of 1 and then unchanged. A corrupt record is rejected.
  - The generated root equals the scripted TRNG bytes, proven by software derivation.
- `apps/rs/hub/{Cargo.toml,Cargo.lock,rust-toolchain.toml,.cargo/config.toml,.gitignore,partitions.csv}` -- standalone firmware crate `coldframe-hub`.
  - Pin the same esp versions as the spike, plus the path deps `coldframe-hal` and `coldframe-crypto`. Features `default = []`, `dev-mode = []`.
  - `partitions.csv`: nvs `0x9000`/`0x5000`, `cf_ident` data subtype `undefined` (0x06 — espflash 4.5 rejects custom subtypes) at `0xE000`/`0x1000`, phy_init `0xF000`/`0x1000`, factory app `0x10000`/`0x3F0000`.
  - The runner is `espflash flash --monitor --partition-table partitions.csv`.
- `apps/rs/hub/src/main.rs` -- entry point.
  - Holds the dev-mode `compile_error!` guard.
  - Init sequence: esp-hal init, heap, esp-rtos start, `WifiController::new`, which puts the radio on.
  - Builds the board adapters, calls `provision_efuse` (or `provision_dev` under `dev-mode`) and logs `identity mode=<efuse|dev> source=<burned KEYn|existing KEYn|generated|stored> device_id=<hex>`.
  - On error it logs the error kind and halts in a loop without retrying a burn. On success it idles, keeping the radio alive with a periodic uptime log.
- `apps/rs/hub/src/board/{mod,radio,rng,efuse,hmac,flash}.rs` -- trait implementations over esp-hal, esp-radio, esp-storage and the ROM.
  - `efuse` reads purpose and RD_DIS through `esp_hal::efuse`. It burns via `extern "C" ets_efuse_write_key` and then calls `ets_efuse_read`. Its `is_unused` uses `ets_efuse_key_block_unused`.
  - `flash` wraps the `cf_ident` `FlashRegion`, found by label.
  - The only `unsafe` lives here.
- `apps/rs/hub/README.md` -- build, flash and dev-mode docs, what the log lines mean, and the irreversible-burn warning.
- `docs/bench/hub-identity-checklist.md` -- manual bench checklist:
  - Check `espflash board-info` key purposes before flashing.
  - Release flash: first boot shows `burned KEYn`, `board-info` shows `HMAC_UP` on one block, a reboot shows `existing KEYn` with the same Device ID, and there are no new purposes.
  - Dev-mode board: `board-info` shows no eFuse change.
  - A release build with `--features dev-mode` fails to compile.
- `apps/rs/README.md`, `packages/rs/README.md`, `docs/quickstart.md` -- update the crate tables and add host test and firmware build commands.

**Acceptance Criteria:**
- Given host tests with the mock HMAC peripheral, when `cargo test --workspace --locked` runs, then the derived purpose keys and Device ID match every `keyHierarchy` vector and every I/O-matrix row passes.
- Given `packages/rs/crypto` and `packages/rs/hal`, when inspected, then neither depends on any esp crate, and identity logic takes only HAL traits.
- Given `apps/rs/hub`, when `cargo build --release` runs with the esp toolchain, then it compiles, and `cargo build --release --features dev-mode` fails with the dev-mode compile error, while a debug `--features dev-mode` build compiles.
- Given the Hub on the bench, when the operator follows `docs/bench/hub-identity-checklist.md`, then the first boot burns the key once (confirmed with `espflash board-info` key purposes), and a dev-mode board shows no eFuse change. This check is manual (operator action).

## Spec Change Log

## Review Triage Log

### 2026-09-29 — Review pass
- verdicts: 26 findings — high 0, medium 5, low 19, false 1, maybe-false 1
- findings:
  - `[medium]` `[patch]` (blind) A build without `dev-mode` burns an eFuse in debug too, but the docs said "release build" — patched: the Hub README and checklist now say "any build without `--features dev-mode` (debug or release)".
  - `[low]` `[patch]` (blind) No documented recovery for HMAC_UP without read protection — patched: the README states the board then halts with `IdentityNotProtected` on every boot and must be retired or run only in dev mode.
  - `[low]` `[reject]` (blind) The `Efuse` trait cannot check write protection — the ROM routine sets WR_DIS together with the key; changing a burned block needs another eFuse write that no Coldframe code performs. The fix adds trait surface for a case not met in normal use.
  - `[low]` `[reject]` (blind) `black_box` wiping is best-effort, and the HMAC output is not wiped — `K_dev` has to live in `DeviceKeys` for enrolment anyway; `zeroize` would be a new dependency for a best-effort gain. Recorded as a residual risk.
  - `[medium]` `[patch]` (blind) CI never builds hal/crypto as no_std — grouped with the verification-gap no_std finding; see that row.
  - `[low]` `[reject]` (blind) The spec was not updated for implementation deviations (espefuse instead of board-info, rust-version 1.95, black_box, `DevIdentityNotVerified`) — the fix edits this build's spec; the deviations are recorded under Auto Run Result instead.
  - `[low]` `[reject]` (blind) Errors after a burn do not name the block — `espefuse summary` shows the block directly; carrying it in the error adds public surface for a rare failure.
  - `[low]` `[reject]` (blind) Missing tests for HMAC failure after a burn, for the existing path touching no radio/TRNG, and for flash failure on write or read-back — no behaviour defect: the code returns `Existing` before `draw_root`, and the post-burn HMAC path is `?`. Per-operation mock failure injection would be new surface.
  - `[low]` `[patch]` (blind) `BoardFlash` does read-erase-rewrite, contradicting the NOR contract of the `Flash` trait — patched: the trait doc now says hardware adapters may read-modify-write, so callers write only erased ranges.
  - `[low]` `[patch]` (blind) The docs said hal has "no dependencies" — patched in the `packages/rs/README.md` row and the hal `Cargo.toml` comment.
  - `[low]` `[reject]` (blind) The no-op radio adapter leaves the ordering rule unproven on hardware — `main` creates `WifiController` before provisioning, and `Trng::try_new` fails without an entropy source; a bench self-test would be new firmware code.
  - `[low]` `[reject]` (blind) The bench checklist lacks failure-path branches — failures log their kind and halt; expanding the procedure is not needed to prove the ACs.
  - `[low]` `[reject]` (blind) `errors_carry_no_secret_material` is weak — `IdentityError`/`HalError` are fieldless or `Copy` enums of error kinds, so by construction they cannot carry key bytes.
  - `[low]` `[reject]` (edge) Accepted identity block not write-protected — same root cause and reason as the blind write-protection row.
  - `[low]` `[reject]` (edge) With no free block, a radio/TRNG failure is reported before `NoFreeKeyBlock` — this follows the spec's burn order; it only changes which error shows when two failures coincide.
  - `[maybe-false]` `[defer]` (edge) `ets_efuse_write_key` could fail after programming part of a block, and the next boot might accept it — whether the ROM writes the purpose/RD_DIS after a failed key-data program needs the ROM source or a bench test; medium if true (unverified).
  - `[low]` `[reject]` (edge) A valid CFID record with an all-zero root is accepted — only reachable by hand-writing the dev partition; the generator rejects bad entropy.
  - `[low]` `[reject]` (edge) Forcing debug-assertions on in a release profile bypasses the dev-mode guard — needs a deliberate profile override (the manifest pins `debug-assertions = false`); a build.rs check is more than a direct fix.
  - `[low]` `[patch]` (edge) MockRtc `+` can overflow — patched with `saturating_add`.
  - `[low]` `[patch]` (edge) The unconfirmed-burn test did not pin the burn count — patched: it asserts 1 for `WithoutReadProtection` and 0 for `SilentlyIgnored`.
  - `[low]` `[reject]` (edge) AC4 names `espflash board-info`, but the checklist uses `espefuse summary` — fixing it means editing this build's spec; espflash 4.5.0 does not print key purposes, so the operator action names espefuse.
  - `[medium]` `[patch]` (verification-gap) The hal no_std build is never checked in CI because `mock` is always unified in — patched: the rust job adds `cargo build -p coldframe-hal -p coldframe-crypto --locked` (passes locally).
  - `[medium]` `[defer]` (verification-gap) The dev-mode release guard and board eFuse/HMAC mappings are only checked by hand — the spec rules out CI firmware builds; needs an esp-toolchain CI job or moving the mappings into hal.
  - `[false]` `[reject]` (intent) The spec is not at awaiting-operator with `operator_actions` — the review ran before finalization; Finalize sets `status: awaiting-operator` and `operator_actions`.
  - `[low]` `[reject]` (intent) AC4 wording names board-info rather than espefuse — duplicate of the edge AC4 row; spec edit.
  - `[medium]` `[defer]` (intent) AC3 and AC4 are exercised only off-CI (esp toolchain, bench) — same root cause as the deferred verification-gap row; AC3 was verified locally, AC4 is the operator action.

## Design Notes

- **Why ROM `ets_efuse_write_key`:** esp-hal 1.2.2 can only read eFuses. The ROM routine applies the Reed-Solomon encoding for key blocks and sets purpose plus read/write protection in one call. Hand-programming the eFuse registers would duplicate that work and risk bricking a block. Whether it behaves as expected under esp-hal's clock setup can only be proven on the bench, so it is a named risk.
- **Half-burned block:** if power is lost mid-burn, a block may hold key data with purpose `User`. The chip then reports it as used, so the next boot picks a different free block. One block is lost, but a key is never reused or re-exposed.
- **Burn order in `provision_efuse`:**
  1. Check for an existing identity, which may be ambiguous or unprotected.
  2. `radio.enable()`.
  3. `trng.fill(root)` with the sanity check.
  4. Pick the first unused block.
  5. `burn_key`.
  6. Verify by re-reading.
  7. Derive via HMAC.

  Zeroise the local root buffer with `core::ptr::write_volatile` or an equivalent that isn't optimised away, before returning on every path.

## Verification

**Commands:**
- `cargo fmt --all --check && cargo clippy --workspace --all-targets --locked -- -D warnings` -- expected: clean
- `cargo test --workspace --locked` -- expected: all pass, including the new hal mock and identity tests
- `cd apps/rs/hub && source ~/export-esp.sh && cargo build --release && cargo build --features dev-mode && ! cargo build --release --features dev-mode` -- expected: the first two build, the third fails with the dev-mode message
- `cd apps/rs/hub && cargo fmt --check && cargo clippy --release -- -D warnings` -- expected: clean

**Manual checks (if no CLI):**
- Operator runs `docs/bench/hub-identity-checklist.md` on a real ESP32-S3 (irreversible eFuse burn).

## Auto Run Result

Status: awaiting-operator

**Summary.** Story 3.2 gives the Hub a hardware-bound identity.
- `packages/rs/hal` now holds the AD-24 trait crate: radio, TRNG, eFuse, HMAC, flash, ADC, RTC and GPIO. It is `no_std`, with std mocks behind `mock` that model the hardware: burn-once eFuse, read protection, HMAC purpose checks, a TRNG gated on the radio, and NOR flash.
- `coldframe-crypto` has a new `identity` module:
  - `provision_efuse` finds an HMAC_UP key block, or on first boot turns the radio on, draws 32 TRNG bytes, checks them, burns them once with read and write protection, re-reads the block, then derives `K_dev` through the HMAC trait.
  - `provision_dev` keeps a checked `CFID` software root in the `cf_ident` flash partition and never touches eFuses.
- `apps/rs/hub` is a new standalone ESP32-S3 firmware crate. It builds adapters over esp-hal, esp-radio, esp-storage and the ROM (`ets_efuse_write_key`), starts the Wi-Fi controller so the radio becomes the TRNG's entropy source, provisions the identity and logs `identity mode=… source=… device_id=…`. A `compile_error!` refuses `dev-mode` in release builds.

**Files changed.**
- `packages/rs/hal/**`: trait modules, `mock.rs`, manifest with optional `mock` dependencies.
- `packages/rs/crypto/{Cargo.toml,src/lib.rs,src/identity.rs}`: identity provisioning over HAL traits.
- `tests/rs/hal/tests/mocks.rs`, `tests/rs/crypto/tests/identity.rs` and their manifests: mock contracts, every keyHierarchy vector through the mock HMAC, every I/O-matrix row.
- `apps/rs/hub/**`: firmware crate, `partitions.csv`, board adapters, README.
- `docs/bench/hub-identity-checklist.md`: manual bench procedure.
- `apps/rs/README.md`, `packages/rs/README.md`, `docs/quickstart.md`: docs.
- `.github/workflows/ci.yml`: build hal and crypto without mocks.
- `Cargo.lock`: internal dependency lines only.

**Deviations from the spec's task text, recorded here instead of editing the spec.**
- `cf_ident` uses data subtype `undefined` (0x06), because espflash 4.5.0 / esp-idf-part 0.6.0 panic on custom subtype `0x40`. It is found by label.
- The `esp` toolchain is Rust 1.95, so `coldframe-hal` and `coldframe-crypto` declare `rust-version = "1.95"`. The workspace stays at 1.97.
- Wiping uses `fill(0)` plus `core::hint::black_box`, because the workspace forbids `unsafe`, which rules out `write_volatile`.
- There is an extra error, `DevIdentityNotVerified`, for a dev record that doesn't read back after writing.
- The bench checklist reads key purposes with `espefuse summary`, because espflash 4.5.0 `board-info` does not print them.
- The RD_DIS mapping is bit n = KEYn. The spec's Code Map note "bits 4..9" was wrong.

**Review findings.** 26 findings: high 0, medium 5, low 19, false 1, maybe-false 1.
- **Patched (7 entries: 2 medium, 5 low).**
  - The burn warning now covers any build without `dev-mode` (medium).
  - A CI no_std build of hal and crypto (medium).
  - A README note on HMAC_UP without read protection.
  - The `Flash` trait documents read-modify-write adapters.
  - The hal dependency wording is fixed.
  - MockRtc uses `saturating_add`.
  - The unconfirmed-burn test pins the burn count.
- **Deferred (2).**
  - A partial ROM burn failure (maybe-false, medium unverified).
  - The firmware-only guard and mappings have no automated check (medium).
- **Rejected.** Every other finding, each with its reason in the Review Triage Log: write-protection trait query, `black_box`/`zeroize`, spec-edit findings, block in errors, extra tests, radio self-test, checklist failure branches, secret-error test, error ordering with no free block, all-zero dev root, forced debug-assertions, and the pre-finalization status (false).

**Follow-up review recommended: true.** Two medium entries were patched. The specific unverified risk is that the new CI step `cargo build -p coldframe-hal -p coldframe-crypto --locked` has run only locally, never on GitHub Actions. The whole eFuse burn path has not run on silicon either (operator action).

**Verification.**
- Host:
  - `cargo fmt --all --check` passes.
  - `cargo clippy --workspace --all-targets --locked -- -D warnings` passes.
  - `cargo test --workspace --locked` passes: 66 tests, 0 failed, including 16 hal mock tests and 23 identity tests.
  - `cargo build -p coldframe-hal -p coldframe-crypto --locked` passes.
- Firmware (`apps/rs/hub`, esp toolchain):
  - `cargo build --release` passes.
  - `cargo build --features dev-mode` passes.
  - `cargo build --release --features dev-mode` fails with the dev-mode message, as intended.
  - `cargo fmt --check` and `cargo clippy --release -- -D warnings` are clean.
  - The linked binary contains the ROM eFuse symbols.
- `espflash partition-table partitions.csv` parses the table.
- Every I/O-matrix row has a passing test.

**Residual risks.**
- The ROM eFuse burn is unproven on silicon: its timing under esp-hal clocks and whether it sets the protection bits. The operator bench run covers it, and the post-burn re-read catches a burn that did not take.
- Zeroisation is best-effort (`black_box`).
- CI builds no firmware (deferred).
- No `no_std` cross-target check runs in CI: the new step builds for the host target with `#![no_std]` active.
- A 1.96+ standard-library API used in hal or crypto would be caught only by clippy's MSRV lint.

