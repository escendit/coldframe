//! Coldframe Node firmware (ESP32-S3).
//!
//! Story 4.1: wake, measure, deep-sleep. Story 4.2: the setup button, BLE setup mode and
//! enrolment. Story 4.4: the report buffer and the ESP-NOW transport to a Hub. Every wake is a
//! reset into `main`:
//! 1. esp-hal at 80 MHz, then the RTC watchdog armed at [`WATCHDOG_MS`] so a hung wake or a
//!    halted panic resets the chip instead of staying awake. The heap and esp-rtos (the timer the
//!    press is timed with). The RTC uptime at this point is the wake start. The setup button
//!    (GPIO7, pull-up).
//! 2. The wake cause: the setup button, a deep-sleep timer wake, or a cold boot (anything else).
//!    On a button wake the press is timed at once ([`classify_press`]), before any flash or
//!    identity work: the press is only observable while the finger is on the button, so only the
//!    bootloader delay eats into a short press.
//! 3. The boot ID from `cf_boot`: a cold boot raises it, a button or timer wake reuses it. It
//!    comes before the identity, so once the boot-counter reservation succeeds a later failure
//!    cannot leave a power-on without its own boot ID. Residual case: if the boot-counter stage
//!    itself fails on a cold boot, the next (timer) wake reuses the previously stored boot ID.
//! 4. The identity: [`provision_efuse`] in a release build, [`provision_dev`] under `dev-mode`,
//!    exactly as the Hub does (AD-12). One log line `identity mode=… source=… device_id=…`. The
//!    radio is started only if a new root has to be drawn (first boot).
//! 5. [`wake_plan`] decides the wake from the cause and the press, logged as
//!    `wake plan=measure|report-now|setup|sleep-again`:
//!    - `measure` (timer wake, cold boot): steps 6–10;
//!    - `report-now` (short press): steps 6–10 now, then a full period;
//!    - `sleep-again` (a bounce): no measurement, a full period;
//!    - `setup` (long press): setup mode ([`setup_mode`]), then steps 6–10 without the radio (the
//!      report is buffered and goes out at the next wake) and a full period.
//! 6. The link state from `cf_link` ([`begin`]): when a downlink set the clock on this boot ID,
//!    the wall clock is restored, so the Readings are stamped with it (AD-11).
//! 7. [`run_wake`]: soil, BME680, battery and charger, with `reading_seq` reserved in `cf_seq`
//!    before any value is issued (AD-17); then [`issue_report_seq`], the `report_seq` of the wake
//!    report from the same counter.
//! 8. The transport ([`run`], Story 4.4): the report goes into the flash buffer `cf_buf`; the
//!    radio starts for ESP-NOW only (no Wi-Fi station, ever); the Node probes for a Hub, collects
//!    the late acknowledgements it kept, sends up to 8 buffered reports as sealed frames under
//!    fresh counters from `cf_frame`, and listens 300 ms. Only a sealed downlink deletes a report
//!    or sets the clock. The radio is dropped, and so off, before the sleep.
//! 9. Two log lines: `wake done …` with the seq range (`seq=none` when no Reading was issued),
//!    the counts and the sleep that follows, and `transport …`. Only a `dev-mode` build logs
//!    Reading values.
//! 10. Deep sleep for [`sleep_after`]: the rest of the 15-minute period after a scheduled wake, a
//!     full period after a press. The setup button is armed as a wakeup unless it is still held
//!     ([`arm_button_wake`]): a stuck button leaves only the timer wake. The value actually slept
//!     is logged as `sleep ms=<n> button_armed=<bool>`.
//!
//! A failure of the identity or of a counter partition logs its kind and deep-sleeps one period,
//! rather than parking awake and draining the battery. A transport failure costs nothing but the
//! send: the report stays buffered. Keys, roots, Readings and sealed payloads are never logged.
//!
//! BLE runs only in setup mode, where a `dev-mode` build also runs an ESP-NOW coexistence probe.
//! The transport is bounded. A send waits at most 200 ms for the radio's callback (normally a
//! few milliseconds). The worst case is a full scan of 13 probes (the known channel first, then
//! the others of 1 to 13), each a send and a 120 ms wait, then 120 ms for kept downlinks, 8
//! frames and the 300 ms window: about 2.1 s with normal sends, and at most
//! 13 × 320 + 120 + 8 × 200 + 300 ms = 6.2 s if every send ran into its timeout. Both are far
//! inside the 30 s watchdog.

