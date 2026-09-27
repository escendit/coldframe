# spike-hub-radio

Throwaway spike firmware for **PRD Open Question 1**: can one ESP32-S3 run Wi-Fi STA,
BLE (GATT peripheral) and ESP-NOW at the same time, reliably, in `no_std` Rust
(esp-hal / esp-radio / trouble-host), with a TLS client on top? It also measures the
Node → Hub → Server → Hub → Node acknowledgement round trip that sets the AD-17
acknowledgement window.

This is not production code: the wire format is spike-only (the real one is in
`packages/proto`, sealed per AD-12), and nothing is hardened. The test plan, success
criteria, results table and decision live in
[`docs/spikes/hub-radio-coexistence.md`](../../../docs/spikes/hub-radio-coexistence.md).

## What is in here

| Path | What |
| --- | --- |
| `src/bin/hub.rs` | The system under test. Wi-Fi STA + DHCP, ESP-NOW relay/ack, BLE GATT peripheral, HTTPS (mbedtls-rs) with keep-alive, SNTP, 30 s heartbeat, 10 s `STATS hub` line. |
| `src/bin/node.rs` | Test harness for the second board. ESP-NOW only: scans channels 1–13 for the Hub, sends 1 frame/s, waits for the ack, logs per-frame results and a 10 s `STATS node` line. Re-scans after 3 consecutive failures (FR-4 channel following). |
| `src/lib.rs` | Spike frame format, latency window (p50/p95/max), helpers. |
| `certs/roots.pem` | Embedded public roots (see `certs/README.md`). |

The crate is standalone. It is not a member of any workspace.

## Pinned stack

esp-hal 1.2.2 (`unstable`), esp-radio 1.0.0-beta.1 (`wifi`, `esp-now`, `ble`, `coex`,
`unstable`), esp-rtos 0.4.0 (`embassy`, `esp-radio`), esp-bootloader-esp-idf 0.6.0,
esp-alloc 0.11.0 (`internal-heap-stats`), trouble-host 0.7.0 (bt-hci 0.9, same as
esp-radio), mbedtls-rs 0.3.0, embassy-net 0.9.1, embassy-executor 0.10.0, embassy-time
0.5.1. `Cargo.lock` is committed.

HTTP is a raw HTTP/1.1 exchange over the mbedtls-rs `Session`, not reqwless. It needs
about 100 lines, it gives separate cold and warm timings, and it avoids an extra
embedded-io adapter layer.

## Prerequisites

- The `esp` Rust toolchain, installed with `espup`. Run `source ~/export-esp.sh` in every shell before you build.
- `espflash` 4.x.
- Two ESP32-S3 boards. Any dev board with native USB or a USB-UART bridge will do.
- A 2.4 GHz Wi-Fi network with WPA2-Personal (or open) security. esp-radio beta.1 has no WPA3 station configuration, so a WPA2/WPA3 mixed-mode network is fine.
- A phone with nRF Connect (or a similar app) for the BLE part.

## Build

Credentials are build-time environment variables. **Never commit them.**

```sh
cd apps/rs/spike-hub-radio
source ~/export-esp.sh

SPIKE_WIFI_SSID='my-2g4-ssid' SPIKE_WIFI_PASSWORD='secret' \
  cargo build --release --bin hub

cargo build --release --bin node
```

The Hub reads these build-time variables (all optional apart from the SSID):

| Variable | Default | Meaning |
| --- | --- | --- |
| `SPIKE_WIFI_SSID` | (empty, logs an error) | Wi-Fi SSID |
| `SPIKE_WIFI_PASSWORD` | (empty = open network) | WPA2-Personal passphrase |
| `SPIKE_URL` | `https://example.com/` | HTTPS endpoint for the relay and heartbeat requests (`https://host[:port]/path`) |
| `SPIKE_HTTP_METHOD` | `POST` | `POST` (small JSON body) or `GET`. Any HTTP status counts as a successful round trip; the status is logged. |
| `SPIKE_ACK_MODE` | `server` | Initial ack mode, `server` or `immediate`. Can be toggled at runtime over BLE (see below). |
| `SPIKE_NTP_SERVER` | `pool.ntp.org` | SNTP server, queried once before the first TLS connection (AD-11) |
| `SPIKE_HEARTBEAT_S` | `30` | Heartbeat period (FR-13) |
| `SPIKE_RELAY_TIMEOUT_MS` | `8000` | Hub-side budget for the server round trip. When it runs out, the Hub acks with `server=1` (failed). |
| `SPIKE_WIFI_PS` | `none` | Wi-Fi modem power save: `none`, `min` or `max`. The Hub logs whether the driver accepted the setting under coex. |
| `SPIKE_WIFI_TX_POWER` | `60` | Max TX power in 0.25 dBm units (60 = 15 dBm). The esp-radio default is 5 dBm. |
| `SPIKE_TLS_HW_ACCEL` | `1` | `0` turns off the SHA/RSA/AES hardware hooks for mbedTLS |
| `SPIKE_BLE` | `1` | `0` builds the Hub without starting BLE, for an A/B baseline run |

The Node reads these:

