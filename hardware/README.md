# hardware

The reference hardware design for the Coldframe Node and Hub: schematics, a bill of materials, the
wiring, and notes on the enclosure and on sealing the soil probe.

> **License.** Everything in `hardware/` is part of Coldframe and is licensed under the
> [Apache License 2.0](../LICENSE), like the rest of the repository.

## Status: reference design, not yet bench-verified

This is a **reference design**. It has not been built and measured on the bench yet.

- Every part is a *reference choice*. Each BOM line also gives the **electrical requirement** the
  part must meet, so you can use a different part that meets it.
- The schematics are Markdown connection tables and ASCII block diagrams, not an EDA project. You
  can review them line by line against the firmware. A KiCad capture and PCB can follow once the
  design has been verified on the bench.
- The Node's sleep current has **not been measured** yet. The budget is an average of about
  100 µA or less, and [`docs/bench/node-power-checklist.md`](../docs/bench/node-power-checklist.md)
  is how to measure it.

The firmware is the source of truth for every pin. If a page here disagrees with
[`apps/rs/node/src/board/pins.rs`](../apps/rs/node/src/board/pins.rs), the firmware is right and the
page has a bug.

## Pages

| Page | What |
| --- | --- |
| [`bom.md`](bom.md) | The bill of materials for the Node and the Hub, with the requirement for each part |
| [`node.md`](node.md) | The Node: block diagram, connection tables per block, and the full pin-to-pin wiring |
| [`hub.md`](hub.md) | The Hub: a dev board on USB power, antenna clearance and placement |
| [`enclosure.md`](enclosure.md) | The Node enclosure (IP rating, venting, glands) and sealing the soil probe |

## Building one

Follow the adopter build guide in [`docs/build/`](../docs/build/README.md). It goes from parts to
assembly, the toolchain, flashing the Hub and the Node, and adding both from the app. The power
guidance (panel placement, the sleep-current budget, and why soil % and battery % are approximate)
is in [`docs/build/power.md`](../docs/build/power.md).
