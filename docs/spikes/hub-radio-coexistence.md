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

### Phase 0 smoke test (2026-09-27, about 23 min)

**Setup:**
- Two ESP32-S3 boards (rev v0.2, 8 MB flash, eFuses untouched): Hub `Hub board` and Node `Node board`, about 1 m apart.
- Firmware `655d413`, default Hub build (BLE on).
- Router on channel 6, WPA2-Personal. The Hub's Wi-Fi signal was weak (RSSI −80 to −84 dBm).
- `SPIKE_URL` was set, but the endpoint returned HTTP 405 to POST. That still measures TLS round trips.
- The phone (nRF Connect) was connected for most of the run.

| Item | Result |
| --- | --- |
| Resets, panics, Guru meditations | **0** on both boards |
| Wi-Fi disconnects | **0** (one association, whole run) |
| TLS handshakes / requests failed | 1 handshake (664 ms) / **0** of 1,217 relay requests failed |
| Heartbeats | **45/45 OK** |
| BLE | 3 connects, 2 remote disconnects, 4 writes; runtime ack-mode switch over BLE worked (`I` → immediate, `S` → server) |
| Heap | 208 KB total; minimum free **54.2 KB** (above the 32 KiB margin) |
| Node → Hub delivery | The Hub received essentially every data frame the Node sent (1,359 received vs 1,355 sent; the counters are not perfectly aligned) |
| Node send status `mac_fail` | 118 of 1,355 (8.7 %), spread evenly over the run, before and after BLE connected. **Every sampled `mac_fail` frame had arrived at the Hub and was acknowledged**; the ack came in after the Node had given up (`late_or_dup` 115). |
| Hub ack send failures (radio level) | 99 of 1,359 |
| Frames with no ack at all | about 4 of 1,355 (**≈0.3 %**) |
| Node re-scans | 1 (after 3 consecutive failures), found the Hub again on channel 6 in 613 ms |

**Round trip at the Node (Node → Hub → [Server] → Hub → Node):**

| Mode | n | p50 | p95 | max |
| --- | --- | --- | --- | --- |
| Server (HTTPS relay per frame) | 1,076 | 52 ms | 111 ms | 277 ms |
| Immediate (no Server round trip) | 160 | 8 ms | 48 ms | 81 ms |

Warm HTTPS request latency at the Hub: p50 45 ms, p95 99 ms, max 270 ms. Cold (with handshake): 756 ms.

**Phase 0 findings:**
1. **Coexistence works functionally.** Wi-Fi STA, ESP-NOW, BLE (connect, write, reconnect), and TLS ran together for 23 minutes with no reset, no Wi-Fi drop, and no TLS failure.
2. **The radio-level send status is not trustworthy under coexistence.** About 9 % of Node sends report failure even though the frame arrived. Firmware must treat only the application-level (sealed) acknowledgement as the truth, and keep waiting for it after a failed send status. It should resend only when no ack arrives within the ack window. That matches AD-9 and AD-17, but it must be explicit in the Node firmware.
3. **True loss before retry is about 0.3 %.** The AD-17 design (buffer, resend, dedupe by `reading_seq`) absorbs it.
4. **AD-17 ack window (provisional):** W = max(2 × p95, max) of server-mode RTT = max(222, 277) = **≈ 300 ms**. This is measured against the configured endpoint, not the real LAN Server. Phase B should confirm it with a LAN endpoint, and it should be re-measured with the real Server.
5. **Still open for Phase A/B:**
   - Whether the 9 % radio-level failures come from BLE time-slicing. Answer with the `SPIKE_BLE=0` baseline.
   - Channel-change recovery.
   - A 2-hour stability run.
   - A better Hub placement (−84 dBm is marginal).

### Phase B main run (2026-09-27, 13:51 to 15:53, 2 h 2 min)

**Setup:**
- Default Hub build: BLE on, server mode, all-channel scan (joined `AP-near`, channel 11, −39 to −46 dBm).
- **Forced roam at uptime 5,404 s** to the channel-6 AP `AP-far` (−90 dBm), using `SPIKE_ROAM_BSSID` and `SPIKE_ROAM_AT_S`.
  - This replaces the router channel change, because the AP settings are fixed. From the Node's side it is the same event: the Hub leaves its channel and reappears on another one, with no Hub reboot.
