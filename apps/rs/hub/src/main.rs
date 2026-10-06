//! Coldframe Hub firmware (ESP32-S3).
//!
//! Story 3.2 brought up the chip and the hardware-bound identity (AD-12); Story 3.4 added the BLE
//! setup service (AD-25); Story 3.5 joins Wi-Fi and heartbeats to the Server (AD-11, AD-12, H-1);
//! Story 4.4 relays the sealed frames of Nodes over ESP-NOW (AD-9).
//!
//! Boot sequence:
//! 1. esp-hal, the heap (144 KiB + 64 KiB reclaimed, as the radio spike measured) and esp-rtos.
//! 2. The BLE controller, then the Wi-Fi controller (BLE first, for coexistence). The radio is on
//!    and is the TRNG's entropy source from here.
//! 3. The identity: [`provision_efuse`] in a release build, [`provision_dev`] under `dev-mode`.
//!    One log line `identity mode=… source=… device_id=…`.
//! 4. The RTC, installed as mbedTLS's wall clock; the embassy-net stack with DHCP (its task always
//!    runs); mbedTLS with the public roots.
//! 5. The `cf_setup` partition: load the setup code, or draw and store it on first boot, and read
//!    the provisioning record. While unprovisioned the code is printed as one serial line
//!    `setup code=XXXXXXXX`; once provisioned it is never shown again. A record that is not valid
//!    (including Story 3.4's version 1) logs `corrupt provisioning record` and counts as none.
//! 6. Unprovisioned: advertise the setup service and run BLE setup sessions until one has joined,
//!    passed the Server check (DHCP, SNTP, one signed heartbeat) and stored the record, then log
//!    `setup provisioned` and the heap. Provisioned: no advertising.
//! 7. Both paths start the relay's radio task ([`relay_task`]): ESP-NOW on the station's radio
//!    and channel. It answers Node probes at once, queues their sealed uplinks and sends sealed
//!    downlinks back, without ever reading an envelope.
//! 8. Both paths run the uplink forever: re-join with backoff whenever the link drops, SNTP once
//!    per boot before any TLS, a signed heartbeat every 30–60 s, and between heartbeats one
//!    signed `POST /device/ingest` per batch of queued uplinks. A separate task logs uptime.
//!
//! A failure before the uplink logs its kind and halts. Nothing retries a burn, and a corrupt
//! setup code is never replaced. Keys, the password, nonces, signatures, bodies and sealed
//! envelopes are never logged.

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
use coldframe_hal::Trng as _;
use coldframe_setup::run_setup;
use coldframe_setup::service::boot;
use coldframe_setup::store::ProvisioningState;
use coldframe_uplink::{Event, HeartbeatOutcome, IngestOutcome, Uplink};
use core::fmt::Display;
use embassy_executor::Spawner;
use embassy_time::{Duration, Instant, Timer};
use esp_backtrace as _;
use esp_hal::clock::CpuClock;
use esp_hal::ram;
use esp_hal::timer::timg::TimerGroup;
use esp_radio::ble::controller::BleConnector;
use esp_radio::wifi::{ControllerConfig, Interface, WifiController};
use log::{LevelFilter, error, info, warn};
use trouble_host::prelude::ExternalController;

use board::ble::{BoardSetupLink, advertised_name, ble_task};
use board::espnow::{RELAY, relay_task};
use board::flash::{BoardStorage, SETUP_PARTITION};
use board::net::{BoardNet, net_task};
use board::radio::BoardRadio;
use board::rng::BoardTrng;
use board::rtc::BoardRtc;
use board::timer::BoardTimer;
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

/// The lowest free heap since boot, in bytes: the budget is at least 32 KiB (spike C5).
fn heap_min_free() -> usize {
    let stats = esp_alloc::HEAP.stats();
    stats.size.saturating_sub(stats.max_usage)
}

/// Logs what one uplink step did. Events carry no secret; neither does this.
fn log_event(event: &Event) {
    match event {
        Event::LinkLost => warn!("wifi link lost; re-joining"),
        Event::Joined { channel, rssi } => info!("wifi joined channel={channel} rssi={rssi}"),
        Event::JoinFailed { failure, retry_ms } => {
            warn!(
                "wifi join failed kind={} retry_ms={retry_ms}",
                failure.kind()
            );
        }
        Event::ClockSet { unix_ms } => info!("sntp clock set unix_s={}", unix_ms / 1_000),
        Event::ClockFailed { error, retry_ms } => {
            warn!("sntp failed kind={} retry_ms={retry_ms}", error.kind());
        }
        Event::Heartbeat { outcome, next_ms } => match outcome {
            HeartbeatOutcome::Accepted { server_time_ms } => info!(
                "heartbeat ok server_time_s={} next_ms={next_ms}",
                server_time_ms / 1_000
            ),
            HeartbeatOutcome::Rejected { status } => {
                warn!("heartbeat rejected status={status} next_ms={next_ms}");
            }
            other => warn!("heartbeat failed kind={} next_ms={next_ms}", other.kind()),
        },
        Event::Ingest { frames, outcome } => match outcome {
            IngestOutcome::Relayed { downlinks } => {
                info!("ingest ok frames={frames} downlinks={downlinks}");
            }
            IngestOutcome::Rejected { status } => {
                warn!("ingest rejected status={status} frames={frames}; batch dropped");
            }
            other => warn!(
                "ingest failed kind={} frames={frames}; batch dropped",
                other.kind()
            ),
        },
        Event::UplinksLost { dropped } => {
            warn!("relay queue was full; uplinks dropped={dropped}");
        }
    }
}

