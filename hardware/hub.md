# Hub

The Hub relays the Nodes' Readings to the Server. It talks ESP-NOW to the Nodes in the garden and
Wi-Fi with TLS to the Server on the home network, and it stores no Readings. It is always on.

> **License.** This design is part of Coldframe and is licensed under the
> [Apache License 2.0](../LICENSE).

> **Reference design, not yet bench-verified.** The parts are the reference choices of the
> [BOM](bom.md#hub), each with the requirement a substitute must meet.

## Schematic

The Hub is an unmodified ESP32-S3 dev board on USB power. The firmware uses **no GPIO**: no button,
no sensor, no LED. Setup is automatic: an unprovisioned Hub advertises over BLE on every boot.

```text
  +----------------+   USB 5 V, >= 500 mA   +------------------------------+
  | USB power      |----------------------->| ESP32-S3-DevKitC-1-N8        |
  | supply (mains) |      data cable        |  on-board 3.3 V regulator    |
  +----------------+                        |  ESP32-S3-WROOM-1 (PCB ant.) |
                                            |  RESET (EN) and BOOT buttons |
                                            +------------------------------+
                                               ^ 2.4 GHz: Wi-Fi to the access point,
                                               |          ESP-NOW to the Nodes,
                                               |          BLE during setup only
```

## Wiring

| From | To | Notes |
| --- | --- | --- |
| USB power supply | Dev board USB port | Either port of the DevKitC-1 powers the board. For flashing, use the same cable from your computer |

That is all. Nothing connects to the pin headers.

The DevKitC-1 has two USB ports, `UART` (through a USB-UART bridge) and `USB` (the ESP32-S3's
built-in USB Serial/JTAG). `espflash` flashes and monitors through either.

The DevKitC-1's on-board buttons are the only controls: `RST` restarts the Hub (an unprovisioned
Hub then prints its setup code again, see [flashing](../docs/build/flash.md#first-boot-and-the-setup-code)),
and `BOOT` is only for the download mode while flashing.

## Power

- 5 V over USB from a mains USB supply, at least 500 mA, always on. The radio's peaks need it.
- The Hub never sleeps, so a battery or a small panel is not an option.
- Unplugging it is safe: it keeps no Readings, and the Nodes keep and resend theirs.

## Antenna clearance

The WROOM-1 module has a PCB antenna at the end of the board, beyond the module's metal shield.

- Keep at least about 15 mm around the antenna end clear of metal, cables and the wall.
- Do not put the Hub in a metal box, behind a radiator, or on a metal shelf.
- A plastic case is fine. Mount the board upright, with the antenna end up, if you can.
- Use the WROOM-1 (PCB antenna), not the WROOM-1U, unless you also fit an external 2.4 GHz antenna.

## Placement on the home network

The Hub has to reach two things at once:

1. **The access point**, over 2.4 GHz Wi-Fi. The ESP32-S3 has no 5 GHz radio. The network must be
   WPA2-Personal or mixed WPA2/WPA3 (transition mode); **WPA3-only networks are not supported**
   (see [`apps/rs/hub/README.md`](../apps/rs/hub/README.md#ble-setup)). The Hub joins the strongest
   access point of the network you choose in the app.
2. **The Nodes**, over ESP-NOW, which uses the same radio and follows the access point's channel.

So place the Hub indoors, on the garden side of the house, for example on a window sill facing the
garden, and still in good Wi-Fi range. Thick walls, metal-coated glass and wet foliage all cut the
range. The Hub is not weatherproof: keep it indoors or in a dry, sheltered place.

The Hub reaches the Server by the Server's host name over HTTPS, so the home network's DNS must
resolve that name to the Server ([split DNS](../docs/operations/split-dns.md)).
