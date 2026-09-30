//! Coldframe Node firmware (ESP32-S3).
//!
//! Story 4.1: wake, measure, deep-sleep. Every wake is a reset into `main`:
//! 1. esp-hal at 80 MHz, then the RTC watchdog armed at [`WATCHDOG_MS`] so a hung wake or a
//!    halted panic resets the chip instead of staying awake. The heap and esp-rtos. The RTC
//!    uptime at this point is the wake start.
//! 2. The wake cause: a deep-sleep timer wake, or a cold boot (anything else).
//! 3. The boot ID from `cf_boot`: a cold boot raises it, a timer wake reuses it. It comes before
//!    the identity, so once the boot-counter reservation succeeds a later failure cannot leave a
//!    power-on without its own boot ID. Residual case: if the boot-counter stage itself fails on a
//!    cold boot, the next (timer) wake reuses the previously stored boot ID.
//! 4. The identity: [`provision_efuse`] in a release build, [`provision_dev`] under `dev-mode`,
//!    exactly as the Hub does (AD-12). One log line `identity mode=… source=… device_id=…`. The
//!    radio is started only if a new root has to be drawn (first boot).
//! 5. [`run_wake`]: soil, BME680, battery and charger, with `reading_seq` reserved in `cf_seq`
//!    before any value is issued (AD-17).
//! 6. One log line with the seq range (`seq=none` when no Reading was issued) and counts. Only a
//!    `dev-mode` build logs Reading values.
//! 7. Deep sleep with an RTC timer wakeup for the rest of the 15-minute period.
//!
//! A failure of the identity or of a counter partition logs its kind and deep-sleeps one period,
//! rather than parking awake and draining the battery. Keys and roots are never logged.
//!
//! No ESP-NOW, no buffer and no clock setting yet (Story 4.4); no setup button or BLE (Story 4.2).

#![no_std]
#![no_main]

// A release Node must bind its identity to the chip. Dev mode is for debug builds only (AD-12).
#[cfg(all(feature = "dev-mode", not(debug_assertions)))]
compile_error!(
    "the `dev-mode` feature is refused in release builds: a release Node uses its eFuse identity"
);

mod board;

use coldframe_crypto::identity::IdentityError;
#[cfg(feature = "dev-mode")]
use coldframe_crypto::identity::provision_dev;
#[cfg(not(feature = "dev-mode"))]
use coldframe_crypto::identity::provision_efuse;
use coldframe_hal::Rtc as _;
use coldframe_sensing::counter::{BOOT_MAGIC, SEQ_MAGIC};
use coldframe_sensing::wake::WAKE_PERIOD_MS;
use coldframe_sensing::{
    ChargeStatus, MeasuredAt, ReservedCounter, Sensors, WakeCause, WakeOutcome, boot_id, run_wake,
};
use core::fmt::Display;
use core::ops::Range;
use embassy_executor::Spawner;
use esp_backtrace as _;
use esp_hal::clock::CpuClock;
use esp_hal::peripherals::LPWR;
use esp_hal::ram;
use esp_hal::time::Duration;
use esp_hal::timer::timg::TimerGroup;
use log::{LevelFilter, error, info, warn};

use board::flash::{BOOT_PARTITION, BoardStorage, SEQ_PARTITION};
use board::pins::DIVIDER;
use board::radio::BoardRadio;
use board::rng::BoardTrng;
use board::rtc::BoardRtc;
use board::sensors::{BoardAdc, BoardCharger, BoardEnv, BoardSwitch};
use board::sleep::{deep_sleep, reset_reason_name, wake_cause};
use board::timer::BoardTimer;

esp_bootloader_esp_idf::esp_app_desc!();

/// The firmware version.
const FIRMWARE_VERSION: &str = env!("CARGO_PKG_VERSION");

/// The longest a wake may run before the RTC watchdog resets the chip.
const WATCHDOG_MS: u64 = 30_000;

