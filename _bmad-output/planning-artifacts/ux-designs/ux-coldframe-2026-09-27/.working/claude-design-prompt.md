# Claude Design prompt: Coldframe (exploration)

Paste everything below the line into Claude Design. The lines marked *(optional)* can be deleted.

---

Design explorations for **Coldframe**, a self-hosted garden watering-alert app. I want **three distinct visual directions** to choose from, not one polished answer. For each direction, show the key screens listed below on **iPhone** and one **Android** screen. Also show the **web app overview** on desktop width.

## What Coldframe is
- Battery- and solar-powered sensor **Nodes** sit in garden beds (**Lots**) and measure soil moisture, temperature, humidity and air quality every 15 minutes.
- A **Hub** on the home Wi-Fi relays their Readings to a self-hosted **Server**.
- When a Lot's soil gets too dry, the gardener gets a push notification at a time of day they chose: "**Tomatoes needs water**".
- The product's one job is the decision **water now, or not**, grounded in measured soil rather than weather.
- It is equally honest about its own failures. A silent or low-battery sensor raises a **Health Alert**, because **silence must never look like "all fine"**.
- It runs on the user's own home network. It is open source, built by a maker for makers and gardeners.
- Soil moisture values are **approximate** (a cheap probe with two-point calibration), so the UI should not suggest false precision.

## People
- **Simon (Owner):** built the hardware and set everything up, and checks the garden status over morning coffee.
- **The neighbour (Member):** invited to watch the garden during a holiday. Gets the same Alerts, can see everything, and cannot change settings. Read-only UI states matter.
- An **Administrator** Role sits between them: it manages Devices, Thresholds, Calibration and Pause, but not Memberships.

## Vocabulary (use these words in the UI)
Site (a garden) · Lot (a bed) · Node (sensor device) · Hub (gateway) · Sensor · Reading · Threshold (low/high) · Alert (Threshold Alert, Health Alert) · Reminder · Notification Window · Silence Window · Pause · Calibration.

## Key screens to design
1. **Site overview (home):**
   - Every Lot with exactly one status: **needs water**, **OK**, **unknown** (Node silent, with "silent for 6 h"), **paused**, or **no Node**.
   - Lots that need water are listed first.
   - Latest moisture per Lot, and the last-updated time.
2. **Lot detail:**
   - Latest Reading of each Sensor (soil moisture %, temperature, humidity, air-quality gas resistance in Ω).
   - A **30-day history chart** with the low/high Threshold band.
   - The Node's battery %, charging / not charging, and last-seen time.
3. **Alerts:**
   - Open Alerts, and how a closed one looks.
   - Threshold Alert ("Tomatoes needs water", or "too wet") versus Health Alerts: "Node on Lot 'Beans' silent for 6 h", "Node battery 14 %, not charging", "Soil sensor on Lot 'Peppers' needs calibration".
4. **Push notifications (lock screen):**
   - A single Alert.
   - A **morning summary** that bundles everything held overnight into one notification, with one line per open Alert.
5. **Server unreachable / stale data:**
   - The app is away from the home network, or the Server is down.
   - It must say so clearly and show how old the displayed data is. A stale Reading must never look current.
6. **Add a Hub (Bluetooth setup flow, mobile only):**
   1. Scan for nearby Hubs.
   2. Enter the Hub's **setup code** (a proof-of-possession code the builder sees at first boot).
   3. Pick the Wi-Fi network and enter its password.
   4. Choose the Site.
   5. Progress, then success ("Hub is online") or errors (wrong password, unsupported WPA3-only network).
7. **Add a Node (Bluetooth, mobile only):**
   1. "Press the setup button on the Node".
   2. A list of Nodes in range, with enough identity to tell which physical one it is.
   3. Enter the setup code.
   4. Assign to a Lot (a Lot that already has a Node cannot take another).
   5. The Node appears with its Sensors.
8. **Calibrate soil moisture:** a two-step flow. Hold the probe in dry soil → record *dry*. Put it in water → record *wet*. Then a confirmation. Until it is done the Sensor shows "needs calibration".
9. **Thresholds:** the low Threshold is required and the high Threshold is optional (it can be left empty). Low must stay below high. Values are in % for calibrated soil sensors.
10. **Pause:** pause a Device or the whole Site (winter, maintenance), with an optional end date. Show what a paused Site looks like on the overview.
11. **My notifications (per user):**
    - Notification Window ("from 07:00", default 07:00–22:00) with the detected **time zone**, which the user can confirm or change.
    - Mute this Site on/off.
    - Reminder cadence (default once a day).
12. **Members:** invite someone by email with a Role; change or remove a Role. A Site always keeps at least one Owner.
13. **First run:** sign in (through an external identity provider page), create your first Site, and an empty state before any Hub or Node exists.

## Tone
- Calm, trustworthy and honest.
- It tells you when to act and when something is broken, and otherwise stays out of the way.
- Never alarmist, never "gamified".
- The notification counter-metric matters: at most about one notification per Lot per day.

## Please vary between the three directions
- Overall visual language and mood.
- How the status of a Lot is communicated at a glance.
- How "unknown / silent" and "stale data" are made unmistakable without being scary.
- Density of the overview (few beds vs 10+ beds).
- *(optional)* Show each direction in light and dark.
- *(optional)* Keep iOS and Android close to their native look (SwiftUI / Material 3); the web app is SvelteKit.

## Out of scope (don't design)
Watering or valve controls, weather forecasts, "mark as watered" or "snooze" buttons, marketplace or account-billing screens, remote access from outside the home network.
