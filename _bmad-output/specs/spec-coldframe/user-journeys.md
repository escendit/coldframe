# Users and journeys

## Jobs to be done
- **Functional:** tell me which Lot needs water today, early enough to water it, without checking by hand.
- **Trust:** tell me when a Node, Hub, or Sensor setup is broken, so that no news really means good news.
- **Control:** let me choose when I am bothered, and let me pause the system for maintenance or the off-season without false alarms.
- **Ownership:** run it on my own hardware and network, with no vendor cloud and no lock-in.
- **Builder:** give me, and other makers, a real end-to-end Rust-on-ESP32 system to learn from and reproduce.

## Non-users (V1)
- Gardeners who cannot or will not build hardware, flash firmware, and run a single-node Kubernetes cluster with an identity provider. There is no hosted service and no pre-built hardware.
- Commercial growers and farms that need field-scale coverage, compliance, or automated irrigation.

## Journeys
- **UJ-1. Simon sets up the garden** (CAP-1, CAP-2, CAP-5, CAP-6, CAP-9, CAP-16).
  1. On the home Wi-Fi, Simon signs in for the first time, creates the Site "Home", and becomes its Owner.
  2. In the mobile app, Simon provisions the Hub over BLE.
  3. Simon powers a Node and assigns it to the Lot "Tomatoes". The Node appears with its Sensors.
  4. Simon calibrates its soil-moisture Sensor (dry and wet) and sets a Notification Window of "from 07:00".
- **UJ-2. A hot week, a morning nudge** (CAP-11, CAP-12, CAP-16).
  1. The "Tomatoes" soil moisture drops below its low Threshold at 02:40.
  2. At 07:00 Simon's phone shows "Tomatoes needs water".
  3. Simon waters before work. The next Readings recover above the low Threshold, the Alert clears, and no Reminder follows.
  - *Edge case:* if nobody waters, a Reminder arrives in the next morning's summary.
- **UJ-3. A Node dies quietly** (CAP-13, CAP-16). The Node at the far end of the garden stops reporting overnight. When Simon's Notification Window opens, the phone shows "Node on Lot 'Beans' silent for 6 h", instead of a false sense that the beans are fine.
- **UJ-4. Off for the winter** (CAP-18). In October Simon pauses the whole Site. No Readings are ingested and no Health Alerts fire for the Nodes brought indoors.
- **UJ-5. A second pair of eyes** (CAP-7, CAP-15, CAP-16).
  1. Before a holiday, Simon invites a neighbor by email as a Member of the Site.
  2. The neighbor accepts on Simon's Wi-Fi.
  3. The neighbor receives the same Alerts in their own Notification Window but cannot change Thresholds.