#![no_std]
#![no_main]

// A release Node must bind its identity to the chip. Dev mode is for debug builds only (AD-12).
#[cfg(all(feature = "dev-mode", not(debug_assertions)))]
compile_error!(
    "the `dev-mode` feature is refused in release builds: a release Node uses its eFuse identity"
);

mod board;

use coldframe_crypto::DeviceKeys;
use coldframe_crypto::identity::IdentityError;
#[cfg(feature = "dev-mode")]
use coldframe_crypto::identity::provision_dev;
#[cfg(not(feature = "dev-mode"))]
use coldframe_crypto::identity::provision_efuse;
use coldframe_hal::Rtc as _;
use coldframe_sensing::counter::{BOOT_MAGIC, SEQ_MAGIC};
use coldframe_sensing::wake::WAKE_PERIOD_MS;
use coldframe_sensing::{
    ChargeStatus, MeasuredAt, ReservedCounter, Sensors, WakeCause, WakeOutcome, WakePlan,
    arm_button_wake, boot_id, classify_press, issue_report_seq, run_wake, sleep_after, sleep_ms,
    wake_plan,
};
use coldframe_setup::store::{SetupStoreError, load_or_create_code};
use coldframe_setup::{NodeSetupEnd, SETUP_WINDOW_MS, run_node_setup};
use coldframe_transport::{Buffered, ClockChange, Hub, Outcome, Report, begin, buffer_only, run};
use core::fmt::Display;
use core::ops::Range;
use embassy_executor::Spawner;
use embassy_futures::select::{Either, select};
use esp_backtrace as _;
use esp_hal::clock::CpuClock;
use esp_hal::peripherals::{BT, LPWR, WIFI};
use esp_hal::ram;
use esp_hal::time::Duration;
use esp_hal::timer::timg::TimerGroup;
use esp_radio::ble::controller::BleConnector;
use esp_radio::wifi::{ControllerConfig, WifiController};
use log::{LevelFilter, error, info, warn};
use trouble_host::prelude::ExternalController;

use board::ble::{BoardSetupLink, advertised_name};
use board::button::BoardButton;
use board::espnow::BoardEspNow;
use board::flash::{BOOT_PARTITION, BoardStorage, SEQ_PARTITION, SETUP_PARTITION};
use board::pins::DIVIDER;
use board::radio::BoardRadio;
use board::rng::{BoardTrng, RadioTrng};
use board::rtc::BoardRtc;
use board::sensors::{BoardAdc, BoardCharger, BoardEnv, BoardSwitch};
use board::sleep::{deep_sleep, reset_reason_name, wake_cause};
use board::timer::BoardTimer;

esp_bootloader_esp_idf::esp_app_desc!();

/// The firmware version.
const FIRMWARE_VERSION: &str = env!("CARGO_PKG_VERSION");

/// The longest a wake may run before the RTC watchdog resets the chip.
const WATCHDOG_MS: u64 = 30_000;

/// The watchdog during setup mode: the whole window, plus the normal margin.
const SETUP_WATCHDOG_MS: u64 = SETUP_WINDOW_MS as u64 + WATCHDOG_MS;

#[cfg(not(feature = "dev-mode"))]
const MODE: &str = "efuse";
#[cfg(feature = "dev-mode")]
const MODE: &str = "dev";

/// Deep-sleeps `ms`, with the setup button armed as a wakeup unless it is still held. Logs the
/// sleep actually taken.
fn sleep(lpwr: LPWR<'static>, button: &mut BoardButton, ms: u32) -> ! {
    let arm_button = arm_button_wake(button);
    if !arm_button {
        warn!("setup button still held; timer wake only");
    }
    info!("sleep ms={ms} button_armed={arm_button}");
    deep_sleep(lpwr, ms, button, arm_button)
}

/// Logs why the wake stopped and deep-sleeps one period. `reason` never holds a secret.
fn fail(lpwr: LPWR<'static>, button: &mut BoardButton, stage: &str, reason: &dyn Display) -> ! {
    error!("wake failed stage={stage} error={reason}");
    sleep(lpwr, button, WAKE_PERIOD_MS)
}

