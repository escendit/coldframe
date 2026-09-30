# Bench checklist: Node setup mode, enrolment and Lot assignment (Story 4.2)

This checklist is the acceptance test for the Node's setup button, its BLE setup window (AD-25) and
its enrolment into a Lot on real ESP32-S3 boards. CI runs every session and window rule and every
press threshold on the host (`tests/rs/setup/tests/node.rs`, `tests/rs/sensing/tests/button.rs`),
and the Server's `Lot.Claim` rules in `tests/cs`; what happens over the air, on the chip and at the
Server is checked here, by hand, with the desktop client `coldframe-setup-client`. Part F also
closes the device-hardware open item: BLE (setup mode) and ESP-NOW coexistence on one Node.

> [!NOTE]
> Use `dev-mode` builds: only they carry the ESP-NOW coexistence probe that Part F needs, and they
> never burn an eFuse. A release build behaves the same in Parts B to E and G, but burns an eFuse
> key block on its first boot and has no probe.

> [!WARNING]
> Never paste a real setup code into this file, a commit or an issue. The Result table records
> outcomes only.

## What you need

- Two ESP32-S3 boards wired as in [`apps/rs/node/README.md`](../../apps/rs/node/README.md#pins),
  each with a momentary switch from GPIO7 to ground (the setup button). Sensors are optional here:
  a missing BME680 only costs its Readings. Label them **board 1** and **board 2**.
- A Linux machine with Bluetooth LE (BlueZ) for the client, as for the
  [Hub setup checklist](hub-setup-checklist.md).
- A Server you can reach, a Site on it where you are an Administrator, and two Lots on that Site
  (**Lot A**, **Lot B**) with no Node. The Server's enrolment key: `publicKey` from
  `GET /enrolment-key` (base64url, 43 characters).
- A stopwatch.

Build the client once, from the repository root:

```sh
cargo build -p coldframe-setup-client
alias cfsetup="$PWD/target/debug/coldframe-setup-client"
```

Run the Node commands from `apps/rs/node` after `source ~/export-esp.sh`. Record the port, the board
and the date for each run.

## A. Flash and record the codes

- [ ] On each board, erase the setup partition so the run starts without a code:
      `espflash erase-region 0x12000 0x2000`.
- [ ] Flash and monitor each board: `cargo run --features dev-mode`. The boot shows
      `wake cause=cold_boot`, the identity line and `wake plan=measure`, then `wake done …`, and the
      board sleeps.
- [ ] Hold the setup button of each board for 3 s. The log shows `wake cause=button`,
      `wake plan=setup`, then exactly one line `setup code=XXXXXXXX` (8 characters from
      `0123456789ABCDEFGHJKMNPQRSTVWXYZ`). Write each code on a sticker for its board, not here.
- [ ] Let the window run out (Part C), then long-press again: the same code is printed. No other
      line contains the code.

## B. No BLE outside setup mode; report now

- [ ] With both boards asleep or measuring on their timer, `cfsetup scan --seconds 10` lists no
      `Coldframe Node`.
- [ ] Press the setup button of board 1 for about 1 s. The log shows `wake cause=button`,
      `wake plan=report-now`, then `wake done readings=…`, and no `ble` or `setup` line. The last
      line before the sleep is `sleep ms=900000 button_armed=true`: a press restarts the schedule
      with a full period. (`wake done … sleep_ms=` is the rest of the period the measurement
      computed, not the sleep taken; read `sleep ms=`.) A `cfsetup scan` during and right after it
      lists no `Coldframe Node`.
- [ ] Tap the button as briefly as you can. The firmware times the press first thing after the
      bootloader, before any flash or identity work, so a tap that is over by then is a bounce: the
      log shows `wake plan=sleep-again`, no `wake done` line, and `sleep ms=900000`. (A tap that
      lasts past the bootloader and the 50 ms debounce is a `report-now`, which is also correct.)
      Note the shortest press that still gives `report-now` in the Result table.

## C. The window closes

- [ ] Hold the setup button of board 1 for 3 s. The log shows `wake plan=setup` before you let go,
      then `setup mode window_ms=180000` and `ble advertising setup service as Coldframe Node XXXX`
      (the first four Device ID digits, uppercase). Start the stopwatch when `setup mode
      window_ms=` is logged: the window runs from the start of advertising, not from the press.
- [ ] `cfsetup scan` lists `Coldframe Node XXXX` with an RSSI.
- [ ] Do not connect. About 180 s after `setup mode window_ms=` the log shows
      `setup end=window-closed`, then `wake done …` and `sleep ms=900000 button_armed=true`.
- [ ] Right after, `cfsetup scan --seconds 10` lists no `Coldframe Node`: advertising stopped.
- [ ] The RTC watchdog did not reset the chip during the window (no `reset=other` wake in between).

## D. A wrong code, then the right one

- [ ] Long-press board 1. Run `cfsetup node-setup --code <the code with its last character changed>
      --site <site> --lot <Lot A> --enrolment-key <key>`.
- [ ] The client prints `wrong setup code` and exits non-zero (`echo $?` is not 0).
- [ ] A `--lot` that is not a UUID (for example `--lot tomatoes`) fails at once with
      `--lot is not a UUID` and the usage, exit code 2, before any BLE traffic.
- [ ] The Node logs `ble connected`, then `ble disconnected` at once, then advertises again
      (`ble advertising setup service …`); the window goes on.
- [ ] Optional: connect with a generic BLE app (nRF Connect) and write nothing. After 60 s the Node
      drops the connection (`ble disconnected`) and advertises again; the window goes on.
- [ ] Within the same window, run the command again with the right code (Part E). It succeeds.

## E. Enrolment into a Lot

- [ ] Long-press board 1 (or continue from Part D). Run `cfsetup node-setup --code <code>
      --site <site> --lot <Lot A> --enrolment-key <key>`.
- [ ] The client prints, in order:
  - `node <address> (Coldframe Node XXXX)` and `session open, mtu=…`;
  - `identity device_id=<the Device ID of the boot log> kind=2 firmware=<version>`;
  - `node binding sent site=<site> lot=<Lot A>`, `enrolment sealed device_id=<same ID>`;
  - `POST /sites/<site>/devices` and a JSON body with `deviceId`, `kind: "node"`, `lotId`,
    `enc` (43 characters) and `ciphertext` (64 characters).
- [ ] The client disconnects. The Node logs `setup end=enrolled`, then `wake done …` and
      `sleep ms=900000 button_armed=true`, and `cfsetup scan` no longer lists it.
- [ ] `POST` the printed body to `/sites/<site>/devices` as an Administrator of the Site, with a
      fresh Idempotency-Key for board 1 (save the body as `board1.json`):

      ```sh
      KEY1=$(uuidgen)
      curl -si -X POST "https://<server>/sites/<site>/devices" \
        -H "Authorization: Bearer $TOKEN" \
        -H "Idempotency-Key: $KEY1" \
        -H "Content-Type: application/json" \
        --data @board1.json
      ```

      `$TOKEN` is an Administrator's access token for the Server. The Server answers 201 with
      `{id, kind: "node", siteId, lotId: <Lot A>}`.
- [ ] Retry: `POST` the same `board1.json` again with the **same** `$KEY1`. The Server answers 201
      with the same body as the first response (`id`, `kind: "node"`, `siteId`, `lotId: <Lot A>`).
- [ ] Lot A is held: `DELETE /sites/<site>/lots/<Lot A>` (same `Authorization` header) answers 409
      `urn:coldframe:problem:lot-claimed`, and Lot A is still listed.
- [ ] Long-press board 2 and run `cfsetup node-setup` with board 2's code for the **same** Lot A.
      Save its body as `board2.json` and `POST` it with a **new** key (`KEY2=$(uuidgen)`): it is
      another Device, and reusing `$KEY1` for a different body answers 422
      `idempotency-key-reused`. The Server answers 409 `urn:coldframe:problem:lot-claimed`
      ("This Lot already has a Node.").
- [ ] Board 2 needs no second BLE session: edit `lotId` in `board2.json` to Lot B and `POST` again
      with the **same** `$KEY2`. The key covers board 2's registration on the Site only, and the
      refused claim persisted nothing for the Device, so the Server answers 201 with
      `lotId: <Lot B>`.

## F. BLE and ESP-NOW coexistence (dev-mode)

Both boards in setup mode at once, while board 1 completes a BLE session.

- [ ] Board 1 can repeat Part E with Lot A: the same Node on the same Lot answers 201 with
      `lotId: <Lot A>` again (move and unassign are Story 4.9).
- [ ] Long-press board 2, then board 1, within a few seconds. Both log
      `coex espnow probing channel=1 every_ms=2000` and `coex espnow tx ok=…` every 2 s.
- [ ] Each board logs `coex espnow rx from=<the other board's MAC>` repeatedly.
- [ ] While both keep probing, complete Part E on board 1 (`node-setup` with Lot A): the
      session succeeds, and board 1 kept logging `coex espnow rx from=…` before and during it.
- [ ] Board 1 logs `setup end=enrolled`; board 2 goes on probing until its window closes.
- [ ] Record the result in the Result table, then replace **pending** in the coexistence open item
      of `_bmad-output/specs/spec-coldframe/device-hardware.md` with the outcome and the date.

## G. A stuck button

- [ ] Hold the setup button of board 1 down (tape or a clamp) through a long press and the whole
      setup window. When the window closes, the log shows
      `setup button still held; timer wake only`, then `sleep ms=900000 button_armed=false`.
- [ ] Keep it held: the Node does **not** wake again at once; the next wake is the timer wake,
      15 minutes later (`wake cause=timer`, `wake plan=measure`).
- [ ] Release the button. After the next wake, a press wakes the Node again (`wake cause=button`).

## Result

| Date | Board | Build | Part | Pass / fail | Device ID | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| | | | | | | |
