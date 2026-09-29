//! Coldframe Hub firmware (ESP32-S3).
//!
//! Story 3.2: bring the chip up, turn the radio on, provision the hardware-bound identity (AD-12)
//! and log the Device ID. BLE setup, Wi-Fi join and heartbeats follow in Stories 3.4 and 3.5.
//!
//! Boot sequence:
//! 1. esp-hal, the heap and esp-rtos.
//! 2. The Wi-Fi controller, which turns the radio on and makes it the TRNG's entropy source.
//! 3. The identity: [`provision_efuse`] in a release build, [`provision_dev`] under `dev-mode`.
//! 4. One log line `identity mode=… source=… device_id=…`, then an idle loop that keeps the radio
//!    alive and logs uptime.
//!
//! A failure logs its kind and halts. Nothing retries a burn.

#![no_std]
#![no_main]

// A release Hub must bind its identity to the chip. Dev mode is for debug builds only (AD-12).
#[cfg(all(feature = "dev-mode", not(debug_assertions)))]
compile_error!(
    "the `dev-mode` feature is refused in release builds: a release Hub uses its eFuse identity"
);

mod board;

use coldframe_crypto::identity::IdentityError;
#[cfg(feature = "dev-mode")]
use coldframe_crypto::identity::provision_dev;
#[cfg(not(feature = "dev-mode"))]
use coldframe_crypto::identity::provision_efuse;
use core::fmt::Display;
use embassy_executor::Spawner;
use embassy_time::{Duration, Instant, Timer};
use esp_backtrace as _;
use esp_hal::clock::CpuClock;
use esp_hal::ram;
use esp_hal::timer::timg::TimerGroup;
use esp_radio::wifi::{ControllerConfig, WifiController};
use log::{error, info};

use board::radio::BoardRadio;
use board::rng::BoardTrng;

esp_bootloader_esp_idf::esp_app_desc!();

/// How often the idle loop logs uptime.
const UPTIME_EVERY: Duration = Duration::from_secs(60);

#[cfg(not(feature = "dev-mode"))]
const MODE: &str = "efuse";
#[cfg(feature = "dev-mode")]
const MODE: &str = "dev";

/// Logs why the Hub stopped and parks it. Never returns and never retries.
async fn halt(stage: &str, reason: &dyn Display) -> ! {
    error!("identity mode={MODE} failed stage={stage} error={reason}; halted");
    loop {
        Timer::after(Duration::from_secs(3600)).await;
    }
}

#[esp_rtos::main]
async fn main(_spawner: Spawner) -> ! {
    esp_println::logger::init_logger_from_env();
    let peripherals = esp_hal::init(esp_hal::Config::default().with_cpu_clock(CpuClock::max()));

    // The radio needs a heap: reclaimed bootloader RAM plus a regular heap.
    esp_alloc::heap_allocator!(#[ram(reclaimed)] size: 64 * 1024);
    esp_alloc::heap_allocator!(size: 64 * 1024);

    let timg0 = TimerGroup::new(peripherals.TIMG0);
    esp_rtos::start(timg0.timer0, peripherals.FROM_CPU_INTR0);

    info!(
        "coldframe-hub {} starting, identity mode={MODE}",
        env!("CARGO_PKG_VERSION")
    );

    // The radio goes on before any root-key byte is drawn: it is the TRNG's entropy source.
    let controller = match WifiController::new(peripherals.WIFI, ControllerConfig::default()) {
        Ok(controller) => controller,
        Err(error) => halt("radio", &error).await,
    };
    let mut radio = BoardRadio::new(controller);
    let mut trng = BoardTrng;

    #[cfg(not(feature = "dev-mode"))]
    let provisioned = {
        let Some(mut efuse) = board::efuse::BoardEfuse::take() else {
            halt("efuse", &"eFuse adapter already taken").await
        };
        let mut hmac = board::hmac::BoardHmac::new(peripherals.HMAC);
        provision_efuse(&mut radio, &mut trng, &mut efuse, &mut hmac)
    };
    #[cfg(feature = "dev-mode")]
    let provisioned = {
        let mut flash = match board::flash::BoardFlash::open(peripherals.FLASH) {
            Ok(flash) => flash,
            Err(error) => halt("partition", &error).await,
        };
        provision_dev(&mut radio, &mut trng, &mut flash)
    };

    let provisioned = match provisioned {
        Ok(provisioned) => provisioned,
        Err(error) => halt("provision", &error as &IdentityError).await,
    };
    let device_id = provisioned.keys.device_id.to_hex();
    let device_id = core::str::from_utf8(&device_id).unwrap_or("?");
    info!(
        "identity mode={MODE} source={} device_id={device_id}",
        provisioned.source
    );
    // Stories 3.4 and 3.5 put `provisioned.keys` to use. `main` never returns, so `radio` (and
    // with it the Wi-Fi controller) stays alive through the idle loop.
    loop {
        Timer::after(UPTIME_EVERY).await;
        info!(
            "uptime s={} device_id={device_id}",
            Instant::now().as_secs()
        );
    }
}
