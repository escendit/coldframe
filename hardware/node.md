# Node

The Node is a battery-powered ESP32-S3 on solar power. Every 15 minutes it wakes, powers the soil
probe and the battery divider for a moment, reads the BME680, sends the Readings to the Hub over
ESP-NOW, and goes back to deep sleep.

> **License.** This design is part of Coldframe and is licensed under the
> [Apache License 2.0](../LICENSE).

> **Reference design, not yet bench-verified.** The parts are the reference choices of the
> [BOM](bom.md), each with the requirement a substitute must meet.

**The source of truth for every pin is
[`apps/rs/node/src/board/pins.rs`](../apps/rs/node/src/board/pins.rs).** This page follows it. If
you change a pin, change it there; the firmware then reads the new pin, and this page needs the
same change.

## Block diagram

```text
                               VBAT (3.3-4.2 V)
 +-----------+    +-----------+     +----------------------+------------------------+
 | Solar     |--->| Charger   |-BAT-+                      |                        |
 | panel     |    | (TP4056)  |     |                      v                        v
 | ~1 W 5-6 V|    |           |   +-+-----------+   +-------------+        +----------------+
 +-----------+    |      TEMP |<--| 1S LiPo     |   | LDO 3.3 V   |        | U2 load switch |<-- GPIO5
                  |      CHRG |-+ | 800 mAh     |   | Iq <= ~5 uA |        | (divider)      |    + 100 k to GND
                  +-----------+ | | PCM, 10k NTC|   +------+------+        +-------+--------+
                                | +-------------+          | 3V3                   |
                                |                          |                    R1 100 k
                                |                          |                       +-----------> GPIO2 (ADC1_CH1)
                                |                          |                    R2 100 k  +  C <= 10 nF
                                |                          |                      GND       GND
                                |                          |
                                |                          +--> U1 load switch (probe) <-- GPIO4 + 100 k to GND
                                |                          |         |
                                |                          |    soil probe --AOUT------------> GPIO1 (ADC1_CH0)
                                |                          |
                                |                          +--> BME680, 0x77 (SDO high) <--I2C--> GPIO8 SDA, GPIO9 SCL
                                |                          |
                                |                          +--> ESP32-S3-WROOM-1 3V3 (EN: 10 k up, 1 uF down)
                                |
                                +---------------------------------- CHRG, active low --------> GPIO6

  Setup button:     GPIO7 ---[ push ]--- GND
  Flashing header:  GND, D- (GPIO19), D+ (GPIO20), EN, GPIO0, TXD0 (GPIO43), RXD0 (GPIO44)
```

The battery feeds the LDO directly. The charger has no power path, which is fine at the Node's
microamp load.

## Power path

