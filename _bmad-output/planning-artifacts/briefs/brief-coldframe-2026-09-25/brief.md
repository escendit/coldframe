---
title: "Product Brief: Coldframe — Garden Intelligence Platform"
status: final
created: 2026-09-25
updated: 2026-09-25
---

# Product Brief: Coldframe — Garden Intelligence Platform

## Executive Summary

Coldframe tells you when your garden needs water — before it is too late. Battery-powered sensor nodes in the garden measure soil moisture, temperature, humidity, and air quality, and relay readings over ESP-NOW to a hub on the home Wi-Fi. A self-hosted server ingests the data and sends a push notification to your phone when a bed crosses its dryness threshold, while there is still time to act.

It starts as a personal fix for one gardener's summer problem and is built in the open: the firmware (Rust on ESP32-S3), the server with its API and web app, and the iOS and Android apps are all public, so other gardeners can build the same setup. Version 1 only notifies; the system is shaped so that a later version can open a valve instead of sending a message.

## The Problem

In summer, soil dries faster than it looks. Checking by hand is easy to forget, and by the time plants show stress, the damage is done. Today the author checks and waters by eye and habit.

Existing options do not close the gap:

- **DIY sensor nodes (ESPHome + Home Assistant)** deliver raw readings and leave the "should I water?" logic to the user.
- **Weather-based irrigation tools** estimate water loss from forecasts but never measure the actual soil — not enough in a hot, dry summer.
- **Commercial sensors (Gardena, Ecowitt, Flower Care)** lock you into their gateway, cloud, or valve ecosystem, and mostly show data rather than tell you what to do.

## The Solution

- **Sensor node** — an ESP32-S3 with a soil-moisture probe plus temperature, humidity, and air-quality Sensors. It reports over ESP-NOW, so it works at the far end of the garden, beyond Wi-Fi reach. Battery management is optional.
- **Hub** — an ESP32-S3 gateway that bridges ESP-NOW to the home Wi-Fi. It joins Wi-Fi only through BLE provisioning from the mobile app — no hard-coded credentials.
- **Server** — self-hosted. It ingests readings, evaluates thresholds and Device health, sends push notifications, and serves an API and a web app.
- **Mobile apps (iOS and Android)** — provision hubs over BLE, set thresholds, view readings, and receive notifications.

What the gardener gets:

- **Dryness alert** — sent early, when a bed crosses its threshold and before the soil is fully dry.
- **Health alerts** — for a low battery, or for a Device that has gone silent for longer than a configured window (for example, an hour or a day). Silence must never be mistaken for "all fine".
- **Pause** — an Administrator can pause ingestion and alerting for maintenance or the off-season, so planned downtime does not trigger failure alerts.
- **Your own thresholds** — Administrators calibrate the Sensors that support Calibration (in V1, the soil-moisture ADC) to your soil, and set the thresholds.

## What Makes This Different

Honestly: no technical moat. The pieces exist separately. Coldframe's value is putting them together as one open, self-hosted system whose job is a single, clear decision — *water now* — grounded in measured soil moisture rather than weather estimates, with Device-health alerts so a failed Sensor is noticed. It is also a real-world, end-to-end Rust-on-ESP32 reference (ESP-NOW, BLE provisioning, deep sleep) for others to learn from.

## Who This Serves

- **Primary: the author (project creator)** — one garden, a node at a distance from the house, an iOS phone. Success is a summer without plants lost to missed watering.
- **Secondary: open-source gardeners and makers** — comfortable flashing an ESP32 and running a small server, and looking for a self-hosted alternative to closed ecosystems.

## Success Criteria

- **One full summer** with the author's garden monitored and no plant loss from missed watering.
- **Early warning:** the dryness alert arrives at least several hours before the soil reaches "fully dry", at the author's Calibration.
- **No silent failures:** every node or hub that stops reporting triggers an alert within its configured window, unless paused.
- **Battery:** a node runs a full season on one charge.
- **Reproducible:** someone other than the author can build a node and hub and run the stack from the public docs.

## Scope

**In V1:**

- **Firmware** in Rust on ESP32-S3: node (Sensors → ESP-NOW) and hub (ESP-NOW → Wi-Fi, BLE provisioning).
- **Server:** ingestion, threshold evaluation, Device-health monitoring, pause control, push notifications, API, and web app.
- **Mobile apps** for iOS and Android — both first-class, mandatory platforms: BLE provisioning, thresholds, readings, and notifications.
- **Multiple Users and multiple gardens (Sites)** per server, each User holding a Role:
  - **Owner** — assigns Members and Devices.
  - **Administrator** — assigns Devices, calibrates Sensors, sets thresholds, and pauses ingestion.
  - **Member** — reads data and receives notifications.

  V1 is field-tested on one garden only.
- **Devices** carry multiple Sensors, each with a Sensor Specification that states what it measures and whether it can be calibrated.
- **Local deployment:** the server runs on the home network and takes no inbound traffic from the internet. Its only internet traffic is outbound, to the Apple and Google push services and any other notification endpoints. The app manages the system from the home network; notifications reach the phone anywhere.

**Out of V1:**

- **Remote app access** — exposing the server on a public or cloud endpoint is the next step after V1.
- **Irrigation control** (valves, pumps) — V1 must not block it, but does not build it.
- **Weather-forecast integration** — soil measurement is primary. Forecasts may later complement it ("rain tomorrow, skip").
- **A hosted service run for the public** — each adopter runs their own server.

## Open Questions and Risks

- **Measuring dryness** — how to turn raw probe readings into a normalized, calibrated value. Cheap capacitive probes are non-linear, soil-dependent, and drift with temperature, so probe choice and Calibration method matter.
- **Radio on the hub** — one ESP32-S3 must run Wi-Fi, BLE, and ESP-NOW. Rust radio support (esp-radio) is still in beta, with caveats listed for S3 BLE/coexistence; validate early. ESP-NOW must follow the router's Wi-Fi channel.
- **BLE provisioning in Rust** — there is no ready-made crate for Espressif's provisioning protocol; expect to implement it or bind the C component.
- **Remote access to the app** — V1 notifications work away from home, but viewing readings or changing settings off the home network waits for the public endpoint.
- **Re-notification policy** — notify once, repeatedly, or until watered.

## Vision

Coldframe becomes the open, self-hosted brain for a home garden: more nodes and beds, soil data combined with weather, and — the natural next step — automatic irrigation, where the "water now" notification becomes a valve opening, with the gardener still in control.
