# Radio coexistence on the Hub

The Hub runs a Wi-Fi station, ESP-NOW and a BLE peripheral on the one 2.4 GHz radio of an ESP32-S3,
with a TLS client on top. Before any Hub code was written, a spike tested whether that works at
all. This page summarises what the spike found and shows how the Hub firmware applies it. It is the
last page of the firmware reference: the previous page is [ble-setup.md](ble-setup.md), and the
full list is in the [index](README.md).

The spike report is [`hub-radio-coexistence.md`](../spikes/hub-radio-coexistence.md). Its results
and criteria tables live there; this page does not repeat them. The requirements it produced are
H-1 to H-3 and N-1 to N-2 in [device-hardware.md](../../_bmad-output/specs/spec-coldframe/device-hardware.md).
The relay itself is governed by AD-9 and AD-17 in the
[architecture spine](../../_bmad-output/planning-artifacts/architecture/architecture-coldframe-2026-09-26/ARCHITECTURE-SPINE.md).

## What the spike found

The spike firmware ran Wi-Fi STA, ESP-NOW, a BLE GATT peripheral and an HTTPS client as concurrent
embassy tasks on one core, with a Node sending a frame every second and a phone writing over BLE
every 10 s. The verdict (section 10 of the report) is **GO: keep the ESP32-S3**. Over about 3 h 45 min
of runs on two boards there was no crash, ESP-NOW loss stayed well under 1 %, the heap was flat and
channel following recovered on its own. The single radio is time-shared by the Espressif coexistence
arbiter; the firmware does not schedule airtime itself.

The build-time findings that shape the Hub code are:

- **F-3, certificate dates.** mbedTLS checks validity dates only when it is built with a wall-clock
  hook, and that build needs cmake and ninja. This became H-2.
- **F-4, one TLS connection.** The default TLS record buffers cost about 32 KiB of heap per
  connection. Budget one connection and never open two at once.
- **F-5, heap size.** The Hub heap is 144 KiB of regular RAM plus 64 KiB of reclaimed bootloader
  RAM, 208 KiB in total. The runtime check is the lowest free heap since boot.
- **F-6, no WPA3-SAE.** esp-radio 1.0.0-beta.1 has no WPA3-SAE station configuration. This became
  H-3.
- **F-7, ESP-NOW comes from the Wi-Fi controller.** There is no separate ESP-NOW init: it is taken
  from the running `WifiController`, and a peer added with `channel: None` follows the station's
  channel.

## How it works

### Boot order: heap, then BLE, then Wi-Fi

`main` in `apps/rs/hub/src/main.rs` sets up the two heap regions first, with the spike's sizes
(F-5): `heap_allocator!` with `#[ram(reclaimed)]` for 64 KiB, then a regular 144 KiB.

It then creates the BLE connector (`BleConnector::new`) *before* the Wi-Fi controller
(`WifiController::new`), as the esp-radio coexistence examples require. If either fails, the Hub
logs the stage and halts. The Wi-Fi controller is also the radio the TRNG draws entropy from, so it
is on before the identity is provisioned.

A provisioned Hub does not advertise: `main` drops the BLE connector and logs
`setup provisioned earlier; not advertising`. An unprovisioned Hub spawns `ble_task` and runs BLE
setup while Wi-Fi joins in the same session ([ble-setup.md](ble-setup.md)). So BLE and Wi-Fi
overlap on the Hub only during setup.

### Heap budget

The budget is at least 32 KiB free at the lowest point (spike criterion C5). `heap_min_free` in
`main.rs` computes it as heap size minus the peak usage. The Hub logs it twice: once after setup,
when BLE, Wi-Fi and the Server check's TLS have all run, and every 60 s in `uptime_task`. Nothing
enforces the budget at run time; the log line is how you check it on the bench.

### ESP-NOW on the access point's channel

ESP-NOW has no channel of its own on the Hub. `BoardRadio::esp_now` in
`apps/rs/hub/src/board/radio.rs` takes it from the running controller, and `send` in
`apps/rs/hub/src/board/espnow.rs` adds each peer with `EspNowWifiInterface::Station` and
`channel: None`. The Hub therefore always transmits on whatever channel its access point uses.

The consequence is on the Node side: when the router changes channel, the Hub follows it, and the
Node has to find the Hub again by probing ([espnow.md](espnow.md)). While the station is not
associated, or the clock is not set, the relay does not answer probes at all, so a Node never
settles on a Hub that cannot forward its frames.

### Choosing the access point (H-1)

When the station is not associated, `Uplink::step` in `packages/rs/uplink/src/uplink.rs` calls its
`join`, which scans and hands the result to `select_bssid` in `packages/rs/uplink/src/bssid.rs`.
`BoardWifi::scan` in `apps/rs/hub/src/board/wifi.rs` does an active scan of every channel and keeps
the strongest 32 access points (`SCAN_CAPACITY`).