#[cfg(not(feature = "dev-mode"))]
const MODE: &str = "efuse";
#[cfg(feature = "dev-mode")]
const MODE: &str = "dev";

/// Logs why the wake stopped and deep-sleeps one period. `reason` never holds a secret.
fn fail(lpwr: LPWR<'static>, stage: &str, reason: &dyn Display) -> ! {
    error!("wake failed stage={stage} error={reason}; sleeping ms={WAKE_PERIOD_MS}");
    deep_sleep(lpwr, WAKE_PERIOD_MS)
}

fn cause_name(cause: WakeCause) -> &'static str {
    match cause {
        WakeCause::ColdBoot => "cold_boot",
        WakeCause::Timer => "timer",
    }
}

fn charging_name(status: ChargeStatus) -> &'static str {
    match status {
        ChargeStatus::Charging => "charging",
        ChargeStatus::NotCharging => "not_charging",
        ChargeStatus::Unknown => "unknown",
    }
}

/// A seq range for the log: `start..end`, or `none` when no Reading was issued.
struct SeqRange(Option<Range<u64>>);

impl Display for SeqRange {
    fn fmt(&self, f: &mut core::fmt::Formatter<'_>) -> core::fmt::Result {
        match &self.0 {
            Some(range) => write!(f, "{}..{}", range.start, range.end),
            None => f.write_str("none"),
        }
    }
}

/// Logs the wake: counts and the seq range always, values only in a `dev-mode` build.
fn log_outcome(outcome: &WakeOutcome) {
    let report = &outcome.report;
    let faults = &outcome.faults;
    if let Some(error) = faults.soil {
        warn!("soil failed error={error}");
    }
    if let Some(error) = faults.env {
        warn!("bme680 failed error={error}");
    }
    if let Some(error) = faults.battery {
        warn!("battery failed error={error}");
    }
    if let Some(error) = faults.charger {
        warn!("charger status failed error={error}");
    }
    if let Some(error) = faults.switch {
        warn!("power switch failed error={error}");
    }
    if let Some(error) = faults.counter {
        error!("reading_seq failed error={error}; no Readings issued");
    }
    let time = match report.measured_at {
        MeasuredAt::Synced { .. } => "synced",
        MeasuredAt::Unsynced { .. } => "unsynced",
    };
    let seq = SeqRange(report.seq_range());
    info!(
        "wake done readings={} seq={seq} measured_at={time} battery={} charging={} sleep_ms={}",
        report.readings.len(),
        if report.battery.is_some() {
            "ok"
        } else {
            "none"
        },
        charging_name(report.charging),
        outcome.sleep_ms
    );
    #[cfg(feature = "dev-mode")]
    {
        for reading in &report.readings {
            info!(
                "dev reading slot={} seq={} value={:?}",
                reading.slot, reading.seq, reading.value
            );
        }
        if let Some(battery) = report.battery {
            info!(
                "dev battery cell_mv={} percent={} approximate={}",
                battery.cell_millivolts, battery.percent, battery.approximate
            );
        }
        info!("dev measured_at={:?}", report.measured_at);
    }
}

