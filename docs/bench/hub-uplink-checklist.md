# Bench checklist: Hub joins Wi-Fi and heartbeats (Story 3.5)

This checklist is the acceptance test for the Hub's uplink on a real ESP32-S3 against the real
Server: the Server check at the end of BLE setup, heartbeats every 30–60 s (FR-13, AD-12), the clock
(SNTP, then `serverTime`, AD-11), TLS with public roots and checked validity dates (AD-13, H-2),
and re-joining the strongest access point after an outage (H-1). CI runs the logic on the host
(`tests/rs/uplink`, `tests/rs/setup`) and the Server's side on the AppHost
(`tests/cs/server.integration`); the radio, the TLS stack and the real network are checked here, by
hand.

> [!NOTE]
> Use a dev-mode build on a board that must never burn an eFuse (board **D** of the
> [identity checklist](hub-identity-checklist.md)), or a release build on a board whose identity is
> already burned.

> [!WARNING]
> Never paste a real Wi-Fi password, a real setup code or a real Server host into this file, a
> commit or an issue. The Result table records outcomes only.

## What you need

- Everything of the [BLE setup checklist](hub-setup-checklist.md): the board, the firmware, the
  desktop client `cfsetup`, a 2.4 GHz WPA2 network.
- The build prerequisites of [`apps/rs/hub/README.md`](../../apps/rs/hub/README.md), including
  **cmake and ninja** (the mbedTLS rebuild for certificate-date checks).
- The Server, reachable from the Hub's network under a DNS name with a publicly trusted
  certificate (AD-13, split DNS to the LAN ingress), and a Site on it where you are an Administrator.
- A way to read the Device's journal stream `device/<Device ID>` (the Aspire dashboard, or `psql` on
  the `coldframe` database: `SELECT version, type_alias, recorded_at FROM journal_events WHERE
  stream_id = 'device/<Device ID>' ORDER BY version;`). The Devices screen arrives with Story 3.7.
- Control of the access point's power, for Part D.

Run the Hub commands from `apps/rs/hub` after `source ~/export-esp.sh`, and keep the serial monitor
open for the whole run (`espflash monitor … | tee hub.log`).

## A. Setup with the Server

- [ ] `./check-image.sh` passes.
- [ ] Erase `cf_setup` (`espflash erase-region 0xC000 0x2000`), flash and monitor.
- [ ] Run [BLE setup Part D](hub-setup-checklist.md#d-full-setup) with
      `--server https://<server host>[:port]`: post the printed enrolment body, press Enter.
- [ ] The client prints `wifi result CONNECTED` and exits 0.
- [ ] The Hub logs `setup provisioned`, then `heap free=… used=… min_free=…`. Record `min_free`.
      Pass: at least 32768 (32 KiB) — it covers BLE, Wi-Fi and the check's TLS connection together.
- [ ] Then `uplink server=<host> port=<port>`.
- [ ] The Device's stream holds `device.enrolled` followed by one `device.seen` (the check's
      heartbeat).

## B. Heartbeats

- [ ] Within 60 s of setup, and then every 30–60 s, the Hub logs
      `heartbeat ok server_time_s=<Unix seconds> next_ms=<30000..60000>`. Note three consecutive
      `next_ms` values: they differ (TRNG jitter).
- [ ] Each one adds a `device.seen` to the Device's stream; `seenAt` advances with each.
- [ ] Over 10 minutes: no `heartbeat failed` or `heartbeat rejected` line.
- [ ] `uptime s=… heap_min_free=…` lines appear once a minute; `heap_min_free` stays at or above
      32768.
- [ ] No line contains the Wi-Fi password, a nonce, a signature, a request or response body, or the
      setup code.

## C. Nothing the Hub sends is refused

The Server's refusals (bad signature, skew, replayed nonce, old timestamp, unknown Device, bad
headers or body) are covered by the integration suite (`HeartbeatTests` in
`tests/cs/server.integration`). On the bench, check the other side:

- [ ] Over the whole run, `heartbeat rejected status=401` never appears: the Hub's timestamps
      strictly increase and its nonces never repeat, even after the clock follows `serverTime`.

## D. Re-join after an access-point power cycle

- [ ] Power the access point off. Within one heartbeat interval the Hub logs `wifi link lost;
      re-joining` (or `heartbeat failed kind=…` first, then `wifi link lost`).
- [ ] While the access point is off: `wifi join failed kind=not-heard retry_ms=…`, with `retry_ms`
      1000, 2000, 4000 … up to 60000 and then 60000 again. Record the sequence.
- [ ] Power it back on. Within about 60 s the Hub logs `wifi joined channel=… rssi=…`, then
      `heartbeat ok` at once; no second `sntp clock set` (SNTP runs once per boot).
- [ ] The next outage starts again at `retry_ms=1000`.
- [ ] Optional, on a mesh with several access points for the SSID: the `channel` and `rssi` of
      `wifi joined` are those of the strongest one heard, not the first.

## E. Re-join after a Hub reboot

- [ ] Reset the Hub. The boot logs `setup provisioned earlier; not advertising`, `uplink server=…`,
      `wifi joined channel=… rssi=…`, `sntp clock set unix_s=…` (within a minute of the real time),
      then `heartbeat ok`.
- [ ] `cfsetup scan` does not list the Hub.
- [ ] The Device's stream gains `device.seen` again; no second `device.enrolled`.

## F. No Server

Run on a board set up again (erase `cf_setup` first).

- [ ] Run BLE setup with `--server https://unreachable.example.org` (a name that does not resolve),
      after posting the enrolment body to the real Server.
- [ ] Within 40 s of the join the client prints `wifi result NO_SERVER` and exits non-zero.
- [ ] The Hub leaves the network (no further `wifi joined` or `heartbeat` lines) and still
      advertises; a reset still prints `setup code=`: nothing was stored.
- [ ] Repeat with the real Server but without posting the enrolment body (`--enrolled yes` on a fresh
      Hub): `NO_SERVER` again, because the Server answers 401 to a Hub it does not know.
- [ ] Expired certificate (H-2): set a fresh Hub up with `--server https://expired.badssl.com` (its
      certificate chains to a public root but has expired). The Hub logs `net tls handshake failed:
      … verify_flags=0x…` and the client prints `wifi result NO_SERVER`. The handshake line is the
      evidence (that host would refuse the heartbeat anyway, so `NO_SERVER` alone proves nothing).
      Needs internet access from the Hub's network. Record the outcome in the Result table.
- [ ] Optional, clock: block NTP (UDP 123) at the router and set the Hub up. The setup ends in
      `NO_SERVER` (no clock, no TLS). On a provisioned Hub, a boot logs `sntp failed kind=…
      retry_ms=…` with the same backoff and never a `heartbeat` line until NTP is unblocked.

## Result

| Date | Board | Build | Part | Pass / fail | Device ID | Heap min free (bytes) | Backoff sequence (Part D) | Expired certificate refused (Part F) | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| | | | | | | | | | |
