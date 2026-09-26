# Research Digest: Garden Sensing Landscape (2026-09-25)

## How comparables position themselves
- **Ecowitt WH51**: cheap, 433 MHz, 1x AA (field reports 12-18 months), 100-200 ft range. Needs an Ecowitt gateway. Has a manual 0%/100% AD calibration per probe for different soil types. Local API plus native Home Assistant (HA) integration. Topsoil only; takes 2-3 days to settle after install. It is what hobbyists reach for.
- **Gardena smart Sensor**: premium (~$79 plus a gateway). 2x AA "one season / ~1 year". Short proprietary radio range (~65 ft). App threshold alerts. Cloud-first: the HA integrations drop after Wi-Fi or internet blips, and one user reported missing 10-20% of status changes. A community local-control project exists because of this.
- **Xiaomi Mi Flora / HHCC**: BLE coin cell, rated 1.5-2 years. Polling from HA cuts that to about 1 month; passive BLE listening fixes it. Very short range.
- **Rachio**: sells irrigation, not sensing. Weather Intelligence runs rain, wind and freeze skips on a *forecast* or estimated soil moisture. No probe is required.
- **SwitchBot / Govee**: air temperature and humidity only (no soil). App push alerts on custom min/max ranges. Hub and cloud required. Govee gives 20 days of free history, and longer history needs a subscription.
- **OSS/DIY**: OpenSprinkler (open hardware, weather-adjusted valve timing, local-only option), Mycodo (Raspberry Pi input/output automation), FarmBot (CNC gantry, a niche product), ESPHome/Tasmota plus HA (the de facto DIY path, where the user must wire and calibrate everything). None of them ship an end-to-end "battery bed node → push alert" product. The DIY path wants ESP-NOW for low power, with a gateway forwarding to MQTT/HA.

## User pain points (ranked by PRD relevance)
1. **Battery life vs. reporting cadence.** Vendor claims cluster around ~12 months on AA or a coin cell. Poor firmware or polling cuts this to weeks (Mi Flora). On DIY ESP32 boards, regulators, LEDs and sensors that stay powered during sleep add 10-50 mA. One maker went to about 4 months after fixing deep sleep. Coldframe needs an explicit battery-life target and a power budget for the sensor.
2. **Calibration and drift.** Readings depend on soil type, salinity, compaction, depth, supply voltage and temperature. Vendors ship loam-calibrated probes and expose dry/wet raw endpoints (Ecowitt 0%/100% AD). Probes need a settling period. Users mostly want *relative* "needs water" signals, not absolute VWC.
3. **Probe durability.** Resistive forks corrode by electrolysis. Cheap capacitive v1.2 boards corrode or absorb moisture at unsealed PCB edges and drift with temperature, and nobody has good long-term data. Specify conformal coating or sealed probes.
4. **Cloud lock-in and reliability.** Gardena, Govee and SwitchBot depend on the cloud. Integrations break on internet blips, history sits behind subscriptions, and users build local workarounds. This is Coldframe's core differentiator: local-first, with alerts that still work when the WAN is down (push notifications themselves still need APNs/FCM).
5. **Alert noise.** Naive threshold alerts flap near the threshold. HA's plant component applies a fixed ~5% hysteresis band. Community blueprints add "watered" confirmation, "Mark watered" and "Snooze 24h" actions, and drying-curve predictions. Build in hysteresis, per-bed cooldown and dedup, snooze, a recovery notice, and alerts for a stale or low-battery node.

Secondary: radio range (BLE and proprietary 65 ft radios are the weak spot, and 433 MHz is the benchmark, so ESP-NOW long-range mode must match it), high per-sensor cost, and app quality or feature gating.

## Implications for Coldframe v1
- Per-node calibration flow: capture dry and wet raw values, show a settling indicator, offer soil-type presets.
- Thresholds with hysteresis, minimum dwell time and cooldown. Treat "sensor silent" and "battery low" as separate, rate-limited alert classes.
- Publish a measured battery-life claim (target ≥ 1 season on the chosen cell), not a theoretical one.
- Local-first. Keep history without time limits and paywalls, and offer an HA/MQTT bridge so users are not forced to pick between Coldframe and HA.

## Sources
- https://shop.ecowitt.com/products/wh51
- https://oss.ecowitt.net/uploads/20251226/WH51Manual.pdf
- https://www.wxforum.net/index.php?topic=46178.0
- https://www.smarthomeexplorer.com/guides/best-smart-soil-moisture-sensors-garden-2026
- https://www.gardena.com/int/products/smart-system/smart-system/smart-sensor/967986501.html
- https://community.home-assistant.io/t/gardena-smart-system-gets-disconnected-everytime-wifi-is-temporarily-disconnected/744101
- https://github.com/py-smart-gardena/hass-gardena-smart-system/issues/176
- https://github.com/cloudless-garden/ha-gardena-smart-local-preview
- https://community.home-assistant.io/t/mi-flora-battery-drain-why-dont-retrive-data-like-the-app-do/249589
- https://community.home-assistant.io/t/mi-flora-battery-life/148532
- https://support.rachio.com/en_us/rachio-weather-intelligence-B153wIJKD
- https://rachio.com/products/smart-hose-timer
- https://support.switch-bot.com/hc/en-us/articles/360049008333-How-to-Configure-Alerts-of-SwitchBot-Meter
- https://opensprinkler.com/
- https://community.home-assistant.io/t/capacitive-soil-moisture-sensor-project-not-going-as-planned/438162
- https://forum.arduino.cc/t/capacitive-soil-moisture-sensor-v1-2-inconsistent/882137
- https://esp32.co.uk/capacitive-soil-moisture-sensor-esp32-esphome-home-assistant/
- https://esp32.co.uk/esp32-battery-powered-sensors-deep-sleep-low-power-design-guide/
- https://maakbaas.com/esp32-soil-moisture-sensor/logs/using-deep-sleep/
- https://github.com/Olen/homeassistant-plant
- https://github.com/spiddeer/ha-plant-notifications
- https://github.com/jacobmiller22/selfhosted/issues/140