| From | To | Notes |
| --- | --- | --- |
| Panel + | Charger VCC (input) | The panel's open-circuit voltage must stay below the charger's maximum input ([BOM N5](bom.md#node)) |
| Panel − | GND | |
| Charger BAT | Cell + (protected terminal) | Charge to 4.2 V, at most 0.5 C |
| Cell − (protected terminal) | GND | |
| Cell NTC | Charger TEMP / NTC | Through the network the charger's datasheet gives for a 0 °C to about 45 °C charging window. Never tie TEMP to ground: that disables the temperature cut-off |
| Cell + (VBAT) | LDO IN | Plus the input capacitor of the LDO datasheet |
| LDO OUT (3V3) | Module 3V3 | 22 µF and 100 nF at the module pin |
| LDO GND, module GND | GND | |
| 3V3 | EN, through 10 kΩ; EN to GND through 1 µF | The reset network |

The battery is read through its own switched divider (below); the firmware maps the voltage to a
percentage with the curve in
[`packages/rs/sensing/src/battery.rs`](../packages/rs/sensing/src/battery.rs). The curve reads
0 % at 3.27 V. Below about 3.4 V the LDO is in dropout and the module runs a little under 3.3 V,
which the ESP32-S3 tolerates down to 3.0 V.

## Switched probe power

The probe is powered only while it is read: the firmware switches it on, waits 100 ms
(`PROBE_SETTLE_MS` in [`packages/rs/sensing/src/wake.rs`](../packages/rs/sensing/src/wake.rs)), and
takes 8 samples.

| From | To | Notes |
| --- | --- | --- |
| GPIO4 | U1 ON (enable) | High = on |
| U1 ON | GND, through 100 kΩ | The pull-down: the pad floats in deep sleep, so the switch must stay off on its own |
| 3V3 | U1 VIN | |
| U1 VOUT | Probe VCC | 0 V while off |
| Probe AOUT | GPIO1 (ADC1_CH0) | Must stay at or below about 3.1 V (11 dB attenuation) |
| Probe GND | GND | |

## Switched battery divider

The divider is switched on the **high side**, between the cell and the top resistor. A low-side
N-MOSFET is ruled out: while off it would leave the midpoint, and GPIO2, at up to 4.2 V through the
top resistor, beyond the pad's rating, and current would leak through the pad. Switched on the high
side, GPIO2 sees 0 V while off.

| From | To | Notes |
| --- | --- | --- |
| GPIO5 | U2 ON (enable) | High = on |
| U2 ON | GND, through 100 kΩ | The pull-down, as for U1 |
| VBAT | U2 VIN | |
| U2 VOUT | R1 100 kΩ (top) | |
| R1 | Midpoint | |
| Midpoint | R2 100 kΩ (bottom) to GND | 4.2 V at the cell reads 2.1 V |
| Midpoint | GPIO2 (ADC1_CH1) | Calibrated millivolts, 11 dB |
| Midpoint | GND, through at most 10 nF | RC = 50 kΩ × 10 nF = 0.5 ms, inside the 10 ms settle time. A larger capacitor needs `DIVIDER_SETTLE_MS` raised to at least 5·RC |

The divider values are `DIVIDER` in [`pins.rs`](../apps/rs/node/src/board/pins.rs). Only ADC1 is
used, because ADC2 conflicts with the radio.

## Charger status

| From | To | Notes |
| --- | --- | --- |
| Charger `CHRG` | GPIO6 | Open-drain, active low: low while charging. The firmware enables the internal pull-up, so no external pull-up is needed. Read on each wake as *charging* or *not charging* |

## Setup button

| From | To | Notes |
| --- | --- | --- |
| GPIO7 | Button | GPIO7 is a low-power (RTC) pad, so a press wakes the Node from deep sleep |
| Button | GND | Active low. The internal pull-up is kept in deep sleep: no external resistor |

A short press (50 ms to under 3 s) sends a Reading now. A long press (3 s) opens the BLE setup
window for 180 s. The timing is in
[`packages/rs/sensing/src/button.rs`](../packages/rs/sensing/src/button.rs).

## I²C to the BME680

| From | To | Notes |
| --- | --- | --- |
| GPIO8 | BME680 SDA | |
| GPIO9 | BME680 SCL (SCK) | |
| 3V3 | BME680 VCC | Not switched: forced mode puts it back to sleep (about 0.15 µA) |
| 3V3 | BME680 SDO | Address **0x77** (`BME680_SECONDARY_ADDRESS` in `pins.rs`) |
| 3V3 | BME680 CS | Selects I²C; breakouts usually do this already |
| GND | BME680 GND | |
| 3V3 | SDA, SCL, through the breakout's pull-ups | Pull-ups to 3.3 V only |

## Flashing header

The ESP32-S3 has a built-in USB Serial/JTAG controller, so the Node needs no USB-UART bridge, which
would draw current while asleep. `espflash` flashes and monitors through it.

| Header pin | To | Notes |
| --- | --- | --- |
| 1 GND | GND | |
| 2 D− | GPIO19 | USB D− |
| 3 D+ | GPIO20 | USB D+ |
| 4 EN | EN | Reset (the RESET button pulls it to ground) |
| 5 IO0 | GPIO0 | Strapping pin: held low at reset, the chip enters the download mode. 10 kΩ pull-up and the BOOT button to ground |
| 6 TXD0 | GPIO43 | UART0, for a USB-UART adapter instead of USB |
| 7 RXD0 | GPIO44 | UART0 |

Power the board from the battery while you flash; do not connect USB VBUS to the 3V3 rail. The USB
serial link drops while the chip sleeps, and `espflash monitor` reconnects on the next wake.

### Connecting USB

The Node has no USB connector. To flash it, wire a USB breakout or a cut USB cable
([BOM N16a](bom.md#node)) to the header:

- USB **GND** to header pin 1 (GND);
- USB **D−** to header pin 2 (GPIO19);
- USB **D+** to header pin 3 (GPIO20);
- leave USB **VBUS unconnected**: the Node runs from its battery while you flash.

Keep the lead short (under about 20 cm) and the D−/D+ wires twisted together or side by side: USB
does not tolerate long, loose data wires.

## Wiring: full pin-to-pin list

Every connection on the module, checked against
[`pins.rs`](../apps/rs/node/src/board/pins.rs). Look up the module pad of each GPIO in the
ESP32-S3-WROOM-1 datasheet.

| Module pin | Signal | Connects to | Direction | Notes |
| --- | --- | --- | --- | --- |
| 3V3 | Supply | LDO OUT; 22 µF + 100 nF to GND | in | |
| GND | Ground | GND | | |
| EN | Reset | 10 kΩ to 3V3, 1 µF to GND, RESET button to GND, header pin 4 | in | |
| GPIO0 | Boot strap | 10 kΩ to 3V3, BOOT button to GND, header pin 5 | in | Strapping pin, only for flashing |
| GPIO1 | Soil probe analogue out | Probe AOUT | in (ADC1_CH0) | Raw count, 11 dB |
| GPIO2 | Battery divider midpoint | R1/R2 midpoint, ≤ 10 nF to GND | in (ADC1_CH1) | Calibrated mV, 11 dB, 100 kΩ / 100 kΩ |
| GPIO4 | Probe power switch | U1 ON, 100 kΩ to GND | out | High-side switch, high = on |
| GPIO5 | Divider switch | U2 ON, 100 kΩ to GND | out | High-side switch, high = on |
| GPIO6 | Charger status (`CHRG`) | Charger `CHRG` | in | Active low, internal pull-up |
| GPIO7 | Setup button | Button to GND | in | Active low, internal pull-up kept in deep sleep; wakes the Node |
| GPIO8 | I²C SDA | BME680 SDA | in/out | |
| GPIO9 | I²C SCL | BME680 SCL | out | |
| GPIO19 | USB D− | Header pin 2 | in/out | Flashing and logs |
| GPIO20 | USB D+ | Header pin 3 | in/out | Flashing and logs |
| GPIO43 | UART0 TX | Header pin 6 | out | Optional |
| GPIO44 | UART0 RX | Header pin 7 | in | Optional |

Every other GPIO is unconnected. Off-module parts:

| Part | Pin | Connects to |
| --- | --- | --- |
| Panel | + / − | Charger VCC / GND |
| Charger | BAT / TEMP / `CHRG` / GND | Cell + / cell NTC network / GPIO6 / GND |
| Cell | + / − / NTC | VBAT (charger BAT, LDO IN, U2 VIN) / GND / charger TEMP |
| LDO | IN / OUT / GND | VBAT / 3V3 / GND |
| U1 (probe switch) | VIN / VOUT / ON / GND | 3V3 / probe VCC / GPIO4 + 100 kΩ to GND / GND |
| U2 (divider switch) | VIN / VOUT / ON / GND | VBAT / R1 / GPIO5 + 100 kΩ to GND / GND |
| R1 100 kΩ | 1 / 2 | U2 VOUT / midpoint |
| R2 100 kΩ | 1 / 2 | Midpoint / GND |
| C ≤ 10 nF | 1 / 2 | Midpoint / GND |
| Soil probe | VCC / AOUT / GND | U1 VOUT / GPIO1 / GND |
| BME680 | VCC / GND / SDA / SCL / SDO / CS | 3V3 / GND / GPIO8 / GPIO9 / 3V3 / 3V3 |
| Setup button | 1 / 2 | GPIO7 / GND |

## Sleep current

The design aims at an average sleep current of at most about 100 µA: the LDO's quiescent current,
the module in deep sleep, the BME680 asleep, the switches' leakage and the charger's drain on the
cell. **It has not been measured yet.** See [power.md](../docs/build/power.md#sleep-current-budget)
for the budget and how to measure it.
