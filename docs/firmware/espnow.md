# ESP-NOW transport and channel following

A Node never joins your Wi-Fi. It talks to the Hub over ESP-NOW: short, connectionless radio
datagrams between two ESP32-S3 chips. The Hub is on the Wi-Fi network and relays what it hears to
the Server. This page shows how a Node finds the Hub, sends its reports and learns that they
arrived, and how it follows the Hub when the router changes channel. Read
[sealing.md](sealing.md) first for what is inside a frame; [deep-sleep.md](deep-sleep.md) comes
next and covers the rest of the wake. The index is [README.md](README.md).

The governing rules are AD-9 (ingestion and acknowledgement), AD-11 (time) and AD-17 (frame
sequence) in the [architecture spine](../../_bmad-output/planning-artifacts/architecture/architecture-coldframe-2026-09-26/ARCHITECTURE-SPINE.md),
and N-1 and N-2 in [device-hardware.md](../../_bmad-output/specs/spec-coldframe/device-hardware.md).

## How it works

### The radio as a datagram port

The transport logic does not know about ESP-NOW. It uses a small trait, `DatagramRadio`, in
`packages/rs/hal/src/datagram.rs`: set a channel, send one payload (at most 250 bytes) to a MAC
address or to broadcast, and receive with a timeout. The Node's adapter in
`apps/rs/node/src/board/espnow.rs` (`BoardEspNow`) takes ESP-NOW from a Wi-Fi controller that
never associates. The Node creates it after the measurement and drops it before deep sleep, so
the radio is only on during the transport step.

The key lesson of this layer is N-1: **the send status is not delivery**. Under Wi-Fi and BLE
coexistence the radio often reports a failed send for a datagram that did arrive. So the
transport ignores the result of every `send`. Only a sealed downlink from the Server counts as
proof of delivery.

### Four messages

`packages/rs/protocol/src/radio.rs` (`Message`) defines the whole radio protocol. The first byte
is the kind:

- **Probe** (Node to broadcast): an 8-byte nonce. It asks "is a Hub on this channel?".
- **Probe reply** (Hub to Node): the nonce echoed, plus `pending`, the number of downlinks the
  Hub kept for this Node (0 to 8).
- **Uplink** (Node to Hub): a sealed frame.
- **Downlink** (Hub to Node): a sealed acknowledgement from the Server.

Probes and replies are not authenticated. A forged reply can waste one burst, but it cannot
delete a Reading or reset the miss count: only a downlink that opens under the `ack/v1` key
can.

### One wake, step by step

`packages/rs/transport/src/transport.rs` (`run`) does the transport for one wake. Its module
documentation lists the steps; in short:

1. **Buffer** the wake report in flash.
2. **Find a Hub** (`find_hub`). Probe the channel a Hub last answered on and wait up to 120 ms
   for a reply with the same nonce (`probe`).
3. **Collect kept downlinks.** If the reply says `pending > 0`, listen up to 120 ms for them.
   These are late acknowledgements of the last wake's frames.
4. **Send a burst.** Reserve one frame counter per report in flash first, then seal and send up
   to 8 buffered reports, oldest first, unicast to the Hub's MAC address.
5. **Listen** for up to 300 ms after the last send (`listen`), or until every frame of the
   burst is acknowledged.
6. **Remember** the outcome in the link state.

A downlink deletes exactly the Readings it lists. It is *fresh* when it acknowledges a frame of
this wake's burst or the previous one that no downlink has acknowledged before
(`Burst::acknowledge` in `packages/rs/transport/src/link.rs`). Only a fresh downlink resets the
miss count and sets the clock (AD-11).

A frame that is never acknowledged is not resent in the same wake. Its report stays in the
buffer and goes out again in the next wake's burst, under a new frame counter.

### Channel following

ESP-NOW only works when both chips are on the same Wi-Fi channel. The Node does not choose the
channel; **the Hub's access point does**. The Hub is a Wi-Fi station, so its radio sits on the
router's channel. Its ESP-NOW peers are added with `channel: None`
(`apps/rs/hub/src/board/espnow.rs`, `send`), which means "use the station's current channel". If
the router moves to another channel, the Hub moves with it, and the Node has to find it again.

The Node does this with the link state, a small record in flash (`LinkState`, magic `CFLK`, in
`packages/rs/transport/src/link.rs`). Every wake is a reboot, so the channel, the miss count and
the scan hold-off must live in flash. `find_hub` uses them like this:

- **Known channel first.** If a channel is stored, it is always probed first, every wake.
- **A miss** is a wake without a fresh downlink. The count goes up by one on each miss and back
  to 0 on the first fresh downlink.
- **Scan.** With no stored channel, or after 3 consecutive misses (`RESCAN_AFTER_MISSES`), the
  Node probes the known channel and then every other channel from 1 to 13. The first channel
  where a Hub answers becomes the stored channel.
- **Back-off.** A full scan that finds no Hub sets `scan_holdoff` to 4 (`SCAN_BACKOFF_WAKES`).
  The next three wakes that want to scan only count it down and probe the known channel. The
  fourth wake scans again. This stops a Node far from the Hub from scanning on every wake.