- The phone (nRF Connect) wrote over BLE and ran 12 connect/disconnect cycles. The `I` write for immediate mode did not arrive, so immediate mode is covered by Phase A only.

| Criterion | Result | Pass |
| --- | --- | --- |
| Resets, panics, Guru meditations | 0 on both boards | ✅ |
| ESP-NOW loss (no ack at all) | **3 of 7,304 (0.04 %)**, all three during the roam; **0** in the 90 min before and the 30 min after | ✅ (< 1 %) |
| Wi-Fi | 1 disconnect, which was the forced roam; re-associated on channel 6 in about 1 s | ✅ |
| Heartbeats | **243/243** | ✅ (≥ 99 %) |
| TLS | 7,538 OK, 1 failed (`NoNetwork` while roaming); re-handshake 627 ms | ✅ |
| BLE | 12 connects, 12 disconnects, 12 writes, 0 advertising errors | ✅ |
| Heap | minimum free **63.6 KB**, flat for 2 h (71.9 KB free at the start and at the end), no leak | ✅ (≥ 32 KiB) |
| Channel-following recovery | Node: the roam frame timed out, then 2 radio-level failures, then "3 consecutive failures on channel 11: re-scanning", then **found the Hub on channel 6 in 613 ms**, and every later frame was acked. End to end about 13 s, dominated by the spike's 10 s per-frame ack timeout. With a 300 ms window it would be about 2 s. | ✅ (< 60 s) |
| RTT, server mode, n = 7,301 | p50 **38 ms**, p95 **81 ms**, max 788 ms (the single frame right after the roam); **1 of 7,301 over 300 ms** | — |

**Phase B findings:**
1. **Coexistence is stable over 2 hours:** Wi-Fi, ESP-NOW, BLE (with churn) and TLS together, with no crashes, no heap drift, and no loss outside the roam.
2. **Channel following works.** The Node's re-scan on 3 consecutive failures finds the Hub on its new channel in under a second. The production Node should re-scan after the ack window expires (AD-17), not after a long timeout.
3. **After the roam, the Hub ran 30 minutes at −90 dBm with 0 radio-level failures.** A weak link alone therefore does not fully explain Phase 0's 8.7 %. That run combined a weak link with an active BLE connection. The cause of Phase 0's radio-level failures is not isolated beyond "weak link, possibly with BLE activity", but in every run they caused ≤ 0.3 % real loss, which retry covers.
4. **The 300 ms ack window covers 99.99 % of acknowledgements** in 2 hours (1 of 7,301 slower, right after a roam).

### Phase A and BLE control (2026-09-27, 3 × 20 min)

The Hub board sat about 1 m from an access point. The router SSID is served by at least two BSSIDs:
- `AP-near`: channel 11, −39 to −46 dBm. This is the AP 1 m away.
- `AP-far`: channel 6, −84 to −90 dBm. A farther AP or mesh node.

With esp-radio's default `ScanMethod::Fast`, the Hub joins the **first** matching BSSID it finds, whatever its signal. That was the far AP in Phase 0 and in A2. Firmware `SPIKE_WIFI_SCAN` now defaults to `ScanMethod::AllChannels`, which joins the strongest AP; the control run used it.

| Run | BLE | AP (channel, RSSI) | Ack mode | Frames | Radio-level send failures at Node | No ack at all | RTT p50 / p95 / max | Min free heap |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Phase 0 | on, phone connected | ch 6, −84 dBm | server | 1,355 | 118 (8.7 %) | ≈4 | 52 / 111 / 277 ms | 54.2 KB |
| A1 | off | ch 11, −43 dBm | server | 1,186 | **0** | **0** | 15 / 68 / 90 ms | 94.3 KB |
| A2 | off | ch 6, −90 dBm | immediate | 1,185 | 5 (0.4 %) | 3 | 3 / 13 / 58 ms | 95.6 KB |
| Control | on, advertising only | ch 11, −39 dBm | server | 1,191 | **0** | **0** | 31 / 76 / 252 ms | 63.7 KB |

All four runs had no resets or panics, no Wi-Fi disconnects, no TLS failures, and 100 % heartbeat success.

