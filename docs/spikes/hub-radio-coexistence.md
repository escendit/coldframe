---
title: "Spike: Hub radio coexistence (PRD Open Question 1)"
created: 2026-09-27
status: firmware built, hardware run pending
owner: Simon Novak
firmware: apps/rs/spike-hub-radio
---

# Spike: Hub radio coexistence on one ESP32-S3

## 1. Question

**Can one ESP32-S3 run Wi-Fi STA, a BLE GATT peripheral and ESP-NOW at the same time,
reliably, in `no_std` Rust (esp-hal 1.2.2, esp-radio 1.0.0-beta.1, trouble-host 0.7.0),
with an mbedTLS client (mbedtls-rs 0.3.0) on top?**

This is PRD Open Question 1 and addendum Risk (a). The spike also produces the number
AD-17 is waiting for:

**How long does a Node → Hub → Server → Hub → Node acknowledgement take?** That value
sets the acknowledgement window: how long a Node stays awake after sending before it
gives up and collects the ack on its next wake. It feeds the Node energy budget
(roughly 100 mA per awake millisecond, see the addendum power table).

Secondary questions:

- Does ESP-NOW channel following work (addendum Risk (b), FR-4 "Readings keep reaching the Server after the router changes its Wi-Fi channel")?
- What are the RAM and flash footprints of the full Hub radio + TLS stack?

## 2. Context

- **Spine Stack table:** esp-hal 1.2.2 (`unstable`), esp-radio 1.0.0-beta.1 (Wi-Fi, BLE, ESP-NOW, coex), trouble-host 0.7.0 (bt-hci 0.9), mbedtls-rs 0.3.0, reqwless 0.14.0.
- **AD-10:** the spike's ESP-NOW frame format is throwaway. The real messages live in `packages/proto`.
- **AD-11:** the Hub bootstraps its clock with SNTP before its first TLS connection. The spike does SNTP first. Validity-date checking depends on a build feature (see §4, F-3).
- **AD-12:** the real uplink and downlink are sealed. The spike sends a 48-byte filler payload so the airtime is realistic, but it does not seal anything.
- **AD-13:** the Hub trusts public roots only. The spike embeds ISRG Root X1/X2, SSL.com TLS ECC Root CA 2022 (for `example.com`) and DigiCert Global Root G2.
- **AD-17:** "The Node waits for its downlink acknowledgement for a bounded window, set by the Node/Hub epics from the radio spike."
- **Fallback (addendum):** the ESP32-C3 is a candidate, not a decision. It also has one 2.4 GHz radio with Wi-Fi + BLE 5 coex. It is RISC-V and has less RAM (400 KB SRAM vs 512 KB).

## 3. What the firmware does

The firmware is `apps/rs/spike-hub-radio` (see its README for build and flash).

**Hub (system under test).** These run as concurrent embassy tasks on one core:

1. **Wi-Fi STA + DHCP.** It reconnects forever and logs the channel on association and on change.
2. **ESP-NOW on the same radio.** The ESP-NOW instance comes from `WifiController::esp_now()`, and its peers follow the STA channel. It answers Node probes. For each DATA frame it either performs an HTTPS round trip and then acks (**server** mode), or acks at once (**immediate** mode). The ack carries the Node's sequence number and the time the Hub spent between receiving the frame and sending the ack.
3. **BLE peripheral (trouble-host).** It advertises continuously as `CF-HUB-SPIKE`. It exposes one GATT service with a writable "provisioning" characteristic and a notify characteristic, and counts connections, writes and disconnects. Writing `I` or `S` switches the ack mode at runtime, so both modes can be measured in one run.
4. **TLS client (mbedtls-rs).** It runs SNTP first, then HTTPS `POST` of a small JSON body to `SPIKE_URL` over a **kept-alive** connection. It records the handshake time, cold latency (DNS + TCP + TLS + request) and warm latency (request on the open connection).
5. **Heartbeat.** An HTTPS request every 30 s (FR-13).
6. **Stats.** One `STATS hub` line every 10 s: uptime, heap free, min free and used; Wi-Fi state, channel, RSSI and reconnects; ESP-NOW rx, acks and send failures; BLE counters; TLS ok, fail and handshake counts; ack latency and HTTPS latency p50/p95/max.

**Node (harness).** ESP-NOW only, with no association. It probes channels 1–13 for the
Hub, then sends a 56-byte frame every second and waits up to 10 s for the ack. It logs
every frame and a `STATS node` line (sent, acked, lost, loss ‰, RTT p50/p95/max per ack
mode, channel). After 3 consecutive failures it re-scans, which prototypes FR-4 channel
following.

