# Device identity: the eFuse root and the key hierarchy

Every Coldframe Device, Hub or Node, has one secret root key. Every other key and the Device ID
are derived from it. This page shows how the firmware creates that root on first boot, keeps it in
a read-protected eFuse block, and derives the rest with HMAC and HKDF. It is the first page of the
[firmware reference](README.md); the next one, [sealing.md](sealing.md), shows how the purpose keys
seal frames. The rule behind it all is AD-12 in the
[architecture spine](../../_bmad-output/planning-artifacts/architecture/architecture-coldframe-2026-09-26/ARCHITECTURE-SPINE.md).

> [!WARNING]
> **eFuse burns are irreversible.** Any build without `--features dev-mode`, debug or release,
> burns a key block on its first boot, including a plain `cargo run`. Check the key purposes
> before you flash ([flash.md](../build/flash.md)) and follow the
> [Hub identity checklist](../bench/hub-identity-checklist.md), which applies to the Node too.

## How it works

### One root, one derived key

The root is 32 random bytes. It derives exactly one key, the device key `K_dev`:

```text
K_dev = HMAC-SHA256(key = root, msg = "coldframe/device/v1")
```

Everything else comes from `K_dev` with HKDF-SHA256 (empty salt, `K_dev` as input key material,
the label as `info`):

- `seal/v1`, 32 bytes: seals uplink frames (see [sealing.md](sealing.md)).
- `ack/v1`, 32 bytes: seals downlinks from the Server.
- `hub-auth/v1`, 32 bytes: the Hub signs its HTTP requests with it.
- `device-id/v1`, 8 bytes: the Device ID, shown as 16 lowercase hex digits.

`DeviceKeys::from_device_key` computes all of them in one go. `K_dev` itself is kept too, because
BLE enrolment seals it with HPKE to the Server, which then derives the same purpose keys (see
[ble-setup.md](ble-setup.md)). The Server never sees the root.

The Device ID is derived, not stored. Two Devices with different roots get different IDs, and the
same chip always gets the same ID back, boot after boot.

### Release builds: the root lives in an eFuse key block

The ESP32-S3 has six 256-bit eFuse key blocks, `KEY0` to `KEY5`. Each has a 4-bit purpose, a
read-disable bit and a write-disable bit. A fuse bit, once set, stays set. Coldframe burns the root
into one block with purpose `HMAC_UP` (raw value 8). The chip then read-protects the block: no
software can read the key any more. Only the HMAC peripheral can use it.

`provision_efuse` runs on every boot and does this:

1. It looks for a block whose purpose is `HMAC_UP`. If there is exactly one and it is
   read-protected, that is the identity; nothing is burned (`existing KEYn` in the log).
2. It refuses to go on, and burns nothing, if two blocks have `HMAC_UP`
   (`AmbiguousIdentity`), if the one block is not read-protected (`IdentityNotProtected`), or if no
   identity exists and no block is unused (`NoFreeKeyBlock`).
3. On first boot it turns the radio on, because the radio is the TRNG's entropy source. Without it
   `BoardTrng` refuses to run rather than return pseudo-random bytes. It draws 32 bytes and rejects
   all-zero or all-`0xFF` output (`BadEntropy`).
4. It burns the bytes into the first unused block with purpose `HMAC_UP`, then re-reads the block
   and checks the purpose and the read protection (`BurnNotVerified` otherwise).
5. It asks the HMAC peripheral for `K_dev` and derives the rest.

The root sits in RAM only between the TRNG draw and the burn. It is held in a small `Wiped`
buffer that zeroes itself when dropped, on every return path. The crate forbids `unsafe`, so it
uses `core::hint::black_box` instead of a volatile write to stop the compiler removing the zeroing.

On the existing-identity path, which is every boot after the first, the radio is never started for
the identity at all.

### The HMAC peripheral in upstream mode

The eFuse purpose and the HMAC peripheral mode have different names for the same idea. The burned
purpose is `HMAC_UP` (`KeyPurpose::HmacUp`); `BoardHmac` configures the esp-hal peripheral with
`HmacPurpose::ToUser`. In that mode software supplies the message and reads the 32-byte result,
while the key stays in the fuse. If the block does not have purpose `HMAC_UP`, the peripheral
refuses and the adapter reports `KeyPurposeMismatch`.

> [!NOTE]
> AD-12 says the root is burned with "HMAC purpose ToUser". In the code, `ToUser` is the
> peripheral mode and the burned eFuse purpose is `HMAC_UP`. This page follows the code.

### Hardware behind traits

`packages/rs/crypto` never touches a register. `provision_efuse` takes four traits from
`packages/rs/hal`: `Radio`, `Trng`, `Efuse` and `HmacPeripheral`. The `Efuse` trait deliberately has
no way to read a key. This keeps the whole flow testable on the host with fakes, and the firmware
only supplies thin adapters in `board/`.

`BoardEfuse` can only read eFuses through esp-hal 1.2.2, so it burns through the ESP32-S3 ROM
routine `ets_efuse_write_key`. That routine applies the Reed-Solomon coding of key blocks and sets
data, purpose, write protection and (for a protected purpose) read protection in one call.
Afterwards `ets_efuse_read` reloads the read registers so the re-check sees the real fuses.
`BoardEfuse::take` hands out at most one adapter, so only one owner can program fuses.

These ROM calls are the only `unsafe` code of the Node. Each `unsafe` block carries a `SAFETY`
comment, and `unsafe_code = "deny"` in the firmware `Cargo.toml` makes any other use fail the
build. `board/hmac.rs` needs no `unsafe`: it uses the safe esp-hal HMAC driver. The Hub has one more
`unsafe` block outside the eFuse adapter, in `apps/rs/hub/src/board/rtc.rs`, which installs the
mbedTLS clock hooks for TLS.