> [!NOTE]
> The Node re-evaluates its channel **only after the link drops**, that is, after 3 misses in a
> row. There is no re-scan when the signal gets weaker (no RSSI check). A Node keeps using a
> channel for as long as a Hub answers there.

A lost or corrupt link state is harmless. It reads as the default state, which costs one scan
and an unsynced clock, never a Reading.

### The Hub relay

The Hub side is a radio task, `relay_task` in `apps/rs/hub/src/board/espnow.rs`, around a plain
state machine, `Relay` in `packages/rs/uplink/src/relay.rs`. The Hub reads only the kind byte:
it never opens an envelope.

- **Probes** are answered at once from the relay state, without asking the Server. So a reply
  never waits for an HTTPS request in flight. A probe is answered only while the Hub can relay
  (`Relay::set_relaying`): the station is associated and the clock is set. Otherwise a Node
  would spend its frame counters on frames the Hub would drop.
- **Uplinks** go into a queue of 8. The uplink task posts them to the Server as one batch. An
  envelope already queued is not queued twice.
- **Downlinks** from the Server are sent once, straight away. With a fresh TLS session per
  request the Node is usually asleep by then. So the Hub also keeps them in RAM, up to 8 per
  Node out of a pool of 24, and sends them once more right after its reply to that Node's next
  probe (`Relay::take_kept`). That is the `pending` count in the reply.

A Hub peer entry lives for one send only, so ESP-NOW's limit of 20 peers never fills up.

> [!NOTE]
> AD-9 says the Hub may keep "the latest" downlink per Node. The code keeps up to 8 per Node
> (one burst), so a whole burst can be acknowledged at the next wake.

## Key constants and timings

| Constant | Value | Where |
| --- | --- | --- |
| `PROBE_WAIT_MS` | 120 ms per channel | `packages/rs/transport/src/transport.rs` |
| `PENDING_WAIT_MS` | 120 ms for kept downlinks | `packages/rs/transport/src/transport.rs` |
| `ACK_WINDOW_MS` | 300 ms after the last send (N-2) | `packages/rs/transport/src/transport.rs` |
| `BURST_MAX` | 8 frames per wake | `packages/rs/transport/src/transport.rs` |
| `RESCAN_AFTER_MISSES` | 3 consecutive misses | `packages/rs/transport/src/transport.rs` |
| `SCAN_BACKOFF_WAKES` | 4 wakes after a failed scan | `packages/rs/transport/src/transport.rs` |
| `FIRST_CHANNEL`, `LAST_CHANNEL` | channels 1 to 13 | `packages/rs/transport/src/transport.rs` |
| `MAX_PAYLOAD` | 250 bytes per message | `packages/rs/protocol/src/radio.rs` |
| `PENDING_MAX` | 8 kept downlinks per Node | `packages/rs/protocol/src/radio.rs` |
| `SEND_TIMEOUT` (Node) | 200 ms per send | `apps/rs/node/src/board/espnow.rs` |
| `SEND_TIMEOUT` (Hub) | 60 ms per send | `apps/rs/hub/src/board/espnow.rs` |
| `UPLINK_QUEUE`, `DOWNLINK_POOL` | 8 uplinks, 24 kept downlinks | `packages/rs/uplink/src/relay.rs` |

The Hub's 60 ms send timeout is deliberately shorter than the Node's 120 ms probe wait: a send to
a Node that is already asleep must not delay the reply to the next probe. The worst-case wake
(a full scan, 8 frames and the window) is worked out in the module documentation of
`apps/rs/node/src/main.rs`: about 2.1 s, and 6.2 s if every send times out.

## Where to read the code

- `packages/rs/transport/src/transport.rs`: the module documentation, then `run` for the order
  of one wake.
- `packages/rs/transport/src/transport.rs` (`find_hub`, `probe`): the known channel, the scan
  and the back-off.
- `packages/rs/transport/src/transport.rs` (`listen`, `Session::apply`): the acknowledgement
  window and what makes a downlink fresh.
- `packages/rs/transport/src/link.rs` (`LinkState`, `Burst`): what the Node remembers between
  wakes, and the flash record layout.
- `packages/rs/protocol/src/radio.rs` (`Message`): the four radio messages and their encoding.
- `packages/rs/hal/src/datagram.rs` (`DatagramRadio`): the radio port, and why the send status
  is ignored.
- `apps/rs/node/src/board/espnow.rs` (`BoardEspNow`): ESP-NOW without a Wi-Fi association.
- `apps/rs/hub/src/board/espnow.rs` (`relay_task`, `send`): the Hub's radio task and the
  `channel: None` peers.
- `packages/rs/uplink/src/relay.rs` (`Relay`): the uplink queue, the kept downlinks and when
  probes are answered.

To see it on real boards, follow the
[Node transport checklist](../bench/node-transport-checklist.md). The radio findings behind N-1
and the `channel: None` peer scheme (F-7) are in
[hub-radio-coexistence.md](../spikes/hub-radio-coexistence.md) and
[radio-coexistence.md](radio-coexistence.md). The build-side READMEs are
[apps/rs/node/README.md](../../apps/rs/node/README.md) and
[apps/rs/hub/README.md](../../apps/rs/hub/README.md).
