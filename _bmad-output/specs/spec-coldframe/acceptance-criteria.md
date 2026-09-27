# Acceptance criteria per capability

Line-item criteria that the SPEC kernel's `success` fields summarize. CAP-N matches PRD FR-N. The architecture spine AD-n IDs are referenced where the *how* is fixed.

## CAP-1: Provision a Hub over BLE
- The Hub joins Wi-Fi only through BLE provisioning. Firmware contains no Wi-Fi credentials.
- After provisioning, the Hub appears on the chosen Site within one minute.
- Credentials are exchanged over the Coldframe BLE setup protocol. The session is encrypted and bound to the Device's proof-of-possession code, so a wrong code fails the session (AD-25). AD-12 enrolment happens in the same session.

## CAP-2: Pair a Node and assign it
- A new Node is *unassigned*.
- A Node is visible over BLE only after its setup button is pressed, and only for a limited time. Otherwise it does not advertise, to save energy.
- The app shows only Nodes within BLE range, so the Administrator knows which physical Node is being assigned.
- Node setup uses the same BLE setup protocol and proof-of-possession check as the Hub (AD-25).
- An unassigned Node's Readings are stored but not evaluated for Alerts (AD-8).
- A Lot that already has a Node cannot take a second one. The existing Node must be moved or unassigned first (AD-18).
- Reassigning a Node to another Lot keeps its Reading history.

## CAP-3: Declare Sensor Specifications
- The Server and apps show Calibration controls only for Sensors with `calibration: true`.
- A Sensor whose Specification defines default Thresholds alerts without further setup. In V1 that is soil moisture only.
- Temperature, humidity, and air quality are only watched until an Owner or Administrator sets Thresholds.
- The air-quality Sensor reports gas resistance (Ω). Lower resistance means more volatile compounds, so its Alert uses the low Threshold like any other Sensor.
- Redeclaring a Specification never overwrites an Owner/Administrator Threshold override (AD-19).

## CAP-4: Report Readings
- One Reading per Sensor every 15 minutes.
- Every battery-powered Device also reports battery level (%) and charging status (*charging* / *not charging*).
- Readings keep reaching the Server after the router changes its Wi-Fi channel, without re-provisioning.
- Readings carry the Node's measurement time, not the Server's arrival time. All Readings from one wake share the same measurement time.
- The Hub is a pass-through relay and stores no Readings.
- If the Server does not acknowledge a Reading, the Node keeps it and resends it once the Server is reachable. A Node buffers at least 24 h of Readings. Acknowledgement happens only after a durable commit (AD-9, AD-17).

## CAP-5: Sign-in via OpenID Connect
- The identity provider (Keycloak) owns accounts, passwords, and registration. The Server stores no passwords.
- Sites, Memberships, and Roles are held in the identity provider (Phase Two Organizations). The Server keeps a projection of them and enforces CAP-6 and CAP-7 rules (AD-3, AD-4).

## CAP-6: Manage Sites and Lots
- Any User can create a Site and becomes its first Owner.
- An Owner can rename the Site.
- An Administrator can create, rename, and remove Lots.
- Removing a Lot requires first moving or unassigning its Node.

## CAP-7: Manage Memberships
- An Owner can invite a person to a Site with a Role by email, change the Role, and remove the Membership.
- The invitee receives an email with a link, signs in or registers with the identity provider, and gets the Membership on acceptance.
- Accepting requires being on the home network in V1.
- Every Site keeps at least one Owner.
- A Member sees data and Alerts but cannot change Devices, Thresholds, Calibration, Pause, or Memberships. The API rejects such changes with an authorization error.
- Roles are enforced per Site: Administrator on Site A grants nothing on Site B.

## CAP-8: View Readings
- The latest Reading of every Sensor on a Site, grouped by Lot, plus a 30-day history chart.
- Each Lot shows exactly one status, computed once on the Server (AD-14):
  - *needs water*
  - *needs calibration*: its soil-moisture Sensor has no Calibration yet (see CAP-21)
  - *OK*
  - *unknown*: Node silent, with the time since its last Reading
  - *paused*
  - *no Node*
- A stale Reading is never shown as the current state.
- The Site overview lists Lots that need water first.
- Each Device shows its last-seen time. Battery-powered Devices also show battery level and charging status.
- When the apps cannot reach the Server, they say so and show how old the displayed data is.
- Readings are retained indefinitely.

