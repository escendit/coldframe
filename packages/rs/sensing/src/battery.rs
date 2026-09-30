//! Battery level: the switched divider and the LiPo discharge curve.
//!
//! The cell voltage is read by ADC through a divider that is switched on only while it is read
//! (no sleep draw). The percentage comes from a typical single-cell LiPo discharge curve with
//! linear interpolation. It is always approximate, and more so while charging.

/// A resistor divider from the cell to the ADC input: `top` from the cell, `bottom` to ground.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Divider {
    /// Resistance between the cell and the ADC input, in ohms.
    pub top_ohms: u32,
    /// Resistance between the ADC input and ground, in ohms.
    pub bottom_ohms: u32,
}

impl Divider {
    /// The cell voltage for `adc_millivolts` at the divider's midpoint, rounded, saturating at
    /// `u16::MAX`. A divider with no bottom resistor passes the reading through.
    #[must_use]
    pub fn cell_millivolts(&self, adc_millivolts: u16) -> u16 {
        if self.bottom_ohms == 0 {
            return adc_millivolts;
        }
        let bottom = u64::from(self.bottom_ohms);
        let total = u64::from(self.top_ohms) + bottom;
        let scaled = (u64::from(adc_millivolts) * total * 2 + bottom) / (2 * bottom);
        u16::try_from(scaled).unwrap_or(u16::MAX)
    }
}

/// The discharge curve: `(cell millivolts, percent)`, from full to empty.
pub const DISCHARGE_CURVE: [(u16, u8); 21] = [
    (4200, 100),
    (4150, 95),
    (4110, 90),
    (4080, 85),
    (4020, 80),
    (3980, 75),
    (3950, 70),
    (3910, 65),
    (3870, 60),
    (3850, 55),
    (3840, 50),
    (3820, 45),
    (3800, 40),
    (3790, 35),
    (3770, 30),
    (3750, 25),
    (3730, 20),
    (3710, 15),
    (3690, 10),
    (3610, 5),
    (3270, 0),
];

/// The percentage for a cell voltage: linear between curve points, rounded to the nearest
/// percent, 100 at or above the top and 0 at or below the bottom.
#[must_use]
pub fn percent_for(cell_millivolts: u16) -> u8 {
    let (full_millivolts, full_percent) = DISCHARGE_CURVE[0];
    if cell_millivolts >= full_millivolts {
        return full_percent;
    }
    for pair in DISCHARGE_CURVE.windows(2) {
        let (high_millivolts, high_percent) = pair[0];
        let (low_millivolts, low_percent) = pair[1];
        if cell_millivolts > low_millivolts {
            // low < cell < high: interpolate, rounding half up.
            let above = u32::from(cell_millivolts - low_millivolts);
            let span = u32::from(high_millivolts - low_millivolts);
            let step = u32::from(high_percent - low_percent);
            let extra = (2 * above * step + span) / (2 * span);
            // `extra` <= `step`, so the sum stays within the curve's percentages.
            return low_percent + u8::try_from(extra).unwrap_or(0);
        }
        if cell_millivolts == low_millivolts {
            return low_percent;
        }
    }
    0
}

/// The battery as reported on each wake.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct BatteryLevel {
    /// The cell voltage in millivolts.
    pub cell_millivolts: u16,
    /// State of charge from the discharge curve, 0–100.
    pub percent: u8,
    /// Always `true`: a voltage curve only approximates the state of charge.
    pub approximate: bool,
}

impl BatteryLevel {
    /// The level for a cell voltage.
    #[must_use]
    pub fn from_cell_millivolts(cell_millivolts: u16) -> Self {
        Self {
            cell_millivolts,
            percent: percent_for(cell_millivolts),
            approximate: true,
        }
    }
}