### What can go wrong during a burn

If power fails mid-burn, a block can end up with data but purpose `USER`. The chip then reports
it as used, so the next boot burns a different free block: one block is lost, but no key is reused.
A burn can also set `HMAC_UP` without read protection; that board then halts with
`IdentityNotProtected` on every boot and can no longer hold an identity. The
[Hub README](../../apps/rs/hub/README.md) covers both cases under "Identity modes".

On an identity failure the Hub logs the stage and error and parks. The Node logs it and deep-sleeps
one period instead, so a broken identity does not drain the battery.

### Dev mode: a software root in flash

A build with `--features dev-mode` uses `provision_dev` instead and compiles no eFuse or HMAC code
at all. The root is a TRNG-drawn software key in the `cf_ident` flash partition, stored as a
41-byte record: the magic `CFID`, version `0x01`, the 32-byte root, and the first 4 bytes of
SHA-256 over the rest as a check.

- An erased partition (all `0xFF`) means first boot: draw a root, write the record, read it back.
- A valid record is used as it is.
- Anything else is `CorruptDevIdentity`. A corrupt identity is never replaced silently; erasing the
  partition is your decision ([flash.md](../build/flash.md), "Recovery: erase a region").

Both modes end in `DeviceKeys::from_device_key`, so a given root gives the same keys whether
`K_dev` comes from the HMAC peripheral or from `derive_device_key` in software.

### The release guard

A dev-mode root is readable from flash, so a release Device must never use it. Both firmware
crates refuse the combination at compile time. The Node's guard reads (the Hub's says "a release Hub"):

```rust
#[cfg(all(feature = "dev-mode", not(debug_assertions)))]
compile_error!(
    "the `dev-mode` feature is refused in release builds: a release Node uses its eFuse identity"
);
```

The guard works only in one direction. A debug build *without* `dev-mode` is an eFuse build and
burns on first boot, as the warning above says.

## Key constants and timings

All from the generated `packages/rs/crypto/src/spec.rs` (source:
`packages/crypto-spec/crypto-spec.json`) unless noted.

| Constant | Value | Where |
| --- | --- | --- |
| `ROOT_KEY_LENGTH` | 32 bytes | `packages/rs/crypto/src/spec.rs` |
| `DEVICE_KEY_LABEL` | `coldframe/device/v1` | `packages/rs/crypto/src/spec.rs` |
| `DEVICE_KEY_LENGTH`, `PURPOSE_KEY_LENGTH` | 32 bytes each | `packages/rs/crypto/src/spec.rs` |
| `SEAL_LABEL`, `ACK_LABEL`, `HUB_AUTH_LABEL` | `seal/v1`, `ack/v1`, `hub-auth/v1` | `packages/rs/crypto/src/spec.rs` |
| `DEVICE_ID_LABEL` | `device-id/v1` | `packages/rs/crypto/src/spec.rs` |
| `DEVICE_ID_LENGTH`, `DEVICE_ID_HEX_LENGTH` | 8 bytes, 16 hex digits | `packages/rs/crypto/src/spec.rs` |
| `KeyPurpose::HmacUp` | raw purpose 8 | `packages/rs/hal/src/efuse.rs` |
| `FIRST_KEY_BLOCK` | 4 (`KEYn` is eFuse block 4 + n) | `apps/rs/node/src/board/efuse.rs` |
| `DEV_RECORD_MAGIC`, `DEV_RECORD_VERSION` | `CFID`, 1 | `packages/rs/crypto/src/identity.rs` |
| `DEV_RECORD_LENGTH` | 41 bytes | `packages/rs/crypto/src/identity.rs` |
| `cf_ident` partition | Node `0xD000`, Hub `0xE000`, 4 KiB each | `apps/rs/node/partitions.csv`, `apps/rs/hub/partitions.csv` |

There are no timings here: provisioning runs once per boot, before anything else needs a key.

## Where to read the code

- `packages/rs/crypto/src/identity.rs` (`provision_efuse`): the release flow, find or burn, then
  derive.
- `packages/rs/crypto/src/identity.rs` (`provision_dev`, `DEV_RECORD_MAGIC`): the dev-mode `CFID`
  record in flash.
- `packages/rs/crypto/src/identity.rs` (`device_keys_from_hmac`, `Wiped`): `K_dev` from the
  peripheral, and how the root is wiped.
- `packages/rs/crypto/src/keys.rs` (`DeviceKeys`, `derive_purpose_key`, `DeviceId`): the HKDF
  hierarchy.
- `packages/rs/hal/src/efuse.rs` (`Efuse`, `KeyPurpose`): the eFuse model, with no read-key method.
- `packages/rs/hal/src/hmac.rs` (`HmacPeripheral`): the upstream-mode HMAC trait.
- `apps/rs/node/src/board/efuse.rs` (`BoardEfuse`): the ROM burn, the only `unsafe` code of the
  Node. `apps/rs/hub/src/board/efuse.rs` has the same code (only its module
  comment differs).
- `apps/rs/node/src/board/hmac.rs` (`BoardHmac`): the esp-hal HMAC driver in `ToUser` mode;
  `apps/rs/hub/src/board/hmac.rs` is identical.
- `apps/rs/node/src/board/rng.rs` (`BoardTrng`): why the radio must be on before the TRNG runs.
- `apps/rs/node/src/main.rs` (`compile_error!`, `MODE`): the release guard and the provisioning
  call; `apps/rs/hub/src/main.rs` has the same guard.
- [Node README](../../apps/rs/node/README.md) and [Hub README](../../apps/rs/hub/README.md),
  "Identity modes": the operator view and the log lines.