`select_bssid` looks only at access points with the provisioned SSID. It skips any whose security
is unsupported and returns the one with the highest RSSI; on a tie, the first heard wins. It never
takes the first match. `BoardWifi::join` then pins the station to that BSSID and channel. This
matters for mesh networks, where several access points share one SSID.

A join failure retries with a backoff of 1 s, doubling to at most 60 s, and reset by a successful
join.

> [!NOTE]
> H-1 also asks the Hub to re-evaluate its access point when RSSI degrades. The code does not do
> this. The Hub rescans only after the link has dropped: while it stays associated it keeps its
> BSSID, however weak.

### Supported security (H-3)

`security` in `apps/rs/hub/src/board/wifi.rs` maps esp-radio's authentication method onto the
`Security` enum in `packages/rs/hal/src/wifi.rs`, and `Security::is_supported` decides:

- **Open** — supported. An empty passphrase joins with no authentication.
- **WPA or WPA2 Personal** (including mixed WPA/WPA2) — supported.
- **WPA2/WPA3 transition mode** — supported. The Hub joins it with WPA2-Personal.
- **WPA3-only** (WPA3 Personal / SAE, and the WPA3 extended-PSK modes) — unsupported.
- **Anything else** (WEP, OWE, enterprise, WAPI) — unsupported.

`BoardWifi::join` only ever configures `Wpa2Personal` (or `Open`), and refuses a passphrase shorter
than 8 characters as a wrong password. If every access point with the SSID is unsupported,
`select_bssid` returns `SelectError::Unsupported`, and the Hub never tries to join. During BLE setup
the app sees this as the `UnsupportedSecurity` Wi-Fi status. If the access point itself rejects the
security at join time (disconnect reasons 210 and 211), `join_error` reports the same thing.

> [!WARNING]
> WPA3-only Wi-Fi is not supported. esp-radio 1.0.0-beta.1 has no WPA3-SAE station mode (spike F-6).
> Put the router in WPA2 or WPA2/WPA3 transition mode for the Hub's network.

### Certificate dates (H-2)

`apps/rs/hub/Cargo.toml` builds mbedtls-rs with `hook-wall-clock`, so certificate validity dates are
checked (the spike's `tls-time-check`). `main` installs the RTC as mbedTLS's wall clock before
anything can open TLS. Until SNTP sets the clock, the RTC reads 1970 and every certificate fails
closed, which is why the uplink runs SNTP once per boot before its first request (AD-11).

## Key constants and timings

| Constant | Value | Where |
| --- | --- | --- |
| Hub heap | 64 KiB reclaimed + 144 KiB regular | `heap_allocator!` in `apps/rs/hub/src/main.rs` |
| Heap budget | at least 32 KiB free at the lowest point (logged, not enforced) | `heap_min_free` in `apps/rs/hub/src/main.rs` |
| Heap log interval | 60 s | `UPTIME_EVERY` in `apps/rs/hub/src/main.rs` |
| Scan results kept | 32 access points | `SCAN_CAPACITY` in `packages/rs/uplink/src/lib.rs` |
| Join timeout | 30 s | `JOIN_TIMEOUT` in `apps/rs/hub/src/board/wifi.rs` |
| Join backoff | 1 s, doubling, at most 60 s | `BACKOFF_INITIAL_MS`, `BACKOFF_MAX_MS` in `packages/rs/uplink/src/lib.rs` |
| ESP-NOW send timeout | 60 ms | `SEND_TIMEOUT` in `apps/rs/hub/src/board/espnow.rs` |
| ESP-NOW peer channel | `None` (the station's channel) | `send` in `apps/rs/hub/src/board/espnow.rs` |

## Where to read the code

- `apps/rs/hub/src/main.rs` (`main`): the heap regions, BLE before Wi-Fi, and when BLE is dropped.
- `apps/rs/hub/src/main.rs` (`heap_min_free`, `uptime_task`): how the heap budget is measured and
  logged.
- `apps/rs/hub/src/board/radio.rs` (`BoardRadio::esp_now`): ESP-NOW taken from the running Wi-Fi
  controller (F-7).
- `apps/rs/hub/src/board/espnow.rs` (`send`, `relay_task`): peers with `channel: None`, one peer
  entry per send.
- `packages/rs/uplink/src/bssid.rs` (`select_bssid`): the strongest supported BSSID for the SSID
  (H-1).
- `packages/rs/uplink/src/uplink.rs` (`Uplink::step`, `join`): rescan and re-join only after the
  link drops, with backoff.
- `apps/rs/hub/src/board/wifi.rs` (`security`, `join_error`, `BoardWifi::join`): which networks the
  Hub can join, and why WPA3-only is refused (H-3).
- `packages/rs/hal/src/wifi.rs` (`Security::is_supported`): the one place the supported set is
  defined.
- `apps/rs/hub/README.md`: building and flashing the Hub ([README](../../apps/rs/hub/README.md)).