/// Logs uptime and the heap once a minute, forever.
#[embassy_executor::task]
async fn uptime_task(device_id: [u8; 16]) -> ! {
    let device_id = core::str::from_utf8(&device_id).unwrap_or("?");
    loop {
        Timer::after(UPTIME_EVERY).await;
        info!(
            "uptime s={} device_id={device_id} heap_free={} heap_used={} heap_min_free={}",
            Instant::now().as_secs(),
            esp_alloc::HEAP.free(),
            esp_alloc::HEAP.used(),
            heap_min_free()
        );
    }
}

#[esp_rtos::main]
async fn main(spawner: Spawner) -> ! {
    // A fixed level, not ESP_LOG: the build reads no environment (FR-1).
    esp_println::logger::init_logger(LevelFilter::Info);
    let peripherals = esp_hal::init(esp_hal::Config::default().with_cpu_clock(CpuClock::max()));

    // Wi-Fi plus BLE (coex) plus one TLS connection need a heap: reclaimed bootloader RAM plus a
    // regular heap, the radio spike's 144 KiB + 64 KiB (F-5).
    esp_alloc::heap_allocator!(#[ram(reclaimed)] size: 64 * 1024);
    esp_alloc::heap_allocator!(size: 144 * 1024);

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

    // The clocks before anything that could open TLS: until SNTP sets it, the RTC reads 1970 and
    // mbedTLS fails every certificate closed.
    let mut rtc = BoardRtc::install(peripherals.RTC_TIMER);
    let mut timer = BoardTimer;

    // The IP stack always runs; DHCP starts whenever the station joins.
    let mut seed = [0u8; 8];
    if let Err(error) = trng.fill(&mut seed) {
        halt_setup("net", &error).await
    }
    let (stack, runner) = board::net::stack(Interface::station(), u64::from_le_bytes(seed));
    match net_task(runner) {
        Ok(task) => spawner.spawn(task),
        Err(error) => halt_setup("net", &error).await,
    }
    let mut net = match BoardNet::new(stack) {
        Ok(net) => net,
        Err(error) => halt_setup("tls", &error).await,
    };

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

    // ESP-NOW for the Node relay, taken from the running controller before it moves on.
    let esp_now = radio.esp_now();
    let mut wifi = BoardWifi::new(radio.into_controller());
    let (record, check_stamp) = if boot.advertises() {
        let name = advertised_name(&device_id_hex);
        let address = board::ble::address(keys.device_id.as_bytes());
        match ble_task(ExternalController::new(connector), name, address) {
            Ok(task) => spawner.spawn(task),
            Err(error) => halt_setup("ble", &error).await,
        }
        let mut link = BoardSetupLink::new();
        match run_setup(
            &mut link,
            &mut wifi,
            &mut net,
            &mut rtc,
            &mut timer,
            &mut trng,
            &mut setup_flash,
            &keys,
            &boot.code,
            FIRMWARE_VERSION,
        )
        .await
        {
            Ok(provisioned) => {
                info!("setup provisioned");
                // BLE, Wi-Fi and the Server check's TLS have all run by now: the epic budget is
                // at least 32 KiB free at the lowest point.
                info!(
                    "heap free={} used={} min_free={}",
                    esp_alloc::HEAP.free(),
                    esp_alloc::HEAP.used(),
                    heap_min_free()
                );
                (provisioned.record, Some(provisioned.check.stamp_ms))
            }
            Err(error) => halt_setup("session", &error).await,
        }
    } else {
        drop(connector);
        info!("setup provisioned earlier; not advertising");
        let ProvisioningState::Provisioned(record) = boot.provisioning else {
            halt_setup("store", &"no provisioning record").await
        };
        (record, None)
    };

    match uptime_task(device_id_hex) {
        Ok(task) => spawner.spawn(task),
        Err(error) => halt_setup("uptime", &error).await,
    }
    match relay_task(esp_now, &RELAY) {
        Ok(task) => spawner.spawn(task),
        Err(error) => halt_setup("relay", &error).await,
    }

    let uplink = Uplink::new(
        record.ssid(),
        record.password(),
        record.server().clone(),
        &keys,
    );
    let mut uplink = match check_stamp {
        Some(stamp) => uplink.after_setup(stamp),
        None => uplink,
    };
    info!(
        "uplink server={} port={}",
        record.server().host(),
        record.server().port()
    );
    uplink
        .run(
            &mut wifi, &mut net, &mut rtc, &mut timer, &mut trng, &RELAY, log_event,
        )
        .await
}
