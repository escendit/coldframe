# Bench and garden checklist: Node transport over ESP-NOW (Story 4.4)

This checklist is the acceptance test for the Reading path from a Node through a Hub to the Server
on real ESP32-S3 boards: the report buffer, sealed frames over ESP-NOW, sealed acknowledgements
(N-1), the 300 ms window and the late acknowledgement (N-2), channel following (FR-4), reach from
the farthest Lot (NFR-11), counters across power cuts (AD-17) and the Node's clock (AD-11). CI runs
every rule on the host (`tests/rs/transport`, `tests/rs/uplink/tests/relay.rs`,
`tests/rs/protocol/tests/device_v1.rs`) and the Server's side on the AppHost
(`tests/cs/server.integration`); the radios, the flash and the garden are checked here, by hand.

Parts A to F are on the bench, Parts G and H in the garden.

> [!NOTE]
> Use `dev-mode` builds on boards that must never burn an eFuse, or release builds on boards whose
> identities are already burned. A `dev-mode` Node also logs its Reading values (`dev reading …`),
> which makes Part B easier to follow. Only a release build shows what a deployed Node logs.

> [!WARNING]
> Never paste a real Wi-Fi password, a real setup code or a real Server host into this file, a
> commit or an issue. The Result table records outcomes only.

## What you need

- A Hub that has passed the [Hub uplink checklist](hub-uplink-checklist.md): provisioned, joined,
  heartbeating to the real Server. Flash it with this story's firmware first (`apps/rs/hub`, with
  cmake and ninja as its README says).