**Findings:**
1. **BLE does not cause ESP-NOW loss.** With a good AP, BLE on and BLE off both lost 0 of about 1,190 frames. The Phase 0 radio-level failures came from the Hub being associated to a **distant AP at −84 dBm**.
2. **BLE costs latency and memory, not reliability.**
   - With BLE advertising, server-mode RTT roughly doubled at p50 (15 → 31 ms), p95 rose from 68 to 76 ms, and the max rose from 90 to 252 ms, from radio time-slicing.
   - The BLE stack costs about 30 KB of heap (94 KB → 64 KB minimum free).
3. **Hub requirement (Hub epic):**
   - Scan all channels and join the **strongest** BSSID for the SSID (`ScanMethod::AllChannels`), never the first found.
   - On mesh networks, re-evaluate when RSSI degrades.
   - A Hub on a weak AP is what raises Node-visible failures.
4. **AD-17 ack window:**
   - With BLE on and a good AP, W = max(2 × 76, 252) ≈ 250 ms. The Phase 0 estimate of about 300 ms stays the conservative value.
   - Recommendation: **300 ms**, confirmed against the real LAN Server later.
   - The Hub-side `ack_max` outliers of about 4 s in A1 and the control run are Hub-internal and never reached the Node (Node max ≤ 252 ms). They are most likely the cold TLS handshake on the first relayed frame.
5. **Verdict so far: GO for the ESP32-S3 as specified.** Open caveats:
   - the 2-hour Phase B stability run
   - recovery after a router channel change
   - WPA3-only networks (not supported by esp-radio beta.1)
   - certificate-date checking needs the mbedTLS C build (cmake and ninja)

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

| Outcome | When | Consequence |
| --- | --- | --- |
| **GO** (S3 as specified) | All of C1–C6 and C8 pass | Close PRD Open Question 1. Put W into AD-17 via the Hub/Node epics. Keep the S3. |
| **GO-WITH-CAVEATS** | All pass except bounded, explainable deviations, for example: loss 1–3 % only during BLE churn; modem sleep required, which raises RTT; BLE and TLS handshakes collide. A firmware-level mitigation exists (retry, scheduling, `PowerSaveMode::Minimum`, pausing BLE advertising while associated and provisioned). | Record each caveat and its mitigation as a Hub-epic story. Keep the S3. Re-test the mitigations. |
| **NO-GO** | Any crash or driver assert under coex that is not fixable in the spike, sustained loss ≥ 3 %, Wi-Fi that will not stay associated with BLE active, or a heap margin below 16 KiB | Consider the ESP32-C3 fallback (addendum): rerun this spike unchanged on a C3 (the firmware only needs the chip features swapped). Alternatively, consider a design change: BLE only during provisioning, with the Wi-Fi/BLE overlap limited to setup. |

**Decision (2026-09-27): GO. Keep the ESP32-S3 as specified.** PRD Open Question 1 is closed.

Across Phase 0, Phase A, the BLE control and Phase B (about 3 h 45 min of runs on two boards), every criterion passed: no crash, loss well under 1 %, stable heap, heartbeats at 100 %, and channel following recovered. Carry these into the Hub and Node epics as requirements, not caveats:

- **H-1:** the Hub scans all channels and joins the strongest BSSID (`ScanMethod::AllChannels`), and re-evaluates on RSSI degradation (mesh networks).
- **H-2:** the Hub's TLS enables certificate-date checks (`tls-time-check`), and the firmware CI image gets cmake and ninja.
- **H-3:** WPA3-only networks are not supported by esp-radio 1.0.0-beta.1. Document it for FR-1, and track upstream.
- **N-1:** the Node treats only the sealed application acknowledgement as delivery. It ignores radio-level send status, and resends and re-scans on ack-window expiry.
- **N-2:** AD-17 acknowledgement window **W = 300 ms**, to be re-measured against the real LAN Server.

**Follow-ups:** update AD-17 with W. Update PRD Open Question 1 and addendum Risk (a).
Decide whether `tls-time-check` (F-3) becomes the Hub default and add cmake + ninja to
the firmware CI image. Handle WPA3 (F-6) in FR-1.