#[esp_rtos::main]
async fn main(_spawner: Spawner) -> ! {
    // A fixed level: the build reads no environment.
    esp_println::logger::init_logger(LevelFilter::Info);
    // 80 MHz cuts the active current of every wake; it is also esp-radio's minimum, so a first
    // boot that draws a new identity root can still start the radio.
    let peripherals = esp_hal::init(esp_hal::Config::default().with_cpu_clock(CpuClock::_80MHz));
    let mut rtc = BoardRtc::new(peripherals.RTC_TIMER);
    rtc.arm_watchdog(Duration::from_millis(WATCHDOG_MS));
    let wake_start_ms = rtc.uptime_millis();
    let lpwr = peripherals.LPWR;

    // Heap for esp-radio, used only when a new identity root is drawn.
    esp_alloc::heap_allocator!(#[ram(reclaimed)] size: 64 * 1024);
    esp_alloc::heap_allocator!(size: 64 * 1024);

    let timg0 = TimerGroup::new(peripherals.TIMG0);
    esp_rtos::start(timg0.timer0, peripherals.FROM_CPU_INTR0);

    let cause = wake_cause();
    info!(
        "coldframe-node {FIRMWARE_VERSION} wake cause={} reset={} identity mode={MODE}",
        cause_name(cause),
        reset_reason_name()
    );

    let mut storage = BoardStorage::new(peripherals.FLASH);

    // Before the identity: once this reservation succeeds, a later failure cannot leave the
    // power-on without its own boot ID. If it fails on a cold boot, the next timer wake reuses the
    // previously stored boot ID.
    let boot = {
        let flash = match storage.partition(BOOT_PARTITION) {
            Ok(flash) => flash,
            Err(error) => fail(lpwr, "boot_partition", &error),
        };
        let mut boots = ReservedCounter::new(flash, BOOT_MAGIC);
        match boot_id(&mut boots, cause) {
            Ok(boot) => boot,
            Err(error) => fail(lpwr, "boot_counter", &error),
        }
    };
    info!("boot id={boot}");

    // The radio stays off unless the identity has to draw a new root.
    let mut radio = BoardRadio::new(peripherals.WIFI);
    let mut trng = BoardTrng;

    #[cfg(not(feature = "dev-mode"))]
    let provisioned = {
        let Some(mut efuse) = board::efuse::BoardEfuse::take() else {
            fail(lpwr, "efuse", &"eFuse adapter already taken")
        };
        let mut hmac = board::hmac::BoardHmac::new(peripherals.HMAC);
        provision_efuse(&mut radio, &mut trng, &mut efuse, &mut hmac)
    };
    #[cfg(feature = "dev-mode")]
    let provisioned = {
        let mut flash = match storage.partition(board::flash::IDENTITY_PARTITION) {
            Ok(flash) => flash,
            Err(error) => fail(lpwr, "partition", &error),
        };
        provision_dev(&mut radio, &mut trng, &mut flash)
    };
    let provisioned = match provisioned {
        Ok(provisioned) => provisioned,
        Err(error) => fail(lpwr, "provision", &error as &IdentityError),
    };
    drop(radio);
    let device_id_hex = provisioned.keys.device_id.to_hex();
    let device_id = core::str::from_utf8(&device_id_hex).unwrap_or("?");
    info!(
        "identity mode={MODE} source={} device_id={device_id}",
        provisioned.source
    );

    let seq_flash = match storage.partition(SEQ_PARTITION) {
        Ok(flash) => flash,
        Err(error) => fail(lpwr, "seq_partition", &error),
    };
    let mut seq = ReservedCounter::new(seq_flash, SEQ_MAGIC);

    let pins = crate::take_pins!(peripherals);
    let mut probe_switch = BoardSwitch::new(pins.probe_switch);
    let mut divider_switch = BoardSwitch::new(pins.divider_switch);
    let mut charger = BoardCharger::new(pins.charger);
    let adc = BoardAdc::new(peripherals.ADC1, pins.soil, pins.battery);
    let (mut env, env_error) = BoardEnv::new(peripherals.I2C0, pins.sda, pins.scl);
    if let Some(error) = env_error {
        warn!("bme680 not found error={error}");
    }
    let mut soil = adc.soil();
    let mut battery = adc.battery();
    let mut timer = BoardTimer;

    let outcome = run_wake(
        Sensors {
            probe_switch: &mut probe_switch,
            divider_switch: &mut divider_switch,
            soil: &mut soil,
            env: &mut env,
            battery: &mut battery,
            charger: &mut charger,
            divider: DIVIDER,
        },
        &rtc,
        &mut timer,
        &mut seq,
        boot,
        wake_start_ms,
    )
    .await;
    log_outcome(&outcome);

    deep_sleep(lpwr, outcome.sleep_ms)
}
