# Bench checklist: Hub BLE setup (Story 3.4)

This checklist is the acceptance test for the Hub's BLE setup service (AD-25) on a real ESP32-S3.
CI runs every session rule on the host (`tests/rs/setup`); what happens over the air, on the chip
and in its flash is checked here, by hand, with the desktop client `coldframe-setup-client`.

> [!NOTE]
> Use a dev-mode build on a board that must never burn an eFuse (board **D** of the
> [identity checklist](hub-identity-checklist.md)). A release build works the same way but burns an
> eFuse key block on its first boot, if it has none yet.

> [!WARNING]
> Never paste a real Wi-Fi password or a real setup code into this file, a commit or an issue. The
> Result table records outcomes only.

## What you need

- An ESP32-S3 board with the Hub firmware, the `esp` toolchain and `espflash` 4.x, as in
  [`apps/rs/hub/README.md`](../../apps/rs/hub/README.md).
- A Linux machine with Bluetooth LE (BlueZ) for the client. The client builds its own libdbus
  (`libdbus-sys` `vendored`), so only a C compiler is needed.
- A 2.4 GHz WPA2 network the Hub may join, and, for Part E, a WPA3-only network if one is at hand.
- The Server's enrolment key: `publicKey` from `GET /enrolment-key` (base64url, 43 characters). Any
  32-byte X25519 public key works for the BLE part; only a real one lets the Server accept the
  printed enrolment body.
- A Site ID to bind to (any non-empty string of at most 36 bytes for the BLE part).

Build the client once, from the repository root:

```sh
cargo build -p coldframe-setup-client
alias cfsetup="$PWD/target/debug/coldframe-setup-client"
```

Run the Hub commands from `apps/rs/hub` after `source ~/export-esp.sh`. Record the port, the board
and the date for each run.

## A. Flash and record the code

- [ ] `./check-image.sh` passes: no Wi-Fi credential canary in the ELF or the flash image.
- [ ] Erase the setup partition so the run starts unprovisioned:
      `espflash erase-region 0xC000 0x2000`. This is required before the first flash of a board that
      ran a build with the older partition table (Story 3.2), where this range belonged to `nvs`.
- [ ] Flash and monitor, for example `cargo run --features dev-mode`.
- [ ] The boot shows the identity line, then exactly one line `setup code=XXXXXXXX`: 8 characters
      from `0123456789ABCDEFGHJKMNPQRSTVWXYZ`. Write the code on a sticker for the board, not here.
- [ ] Then `ble advertising setup service as Coldframe Hub XXXX`, where `XXXX` are the first four
      digits of the Device ID, uppercase.
- [ ] Reset the board. The same `setup code=` line appears with the same code.
- [ ] No other line contains the code.

## B. Scan

- [ ] `cfsetup scan` lists the Hub with its address, `Coldframe Hub XXXX` and an RSSI.

## C. Wrong code

- [ ] Run `cfsetup setup --code <the code with its last character changed> --ssid <ssid>
      --password <password> --site <site> --enrolment-key <key>`.
- [ ] The client prints `wrong setup code` and exits non-zero (`echo $?` is not 0).
- [ ] The Hub logs `ble connected` then `ble disconnected` at once, and advertises again.
- [ ] Nothing was stored: a reset still prints `setup code=` and advertises.

## C2. Idle timeout

- [ ] Connect without writing anything, for example with `bluetoothctl` (`connect <address>`), and
      wait. After 300 s the Hub drops the connection (`ble disconnected`) and logs
      `ble advertising setup service as Coldframe Hub XXXX` again; `cfsetup scan` finds it again.

## D. Full setup

- [ ] Run `cfsetup setup --code <code> --ssid <ssid> --password <password> --site <site>
      --enrolment-key <key>`. Lowercase code input works too.
- [ ] The client prints, in order:
  - `session open, mtu=…`;
  - `identity device_id=<the Device ID of the boot log> kind=1 firmware=<version>`;
  - the networks the Hub hears, one line per SSID, strongest first, WPA3-only ones marked
    `wpa3-only (unsupported)`;
  - `site binding sent`, `enrolment sealed device_id=<same ID>`;
  - `wifi result CONNECTED`;
  - `POST /sites/<site>/devices` and a JSON body with `deviceId`, `kind: "hub"`, `enc` (43
    characters) and `ciphertext` (64 characters).
- [ ] The client exits 0.
- [ ] The Hub logs `setup provisioned`, with no SSID, password or Site on the line.
- [ ] The BLE connection survived the Wi-Fi scan and the join while both radios shared the chip:
      the client received the scan list and `CONNECTED` over the same connection, so a completed
      run is the evidence.
- [ ] Right after, in the same boot, `cfsetup scan` no longer finds the Hub: advertising stopped
      at provisioning.
- [ ] The next line is `heap free=… used=…`. Record `free` in the Result table. Pass: at least
      32768 (32 KiB) with BLE and Wi-Fi both active. The `uptime` lines repeat `heap_free=` once a
      minute; it must stay at or above 32768.
- [ ] Optional, with a real Server key: `POST` the printed body to `/sites/<site>/devices` as an
      Administrator. The Server answers 201.
- [ ] Optional: a wrong password in a first run gives `wifi result WRONG_PASSWORD`; nothing is stored
      (a reset still shows the code). A second run with the right password gives `CONNECTED`.

## E. Unsupported and unknown networks (optional, run before Part D)

After `CONNECTED` the session refuses further Wi-Fi configuration, and the provisioned Hub stops
advertising, so run these first, or after erasing `cf_setup` again.

- [ ] `--ssid <a WPA3-only network>` gives `wifi result UNSUPPORTED_SECURITY`,
      and the Hub does not try to join.
- [ ] `--ssid <an SSID that is not in range>` gives `wifi result NETWORK_NOT_FOUND`.

## F. Reboot after setup

- [ ] Reset the board after Part D.
- [ ] The boot shows the identity line and `setup provisioned earlier; not advertising`. The Hub
      does not re-join Wi-Fi on this boot; that arrives with Story 3.5.
- [ ] **No** `setup code=` line appears.
- [ ] `cfsetup scan` no longer lists the Hub.
- [ ] To set the Hub up again, erase `cf_setup` (`espflash erase-region 0xC000 0x2000`): the next
      boot draws a new code.

## Result

| Date | Board | Build | Part | Pass / fail | Device ID | Heap free after setup (bytes) | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- |
| | | | | | | | |