fn cause_name(cause: WakeCause) -> &'static str {
    match cause {
        WakeCause::ColdBoot => "cold_boot",
        WakeCause::Timer => "timer",
        WakeCause::Button => "button",
    }
}

fn plan_name(plan: WakePlan) -> &'static str {
    match plan {
        WakePlan::Measure => "measure",
        WakePlan::ReportNow => "report-now",
        WakePlan::Setup => "setup",
        WakePlan::SleepAgain => "sleep-again",
    }
}

/// Setup mode (Story 4.2), entered by a long press: the BLE setup window, then back to the wake.
///
/// 1. Load the setup code from `cf_setup`, drawing it on the first long press (the Wi-Fi radio
///    starts only then, as the TRNG's entropy source). A corrupt record is never replaced: the
///    Node logs an error and skips setup mode.
/// 2. Print `setup code=…` as a plain serial line, as the Hub does.
/// 3. Raise the RTC watchdog to the window plus 30 s.
/// 4. Start the BLE controller and, in a `dev-mode` build, the Wi-Fi controller for the ESP-NOW
///    coexistence probe (BLE first, for coexistence).
/// 5. Run [`run_node_setup`] for [`SETUP_WINDOW_MS`] and log `setup end=enrolled|window-closed`.
/// 6. Drop the controllers (BLE stops advertising) and restore the watchdog to 30 s.
///
/// The Node stores nothing here but the setup code: no Site, Lot or Wi-Fi settings.
async fn setup_mode(
    storage: &mut BoardStorage,
    mut wifi: WIFI<'_>,
    bt: BT<'_>,
    rtc: &mut BoardRtc,
    keys: &DeviceKeys,
    device_id_hex: &[u8; 16],
) {
    let code = {
        let mut flash = match storage.partition(SETUP_PARTITION) {
            Ok(flash) => flash,
            Err(error) => {
                error!("setup skipped stage=partition error={error}");
                return;
            }
        };
        let mut radio = BoardRadio::new(wifi.reborrow());
        let mut trng = RadioTrng::new(&mut radio);
        match load_or_create_code(&mut trng, &mut flash) {
            Ok((code, _)) => code,
            Err(SetupStoreError::CorruptSetupCode) => {
                error!("setup skipped: corrupt setup code record in cf_setup; not regenerated");
                return;
            }
            Err(error) => {
                error!("setup skipped stage=store error={error}");
                return;
            }
        }
    };
    // The one place the code is shown: a plain serial line, never a log record.
    esp_println::println!("setup code={}", code.as_str());

    rtc.arm_watchdog(Duration::from_millis(SETUP_WATCHDOG_MS));
    info!("setup mode window_ms={SETUP_WINDOW_MS}");
    {
        // BLE before Wi-Fi, as the esp-radio coexistence examples require. BLE is also the
        // TRNG's entropy source for the session and enrolment.
        let connector = match BleConnector::new(bt, Default::default()) {
            Ok(connector) => connector,
            Err(error) => {
                error!("setup failed stage=ble error={error:?}");
                rtc.arm_watchdog(Duration::from_millis(WATCHDOG_MS));
                return;
            }
        };
        #[cfg(feature = "dev-mode")]
        let probe = board::coex::start(wifi, keys.device_id.as_bytes());
        #[cfg(not(feature = "dev-mode"))]
        let probe = {
            let _ = wifi;
            core::future::pending::<()>()
        };

        let name = advertised_name(device_id_hex);
        let address = board::ble::address(keys.device_id.as_bytes());
        let host = board::ble::run(ExternalController::new(connector), name, address);
        let mut link = BoardSetupLink::new();
        let mut trng = BoardTrng;
        let window = run_node_setup(
            &mut link,
            &*rtc,
            &mut trng,
            keys,
            &code,
            FIRMWARE_VERSION,
            SETUP_WINDOW_MS,
        );
        let radios = async {
            select(host, probe).await;
        };
        match select(radios, window).await {
            Either::First(()) => error!("setup failed stage=ble error=host stopped"),
            Either::Second(Ok(NodeSetupEnd::Enrolled)) => info!("setup end=enrolled"),
            Either::Second(Ok(NodeSetupEnd::WindowClosed)) => info!("setup end=window-closed"),
            Either::Second(Err(error)) => error!("setup failed stage=session error={error}"),
        }
        // Leaving the scope drops the BLE host, the controllers and the probe.
    }
    rtc.arm_watchdog(Duration::from_millis(WATCHDOG_MS));
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

/// A `report_seq` for the log: the number, or `none` when the counter failed.
struct ReportSeq(Option<u64>);

impl Display for ReportSeq {
    fn fmt(&self, f: &mut core::fmt::Formatter<'_>) -> core::fmt::Result {
        match self.0 {
            Some(seq) => write!(f, "{seq}"),
            None => f.write_str("none"),
        }
    }
}

/// Logs the wake: counts and the seq range always, values only in a `dev-mode` build.
/// `sleep_ms` is the deep sleep that follows, computed after the transport.
fn log_outcome(outcome: &WakeOutcome, sleep_ms: u32) {
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
    let report_seq = ReportSeq(report.report_seq);
    info!(
        "wake done readings={} seq={seq} report_seq={report_seq} measured_at={time} battery={} \
         charging={} sleep_ms={}",
        report.readings.len(),
        if report.battery.is_some() {
            "ok"
        } else {
            "none"
        },
        charging_name(report.charging),
        sleep_ms
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

/// Logs the transport of a wake: counts and states only, never a Reading or a payload.
fn log_transport(outcome: &Outcome) {
    let faults = &outcome.faults;
    if let Some(partition) = faults.partition {
        error!("transport partition failed name={}", partition.name());
    }
    if let Some(error) = faults.buffer {
        error!("transport buffer failed error={error}");
    }
    if let Some(error) = faults.counter {
        error!("transport frame counter failed error={error}; nothing sealed");
    }
    if let Some(error) = faults.link {
        warn!("transport link state failed error={error}");
    }
    if faults.seal.is_some() {
        error!("transport seal failed; frame not sent");
    }
    let buffered = match outcome.buffered {
        Buffered::NoReport => "none",
        Buffered::Stored { dropped: 0 } => "ok",
        Buffered::Stored { dropped } => {
            warn!("transport buffer full; oldest reports dropped={dropped}");
            "ok"
        }
        Buffered::Failed => "failed",
    };
    let clock = match outcome.clock {
        ClockChange::Unchanged => "unchanged",
        ClockChange::Set => "set",
        ClockChange::Forward => "forward",
        ClockChange::Back => "back",
    };
    let (hub, channel, scanned) = match outcome.hub {
        Hub::NotTried => ("off", 0, false),
        Hub::Found { channel, scanned } => ("found", channel, scanned),
        Hub::NotFound { scanned } => ("none", 0, scanned),
    };
    info!(
        "transport buffered={buffered} hub={hub} channel={channel} scanned={scanned} pending={} \
         sent={} downlinks={} deleted={} fresh={} misses={} clock={clock} specs_sent={} backlog={}",
        outcome.pending,
        outcome.sent,
        outcome.downlinks,
        outcome.deleted,
        outcome.fresh,
        outcome.misses,
        outcome.specifications_sent,
        outcome.backlog
    );
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

    // Heap for esp-radio: the Wi-Fi radio when a new identity root or setup code is drawn, BLE
    // (and in dev-mode Wi-Fi for the ESP-NOW probe) in setup mode.
    esp_alloc::heap_allocator!(#[ram(reclaimed)] size: 64 * 1024);
    esp_alloc::heap_allocator!(size: 128 * 1024);

    let timg0 = TimerGroup::new(peripherals.TIMG0);
    esp_rtos::start(timg0.timer0, peripherals.FROM_CPU_INTR0);

    let pins = crate::take_pins!(peripherals);
    let mut button = BoardButton::new(pins.button);
    let mut timer = BoardTimer;

    // Time the press first: every millisecond of boot work before the first poll shortens the
    // shortest press that counts.
    let cause = wake_cause();
    let press = match cause {
        WakeCause::Button => Some(classify_press(&mut button, &mut timer).await),
        WakeCause::Timer | WakeCause::ColdBoot => None,
    };

    let mut wifi = peripherals.WIFI;
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
            Err(error) => fail(lpwr, &mut button, "boot_partition", &error),
        };
        let mut boots = ReservedCounter::new(flash, BOOT_MAGIC);
        match boot_id(&mut boots, cause) {
            Ok(boot) => boot,
            Err(error) => fail(lpwr, &mut button, "boot_counter", &error),
        }
    };
    info!("boot id={boot}");

    // The radio stays off unless the identity has to draw a new root.
    let mut radio = BoardRadio::new(wifi.reborrow());
    let mut trng = BoardTrng;

    #[cfg(not(feature = "dev-mode"))]
    let provisioned = {
        let Some(mut efuse) = board::efuse::BoardEfuse::take() else {
            fail(lpwr, &mut button, "efuse", &"eFuse adapter already taken")
        };
        let mut hmac = board::hmac::BoardHmac::new(peripherals.HMAC);
        provision_efuse(&mut radio, &mut trng, &mut efuse, &mut hmac)
    };
    #[cfg(feature = "dev-mode")]
    let provisioned = {
        let mut flash = match storage.partition(board::flash::IDENTITY_PARTITION) {
            Ok(flash) => flash,
            Err(error) => fail(lpwr, &mut button, "partition", &error),
        };
        provision_dev(&mut radio, &mut trng, &mut flash)
    };
    let provisioned = match provisioned {
        Ok(provisioned) => provisioned,
        Err(error) => fail(lpwr, &mut button, "provision", &error as &IdentityError),
    };
    drop(radio);
    let device_id_hex = provisioned.keys.device_id.to_hex();
    let device_id = core::str::from_utf8(&device_id_hex).unwrap_or("?");
    info!(
        "identity mode={MODE} source={} device_id={device_id}",
        provisioned.source
    );

    let plan = wake_plan(cause, press);
    info!("wake plan={}", plan_name(plan));
    match plan {
        WakePlan::SleepAgain => sleep(lpwr, &mut button, sleep_after(plan, 0)),
        WakePlan::Setup => {
            setup_mode(
                &mut storage,
                wifi.reborrow(),
                peripherals.BT,
                &mut rtc,
                &provisioned.keys,
                &device_id_hex,
            )
            .await;
        }
        WakePlan::Measure | WakePlan::ReportNow => {}
    }

    // The link state, and with it the wall clock a downlink set on this boot: before the
    // measurement, so its Readings are stamped with it.
    let (mut link, link_fault) = begin(&mut storage, &mut rtc, boot);
    if let Some(error) = link_fault {
        warn!("link state unreadable error={error}; starting from none");
    }

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

    let outcome = {
        let seq_flash = match storage.partition(SEQ_PARTITION) {
            Ok(flash) => flash,
            Err(error) => fail(lpwr, &mut button, "seq_partition", &error),
        };
        let mut seq = ReservedCounter::new(seq_flash, SEQ_MAGIC);
        let mut outcome = run_wake(
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
        // The report itself takes one more value of the same counter. A fault is recorded in the
        // outcome and logged with it; the report then has no report_seq and cannot be sent.
        let _ = issue_report_seq(&mut outcome, &mut seq);
        outcome
    };

    let report = Report::from_wake(&outcome.report);
    if report.is_none() {
        error!("wake report has no report_seq; not buffered");
    }
    let transport = if plan == WakePlan::Setup {
        // A wake that entered setup mode does not transmit: the report waits for the next wake.
        buffer_only(&mut storage, report.as_ref())
    } else {
        // ESP-NOW only, never a Wi-Fi station. The controller and ESP-NOW are dropped at the end
        // of this arm, so the radio is off before the deep sleep.
        match WifiController::new(wifi.reborrow(), ControllerConfig::default()) {
            Ok(controller) => {
                let mut radio = BoardEspNow::new(controller.esp_now());
                let mut trng = BoardTrng;
                run(
                    &mut storage,
                    &mut radio,
                    &mut rtc,
                    &mut trng,
                    &provisioned.keys,
                    boot,
                    &mut link,
                    report.as_ref(),
                )
                .await
            }
            Err(error) => {
                error!("transport radio failed error={error:?}; report buffered only");
                buffer_only(&mut storage, report.as_ref())
            }
        }
    };

    // The sleep is measured from the wake start to now, the transport included. After a press
    // the schedule restarts: a full period from now.
    let elapsed = rtc.uptime_millis().saturating_sub(wake_start_ms);
    let sleep_for = sleep_after(plan, sleep_ms(elapsed));
    log_outcome(&outcome, sleep_for);
    log_transport(&transport);
    sleep(lpwr, &mut button, sleep_for)
}