## 4. Build-time findings (2026-09-27, no board attached)

| # | Finding | Impact |
| --- | --- | --- |
| **F-1** | **Wi-Fi + ESP-NOW + BLE + coex + mbedTLS compile and link together for the ESP32-S3.** esp-radio 1.0.0-beta.1 features `wifi,esp-now,ble,coex,unstable` plus trouble-host 0.7.0 plus mbedtls-rs 0.3.0. No feature conflict and no link error. The esp-radio README lists the ESP32-S3 as supporting Wi-Fi, BLE, coex (Wi-Fi + BLE) and ESP-NOW. The whole graph resolves to esp-hal 1.2.2 and bt-hci 0.9.0 from crates.io with no `[patch]` (the mbedtls-rs examples still patch esp-hal from git for esp-radio beta.0). | No compile-time blocker. Open Question 1 is now a runtime question only. |
| **F-2** | **The full Hub fits comfortably in flash.** The app image is 1,165,680 B, 28.2 % of the 4 MB default app partition. The Node image is 463,200 B (11.2 %). | No flash constraint. OTA with two app slots is feasible. |
| **F-3** | **mbedtls-rs does not check certificate validity dates by default.** The prebuilt mbedTLS is built without `MBEDTLS_HAVE_TIME_DATE`. Chain, signature and hostname are verified, but expired or not-yet-valid certificates pass. Turning on `hook-wall-clock` (this crate's `tls-time-check` feature) forces an **on-the-fly mbedTLS C rebuild**. That rebuild needs `cmake` and `ninja`, which the dev host does not have by default. With `cmake` and `ninja` from PyPI (`uv`), espup's Xtensa GCC (`use-gcc`) and esp-clang's libclang, it **builds cleanly**. That image is 1,119,792 B. The default build logs a warning that the checks are off. | AD-11 is achievable, but the Hub build needs cmake + ninja in CI and on developer machines. That affects NFR-7 reproducibility docs. The Hub epic should make `tls-time-check` the default. |
| **F-4** | mbedTLS record buffers default to 16 KiB in + 16 KiB out, about 32 KiB of heap per TLS connection. Shrinking them (`ssl-in/out-content-len-*`) also forces the on-the-fly rebuild. | Budget one TLS connection (about 40 KiB with handshake transients) in the Hub heap. Do not plan concurrent TLS connections. |
| **F-5** | **Static RAM (Hub):** `.data` 20.4 KB, `.bss` 183 KB (144 KB of it is the heap arena; about 39 KB is real statics), IRAM `.rwtext` 64.6 KB (52.6 KB of it Wi-Fi blobs). The heap is 144 KB plus 64 KB reclaimed bootloader RAM, 208 KB in total. About 72.7 KB of stack is left for the main thread. **Node:** `.data` 12.3 KB, `.bss` 87.5 KB (72 KB heap). | The runtime check is `heap_min_free` (see §6). |
| **F-6** | esp-radio beta.1 station authentication stops at `WpaWpa2Personal`: there is no WPA3-SAE station configuration. | FR-1 provisioning cannot join a WPA3-only network. Document "WPA2 or WPA2/WPA3 mixed" as a requirement, or track esp-radio. |
| **F-7** | API drift to know about. On crates.io, esp-rtos 0.4.0 is `esp_rtos::start(timer, FROM_CPU_INTR0)` (two arguments), while the esp-hal repo examples at tag `esp-radio-v1.0.0-beta.1` call `start(timer)`. ESP-NOW is taken from a running `WifiController` with `esp_now(&self)`: Wi-Fi keeps running, and there is no separate init. Peers must be added with `EspNowWifiInterface::Station` and `channel: None` so that they follow the STA's channel. | Base Hub-epic code on the published crate sources, not on repo examples. |
| **F-8** | The spike uses raw HTTP/1.1 over the mbedtls-rs `Session` instead of reqwless 0.14. This gives separate cold and warm timings with about 100 lines and no embedded-io adapter layer. | The spine still names reqwless. Revisit in the Hub epic. reqwless is not required for the round trip. |

Build commands used for verification (dummy credentials):

```sh
cd apps/rs/spike-hub-radio && source ~/export-esp.sh
SPIKE_WIFI_SSID=dummy SPIKE_WIFI_PASSWORD=dummypass cargo build --release --bin hub   # ok, 0 warnings
cargo build --release --bin node                                                     # ok, 0 warnings
SPIKE_BLE=0 SPIKE_WIFI_SSID=dummy SPIKE_WIFI_PASSWORD=dummypass cargo build --release --bin hub   # ok (A/B baseline)
PATH=<venv with cmake+ninja>:$PATH SPIKE_WIFI_SSID=dummy SPIKE_WIFI_PASSWORD=dummypass \
  cargo build --release --bin hub --features tls-time-check                          # ok
```

## 5. Hypotheses

- **H1, functional coexistence.** All three radios work at the same time on the S3 without crashes or driver asserts. The single 2.4 GHz radio is time-shared by the Espressif coex arbiter.
- **H2, BLE costs ESP-NOW jitter, not loss.** BLE advertising and connection events take airtime, which raises ESP-NOW RTT jitter. ESP-NOW unicast MAC retries absorb most collisions, so loss stays below 1 %.
- **H3, modem sleep dominates radio latency if coex forces it.** ESP-IDF documentation ties Wi-Fi/BT coexistence to Wi-Fi modem sleep. If `PowerSaveMode::None` is rejected or ignored under coex, the Hub's STA sleeps between DTIM beacons. Unicast ESP-NOW frames to the Hub then wait for retries, and **immediate-mode RTT p95 could reach the DTIM period (about 100–300 ms)** instead of a few ms. The Hub logs whether `set_power_saving(None)` was accepted.
- **H4, server mode is dominated by HTTPS latency.** With a kept-alive connection, server-mode RTT is about equal to immediate-mode RTT plus warm HTTPS latency. That is roughly 20–80 ms to a LAN server and 50–250 ms to `example.com`. A **cold** connection (DNS + TCP + TLS with software ECDSA P-384 verification on Xtensa) takes an estimated 1–3 s and must stay out of the ack path.
- **H5, heap is enough.** Coex + ESP-NOW + one TLS session fit in the 208 KB heap with at least 32 KB minimum free, and there is no leak over 2 h.
- **H6, channel following works.** After a router channel change, the Hub re-associates on the new channel within about 15 s. The Node sees immediate MAC-level send failures, re-scans after 3 failures, and is acked again within about 20 s of the Hub coming back.

## 6. Success criteria

The main run (§7, Phase B) takes **2 hours**, with a phone connected over BLE writing
to the provisioning characteristic **every 10 s**. The Node sends 1 frame/s.

| # | Criterion | Threshold | Source |
| --- | --- | --- | --- |
| C1 | Wi-Fi stays associated | `wifi_disconnects` = 0 outside the deliberate channel-change window. After the change: exactly one reconnect, on the new channel. | `STATS hub` |
| C2 | ESP-NOW loss | **< 1 %** (`lost / sent` at the Node), excluding the channel-change window, which is reported separately | `STATS node` |
| C3 | Hub stability | **0** crashes, panics, watchdog resets or reboots. `up_s` increases throughout the run. | Hub log |
| C4 | TLS heartbeat success | **≥ 99 %** (`hb_ok / (hb_ok + hb_fail)`) | `STATS hub` |
| C5 | Heap margin | `heap_min_free` **≥ 32 KiB** for the whole run, and `heap_free` at 2 h within 4 KiB of its value at 10 min (no leak) | `STATS hub` |
| C6 | BLE stays usable | Every write succeeds. The notify arrives. After 10 manual disconnect/reconnect cycles, the Hub advertises again within 2 s each time. `ble_adv_err` = 0. | phone + `STATS hub` |
| C7 | Ack RTT reported | p50/p95/max of the Node-side RTT in **both** modes (immediate and server) and the Hub-side rx→ack time. Informational, no pass threshold: this sets AD-17. | `STATS node`, `STATS hub` |
| C8 | Channel following | After the router channel change, the Node is acked again within **60 s** with no manual action | Node log |

## 7. Procedure

### Setup

1. Put the router on a fixed 2.4 GHz channel (say **1**) and write down the model and firmware. Keep both boards 1–3 m from the router and 1–5 m from each other. Record the RSSI (the Hub logs it in `STATS hub wifi_rssi`, and the Node logs it per frame).
2. Choose the endpoint. The number that matters for AD-17 comes from a **LAN server with a public certificate** (the real AD-13 topology). If one is reachable, set `SPIKE_URL=https://<lan-host>/<path>`; any path works, since any HTTP status counts. `example.com` (the default) crosses the internet, so treat its result as an upper bound. If time allows, run Phase B once against each.
3. Build and flash (see the crate README):

   ```sh
   cd apps/rs/spike-hub-radio && source ~/export-esp.sh
   ESPFLASH_PORT=/dev/ttyACM0 SPIKE_WIFI_SSID=... SPIKE_WIFI_PASSWORD=... [SPIKE_URL=...] \
     cargo run --release --bin hub
   ESPFLASH_PORT=/dev/ttyACM1 cargo run --release --bin node
   ```

4. Record both logs to files for the whole session (`espflash monitor --non-interactive ... | tee`). Note the wall-clock start time. The Hub logs `unix_s` after SNTP.
5. Phone: nRF Connect (Android or iOS). Connect to `CF-HUB-SPIKE`. For the 10 s write loop, use nRF Connect **Macros**: record one write of a 64-byte payload that does *not* start with `I` or `S` (for example `ssid=coldframe-test;psk=...`) to `c01df4a0-0001-…`, then repeat it with a 10 s delay. Enable notifications on `c01df4a0-0002-…`.

### Phase 0: smoke test (10 min)

- Hub log: `wifi power save = None` or a rejection warning (**record which**, it bears on H3), then `WIFI associated … channel=1`, `NET got ip`, `SNTP ok`, `TLS handshake ok in N ms`, and `HTTPS Heartbeat … status=…`.
- Node log: `SCAN found hub … on channel 1`, then `FRAME … result=acked`.
- BLE: write `I` and check that the Node lines show `mode=immediate`. Write `S` to switch back.

### Phase A: baseline without BLE (2 × 20 min)

Build the Hub with `SPIKE_BLE=0`:

- **A1:** `SPIKE_ACK_MODE=server`, 20 min.
- **A2:** `SPIKE_ACK_MODE=immediate`, 20 min.

Record the last `STATS` lines. This separates BLE's cost (H2) from Wi-Fi + ESP-NOW + TLS alone.

### Phase B: main coexistence run (2 h, BLE on, default build)

| Time | Action |
| --- | --- |
| 0:00 | Reflash the default Hub (BLE on, server mode). Start the Node. Connect the phone and start the 10 s write macro. |
| 0:00–0:50 | **Server mode.** Leave it alone. |
| 0:50 | Write `I` (from a second macro or by hand) → **immediate mode**. |
| 0:50–1:10 | Immediate mode. |
| 1:10 | Write `S` → back to server mode. |
| 1:15 | **BLE churn:** disconnect and reconnect the phone 10 times, about 10 s apart. Note how long it takes to reconnect each time (C6). Then resume the write macro. |
| 1:30 | **Router channel change** 1 → 11 (or 6), without rebooting the router if it allows that. Note the exact time. Watch for `WIFI disconnected`, `WIFI associated … channel=11` (Hub), and `re-scanning` then `SCAN found hub … on channel 11` (Node). |
| 1:30–2:00 | Server mode on the new channel. |
| 2:00 | Stop. Save both logs. Copy the last `STATS hub` and `STATS node` lines, plus the ones taken just before 0:50, 1:10 and 1:30. |

### Phase C: optional variations (30 min each)

- **C-PS:** `SPIKE_WIFI_PS=min`. Measures the modem-sleep penalty on immediate-mode RTT (H3). Relevant if Phase 0 showed that `None` was rejected.
- **C-TIME:** build with `--features tls-time-check` and confirm that the handshakes still succeed with validity dates checked (AD-11). To check fail-closed behaviour, block NTP at the router: the handshake must then fail.
- **C-LAN / C-WAN:** repeat Phase B's server-mode segment against the other endpoint.

### Pulling numbers out of the logs

```sh
grep 'STATS hub'  hub.log  | tail -1 | tr ' ' '\n' | grep =
grep 'STATS node' node.log | tail -1 | tr ' ' '\n' | grep =
grep -c 'result=timeout\|result=mac_fail' node.log
grep -E 'panic|Exception|rst:|Guru|abort' hub.log     # C3: must be empty
```

## 8. Results

*To be filled in after the hardware run.*

### Environment

| Item | Value |
| --- | --- |
| Date / duration | |
| Firmware commit | |
| Hub board / Node board | |
| Router (model, firmware), channel(s), security | |
| Endpoint (`SPIKE_URL`), LAN or WAN | |
| Distance Hub↔router / Hub↔Node; RSSI Hub (`wifi_rssi`) / Node (per-frame `rssi`) | |
| Phone / BLE app | |
| Power save setting accepted? (`wifi power save = …` line) | |

### Latency (ms)

| Metric | A1 no BLE, server | A2 no BLE, immediate | B server (0:50) | B immediate (1:10) | B server after channel change (2:00) |
| --- | --- | --- | --- | --- | --- |
| Node RTT p50 | | | | | |
| Node RTT p95 | | | | | |
| Node RTT max | | | | | |
| Hub rx→ack p50 / p95 / max | | | | | |
| HTTPS warm p50 / p95 / max | | — | | — | |
| HTTPS cold p50 / max | | — | | — | |
| TLS handshake p50 / max | | — | | — | |

### Reliability

| Metric | A1 | A2 | B total | B excl. channel change |
| --- | --- | --- | --- | --- |
| Node sent / acked / lost / loss % | | | | |
| MAC send failures (Node `mac_fail`) | | | | |
| Hub `now_send_fail` / `now_relay_fail` | | | | |
| Wi-Fi disconnects | | | | |
| Heartbeats ok / failed (%) | | | | |
| TLS ok / fail / handshakes / `http_reconnects` | | | | |
| BLE conns / writes / disconnects / adv errors | — | — | | |
| Hub resets / panics | | | | |

### Memory

| Metric | Value |
| --- | --- |
| `heap_size` | |
| `heap_free` at 10 min / at 2 h | |
| `heap_min_free` (whole run) | |

### Channel change (Phase B, 1:30)

| Metric | Value |
| --- | --- |
| Router change time | |
| Hub `WIFI disconnected` → `WIFI associated channel=…` | s |
| First Node failure → `SCAN found hub` | s |
| Router change → first `result=acked` on the new channel | s |
| Frames lost during the change | |

### Criteria

| # | Result | Pass? |
| --- | --- | --- |
| C1 Wi-Fi associated | | |
| C2 ESP-NOW loss < 1 % | | |
| C3 0 resets | | |
| C4 heartbeat ≥ 99 % | | |
| C5 heap_min_free ≥ 32 KiB, no leak | | |
| C6 BLE usable, re-advertises | | |
| C7 RTT reported (both modes) | | n/a |
| C8 channel following ≤ 60 s | | |

## 9. Deriving the AD-17 acknowledgement window

The window W is how long the Node listens after its last send. Use Phase B, server mode:

- **W = max(2 × p95, max) of the warm server-mode Node RTT, rounded up to 50 ms, with 150 ms as the floor.** The floor absorbs one ESP-NOW retry and the scheduling jitter of a cold-booted Node.
- Cold TLS connections must **not** be inside W. If the observed cold latency exceeds W, the Hub epic must keep the connection warm (the 30 s heartbeat does this if the server's keep-alive idle timeout is longer than 30 s), and a Node that misses W collects its ack on the next wake (AD-17's existing fallback). Record the server's idle timeout from `http_reconnects` and the timing of the `warm connection failed` log lines.
- Energy check: awake time per wake ≈ boot + sensor read + send + W. Every 100 ms of W costs about 0.27 mAh/day at 96 wakes/day and 100 mA. Compare this against the addendum's roughly 2.5 mAh/day budget.

| Value | Result |
| --- | --- |
| Warm server-mode RTT p95 / max | |
| **Proposed W** | |
| Cold latency (why it must stay out of W) | |

## 10. Decision

*To be filled in after the hardware run.*

| Outcome | When | Consequence |
| --- | --- | --- |
| **GO** (S3 as specified) | All of C1–C6 and C8 pass | Close PRD Open Question 1. Put W into AD-17 via the Hub/Node epics. Keep the S3. |
| **GO-WITH-CAVEATS** | All pass except bounded, explainable deviations, for example: loss 1–3 % only during BLE churn; modem sleep required, which raises RTT; BLE and TLS handshakes collide. A firmware-level mitigation exists (retry, scheduling, `PowerSaveMode::Minimum`, pausing BLE advertising while associated and provisioned). | Record each caveat and its mitigation as a Hub-epic story. Keep the S3. Re-test the mitigations. |
| **NO-GO** | Any crash or driver assert under coex that is not fixable in the spike, sustained loss ≥ 3 %, Wi-Fi that will not stay associated with BLE active, or a heap margin below 16 KiB | Consider the ESP32-C3 fallback (addendum): rerun this spike unchanged on a C3 (the firmware only needs the chip features swapped). Alternatively, consider a design change: BLE only during provisioning, with the Wi-Fi/BLE overlap limited to setup. |

**Decision:** _pending_

**Follow-ups:** update AD-17 with W. Update PRD Open Question 1 and addendum Risk (a).
Decide whether `tls-time-check` (F-3) becomes the Hub default and add cmake + ninja to
the firmware CI image. Handle WPA3 (F-6) in FR-1.
