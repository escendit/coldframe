# coldframe-hub

Hub firmware for the ESP32-S3, in `no_std` Rust on esp-hal.

Story 3.2 brings the chip up with a hardware-bound identity (AD-12):

1. It starts the radio, which is also the entropy source of the TRNG.
2. It finds its identity root in an eFuse key block, or burns one on first boot.
3. It derives `K_dev`, the purpose keys and the Device ID, logs the Device ID and idles.

Story 3.4 adds the BLE setup service (AD-25), see [BLE setup](#ble-setup):

4. It loads its setup code from the `cf_setup` partition, or draws and stores one on first boot.
5. While unprovisioned, it prints the code on the serial console, advertises the setup service and
   runs setup sessions until the app has bound it to a Site, enrolled it and given it a Wi-Fi
   network it could join.

Story 3.5 connects it to the Server, see [Uplink](#uplink):

6. The setup session checks the Server after the join (DHCP, SNTP, one signed heartbeat) and stores
   the provisioning record, with the Server address, only when the Server accepted the Hub.
7. From then on, and on every provisioned boot, it keeps the strongest access point joined, sets its
   clock by SNTP once per boot, and sends a signed heartbeat every 30–60 s.

Story 4.4 makes it the relay between Nodes and the Server, see [Relay](#relay):

8. ESP-NOW runs on the Wi-Fi station's radio and channel. The Hub answers the probes of Nodes,
   forwards their sealed frames to `POST /device/ingest` unread, and passes the sealed downlinks
   back. It stores no Reading and builds no acknowledgement.

The logic is not in this crate. The identity lives in [`coldframe-crypto`](../../../packages/rs/crypto)
(`identity` module), the setup service in [`coldframe-setup`](../../../packages/rs/setup) and the
uplink in [`coldframe-uplink`](../../../packages/rs/uplink), all behind the traits of
[`coldframe-hal`](../../../packages/rs/hal), and all tested on the host with their mocks
(`cargo test --workspace` in the repository root). This crate only implements those traits for the
chip, in `src/board/`, and wires them up in `src/main.rs`.

## Layout

| Path | What |
| --- | --- |
| `src/main.rs` | Boot sequence, the dev-mode guard, the identity log line, the setup code line, `run_setup`, the relay task, the uplink loop and its log lines, the uptime task |
| `src/board/radio.rs` | `Radio` over the esp-radio Wi-Fi controller, and ESP-NOW from it |
| `src/board/espnow.rs` | The relay's radio task over esp-radio's ESP-NOW, and the `Relay` it shares with the uplink (`RelayPort`): one short critical section per access |
| `src/board/ble.rs` | `SetupLink` over trouble-host: the setup GATT service, advertising and one connection, bridged to the setup service through embassy-sync channels |
| `src/board/wifi.rs` | `Wifi` over the esp-radio station: active all-channel scan, join pinned to a BSSID and channel, link state, leave |
| `src/board/net.rs` | `Net`: the embassy-net stack (DHCP, DNS) and its task, SNTP over UDP, HTTPS through one mbedtls-rs session per request with reqwless writing the request and parsing the response. Its buffers hold an ingest request and answer of a full batch |
| `src/board/rtc.rs` | `Rtc` over the ESP32-S3 RTC, installed as mbedTLS's wall clock (and embassy-time as its timer) |
| `src/board/timer.rs` | `Timer` over embassy-time |
| `src/board/roots.rs` | The public root certificates, inlined as a string literal |
| `src/board/rng.rs` | `Trng` over the esp-hal TRNG, which refuses to run while the radio is off |
| `src/board/efuse.rs` | `Efuse`: reads through `esp_hal::efuse`, burns through the ROM routine `ets_efuse_write_key`. With the two mbedTLS clock hooks in `rtc.rs`, the only `unsafe` code of the Hub. Builds without `dev-mode` only |
| `src/board/hmac.rs` | `HmacPeripheral` over the HMAC accelerator in upstream mode. Builds without `dev-mode` only |
| `src/board/flash.rs` | `Flash` over the Coldframe data partitions (`cf_setup`, and `cf_ident` in dev mode), one esp-storage instance shared by both |
| `partitions.csv` | The partition table, with `cf_setup` (setup code and provisioning record) and the dev-mode identity partition `cf_ident` |
| `check-image.sh` | FR-1: builds the release image with Wi-Fi credential canaries in the environment and fails if one appears in the ELF or the flash image |

The crate is standalone and not a member of the host workspace. `Cargo.lock` is committed.

## Prerequisites

- The `esp` Rust toolchain, installed with `espup` (CI pins espup 0.17.1 and toolchain 1.95.0.0).
  Run `source ~/export-esp.sh` in every shell before you build.
- **cmake and ninja** on `PATH`. mbedtls-rs is built with `hook-wall-clock`, so certificate validity
  dates are checked (spike H-2), and that rebuilds the mbedTLS C sources with espup's Xtensa GCC
  (`use-gcc`) and esp-clang's libclang. For example `uv tool install cmake ninja`, or
  `sudo apt install cmake ninja-build`.
- `espflash` 4.x.
- An ESP32-S3 board on USB.

## Build and flash

Run everything from this folder.

```sh
source ~/export-esp.sh
cargo build --release            # release firmware: eFuse identity
cargo build --features dev-mode  # debug firmware: software identity in flash
cargo fmt --check
cargo clippy --release -- -D warnings
```

`cargo run --release` flashes the board with `partitions.csv` and opens the serial monitor (the
runner is `espflash flash --monitor --partition-table partitions.csv`).

FR-1 says the image holds no Wi-Fi credentials; credentials arrive only over BLE setup. Two checks
look for the build-time ways one could get in; they do not prove FR-1 on their own:

- `tests/rs/setup/tests/image_guard.rs` (host, every CI run) scans the sources the image links, this
  crate and `packages/rs/{setup,uplink,protocol,crypto,hal}`, plus their build scripts. It fails on any
  environment read other than Cargo's `CARGO_PKG_*`, `OUT_DIR` and `CARGO_MANIFEST_DIR`, on any
  `include_str!`/`include_bytes!`, and on an `[env]` table in this crate's Cargo configuration
  (`.cargo/config.toml` has none; the log level is set in code).
- `./check-image.sh` (the CI `firmware` job, or by hand with the esp toolchain) builds the release
  image with canary values in `WIFI_SSID`, `WIFI_PASSWORD`, `SSID`, `PASSWORD` and `CF_WIFI_SSID`,
  and fails if one appears in the ELF or the flash image.

The CI `firmware` job runs the release and dev-mode builds, clippy, rustfmt and `check-image.sh` on
every change to Rust code. The Server address is no build input either: the app sends it in
`SiteBinding` during setup.

> [!NOTE]
> Story 3.4 moved the partition table: `nvs` shrank to `0x3000` and `cf_setup` sits at `0xC000`,
> inside the old `nvs` range. `cf_ident` stays at `0xE000`. The firmware never writes `nvs`, so the
> range is normally erased; if a board flashed with an older build halts with
> `setup code record is corrupt`, erase it once: `espflash erase-region 0xC000 0x2000`.

> [!WARNING]
> **The first boot of any build without `--features dev-mode` (debug or release) burns an eFuse
> key block, and that cannot be undone.** A plain `cargo build` or `cargo run` burns too.
> One of the six key blocks becomes a read-protected, write-protected HMAC key with purpose
> `HMAC_UP`. The Hub then keeps that identity for life. Only flash a build without
> `--features dev-mode` (debug or release) onto a board meant to be a Coldframe Hub, and check its key purposes first (see the
> [bench checklist](../../../docs/bench/hub-identity-checklist.md)).

## Identity modes

### eFuse (default: any build without `--features dev-mode`, debug or release)

- A key block with purpose `HMAC_UP` (8) is the identity. If there is one, it is used and nothing
  is burned.
- On first boot, the Hub takes 32 bytes from the TRNG with the radio on and rejects all-zero or
  all-`0xFF` output. It burns the bytes into the first unused key block with purpose `HMAC_UP`,
  which also sets read and write protection. It then re-reads the block and checks the purpose
  and the read protection.
- `K_dev = HMAC-SHA256(root, "coldframe/device/v1")` is computed by the HMAC peripheral. The root
  never leaves the eFuse.
- It refuses to go on, and burns nothing, when:
  - two blocks have purpose `HMAC_UP`;
  - the `HMAC_UP` block is not read-protected;
  - no key block is unused.

If power fails during the burn, the block can end up holding data with purpose `USER`. The chip
then reports that block as used, so the next boot burns a different free block. One block is
lost, but a key is never reused.

A burn can also set purpose `HMAC_UP` without setting the block's read protection (`RD_DIS`). The
first boot then reports `BurnNotVerified`, and every later boot halts with `IdentityNotProtected`.
Such a board can no longer hold a Hub identity: retire it, or use it only with `dev-mode`.

### Dev mode (`--features dev-mode`)

- The root is a TRNG-generated software key, stored in the `cf_ident` partition as a 41-byte record:
  `CFID ‖ 0x01 ‖ root[32] ‖ SHA-256(prefix ‖ root)[0..4]`.
- On first boot the partition is erased (`0xFF`), so the Hub writes the record and reads it back.
  Later boots read the record.
- A record that is neither erased nor valid stops the Hub. It is never replaced silently. To start
  over with a new identity on purpose, erase the partition:
  `espflash erase-region 0xE000 0x1000`.
- A dev-mode build compiles no eFuse or HMAC code at all, so it cannot burn anything.
- **Release builds refuse the feature:** `cargo build --release --features dev-mode` fails with a
  `compile_error!`. Dev mode is for debug builds only.

## Log lines

| Line | Meaning |
| --- | --- |
| `identity mode=efuse source=burned KEYn device_id=<16 hex>` | First boot: the root was burned into key block `n` just now |
| `identity mode=efuse source=existing KEYn device_id=<16 hex>` | A later boot: the identity in key block `n` was used, and nothing was burned |
| `identity mode=dev source=generated device_id=<16 hex>` | Dev mode, first boot: a new root was written to `cf_ident` |
| `identity mode=dev source=stored device_id=<16 hex>` | Dev mode, later boot: the root was read from `cf_ident` |
| `identity mode=… failed stage=… error=…; halted` | Provisioning failed. The Hub stops and does not retry. The error names only the kind of failure |
| `setup code=XXXXXXXX` | A plain serial line (not a log record), on first boot and on every boot while unprovisioned. Never shown once provisioned |
| `corrupt provisioning record` | The `CFWP` record is neither erased nor valid; the Hub treats itself as unprovisioned |
| `ble advertising setup service as Coldframe Hub XXXX` | The setup service is waiting for the app |
| `ble connected` / `ble disconnected` | One setup session begins or ends |
| `setup provisioned` | A session passed the Server check and stored the provisioning record; the Hub stops advertising. No SSID, Site or Server is logged |
| `heap free=… used=… min_free=…` | Once, right after setup. `min_free` is the lowest free heap since boot, which covers BLE, Wi-Fi and the check's TLS connection; the budget is at least 32 KiB |
| `setup provisioned earlier; not advertising` | A provisioned boot: no advertising, straight to the uplink |
| `setup failed stage=… error=…; halted` | The setup store, the BLE link, the network stack or mbedTLS failed to start. The Hub stops |
| `uplink server=… port=…` | The uplink starts, to the Server host the app bound it to |
| `wifi joined channel=… rssi=…` | Joined the strongest access point of the SSID |
| `wifi join failed kind=… retry_ms=…` | A join failed (`scan-failed`, `not-heard`, `unsupported`, `wrong-password`, `not-found`, `join-unsupported`, `join-failed`); the next try follows after 1 s, doubling to 60 s |
| `wifi link lost; re-joining` | The station lost its access point |
| `sntp clock set unix_s=…` | Once per boot, before any TLS (a Hub fresh from setup already set it during the check) |
| `sntp failed kind=… retry_ms=…` | SNTP gave no time; retried with the same backoff. No heartbeat goes out until it succeeds |
| `heartbeat ok server_time_s=… next_ms=…` | The Server accepted a heartbeat; the clock follows `serverTime`; the next one in 30–60 s |
| `heartbeat rejected status=… next_ms=…` | The Server answered another status (401: not enrolled, or the authentication failed) |
| `heartbeat failed kind=… next_ms=…` | `no-ip`, `dns`, `tls`, `http`, `timeout`, `bad-reply` (200 without a valid `serverTime`), `no-clock` or `no-entropy`; `next_ms=0` means the link is down and the Hub re-joins at once |
| `net tls handshake failed: … verify_flags=0x…` | The TLS handshake failed; the flags are mbedTLS's certificate verification result (for example an unknown root, a name mismatch, or dates outside the certificate's validity) |
| `relay listening` | The relay's radio task runs |
| `relay probe from=<mac> pending=<n>` | A Node probed and was answered; `pending`: how many downlinks the Hub held for it and sent right after the reply, each for the last time |
| `relay probe from=<mac> unanswered: not relaying` | A Node probed while the Hub was not associated or had no clock; it is left without a reply, so it sends nothing |
| `relay uplink from=<mac> bytes=<n>` | A sealed frame was queued for the Server |
| `relay queue full; uplink from=<mac> dropped` | More than 8 frames arrived before a request went out; the Node sends it again |
| `relay downlink to=<mac> bytes=<n>` | A sealed downlink from the Server went out over the radio (and stays kept until that Node's next probe) |
| `ingest ok frames=<n> downlinks=<m>` | One `POST /device/ingest` relayed `n` frames; `m` results carried a downlink. A status is not an acknowledgement, so none is logged per frame |
| `ingest rejected status=… frames=<n>; batch dropped` | The Server answered another status (401, 503); nothing is retried, the Nodes send again |
| `ingest failed kind=… frames=<n>; batch dropped` | `no-ip`, `dns`, `tls`, `http`, `timeout`, `bad-reply` (200 that is not one result per frame), `not-linked`, `no-clock` or `no-entropy`; the last three mean nothing was sent |
| `relay queue was full; uplinks dropped=<n>` | How many frames the full queue cost since the last request |
| `uptime s=… device_id=… heap_free=… heap_used=… heap_min_free=…` | Once a minute |

The Device ID is the only identity value that is ever logged. No root, key or HMAC output is
logged, and neither is the setup code (outside its one serial line), a Wi-Fi password, a Site ID,
any setup message, a request nonce, signature or body, or a sealed envelope. A Node appears in the
relay lines by its MAC address only.

## BLE setup

The transport and message order are in [`packages/proto/README.md`](../../../packages/proto/README.md#ble-transport).
On the Hub:

- **Setup code.** 8 Crockford base32 characters (`0123456789ABCDEFGHJKMNPQRSTVWXYZ`, 40 bits)
  drawn from the TRNG once, stored at offset 0 of `cf_setup` as
  `CFPC ‖ 0x01 ‖ code[8] ‖ SHA-256(prefix ‖ code)[0..4]`. A corrupt record halts the Hub
  (`setup failed stage=store error=setup code record is corrupt`); it is never regenerated,
  because the code may already be written down. Erasing `cf_setup` (`espflash erase-region 0xC000 0x2000`)
  gives a new code on the next boot and unprovisions the Hub.
- **Provisioning record.** At offset `0x1000` of `cf_setup`:
  `CFWP ‖ 0x02 ‖ len ‖ ssid ‖ len ‖ password ‖ len ‖ site_id ‖ len ‖ server_url ‖ SHA-256(prefix ‖ …)[0..4]`,
  written only after a successful join and Server check. Its presence stops advertising. Any other
  version, including Story 3.4's `0x01` (which has no Server address), is corrupt: the Hub logs
  `corrupt provisioning record`, advertises again with the same code, and must be set up again.
- **Advertising.** Name `Coldframe Hub XXXX` (the first four Device ID hex digits, uppercase), the
  setup service UUID in the advertising data, only while unprovisioned.
- **Wi-Fi.** The Hub scans every channel, answers with one entry per SSID (its strongest access
  point, strongest first, at most 16), and joins the strongest BSSID of the chosen SSID with
  WPA2-Personal, or open for an empty password. WPA3-only networks are listed as
  `WIFI_SECURITY_WPA3_ONLY` and refused with `UNSUPPORTED_SECURITY`: esp-radio has no WPA3-SAE
  station configuration. The choice of access point is `coldframe_uplink::select_bssid`, the same
  function the uplink uses when it re-joins.
- **Server check.** `SiteBinding` must carry the Server address (`server_url`, `https://host[:port]`).
  After the join the Hub waits for DHCP, sets its clock by SNTP (`pool.ntp.org`) and sends one signed
  heartbeat, all within 40 s. An HTTP 200 with a valid `serverTime` stores the record and answers
  `CONNECTED`; anything else (including a 401 because the app has not enrolled the Hub yet) leaves
  the network, stores nothing and answers `NO_SERVER`, and the session stays open for a retry. After
  `CONNECTED` the session refuses further Site, enrolment and Wi-Fi messages, so nothing is stored
  twice.
- **Provisioned reboots.** A provisioned Hub neither advertises nor shows its code; it joins its
  Wi-Fi from the stored record and heartbeats ([Uplink](#uplink)).
- **Residual risk.** The Wi-Fi password sits in plain flash: there is no flash encryption (by
  design since Story 3.2).

The bench procedure is [`docs/bench/hub-setup-checklist.md`](../../../docs/bench/hub-setup-checklist.md),
driven by the desktop client in [`tests/rs/setup-client`](../../../tests/rs/setup-client).

## Uplink

After setup, and on every provisioned boot, `coldframe_uplink::Uplink::run` keeps the Hub online:

- **Wi-Fi (H-1).** Before each heartbeat and after any network error it checks the link. While the
  station is not associated it scans every channel and joins the strongest supported BSSID of the
  SSID. Every failure retries after 1 s, doubling to 60 s; a successful join resets the wait.
- **Clock (AD-11).** Once per boot, before the first TLS connection: DHCP, then SNTP against
  `pool.ntp.org`, retried with the same backoff. Until the clock is set, mbedTLS sees 1970 and
  refuses every certificate, and no heartbeat is attempted. Afterwards the clock follows each
  heartbeat's `serverTime`; the Hub never stamps data with it.
- **Heartbeat (AD-12, FR-13).** Every 30–60 s (45 s ± up to 15 s from the TRNG), `POST
  /device/heartbeat` with `{"protocolVersion":1,"uptimeMs":<n>}`, signed with the `hub-auth/v1` key
  over a strictly increasing timestamp (`max(clock, last + 1)`) and a fresh 16-byte TRNG nonce
  (`packages/crypto-spec`). One TLS connection at a time, closed after each exchange. A non-200
  answer or a network error is logged by kind and the schedule continues.
- **TLS (AD-13, H-2).** mbedtls-rs 0.3.0 with `hook-wall-clock`: chain, host name (SNI) and
  validity dates are checked, against the public roots of `src/board/roots.rs` only (ISRG Root X1
  and X2, SSL.com TLS ECC Root CA 2022, DigiCert Global Root G2). No mTLS and no private CA. The
  request is written and the response parsed by reqwless 0.14 over the mbedtls-rs session.
- **Heap.** 144 KiB plus 64 KiB of reclaimed bootloader RAM, as the radio spike measured (F-5).
  mbedTLS's record buffers take about 32 KiB per connection (F-4), which is why there is never more
  than one.

The bench procedure is [`docs/bench/hub-uplink-checklist.md`](../../../docs/bench/hub-uplink-checklist.md).

## Relay

The Hub is the bridge between Nodes on ESP-NOW and the Server on HTTPS (AD-9). The radio messages
are in [`packages/proto/README.md`](../../../packages/proto/README.md#esp-now-transport); the state
and its rules are `coldframe_uplink::relay`, tested on the host.

- **Radio.** ESP-NOW is taken from the running Wi-Fi controller, so it is on the station's channel
  and follows the access point (peers are added with `channel: None`). A peer entry lives for one
  send only, so the 20-peer limit never matters. `esp-radio` has the `esp-now` feature for it.
- **Radio task.** `relay_task` is its own task. It answers a Probe from the relay state alone,
  without waiting for the HTTPS request that may be in flight, and queues each Uplink. It answers
  only while the Hub can relay (associated, clock set): otherwise a Node would spend a burst and
  its frame counters on frames the Hub drops. A send gives up after 60 ms, well inside the 120 ms
  a Node waits for a reply, so a send to a Node that has gone back to sleep cannot make the answer
  to the next Probe late.
- **Uplinks.** At most 8 wait for one request. With 8 queued the newest is dropped and counted; the
  Node sends it again. The same bytes are never queued twice, and the queue is emptied before the
  request, so no envelope is ever posted twice.
- **Ingest.** Between heartbeats the uplink posts the queued envelopes as one signed
  `POST /device/ingest` with `{"frames":[…]}`: each envelope unread, in standard padded base64, in
  arrival order. It is signed like a heartbeat, for its own path, and shares the heartbeat's
  strictly increasing timestamps. A request that fails (a transport error, any status but 200, a
  reply without one result per frame) drops its batch: nothing is retried, and the Node resends,
  freshly sealed. While the station is not associated or the clock is not set, uplinks are dropped
  and nothing is posted. A request in flight when a heartbeat falls due finishes first.
- **Downlinks.** `results[i].downlink` goes to the sender of `frames[i]`: its JSON string escapes
  resolved (the Server writes `+` as `\u002B`), decoded from base64 and otherwise untouched. The Hub never treats a status as an acknowledgement and never builds one.
- **Kept downlinks.** Up to 8 downlinks per Node, keyed by the source MAC address of its frames
  and in arrival order, are kept in RAM only, out of one pool of 24. A Node with 8 kept loses its
  oldest; with the pool full, the Node heard from longest ago loses all of its. A fresh TLS session
  per request takes longer than a Node's 300 ms window, so the first send usually finds the Node
  asleep. At that Node's next Probe the Hub says how many it holds (`pending`), sends them once
  more, oldest first, and drops them. A reboot empties the pool, and the Nodes send again.
- **After an outage.** A Node sends its backlog as one burst of up to 8 frames. Each frame gets
  its own downlink, all are kept, and the Node collects them at its next wake: the backlog is
  acknowledged one wake after its burst and nothing is sent twice. A kept-alive connection would
  bring the acknowledgements inside the window; it is not built yet.
- **Memory.** The relay state is about 8 KiB of static RAM, and the uplink's request and answer
  buffers about 12 KiB more. That comes out of the main stack's region, which is now about 34 KiB;
  the heap is unchanged.

The bench and garden procedure is
[`docs/bench/node-transport-checklist.md`](../../../docs/bench/node-transport-checklist.md).

## Why `cf_ident` and `cf_setup` have subtype `undefined`

espflash 4.5.0 parses partition tables with esp-idf-part 0.6.0, which only accepts the named ESP-IDF
data subtypes and panics on a custom one such as `0x40`. Both partitions therefore use `undefined`
(0x06), and the firmware finds them by label. A build without `dev-mode` does not need `cf_ident`;
every build needs `cf_setup`.

## Risks to prove on the bench

- The eFuse burn goes through the ROM routine `ets_efuse_write_key`, because esp-hal has no eFuse
  write API. Whether it programs correctly under esp-hal's clock setup can only be shown on real
  hardware. The re-read check after the burn (`BurnNotVerified`) catches a burn that did not take.
- BLE and Wi-Fi share the radio (coex). A Wi-Fi scan or join while a BLE connection is open is
  what the [setup checklist](../../../docs/bench/hub-setup-checklist.md) exercises.
- A TLS handshake with a BLE connection open, and a join, SNTP and handshake inside the setup
  check's 40 s, are what the [uplink checklist](../../../docs/bench/hub-uplink-checklist.md) measures,
  together with the heap's lowest point.
- ESP-NOW next to an associated station and an open TLS session, a probe answered during a
  request, and the main stack's margin with the larger request buffers are what the
  [transport checklist](../../../docs/bench/node-transport-checklist.md) exercises.
- The [identity](../../../docs/bench/hub-identity-checklist.md),
  [setup](../../../docs/bench/hub-setup-checklist.md),
  [uplink](../../../docs/bench/hub-uplink-checklist.md) and
  [transport](../../../docs/bench/node-transport-checklist.md) bench checklists are the acceptance
  tests for the on-device behaviour. CI tests the host crates and builds and lints the firmware; it runs no
  hardware.
