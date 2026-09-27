# Glossary: Coldframe

These terms are used verbatim in code, API, and UI. Use `Lot`, not `Bed`; use `Reading`, not `Measurement`.

- **User:** a person with an account on a Server. Has one Notification Window, one IANA time zone, and personal Alert settings.
- **Site:** a garden, named by its Owner. A Server hosts one or more Sites.
- **Lot:** a named area on a Site (a bed). A Site has many Lots. A Lot has at most one Node, and a Node sits on exactly one Lot. A Hub belongs to a Site, not to a Lot.
- **Membership:** the link User × Site × exactly one Role. A User may hold Memberships on several Sites with different Roles.
- **Role:** Owner ⊃ Administrator ⊃ Member. Each includes the privileges of the ones below it.
  - **Owner:** highest authority on a Site; manages Memberships.
  - **Administrator:** assigns Devices, sets Thresholds, performs Calibration, pauses Devices and the Site, manages Lots.
  - **Member:** reads data and receives Alerts.
- **Device:** a Node or a Hub, bound to one Site. A Node is also bound to one Lot. Only Nodes carry Sensors.
  - **Node:** a battery-powered, solar-charged Device in the garden that measures and reports Readings.
  - **Hub:** a mains-powered Device on the home Wi-Fi that relays Readings from Nodes to the Server without storing them. Has no Sensors.
- **Sensor:** one measuring element on a Node, described by a Sensor Specification.
- **Sensor Specification:** declared by the Device. It gives:
  - the measured quantity, unit and range;
  - optional default low and high Thresholds;
  - `calibration: true|false`, with the Calibration method (V1: two-point dry/wet).

  Digital Sensors declare `calibration: false`.
- **Calibration:** a per-Sensor mapping from raw value to normalized value (0–100 %), for Sensors whose Specification says `calibration: true`.
- **Reading:** one timestamped value from one Sensor, stamped with the Node's measurement time.
- **Threshold:** an alerting Sensor has a required **low Threshold** and an optional **high Threshold**. A Reading below the low one or above the high one is alert-worthy. Defaults come from the Sensor Specification, and an Owner or Administrator can override them per Sensor. A Sensor without Thresholds is only watched (recorded and shown), never alerted on.
- **Alert:** a condition the Server tells Users about. There are two kinds:
  - **Threshold Alert:** a Sensor's Readings went below its low or above its high Threshold. For soil moisture below low, it reads "‹Lot› needs water".
  - **Health Alert:** a Device is silent beyond its Silence Window, its battery is low, or one of its Sensors needs Calibration.
- **Reminder:** a repeated notification for a still-open Alert. The cadence resolves in order: the User's own setting for the Site, then the Site setting, then the default (once per day). Health Alerts remind at most once per day.
- **Notification Window:** a User's daily from/to range, evaluated in the User's time zone. Notifications are delivered only inside it; anything falling due outside it is delivered in bulk as one summary when it opens.
- **Silence Window:** how long a Device may go without reporting before a Health Alert opens.
- **Pause:** a Device state in which Readings are not ingested and no Alerts open. Pausing a Site pauses all its Devices.
- **Server:** the self-hosted backend. It covers ingestion, Alert evaluation, notifications, the API, and the web app.
