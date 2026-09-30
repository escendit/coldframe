//! The battery: divider scaling and the LiPo discharge curve.

use coldframe_sensing::battery::{DISCHARGE_CURVE, percent_for};
use coldframe_sensing::{BatteryLevel, Divider};

const EQUAL: Divider = Divider {
    top_ohms: 100_000,
    bottom_ohms: 100_000,
};

#[test]
fn an_equal_divider_doubles_the_adc_voltage() {
    assert_eq!(EQUAL.cell_millivolts(1_900), 3_800);
    assert_eq!(EQUAL.cell_millivolts(0), 0);
}

#[test]
fn the_divider_rounds_to_the_nearest_millivolt() {
    let divider = Divider {
        top_ohms: 100_000,
        bottom_ohms: 300_000,
    };
    // 1000 × 4/3 = 1333.33…
    assert_eq!(divider.cell_millivolts(1_000), 1_333);
    // 1001 × 4/3 = 1334.67 → 1335
    assert_eq!(divider.cell_millivolts(1_001), 1_335);
}

#[test]
fn the_divider_saturates_instead_of_wrapping() {
    assert_eq!(EQUAL.cell_millivolts(u16::MAX), u16::MAX);
}

#[test]
fn a_divider_without_a_bottom_resistor_passes_the_reading_through() {
    let divider = Divider {
        top_ohms: 0,
        bottom_ohms: 0,
    };
    assert_eq!(divider.cell_millivolts(3_700), 3_700);
}

#[test]
fn every_curve_point_maps_to_its_own_percent() {
    for (millivolts, percent) in DISCHARGE_CURVE {
        assert_eq!(percent_for(millivolts), percent, "at {millivolts} mV");
    }
}

#[test]
fn the_curve_is_strictly_decreasing() {
    for pair in DISCHARGE_CURVE.windows(2) {
        assert!(pair[0].0 > pair[1].0 && pair[0].1 > pair[1].1);
    }
}

#[test]
fn the_ends_clamp_to_zero_and_a_hundred() {
    assert_eq!(percent_for(4_200), 100);
    assert_eq!(percent_for(4_350), 100);
    assert_eq!(percent_for(u16::MAX), 100);
    assert_eq!(percent_for(3_270), 0);
    assert_eq!(percent_for(3_000), 0);
    assert_eq!(percent_for(0), 0);
}

#[test]
fn between_points_the_percent_is_interpolated_linearly() {
    // 3610 → 5 and 3690 → 10: 3650 is halfway, 7.5 rounds to 8.
    assert_eq!(percent_for(3_650), 8);
    // 3270 → 0 and 3610 → 5: 3440 is halfway, 2.5 rounds to 3.
    assert_eq!(percent_for(3_440), 3);
    // 4020 → 80 and 4080 → 85: 4050 is halfway, 82.5 rounds to 83.
    assert_eq!(percent_for(4_050), 83);
    // 4150 → 95 and 4200 → 100: 4160 is a fifth of the way, 96.
    assert_eq!(percent_for(4_160), 96);
    // 3840 → 50 and 3850 → 55: 3841 gives 50.5, rounds to 51.
    assert_eq!(percent_for(3_841), 51);
    // 3270 → 0 and 3610 → 5: 3271 gives 0.0147, rounds to 0.
    assert_eq!(percent_for(3_271), 0);
}

#[test]
fn the_percent_never_decreases_with_voltage() {
    let mut last = 0;
    for millivolts in 3_000..=4_300 {
        let percent = percent_for(millivolts);
        assert!(percent >= last, "at {millivolts} mV");
        assert!(percent <= 100);
        last = percent;
    }
}

#[test]
fn a_level_is_always_approximate() {
    let level = BatteryLevel::from_cell_millivolts(3_870);
    assert_eq!(
        level,
        BatteryLevel {
            cell_millivolts: 3_870,
            percent: 60,
            approximate: true,
        }
    );
    assert!(BatteryLevel::from_cell_millivolts(4_300).approximate);
    assert!(BatteryLevel::from_cell_millivolts(0).approximate);
}
