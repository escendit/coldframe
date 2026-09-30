//! The BME680 conversion: driver floats to the integer units every Reading carries.

use coldframe_hal::{EnvError, EnvSample};
use coldframe_sensing::env::env_sample_from;

#[test]
fn known_values_round_to_milli_units() {
    assert_eq!(
        env_sample_from(21.5, 55.25, Some(120_000.4)),
        Ok(EnvSample {
            temperature_milli_c: 21_500,
            humidity_milli_pct: 55_250,
            gas_ohms: Some(120_000),
        })
    );
}

#[test]
fn negative_temperatures_round_half_away_from_zero() {
    let sample = env_sample_from(-3.4996, 40.0, None).unwrap();
    assert_eq!(sample.temperature_milli_c, -3_500);
    let sample = env_sample_from(-0.0004, 40.0, None).unwrap();
    assert_eq!(sample.temperature_milli_c, 0);
}

#[test]
fn gas_rounds_to_the_nearest_ohm() {
    let sample = env_sample_from(20.0, 50.0, Some(99.5)).unwrap();
    assert_eq!(sample.gas_ohms, Some(100));
    let sample = env_sample_from(20.0, 50.0, Some(99.4)).unwrap();
    assert_eq!(sample.gas_ohms, Some(99));
}

#[test]
fn humidity_is_clamped_to_zero_through_one_hundred_percent() {
    let sample = env_sample_from(20.0, 101.2, None).unwrap();
    assert_eq!(sample.humidity_milli_pct, 100_000);
    let sample = env_sample_from(20.0, -0.7, None).unwrap();
    assert_eq!(sample.humidity_milli_pct, 0);
}

#[test]
fn a_missing_nan_infinite_or_negative_gas_value_is_none() {
    for gas in [None, Some(f32::NAN), Some(f32::INFINITY), Some(-1.0)] {
        let sample = env_sample_from(20.0, 50.0, gas).unwrap();
        assert_eq!(sample.gas_ohms, None, "gas {gas:?}");
        assert_eq!(sample.temperature_milli_c, 20_000);
        assert_eq!(sample.humidity_milli_pct, 50_000);
    }
}

#[test]
fn a_huge_gas_value_saturates() {
    let sample = env_sample_from(20.0, 50.0, Some(1.0e12)).unwrap();
    assert_eq!(sample.gas_ohms, Some(u32::MAX));
}

#[test]
fn a_non_finite_temperature_or_humidity_is_out_of_range() {
    for (temperature, humidity) in [
        (f32::NAN, 50.0),
        (f32::INFINITY, 50.0),
        (f32::NEG_INFINITY, 50.0),
        (20.0, f32::NAN),
        (20.0, f32::INFINITY),
    ] {
        assert_eq!(
            env_sample_from(temperature, humidity, Some(1_000.0)),
            Err(EnvError::OutOfRange),
            "{temperature} {humidity}"
        );
    }
}

#[test]
fn a_temperature_beyond_the_integer_range_is_out_of_range() {
    assert_eq!(
        env_sample_from(3.0e6, 50.0, None),
        Err(EnvError::OutOfRange)
    );
    assert_eq!(
        env_sample_from(-3.0e6, 50.0, None),
        Err(EnvError::OutOfRange)
    );
}