| Variable | Default | Meaning |
| --- | --- | --- |
| `SPIKE_NODE_INTERVAL_MS` | `1000` | Frame period |
| `SPIKE_NODE_ACK_TIMEOUT_MS` | `10000` | How long to wait for the ack. Keep it above the Hub's relay timeout. |
| `SPIKE_NODE_RESCAN_AFTER` | `3` | Consecutive failures that trigger a channel re-scan |

Cargo tracks these variables, so changing one triggers a rebuild.

### Optional: X.509 validity-time checks (`tls-time-check`)

By default, **certificate validity dates are not checked**. The mbedtls-rs prebuilt
library has no wall clock (`MBEDTLS_HAVE_TIME_DATE` is off). Chain, signature and
hostname are still verified, and the Hub logs a warning at startup. To check the full
AD-11 behaviour (SNTP, then the RTC, then mbedTLS date checks):

```sh
# one-time: cmake + ninja (needed for the mbedTLS on-the-fly rebuild), e.g.
uv tool install cmake && uv tool install ninja   # or: pip install --user cmake ninja

SPIKE_WIFI_SSID=... SPIKE_WIFI_PASSWORD=... \
  cargo build --release --bin hub --features tls-time-check
```

The feature turns on `mbedtls-rs/hook-wall-clock`, which forces an on-the-fly
mbedTLS C build. The C is compiled with espup's Xtensa GCC (`use-gcc`), and the
bindings use esp-clang's libclang from `LIBCLANG_PATH`. No system clang is needed.

## Flash and monitor (two boards on one machine)

Find the ports with `espflash board-info` or `ls /dev/ttyACM* /dev/ttyUSB*`.

```sh
# Hub (board A)
ESPFLASH_PORT=/dev/ttyACM0 SPIKE_WIFI_SSID=... SPIKE_WIFI_PASSWORD=... \
  cargo run --release --bin hub

# Node (board B), in a second terminal
ESPFLASH_PORT=/dev/ttyACM1 cargo run --release --bin node
```

`cargo run` flashes and then opens the monitor (the runner is `espflash flash --monitor`).
For long runs, record the logs to a file:

```sh
espflash monitor --port /dev/ttyACM0 --non-interactive \
  --elf target/xtensa-esp32s3-none-elf/release/hub | tee hub-$(date +%F-%H%M).log
espflash monitor --port /dev/ttyACM1 --non-interactive \
  --elf target/xtensa-esp32s3-none-elf/release/node | tee node-$(date +%F-%H%M).log
```

## BLE interaction

The Hub advertises as **`CF-HUB-SPIKE`**. The GATT service is
`c01df4a0-0000-4000-8000-00000000c0de`:

| Characteristic | UUID | Properties | Behaviour |
| --- | --- | --- | --- |
| provisioning | `c01df4a0-0001-…-c0de` | read, write (≤128 B) | Simulates Wi-Fi credential writes and counts them. A payload starting with `I` switches to **immediate** acks, one starting with `S` switches back to **server** acks. Anything else is only counted. |
| status | `c01df4a0-0002-…-c0de` | read, notify | After every write: `[writes u32 LE, ack mode (1=server, 2=immediate), wifi up, wifi channel, 0]` |

## Log lines

Everything uses plain `log` over esp-println, with no defmt. The lines to look for:

- `STATS hub up_s=… heap_free=… heap_min_free=… wifi_up=… wifi_ch=… wifi_disconnects=… now_rx=… now_acks=… now_send_fail=… ble_conns=… ble_writes=… tls_ok=… tls_fail=… hb_ok=… hb_fail=… ack_p50_ms=… ack_p95_ms=… ack_max_ms=… https_warm_p50_ms=… https_cold_p95_ms=…` every 10 s.
- `STATS node up_s=… ch=… sent=… acked=… lost=… loss_permille=… rtt_server_p50_ms=… rtt_server_p95_ms=… rtt_immediate_p95_ms=…` every 10 s.
- `FRAME seq=… result=acked rtt_us=… hub_us=… mode=…` or `result=timeout` or `result=mac_fail` (Node, one line per frame).
- `SCAN found hub … on channel … after … ms` (Node, after each channel scan).
- `WIFI associated … channel=…`, `WIFI disconnected`, `WIFI channel changed` (Hub).
- `TLS handshake ok in … ms`, `HTTPS Relay|Heartbeat seq=… status=… cold|warm latency_ms=…` (Hub).

Percentiles cover the last 256 samples. `*_max_ms` and `*_n` cover the whole run.
Pull the last stats line into key/value rows:

```sh
grep 'STATS hub' hub.log | tail -1 | tr ' ' '\n' | grep =
grep 'STATS node' node.log | tail -1 | tr ' ' '\n' | grep =
```

## Test procedure and results

See [`docs/spikes/hub-radio-coexistence.md`](../../../docs/spikes/hub-radio-coexistence.md)
for the step-by-step procedure (including the mid-test router channel change) and the
results template to fill in.

Quick smoke test:

1. Flash the Hub. Check for `WIFI associated … channel=N`, `NET got ip`, `SNTP ok`, `TLS handshake ok`, then `HTTPS Heartbeat … status=…` within about 30 s.
2. Flash the Node. Check for `SCAN found hub … on channel N`, then `FRAME seq=1 result=acked`.
3. Connect with nRF Connect and write `I`. The Node's `mode=` changes to `immediate`. Write `S` to switch back.