## CAP-9: Calibrate a Sensor
- Two reference points: *dry* (probe in dry soil) and *wet* (probe in water). Each point is the raw value of a stored Reading, submitted over REST and never over BLE (AD-9). A short press of the Node's setup button triggers an immediate Reading (device-hardware N-3).
- Thresholds of a `calibration: true` Sensor are always in normalized units (0–100 %). Its Readings show in the same units once calibrated.
- Until it is calibrated, such a Sensor opens no Threshold Alerts (see CAP-21).
- Recalibrating applies to new Readings and leaves the Thresholds' % values unchanged. History keeps the Calibration it was recorded with.

## CAP-10: Set Thresholds
- Only an Owner or Administrator can set, change, or remove Thresholds, which turns alerts on for watched Sensors.
- On an alerting Sensor the low Threshold is required. The high Threshold may be empty, and an empty high Threshold never opens an Alert.
- When both are set, low must be below high. The Server rejects anything else.
- A new Sensor starts with the Specification's default low and high Thresholds, or none.
- Turning on alerts without a Specification default proposes low = Min + 20 % × (Max − Min) of the Sensor's range. The high Threshold has no fallback.
- An Owner or Administrator can clear a default high Threshold or set one where none exists.

## CAP-11: Open and close Threshold Alerts
- Opens after **three consecutive Readings** below low or above high. Closes after **three consecutive Readings** back within the Thresholds.
- One or two Readings on the other side neither open nor close an Alert.
- At the 15-minute interval, an Alert opens about 30 minutes after the first crossing Reading.
- The Alert names the side crossed. For soil moisture low it reads "‹Lot› needs water".
- A closed Alert sends no further Reminders.

## CAP-12: Remind while open
- The cadence resolves as: the User's own setting for the Site, else the Site setting, else once per day.
- An Owner or Administrator sets the Site cadence. Every User may set their own cadence per Site.

## CAP-13: Silent Device Alert
- Applies to Nodes and Hubs. A silent Hub is reported as the Hub, not as every Node behind it.
- Default Silence Window: **6 h for a Node**, **5 min for a Hub**. An Administrator can change it per Device.
- A Hub sends a heartbeat every 30–60 s even when no Node has reported.
- Closes when the Device reports again. Server downtime never counts as Device silence (AD-6).
- Health Alerts remind at most once per day.

## CAP-14: Low-battery Alert
- Opens after **three consecutive reports** below 20 % while *not charging*. Closes after **three consecutive reports** that are *charging* or ≥ 20 %.
- Below 20 % while *charging* raises no Alert. *Not charging* above 20 % is never an Alert on its own.
- The Alert shows battery level and charging status.

## CAP-15: Push notifications
- Delivered to the iOS and Android apps of every User with a Membership on the Site.
- Notifications reach the phone away from home. Opening the app for details requires the home network in V1.
- Payloads are self-contained: Lot, Sensor or Device, and condition in plain words.

## CAP-16: Notification Window
- Default 07:00–22:00. The simplest form is "from 07:00".
- Evaluated in the User's time zone, including across daylight-saving changes. The zone is detected from the phone OS or the browser, with IP as a last resort. The User confirms it or picks another (AD-11).
- Threshold Alerts, Health Alerts, and Reminders falling due outside the window are pushed as one summary when it opens.
- An Alert that opened and closed while held is not delivered.
- A summary has at most one entry per open Alert, however many Reminders fell due for it.

## CAP-17: Mute a Site
- Per User, per Site, on/off.
- Other Users keep receiving Alerts. Alerts still open and close as normal.

## CAP-18: Pause a Device or a Site
- While paused, a Device's Readings are not ingested and no Alerts open for it. Its open Alerts close.
- An optional end date resumes the Device automatically. Without one, the Pause lasts until an Administrator resumes it.
- On resume, the Silence Window restarts from the moment of resume.
- Devices added to a paused Site start paused.
- A Device paused both individually and by its Site resumes only when both are lifted (AD-8).

## CAP-19: Mobile apps
- iOS and Android both support:
  - BLE provisioning, and BLE identification and assignment of Nodes
  - viewing Readings
  - Thresholds and Calibration
  - Pause
  - Notification Window and Reminder settings
  - Membership management
  - receiving notifications

## CAP-20: Web app
- Everything the mobile apps offer except Bluetooth functions (Hub provisioning, Node identification).
- Browser notifications while the web app is open follow the same Notification Window and mute settings.
- No background web push when no browser has the app open.

## CAP-21: Uncalibrated Sensor Alert
- Opens when a Node is assigned to a Lot and one of its `calibration: true` Sensors has no Calibration.
- Closes once that Sensor is calibrated.
- The notification names the Lot and the Sensor and asks an Owner or Administrator to calibrate it.