- A Node wired as in [`apps/rs/node/README.md`](../../apps/rs/node/README.md#pins), enrolled and
  assigned to a Lot by the [Node setup checklist](node-setup-checklist.md). Flash it with this
  story's firmware (`apps/rs/node`). A second Node for Part E, optional.
- The serial monitor of both, kept open and logged for the whole run
  (`espflash monitor … | tee hub.log`, `… | tee node.log`). The Node's USB serial link drops while
  it sleeps and comes back at the next wake.
- A way to read what the Server stored for the Node: the Lot detail in the app, or `psql` on the
  `coldframe` database (the Readings table, by Device ID and `reading_seq`).
- Control of the router's 2.4 GHz channel (Parts D and H) and of the Hub's power (Parts C and G).
- For Parts G and H: the Node on its battery at the farthest Lot of the garden, and a laptop or a
  UART logger there to read its log.

The Node's wake period is 15 minutes. A short press of its setup button makes it measure and send
now (`wake plan=report-now`), which keeps the bench parts short; the garden parts use real wakes.

## A. Flash and first contact

- [ ] Hub: `cargo build --release --locked` and `./check-image.sh` pass. Flash it. The log shows
      `relay listening`, then `wifi joined channel=<c> rssi=…` and `heartbeat ok`. Record `<c>`.
- [ ] Node: the range `0x14000`–`0x1FFFF` is new in this story. On a board that ran an older build,
      erase it once: `espflash erase-region 0x14000 0xC000`. Then flash.
- [ ] Node, first wake: `wake done readings=4 seq=<a>..<a+4> report_seq=<a+4> measured_at=unsynced …`
      and `transport buffered=ok hub=found channel=<c> scanned=true pending=0 sent=1 … misses=1
      clock=unchanged … backlog=1` (both lines come after the transport). `scanned=true`: a Node with no known channel scans at once.
      `misses=1` and `backlog=1` are expected: the acknowledgement arrives after the window.
- [ ] Hub, at that moment: `relay probe from=<node mac> pending=0`, `relay uplink from=<node mac>
      bytes=<n>`, then `ingest ok frames=1 downlinks=1` and `relay downlink to=<node mac> bytes=<m>`.
      Record the time between `relay uplink` and `ingest ok` (the TLS round trip).
- [ ] The Server shows the Node's four Readings with the `reading_seq` values `<a>` to `<a+3>`, and
      the Node's battery and charging status.
- [ ] No line of either log shows a key, a nonce, a signature, a body or a sealed envelope. A
      release Node shows no Reading value.

## B. Acknowledgement, late and in the window

- [ ] Short-press the Node. Its log: `transport buffered=ok hub=found channel=<c> scanned=false
      pending=1 sent=1 downlinks=1 deleted=5 fresh=true misses=0 clock=set … backlog=1`.
      The kept downlink of Part A deleted that report (four Readings and the report, `deleted=5`)
      before the new one was sent, and set the clock.
- [ ] Hub: `relay probe from=<node mac> pending=1`. The downlink is offered this once: a Hub
      whose Server is away answers the probe after with `pending=0`.
- [ ] Short-press again. The Node logs `measured_at=synced` now, and `clock=forward` or
      `clock=back`.
- [ ] The Server holds each `reading_seq` exactly once: a report is sent once and acknowledged at
      the next wake.
- [ ] Over ten presses: `backlog` is 1 after every wake (the report just sent) and never grows.
      Note whether any wake shows `downlinks=2`: the second one arrived inside the 300 ms window.
- [ ] Radio status is not delivery (N-1): nothing in the Node's log depends on a send status, and a
      wake with `sent=1` and no downlink keeps its report (`backlog` does not fall).

## C. The Hub is off

- [ ] Unplug the Hub. Short-press the Node three times, a minute apart. The first of these wakes
      has nothing to collect, so `backlog` grows from 1: each wake logs
      `hub=none scanned=false sent=0 … misses=<1, 2, 3> … backlog=<2, 3, 4>`: one probe on the
      known channel, nothing sealed.
- [ ] Press a fourth time: `hub=none scanned=true … misses=4`. The wake takes about 1.6 s of radio
      (13 channels × 120 ms). Record the time from `wake done` to `sleep ms=`.
- [ ] The next three presses do not scan again (`scanned=false`); the fourth after the scan does.
- [ ] Plug the Hub in and wait for `heartbeat ok`. Press the Node until it logs `hub=found`
      (at once when the Hub is still on the known channel). That wake logs `sent=<backlog>`, at
      most 8.
- [ ] The Server now holds the Readings of every press of this part, each `reading_seq` once, with
      the times they were measured at (`measured_at`), not the time they arrived.
- [ ] The wake after that burst logs `pending=<sent of the burst>`, `downlinks=` and `deleted=`
      to match (5 per report), `sent=1` and `backlog=1`: the Hub kept one downlink per frame and
      the Node collected them all. Nothing of the backlog is sent a second time (the Hub logs
      `ingest ok frames=1` for that wake). A backlog over 8 takes one more wake per 8 reports.
      Record the number of wakes from `hub=found` to `backlog=1`.
- [ ] While the Hub is booting and before its first `wifi joined` and clock, a Node probe logs
      `relay probe from=… unanswered: not relaying` on the Hub and `hub=none … sent=0` on the
      Node: no frame is spent on a Hub that cannot relay.

## D. A router channel change

- [ ] Note the channel `<c>` the Node reports. Change the router's 2.4 GHz channel to one at least
      five away (1 → 11, or 11 → 1). Note the time.
- [ ] Hub: `wifi link lost; re-joining`, then `wifi joined channel=<new>`, then `heartbeat ok`. No
      second `sntp clock set`.
- [ ] Node: three wakes with `hub=none … misses=1, 2, 3` on the old channel, then one with
      `hub=found channel=<new> scanned=true sent=<backlog>`. From then on `channel=<new>
      scanned=false`.
- [ ] No Reading of these wakes is missing on the Server.
- [ ] Record the time from the channel change to the first `hub=found` on the new channel (with
      real 15-minute wakes: up to four wakes, about an hour).

## E. Counters and the buffer across power cuts

- [ ] Over **at least 20 wakes** (short presses), cut the Node's power at random points: during the
      measurement, during the `transport` step, and during sleep. Restore it each time. A cut wake
      comes back as `wake cause=cold_boot` with a higher `boot id`.
- [ ] From the Node's log, the `seq` ranges and `report_seq` values never overlap an earlier one
      (gaps are allowed).
- [ ] No wake logs `transport frame counter failed`. On the Server, no frame of the Node is
      `rejected_replay` (the ingest log, or the Hub's `ingest ok … downlinks=` matching its
      `frames=` for a single Node): a repeated counter would be refused as a replay.
- [ ] Every report whose `transport buffered=ok` line was logged before a cut is on the Server by
      the end, or still in the Node's `backlog`. None is on the Server twice under different
      `reading_seq` values.
- [ ] Optional, with a second Node: both report through the same Hub; `ingest ok frames=2` appears
      when their wakes coincide, and each Node gets its own downlink (`relay downlink to=` shows
      both MAC addresses).
- [ ] Optional, buffer overflow: leave the Hub off for more than 24 h. After the 97th unsent wake
      the Node logs `transport buffer full; oldest reports dropped=1` and `backlog=96`.

## F. The clock

- [ ] A Node fresh from a power cut logs `measured_at=unsynced` on its first wake and `clock=set`
      on the first wake that applies a fresh downlink of this boot. The Server stores the unsynced
      Readings as `time_unsynced`, with the time it rebased them to.
- [ ] On the wakes after, `measured_at=synced`, and the `measured_at` the Server stores is within a
      few seconds of the wall-clock time of the wake.
- [ ] Over ten real 15-minute wakes, note each `clock=` value. `clock=back` on every wake means the
      Node's RTC runs fast by more than the 1 s a downlink may take back; record it under Notes
      (the open decision DW-64).
- [ ] Reset the Node (not a power cut). Its `boot id` rises, and its first wake is
      `measured_at=unsynced` again: a clock belongs to one boot ID.

## G. Reach from the farthest Lot, and an hour without the Hub (garden)

The Hub is where it will live, on mains power, joined to the house Wi-Fi. The Node is on its battery
at the farthest Lot, where Wi-Fi is unusable (check with a phone).

- [ ] At the farthest Lot, the Node's wakes log `hub=found … sent=<n>`, and on the next wake
      `pending=<n> … fresh=true`. Over **eight consecutive real wakes (two hours)** at most one
      is a miss (`fresh=false`), and `backlog` is back at 1 after it.
- [ ] The Hub logs `relay uplink from=<node mac>` for each of those wakes. Note the Hub's Wi-Fi
      `rssi` from its last `wifi joined` line.
- [ ] The Server's Lot detail shows a Reading for each wake, 15 minutes apart, none missing.
- [ ] If more than one wake in eight misses: move the Hub (not the Node) and repeat. Record both
      places and results. A Lot the Node cannot report from fails NFR-11.
- [ ] Unplug the Hub for **one hour** (four wakes). The Node logs `hub=none` and a growing
      `backlog`; from the fourth miss on it scans.
- [ ] Plug the Hub in. Within four wakes of `heartbeat ok` the Node logs `hub=found sent=<backlog>`
      (5 after an hour), and the wake after it `pending=5 … backlog=1`, as in Part C.
- [ ] The Server now shows the Readings of the hour without the Hub, with the times they were
      measured at. None is missing and none is doubled.
- [ ] With a power profiler on the Node's battery (as in the
      [power checklist](node-power-checklist.md)): record the duration and the charge of one normal
      wake with the transport, and of one wake with a full scan. Enter the wake duration in the
      Result table; the power budget is the power checklist's.

