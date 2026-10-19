# Bill of materials

The parts for one Node and one Hub. A site needs one Hub and any number of Nodes.

> **License.** This design is part of Coldframe and is licensed under the
> [Apache License 2.0](../LICENSE).

> **Reference choices, not yet bench-verified.** Each line names an example part, and the
> requirement a substitute must meet. The example parts have not been built and measured together
> yet (see the [status](README.md#status-reference-design-not-yet-bench-verified)). Check every
> datasheet before you buy. Part numbers are examples, not endorsements.

## Node

| # | Qty | Part | Example | Requirement |
| --- | --- | --- | --- | --- |
| N1 | 1 | ESP32-S3 module | Espressif ESP32-S3-WROOM-1-N8 (8 MB flash, PCB antenna) | ESP32-S3; at least 4 MB flash (the [partition table](../apps/rs/node/partitions.csv) ends at 4 MB); PCB antenna; 3.0–3.6 V supply. A bare module, not a dev board: a stock dev board draws about 1 mA asleep (see [power](../docs/build/power.md#sleep-current-budget)) |
| N2 | 1 | Capacitive soil probe | "Capacitive soil moisture sensor v2.0" with a TLC555 (CMOS 555) | Capacitive, not resistive. Runs at 3.3 V. Analogue output stays at or below about 3.1 V, the range of the ADC at 11 dB. **NE555 caveat:** many cheap "v1.2" boards carry an NE555, which works poorly at 3.3 V; read the chip marking and prefer a TLC555 or another CMOS timer. Their edges are unsealed: seal the probe as in [enclosure.md](enclosure.md#sealing-the-soil-probe) |
| N3 | 1 | BME680 breakout | A bare BME680 breakout (sensor, decoupling and I²C pull-ups only) with SDO pulled high, or the bare sensor on the Node board | Bosch BME680 on I²C at **0x77** (SDO to VCC). Powered from 3.3 V directly: no always-on regulator, no level shifter and no power LED, which would break the sleep budget. Boards like Adafruit 3660 carry an on-board 3.3 V regulator and level shifter (and the STEMMA QT revision a power LED), so they do **not** qualify unless modified. I²C pull-ups to 3.3 V |
| N4 | 1 | 1S LiPo cell | A 3.7 V, 800 mAh pouch (size code about 603040) with a protection circuit (PCM) and a 10 kΩ NTC lead | 1S LiPo, 4.2 V full, about 800 mAh. Built-in protection (over-discharge, over-current). A **10 kΩ NTC** (B about 3435 K) in the pack, wired to the charger's NTC input |
| N5 | 1 | Solar panel | A ~1 W, 5–6 V epoxy or glass panel (about 110 × 60 mm) | About 1 W. The open-circuit voltage must stay below the charger's maximum input: a TP4056 is rated to 8 V at most, and a "6 V" panel often reaches 7 V or more open-circuit, so pair a TP4056 with a 5 V panel, or use the CN3791 |
| N6 | 1 | Charger | TP4056 (linear, 1S LiPo, TEMP pin and open-drain `CHRG`). Solar alternative: CN3791 (MPPT, NTC input, open-drain `CHRG` and `DONE`) | 1S LiPo CC/CV charge to 4.2 V. An **NTC input** that stops charging below 0 °C and above about 45 °C. An **open-drain, active-low** charge status pin (`CHRG`), read by GPIO6. Charge current at most 0.5 C (400 mA for 800 mAh); a ~1 W panel delivers about 200 mA, so set it near that (TP4056: R<sub>PROG</sub> 5 kΩ gives about 250 mA, per its datasheet table). Many TP4056 modules tie TEMP to ground, which disables the NTC: use one that exposes TEMP, or build the charger from the IC |
| N7 | 1 | 3.3 V LDO | Richtek RT9080-33GJ5 (600 mA, about 2 µA quiescent) | 3.3 V out. **Quiescent current at most about 5 µA**, it flows all the time. **At least 500 mA** output for the radio peaks. Low dropout. Stable with ceramic capacitors |
| N8 | 2 | High-side load switch | TI TPS22917DBV (SOT-23-6, quick output discharge) | Input range covering 3.3 V (probe feed) and 4.2 V (cell, divider feed). **Active-high enable**, with an **external pull-down** on the enable pin (N11): the ESP32-S3 pads float in deep sleep. Off-state leakage and quiescent current at most about 1 µA. Output at 0 V when off (output discharge helps). A discrete P-MOSFET with a level-shifting driver also meets it. **Not** a low-side N-MOSFET (see [node.md](node.md#switched-battery-divider)) |
| N9 | 2 | Divider resistors | 100 kΩ, 1 %, 0603 | Exactly 100 kΩ / 100 kΩ: the firmware's `DIVIDER` in [`pins.rs`](../apps/rs/node/src/board/pins.rs) assumes it. 1 % or better |
| N10 | 1 | Divider midpoint capacitor | 4.7 nF ceramic, 0603 | **At most 10 nF.** With 100 kΩ / 100 kΩ that is RC = 0.5 ms, well inside the firmware's 10 ms settle time; a larger capacitor needs the firmware changed |
| N11 | 2 | Pull-downs | 100 kΩ, 0603 | One on each load-switch enable (GPIO4, GPIO5), to ground, so both switches stay off in deep sleep |
| N12 | 1 | Setup button | An IP67 panel-mount momentary push button (normally open) | Momentary, normally open, from GPIO7 to ground. No external pull-up: the firmware uses the internal one, kept in deep sleep |
| N13 | 1 | EN reset network | 10 kΩ pull-up and 1 µF to ground on EN | As in the ESP32-S3-WROOM-1 datasheet, so the module starts cleanly when power comes up |
| N14 | 2 | Bench buttons | 6 × 6 mm tactile switches, on the board | `RESET` (EN to ground) and `BOOT` (GPIO0 to ground, with a 10 kΩ pull-up), for flashing. Not reachable from outside the enclosure |
| N15 | 1 | Decoupling | 22 µF and 100 nF ceramic at the module's 3V3 pin; the LDO's input and output capacitors per its datasheet | As in the module and LDO datasheets |
| N16 | 1 | Flashing header | 1 × 7 pin header, 2.54 mm | GND, USB D− (GPIO19), USB D+ (GPIO20), EN, GPIO0, UART0 TX (GPIO43) and RX (GPIO44). The ESP32-S3's built-in USB Serial/JTAG needs no USB-UART bridge, which would draw current in sleep. See [node.md](node.md#flashing-header) |
| N16a | 1 | USB lead for flashing | A USB breakout board (USB-C or micro-USB) or a cut USB cable, wired to the header | GND, D− and D+ to header pins 1–3; **VBUS left unconnected**. Short (under about 20 cm). See [node.md](node.md#connecting-usb) |
| N17 | 1 | Enclosure and parts | An IP65 (or better) ABS or polycarbonate box, cable glands, a vent membrane, conformal coating and epoxy, heat-shrink | See [enclosure.md](enclosure.md) |

## Hub

The Hub uses no GPIO: it is a dev board on USB power, indoors.

| # | Qty | Part | Example | Requirement |
| --- | --- | --- | --- | --- |
| H1 | 1 | ESP32-S3 dev board | Espressif ESP32-S3-DevKitC-1-N8 | ESP32-S3 on a WROOM-1 module (PCB antenna, not the -1U external-antenna variant) with at least 4 MB flash (the [partition table](../apps/rs/hub/partitions.csv) ends at 4 MB), and a USB port |
| H2 | 1 | USB power supply | Any 5 V USB charger | 5 V, at least 500 mA, always on. A good supply: brownouts reboot the Hub |
| H3 | 1 | USB cable | USB-A or USB-C to micro-USB or USB-C, to match the board | A data cable (for flashing), not a charge-only cable |
| H4 | 1 | Case | A small plastic case, or none | Not metal: the antenna needs clearance (see [hub.md](hub.md#antenna-clearance)) |

Sleep current does not matter for the Hub: it is always on and mains powered.
