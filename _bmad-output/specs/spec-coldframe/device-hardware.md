# Device hardware and firmware requirements

Node and Hub facts that bind firmware and hardware work. Evidence: PRD addendum (power, sensors) and `docs/spikes/hub-radio-coexistence.md` (radio). Cross-unit protocol and trust rules live in the architecture spine (AD-9, AD-10, AD-11, AD-12, AD-17).

## Platform
- Both Devices use Rust firmware on **ESP32-S3**: `coldframe-node` (Sensors → ESP-NOW, deep sleep) and `coldframe-hub` (ESP-NOW ⇄ Wi-Fi/TLS, BLE provisioning).
- ESP-NOW carries Node → Hub traffic, because it reaches Lots beyond usable Wi-Fi (NFR-11).
- ESP-NOW and the Hub's Wi-Fi station share one channel, so the Node must follow the router's channel.

## Hub requirements (radio spike, GO 2026-09-27)
- Wi-Fi station, BLE, ESP-NOW, and a TLS client run concurrently on one S3. They were validated for about 3 h 45 min, with 0.04 % ESP-NOW loss over 2 h and a flat heap.
- **H-1:** scan all channels and join the **strongest** BSSID for the SSID, never the first match. Re-evaluate when RSSI degrades, for mesh networks.
- **H-2:** TLS checks certificate validity dates. This needs mbedTLS built from C, so cmake and ninja must be in the Hub build and CI.
- **H-3:** WPA3-only networks are not supported by esp-radio 1.0.0-beta.1. Document the limitation for CAP-1.
- The BLE stack costs about 30 KB of heap. Keep at least 32 KiB free heap under full load.

## Node requirements
- **N-1:** only the sealed application acknowledgement counts as delivery. The radio-level send status is unreliable under coexistence (false failures were seen), so ignore it.
- **N-2:** the acknowledgement window is **W = 300 ms** (AD-17). On expiry, resend; after repeated misses, re-scan channels. The spike measured re-discovery in about 0.6 s.
- BLE advertising only in setup mode, entered by a **physical setup button** with a timeout. Continuous advertising breaks the energy budget.
- Buffer at least 24 h of unacknowledged Readings: 96 per Sensor, a few KB. Keep them in RTC RAM (survives deep sleep) or flash (survives power loss; mind write wear). Batch resends.
- No Wi-Fi on the Node. Keep wakes short, with a channel re-scan only on acknowledgement failure.
- **Open:** BLE (setup mode) and ESP-NOW coexistence on the Node is not yet validated. The spike's Node ran ESP-NOW only. Validate it in the Node epic.

## Power budget (NFR-4)
- 800 mAh LiPo, about 640 mAh usable. One wake every 15 min (96/day): boot, read Sensors, ESP-NOW send.

| Board | Wake | Sleep current | mAh/day | No-sun runtime |
| --- | --- | --- | --- | --- |
| Optimized custom | 0.3 s @ 80 mA | ~20 µA | ~1.1 | ~1 year (self-discharge) |
| Reasonable custom | 0.5 s @ 100 mA | ~50 µA | ~2.5 | ~8 months |
| Stock dev board | 0.5 s @ 100 mA | ~1 mA | ~25 | ~3–4 weeks |

- A 6-month season without sun needs about 3.5 mAh/day, i.e. an average sleep current of ≤ ~100 µA. Prefer a low-quiescent custom board, and switch Sensors off during sleep.
- **Solar:** a ~1 W panel yields about 500 mAh/day in full sun and 25–50 mAh/day in heavy overcast. Both exceed the budget, so the real question is how many dark days the battery bridges (≈ 250 days at 2.5 mAh/day).
  - Place the panel **above the canopy**. Shading shows up as falling battery % while *not charging*, until CAP-14 fires.
  - The charger must stop charging below 0 °C and above ~45 °C. Use a charger IC with an NTC input.
- **Charging status:** read the charger IC's status pin as a GPIO on each wake (e.g. TP4056 `CHRG`/`STDBY`, BQ24074 `CHG`/`PGOOD`, CN3791 `CHRG`/`DONE`). Fallback: the voltage trend across daylight hours.
- **Battery %:** measure the voltage by ADC through a switched divider (no sleep draw) and map it with a LiPo discharge curve. The value is approximate, especially while charging.

## Sensors
- **Soil moisture:** capacitive probe, not resistive. Cheap "v1.2" boards often use an NE555 (poor at 3.3 V), have unsealed edges, and give non-linear output, so seal or coat them. Probes need 2–3 days to settle after insertion. Temperature and salinity shift Readings. Probe choice and temperature compensation are open (PRD OQ4).
- **BME680** covers temperature, humidity, and gas resistance (air quality). Pressure is measured but is not a V1 Sensor.
  - Use forced mode, one measurement per 15-minute wake: about 0.05 mAh/day. The gas heater draws ~12 mA for ~150 ms only.
  - Report raw gas resistance (Ω). No BSEC: it is a closed-source binary incompatible with the Apache-2.0 repositories, and it needs a 3 s or 300 s rhythm. BSEC could become an adopter opt-in later.
