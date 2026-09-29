//! Coldframe Hub firmware (ESP32-S3).
//!
//! Story 3.2 brought up the chip and the hardware-bound identity (AD-12); Story 3.4 adds the BLE
//! setup service (AD-25). Wi-Fi reachability checks and heartbeats follow in Story 3.5.
//!
//! Boot sequence:
//! 1. esp-hal, the heap and esp-rtos.
//! 2. The BLE controller, then the Wi-Fi controller (BLE first, for coexistence). The radio is on
//!    and is the TRNG's entropy source from here.
//! 3. The identity: [`provision_efuse`] in a release build, [`provision_dev`] under `dev-mode`.
//!    One log line `identity mode=… source=… device_id=…`.
//! 4. The `cf_setup` partition: load the setup code, or draw and store it on first boot, and read
//!    the provisioning record. While unprovisioned the code is printed as one serial line
//!    `setup code=XXXXXXXX`; once provisioned it is never shown again.
//! 5. Unprovisioned: advertise the setup service and run BLE setup sessions until one stores the
//!    provisioning record, then log `setup provisioned`. Provisioned: no advertising.
//! 6. An idle loop that keeps the radio alive and logs uptime.
//!
//! A failure logs its kind and halts. Nothing retries a burn, and a corrupt setup code is never
//! replaced.

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
use coldframe_setup::run_setup;
use coldframe_setup::service::boot;
use coldframe_setup::store::ProvisioningState;
use core::fmt::Display;
use embassy_executor::Spawner;
use embassy_time::{Duration, Instant, Timer};
use esp_backtrace as _;
use esp_hal::clock::CpuClock;
use esp_hal::ram;
use esp_hal::timer::timg::TimerGroup;
use esp_radio::ble::controller::BleConnector;
use esp_radio::wifi::{ControllerConfig, WifiController};
use log::{LevelFilter, error, info, warn};
use trouble_host::prelude::ExternalController;

use board::ble::{BoardSetupLink, advertised_name, ble_task};
use board::flash::{BoardStorage, SETUP_PARTITION};
use board::radio::BoardRadio;
use board::rng::BoardTrng;
use board::wifi::BoardWifi;

esp_bootloader_esp_idf::esp_app_desc!();

/// The firmware version reported in `Identity`.
const FIRMWARE_VERSION: &str = env!("CARGO_PKG_VERSION");

/// How often the idle loop logs uptime.
const UPTIME_EVERY: Duration = Duration::from_secs(60);

#[cfg(not(feature = "dev-mode"))]
const MODE: &str = "efuse";
#[cfg(feature = "dev-mode")]
const MODE: &str = "dev";

/// Parks the Hub forever. Never returns and never retries.
async fn park() -> ! {
    loop {
        Timer::after(Duration::from_secs(3600)).await;
    }
}

/// Logs why identity provisioning stopped the Hub and parks it.
async fn halt(stage: &str, reason: &dyn Display) -> ! {
    error!("identity mode={MODE} failed stage={stage} error={reason}; halted");
    park().await
}

/// Logs why BLE setup stopped the Hub and parks it. `reason` never holds a secret.
async fn halt_setup(stage: &str, reason: &dyn Display) -> ! {
    error!("setup failed stage={stage} error={reason}; halted");
    park().await
}

#[esp_rtos::main]
async fn main(spawner: Spawner) -> ! {
    // A fixed level, not ESP_LOG: the build reads no environment (FR-1).
    esp_println::logger::init_logger(LevelFilter::Info);
    let peripherals = esp_hal::init(esp_hal::Config::default().with_cpu_clock(CpuClock::max()));

    // Wi-Fi plus BLE (coex) need a heap: reclaimed bootloader RAM plus a regular heap.
    esp_alloc::heap_allocator!(#[ram(reclaimed)] size: 64 * 1024);
    esp_alloc::heap_allocator!(size: 128 * 1024);

    let timg0 = TimerGroup::new(peripherals.TIMG0);
    esp_rtos::start(timg0.timer0, peripherals.FROM_CPU_INTR0);

    info!("coldframe-hub {FIRMWARE_VERSION} starting, identity mode={MODE}");

    // BLE before Wi-Fi, as the esp-radio coexistence examples require.
    let connector = match BleConnector::new(peripherals.BT, Default::default()) {
        Ok(connector) => connector,
        Err(error) => halt("ble", &error).await,
    };
    // The radio goes on before any root-key byte is drawn: it is the TRNG's entropy source.
    let controller = match WifiController::new(peripherals.WIFI, ControllerConfig::default()) {
        Ok(controller) => controller,
        Err(error) => halt("radio", &error).await,
    };
    let mut radio = BoardRadio::new(controller);
    let mut trng = BoardTrng;
    let mut storage = BoardStorage::new(peripherals.FLASH);

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
        let mut flash = match storage.partition(board::flash::IDENTITY_PARTITION) {
            Ok(flash) => flash,
            Err(error) => halt("partition", &error).await,
        };
        provision_dev(&mut radio, &mut trng, &mut flash)
    };

    let provisioned = match provisioned {
        Ok(provisioned) => provisioned,
        Err(error) => halt("provision", &error as &IdentityError).await,
    };
    let keys = provisioned.keys;
    let device_id_hex = keys.device_id.to_hex();
    let device_id = core::str::from_utf8(&device_id_hex).unwrap_or("?");
    info!(
        "identity mode={MODE} source={} device_id={device_id}",
        provisioned.source
    );

    let mut setup_flash = match storage.partition(SETUP_PARTITION) {
        Ok(flash) => flash,
        Err(error) => halt_setup("partition", &error).await,
    };
    let boot = match boot(&mut trng, &mut setup_flash) {
        Ok(boot) => boot,
        Err(error) => halt_setup("store", &error).await,
    };
    if let ProvisioningState::Corrupt = boot.provisioning {
        warn!("corrupt provisioning record");
    }
    if boot.shows_code() {
        // The one place the code is shown: a plain serial line, never a log record.
        esp_println::println!("setup code={}", boot.code.as_str());
    }

    // Kept alive through the idle loop: the station stays joined once setup ends.
    let _wifi = if boot.advertises() {
        let name = advertised_name(&device_id_hex);
        let address = board::ble::address(keys.device_id.as_bytes());
        match ble_task(ExternalController::new(connector), name, address) {
            Ok(task) => spawner.spawn(task),
            Err(error) => halt_setup("ble", &error).await,
        }
        let mut link = BoardSetupLink::new();
        let mut wifi = BoardWifi::new(radio.into_controller());
        match run_setup(
            &mut link,
            &mut wifi,
            &mut trng,
            &mut setup_flash,
            &keys,
            &boot.code,
            FIRMWARE_VERSION,
        )
        .await
        {
            Ok(_) => {
                info!("setup provisioned");
                // BLE and Wi-Fi are both still up here: the epic budget is >= 32 KiB free.
                info!(
                    "heap free={} used={}",
                    esp_alloc::HEAP.free(),
                    esp_alloc::HEAP.used()
                );
            }
            Err(error) => halt_setup("session", &error).await,
        }
        wifi
    } else {
        drop(connector);
        info!("setup provisioned earlier; not advertising");
        BoardWifi::new(radio.into_controller())
    };

    loop {
        Timer::after(UPTIME_EVERY).await;
        info!(
            "uptime s={} device_id={device_id} heap_free={} heap_used={}",
            Instant::now().as_secs(),
            esp_alloc::HEAP.free(),
            esp_alloc::HEAP.used()
        );
    }
}
