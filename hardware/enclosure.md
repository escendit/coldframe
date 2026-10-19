# Enclosure and probe sealing

The Node lives outdoors for a whole season, with its probe in wet soil. The Hub lives indoors and
needs only a plastic case (see [hub.md](hub.md#antenna-clearance)).

> **License.** This design is part of Coldframe and is licensed under the
> [Apache License 2.0](../LICENSE).

> **Reference design, not yet bench-verified.** These are the reference choices; none of them has
> been through a season outdoors yet.

## The outdoor requirement (NFR-5)

A Node, its solar panel and its soil probe survive a season outdoors in wet soil, **without
corrosion-driven drift making Alerts unreliable**. Everything below serves that: keep water out of
the electronics, and keep the probe's reading stable over months.

## Node enclosure

- **IP rating:** IP65 or better (dust-tight, protected against water jets). An IP67 box tolerates
  standing water, for example when the Node falls over. ABS or polycarbonate with UV stabilisation;
  plain ABS goes brittle in sun.
- **Colour:** light grey or white. A dark box in full sun heats up, and the charger must stop
  charging above about 45 °C anyway.
- **Venting for the BME680:** the BME680 measures air temperature, humidity and gas resistance, so
  it needs outside air. Fit a breathable **ePTFE vent membrane** (an M12 vent plug) and mount the
  BME680 right behind it, away from the LDO and the module, which warm up a little. A sealed box
  with no vent reads its own air and collects condensation.
- **Cable glands:** one gland per cable: the probe cable, and the panel cable if the panel is
  separate. Size the gland to the cable (for example M12 / PG7 for 3–6 mm cables). Leave a drip
  loop below each gland so water runs off before it reaches it. Glands on the bottom face are
  safest.
- **Button:** an IP67 panel-mount push button for the setup button
  ([BOM N12](bom.md#node)), so you can press it without opening the box.
- **Mounting:** on a stake or post, with the box's glands facing down.
- **Panel and antenna clear:** mount the panel **above the canopy**, angled to the sun and not
  shaded by the plant as it grows ([power.md](../docs/build/power.md#panel-placement)). Keep the
  module's antenna end free of metal (no metal box, no metal mounting plate behind the antenna),
  and keep wet foliage away from it.
- **Inside:** a small silica-gel bag absorbs the moisture trapped when you close the box. Replace
  it when you open the box.

## Sealing the soil probe

Cheap capacitive probes have bare electronics at the top and **unsealed cut edges** along the
board. Water creeps into the board's edges and under the components, and the reading drifts as the
board absorbs it. Seal every probe before its first use:

1. **Mark the insertion line.** The probe has a line (or a printed limit) above which it must not
   go into the soil. Mark it with a pen. Only the part below the line goes into the soil; the
   electronics stay above it.
2. **Coat the cut edges.** Paint every cut edge of the board, along its full length and around the
   tip, with conformal coating or a thin layer of epoxy. The flat sensing faces are already covered
   by solder mask; coat the edges so water cannot creep in.
3. **Pot the electronics.** Cover the components at the top (the timer chip, the regulator, the
   connector) with conformal coating, or pot them in epoxy. Let it cure fully.
4. **Heat-shrink.** Slide adhesive-lined heat-shrink over the top of the probe, from the cable down
   to just above the insertion line, and shrink it. It covers the electronics and the cable entry.
5. **Cable.** Use an outdoor-rated cable from the probe to the Node, and run it through a cable
   gland.

A sealed probe still needs **2–3 days to settle** after you insert it, and a calibration in your
soil; see [power.md](../docs/build/power.md#soil-moisture) for why soil % is approximate.

Inspect the probe at the end of the season. Cracked coating, a lifted heat-shrink or a corroded
edge means the Readings of the last weeks may have drifted.