## H. A router channel change in the garden

- [ ] With the Node at the farthest Lot, change the router's 2.4 GHz channel as in Part D.
- [ ] Without touching the Node: within four wakes after the Hub's `wifi joined channel=<new>` the
      Node logs `hub=found channel=<new> scanned=true`.
- [ ] The Readings of the wakes in between arrive with that burst, none missing.

## I. The Hub under load

- [ ] Over the whole run the Hub never resets or panics, and `uptime … heap_min_free=` stays at or
      above 32768. The relay adds about 20 KiB of static RAM, which leaves the main stack about
      34 KiB: a reset during an `ingest` request is the sign of a stack too small. Record any.
- [ ] `relay probe from=…` appears within the same second as the Node's probe even while an
      `ingest` or `heartbeat` request is in flight (compare the two logs' timestamps).
- [ ] Heartbeats keep their 30–60 s rhythm through every part: no gap longer than about 80 s
      (one interval plus one request), also while frames are relayed.
- [ ] No `ingest rejected status=401` over the run: the Hub's request timestamps strictly increase
      over heartbeats and ingests.
- [ ] With the Server stopped: `ingest failed kind=…; batch dropped` or `ingest rejected
      status=503`, no `relay downlink`, heartbeats still attempted. The Node keeps its reports
      (Part C's behaviour, with `hub=found`).

## Result

| Date | Node board | Hub board | Build | Part | Pass / fail | Node Device ID | Uplink → `ingest ok` (ms) | Wakes from `hub=found` to `backlog=1` (Part C) | Channel change → `hub=found` | Wake duration, normal / full scan (ms) | Hub `heap_min_free` | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| | | | | | | | | | | | | |
