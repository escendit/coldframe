# Build guide

How to build your own Coldframe Node and Hub from this repository, flash them, and add them to your
garden. Work through the steps in order.

The hardware is a reference design that has not been bench-verified yet; see
[`hardware/README.md`](../../hardware/README.md) for its status and license.

## 1. Get the parts

[`hardware/bom.md`](../../hardware/bom.md) lists every part for one Node and one Hub, with an
example part and the requirement a substitute must meet. One Hub serves a garden; add as many Nodes
as you like.

## 2. Assemble

- **Node:** [`hardware/node.md`](../../hardware/node.md): the schematic, every connection, and the
  full pin-to-pin wiring. Then the enclosure and the probe sealing:
  [`hardware/enclosure.md`](../../hardware/enclosure.md).
- **Hub:** [`hardware/hub.md`](../../hardware/hub.md): a dev board on USB power. Nothing to wire.
- **Placement and power:** [power.md](power.md): where the panel goes, the sleep-current budget and
  how to measure it, and why soil % and battery % are approximate.

## 3. Install the toolchain

[flash.md: Toolchain](flash.md#toolchain): `espflash`, and for building from source, `espup` and the
esp Rust toolchain at the versions CI pins.

## 4. Flash the Hub

[flash.md](flash.md): from the release binaries or from source. **Read the eFuse warning at the top
first**: the first boot of a build without dev mode burns the chip's identity, and that cannot be
undone. Note the Hub's setup code from its serial output.

## 5. Flash the Node

The same page, for the Node. Its setup code appears when you long-press its button (3 s).

## 6. Add them from the app

Build the iOS or Android app for your own Server: [apps.md](apps.md). The Server must be running
first: the [deploy guide](../deploy/README.md) sets it up on your home server (the developer
[quickstart](../quickstart.md) runs one locally). Then, in the app:

1. **Add a Hub.** The app finds the unprovisioned Hub over BLE, asks for its setup code, and sends it
   your Wi-Fi network (2.4 GHz, WPA2 or mixed WPA2/WPA3) and your Server's address.
2. **Add a Node.** Long-press the Node's setup button (3 s), pick the Lot it measures, and enter
   its setup code within the 180 s setup window.
3. Calibrate the soil probe once it has settled in the soil for 2–3 days
   ([why](power.md#soil-moisture)).

Reproducing Coldframe as someone other than its author? Follow the
[reproduction checklist](../reproduce/README.md), which walks this guide and the deploy guide end
to end.

## Checking this guide

CI checks these pages on every change (`.github/workflows/docs.yml`):

- `docs/check-links.sh` fails on any broken relative link or referenced repository path.
- `docs/check-commands.sh` runs every command block marked `<!-- ci:run <dir> -->` against the
  firmware crates (builds only, never flashing), and for blocks marked `<!-- ci:hardware -->` checks
  only that their `espflash` subcommands exist. A command block without a marker fails.
