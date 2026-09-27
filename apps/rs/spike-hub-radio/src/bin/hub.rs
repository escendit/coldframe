//! Spike Hub: the system under test for PRD Open Question 1.
//!
//! One ESP32-S3 running, concurrently on embassy tasks:
//! 1. Wi-Fi STA + DHCP (embassy-net), reconnecting forever.
//! 2. ESP-NOW on the same radio: answers Node probes, and acks every DATA frame,
//!    either after a real HTTPS round trip ("server" mode) or immediately
//!    ("immediate" mode).
//! 3. BLE peripheral (trouble-host): continuous advertising, one GATT service
//!    with a writable "provisioning" characteristic and a notify characteristic.
//! 4. TLS client (mbedtls-rs): HTTPS POST to `SPIKE_URL` over a kept-alive
//!    connection, with SNTP first (AD-11).
//! 5. Heartbeat: an HTTPS request every 30 s (FR-13).
//! 6. Stats: one machine-parsable `STATS hub ...` line every 10 s.
//!
//! THROWAWAY SPIKE CODE — see docs/spikes/hub-radio-coexistence.md.

#![no_std]
#![no_main]
#![recursion_limit = "256"]

extern crate alloc;

use core::{
    ffi::CStr,
    fmt::Write as _,
    sync::atomic::{AtomicBool, AtomicI32, AtomicU32, Ordering::Relaxed},
};

use embassy_executor::Spawner;
use embassy_futures::{
    join::join,
    select::{Either, select},
};
use embassy_net::{
    Runner, Stack, StackResources,
    dns::DnsQueryType,
    tcp::TcpSocket,
    udp::{PacketMetadata, UdpSocket},
};
use embassy_sync::{
    blocking_mutex::raw::CriticalSectionRawMutex, channel::Channel, signal::Signal,
};
use embassy_time::{Duration, Instant, Ticker, Timer, with_timeout};
use esp_backtrace as _;
use esp_hal::{
    clock::CpuClock,
    ram,
    rng::{Trng, TrngSource},
    rtc_cntl::Rtc,
    timer::timg::TimerGroup,
};
use esp_radio::{
    ble::controller::BleConnector,
    esp_now::{EspNow, EspNowWifiInterface, PeerInfo},
    wifi::{
        AuthenticationMethodConfig, Config as WifiConfig, ControllerConfig, Interface,
        PowerSaveMode, WifiController,
        sta::{ScanMethod, StationConfig},
    },
};
use log::{error, info, warn};
use mbedtls_rs::{
    Certificate, ClientSessionConfig, Session, SessionConfig, SessionError, Tls, X509,
    io::{Read, Write},
    sys::hook::backend::esp::{EspAccel, EspAccelQueue},
};
use spike_hub_radio::{
    ACK_MODE_IMMEDIATE, ACK_MODE_SERVER, Frame, Mac, SERVER_FAILED, SERVER_OK, SERVER_SKIPPED,
    SharedLatency, mk_static, mode_name, parse_u32, str_eq,
};
use trouble_host::prelude::*;

esp_bootloader_esp_idf::esp_app_desc!();

// ---------------------------------------------------------------------------
// Build-time configuration (never commit credentials; pass them as env vars)
// ---------------------------------------------------------------------------

const SSID: &str = match option_env!("SPIKE_WIFI_SSID") {
    Some(s) => s,
    None => "",
};
const PASSWORD: &str = match option_env!("SPIKE_WIFI_PASSWORD") {
    Some(s) => s,
    None => "",
};
const URL: &str = match option_env!("SPIKE_URL") {
    Some(s) => s,
    None => "https://example.com/",
};
const NTP_SERVER: &str = match option_env!("SPIKE_NTP_SERVER") {
    Some(s) => s,
    None => "pool.ntp.org",
};
/// `POST` (default) or `GET`.
const HTTP_METHOD: &str = match option_env!("SPIKE_HTTP_METHOD") {
    Some(s) => s,
    None => "POST",
};
/// Initial ack mode: `server` (default) or `immediate`. Can be toggled at
/// runtime over BLE (write `I` / `S` to the provisioning characteristic).
const ACK_MODE_DEFAULT_IMMEDIATE: bool = match option_env!("SPIKE_ACK_MODE") {
    Some(s) => str_eq(s, "immediate"),
    None => false,
};
/// AP selection: `all` (default, scan all channels and join the strongest AP) or `fast`
/// (esp-radio default: join the first matching AP found, whatever its signal).
const WIFI_SCAN_ALL: bool = match option_env!("SPIKE_WIFI_SCAN") {
    Some(s) => !matches!(s.as_bytes(), b"fast"),
    None => true,
};
/// Forced roam for channel-following tests: after `SPIKE_ROAM_AT_S` seconds of uptime the Hub
/// disconnects and re-associates pinned to `SPIKE_ROAM_BSSID` (e.g. another mesh AP on a
/// different channel). Unset = never roam.
const ROAM_BSSID: Option<&str> = option_env!("SPIKE_ROAM_BSSID");
const ROAM_AT_S: u32 = parse_u32(option_env!("SPIKE_ROAM_AT_S"), 0);

fn parse_mac(s: &str) -> Option<[u8; 6]> {
    let mut out = [0u8; 6];
    let mut n = 0;
    for part in s.split(':') {
        if n == 6 {
            return None;
        }
        out[n] = u8::from_str_radix(part, 16).ok()?;
        n += 1;
    }
    (n == 6).then_some(out)
}

fn station_config(bssid: Option<[u8; 6]>) -> WifiConfig {
    let mut c = StationConfig::default()
        .with_ssid(SSID.try_into().unwrap())
        .with_scan_method(if WIFI_SCAN_ALL {
            ScanMethod::AllChannels
        } else {
            ScanMethod::Fast
        })
        .with_authentication(if PASSWORD.is_empty() {
            AuthenticationMethodConfig::Open
        } else {
            AuthenticationMethodConfig::Wpa2Personal(PASSWORD.try_into().unwrap())
        });
    if let Some(b) = bssid {
        c = c.with_bssid(b);
    }
    WifiConfig::Station(c)
}

/// Wi-Fi modem power save: `none` (default) or `min` / `max`.
const WIFI_PS: &str = match option_env!("SPIKE_WIFI_PS") {
    Some(s) => s,
    None => "none",
};
/// Max TX power in 0.25 dBm units (esp-radio default is only 5 dBm).
const WIFI_TX_POWER: u32 = parse_u32(option_env!("SPIKE_WIFI_TX_POWER"), 60);
/// Hardware crypto acceleration for mbedTLS (SHA/RSA/AES): 1 (default) or 0.
const TLS_HW_ACCEL: bool = parse_u32(option_env!("SPIKE_TLS_HW_ACCEL"), 1) == 1;
/// BLE stack on (default) or off (`SPIKE_BLE=0`) for A/B comparison runs.
const BLE_ENABLED: bool = parse_u32(option_env!("SPIKE_BLE"), 1) == 1;
const HEARTBEAT_EVERY: Duration =
    Duration::from_secs(parse_u32(option_env!("SPIKE_HEARTBEAT_S"), 30) as u64);
/// Hub-side budget for the server round trip before acking with SERVER_FAILED.
const RELAY_TIMEOUT: Duration =
    Duration::from_millis(parse_u32(option_env!("SPIKE_RELAY_TIMEOUT_MS"), 8000) as u64);
const HTTP_TIMEOUT: Duration = Duration::from_secs(15);
const STATS_EVERY: Duration = Duration::from_secs(10);

/// Public roots, PEM, NUL-terminated for mbedTLS. See certs/README.md.
const CA_BUNDLE: &CStr = match CStr::from_bytes_with_nul(
    concat!(include_str!("../../certs/roots.pem"), "\0").as_bytes(),
) {
    Ok(b) => b,
    Err(_) => panic!("CA bundle is not valid"),
};

// ---------------------------------------------------------------------------
// Shared state / counters
// ---------------------------------------------------------------------------

static ACK_IMMEDIATE: AtomicBool = AtomicBool::new(ACK_MODE_DEFAULT_IMMEDIATE);

static WIFI_UP: AtomicBool = AtomicBool::new(false);
static WIFI_CHANNEL: AtomicU32 = AtomicU32::new(0);
static WIFI_CONNECTS: AtomicU32 = AtomicU32::new(0);
static WIFI_DISCONNECTS: AtomicU32 = AtomicU32::new(0);
static WIFI_RSSI: AtomicI32 = AtomicI32::new(0);

static NOW_RX: AtomicU32 = AtomicU32::new(0);
static NOW_DATA: AtomicU32 = AtomicU32::new(0);
static NOW_PROBES: AtomicU32 = AtomicU32::new(0);
static NOW_ACKS: AtomicU32 = AtomicU32::new(0);
static NOW_SEND_FAIL: AtomicU32 = AtomicU32::new(0);
static NOW_RELAY_FAIL: AtomicU32 = AtomicU32::new(0);

static BLE_CONNECTED: AtomicBool = AtomicBool::new(false);
static BLE_CONNS: AtomicU32 = AtomicU32::new(0);
static BLE_WRITES: AtomicU32 = AtomicU32::new(0);
static BLE_DISCONNECTS: AtomicU32 = AtomicU32::new(0);
static BLE_ADV_ERRORS: AtomicU32 = AtomicU32::new(0);

static TLS_OK: AtomicU32 = AtomicU32::new(0);
static TLS_FAIL: AtomicU32 = AtomicU32::new(0);
static TLS_HANDSHAKES: AtomicU32 = AtomicU32::new(0);
static TLS_HANDSHAKE_FAIL: AtomicU32 = AtomicU32::new(0);
static TLS_HANDSHAKE_MS_LAST: AtomicU32 = AtomicU32::new(0);
static HTTP_RECONNECTS: AtomicU32 = AtomicU32::new(0);
static HB_OK: AtomicU32 = AtomicU32::new(0);
static HB_FAIL: AtomicU32 = AtomicU32::new(0);
static HTTP_LAST_STATUS: AtomicU32 = AtomicU32::new(0);
static SNTP_OK: AtomicBool = AtomicBool::new(false);
/// Unix time (s) at embassy `Instant` zero, set by SNTP.
static UNIX_S_AT_BOOT: AtomicU32 = AtomicU32::new(0);

/// Frame rx → ack tx at the Hub (ms).
static ACK_LAT: SharedLatency = SharedLatency::new();
/// HTTPS request latency on a warm (kept-alive) connection (ms).
static HTTPS_WARM: SharedLatency = SharedLatency::new();
/// HTTPS request latency including DNS + TCP + TLS handshake (ms).
static HTTPS_COLD: SharedLatency = SharedLatency::new();
/// TLS handshake alone (ms).
static HANDSHAKE_LAT: SharedLatency = SharedLatency::new();

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum ReqKind {
    Relay,
    Heartbeat,
}

#[derive(Clone, Copy, Debug)]
struct HttpReq {
    kind: ReqKind,
    seq: u32,
}

static HTTP_REQS: Channel<CriticalSectionRawMutex, HttpReq, 4> = Channel::new();
/// (seq, ok) for relay requests.
static RELAY_DONE: Signal<CriticalSectionRawMutex, (u32, bool)> = Signal::new();

// ---------------------------------------------------------------------------
// BLE GATT definition (simulated provisioning service)
// ---------------------------------------------------------------------------

const CONNECTIONS_MAX: usize = 1;
const L2CAP_CHANNELS_MAX: usize = 2; // signal + att

#[gatt_server]
struct Server {
    prov: ProvisioningService,
}

/// Stand-in for the Hub provisioning service (FR-1). UUIDs are spike-only.
#[gatt_service(uuid = "c01df4a0-0000-4000-8000-00000000c0de")]
struct ProvisioningService {
    /// Writable: simulates Wi-Fi credential writes. Writing `I` switches the
    /// Hub to immediate acks, `S` back to server-round-trip acks.
    #[characteristic(uuid = "c01df4a0-0001-4000-8000-00000000c0de", read, write)]
    creds: heapless::Vec<u8, 128>,
    /// Notify: [writes u32 LE, ack mode, wifi up, wifi channel, 0].
    #[characteristic(uuid = "c01df4a0-0002-4000-8000-00000000c0de", read, notify)]
    status: [u8; 8],
}

// ---------------------------------------------------------------------------
// main
// ---------------------------------------------------------------------------

#[esp_rtos::main]
async fn main(spawner: Spawner) -> ! {
    esp_println::logger::init_logger_from_env();
    let peripherals = esp_hal::init(esp_hal::Config::default().with_cpu_clock(CpuClock::max()));

    // Wi-Fi + BLE (coex) + TLS need a lot of heap: reclaimed bootloader RAM
    // plus a regular heap.
    esp_alloc::heap_allocator!(#[ram(reclaimed)] size: 64 * 1024);
    esp_alloc::heap_allocator!(size: 144 * 1024);

    let timg0 = TimerGroup::new(peripherals.TIMG0);
    esp_rtos::start(timg0.timer0, peripherals.FROM_CPU_INTR0);

    info!(
        "SPIKE hub starting: url={} method={} ack_mode={} wifi_ps={} tx_power={} hw_accel={} heartbeat={}s ble={}",
        URL,
        HTTP_METHOD,
        if ACK_IMMEDIATE.load(Relaxed) {
            "immediate"
        } else {
            "server"
        },
        WIFI_PS,
        WIFI_TX_POWER,
        TLS_HW_ACCEL,
        HEARTBEAT_EVERY.as_secs(),
        BLE_ENABLED
    );
    if SSID.is_empty() {
        error!(
            "SPIKE_WIFI_SSID was not set at build time; rebuild with SPIKE_WIFI_SSID=... SPIKE_WIFI_PASSWORD=..."
        );
    }

    // ---- TLS prerequisites: TRNG + optional HW accel ---------------------
    let _trng_source = mk_static!(
        TrngSource<'static>,
        TrngSource::new(peripherals.RNG, peripherals.ADC1)
    );
    let trng = mk_static!(Trng, Trng::try_new().expect("TRNG"));
    let seed = (trng.random() as u64) << 32 | trng.random() as u64;

    if TLS_HW_ACCEL {
        let accel = mk_static!(
            EspAccel<'static>,
            EspAccel::new()
                .with_sha(peripherals.SHA)
                .with_rsa(peripherals.RSA)
                .with_aes(peripherals.AES)
        );
        let queue = mk_static!(EspAccelQueue<'static, 'static>, accel.start());
        // SAFETY: hooks registered before any mbedTLS use; guard leaked so the
        // hooks stay registered for the program's lifetime.
        let guard = unsafe { queue.hook() };
        core::mem::forget(guard);
    }
    let tls = Tls::new(trng).expect("mbedTLS instance");

    // Wall clock for X.509 validity checks, fed by SNTP (AD-11). Only wired
    // into mbedTLS with `--features tls-time-check` (forces an on-the-fly
    // mbedTLS C rebuild, see README).
    let rtc: &'static Rtc<'static> = mk_static!(Rtc<'static>, Rtc::new(peripherals.RTC_TIMER));
    #[cfg(feature = "tls-time-check")]
    {
        use mbedtls_rs::sys::hook::backend::{
            embassy::timer::EmbassyTimer, esp::wall_clock::EspRtcWallClock,
        };
        let timer = mk_static!(EmbassyTimer, EmbassyTimer);
        let clock = mk_static!(
            EspRtcWallClock<&'static Rtc<'static>>,
            EspRtcWallClock::new(rtc)
        );
        // SAFETY: installed once, before any X.509 use; both are 'static.
        unsafe {
            mbedtls_rs::sys::hook::timer::hook_timer(Some(timer));
            mbedtls_rs::sys::hook::wall_clock::hook_wall_clock(Some(clock));
        }
        info!("TLS X.509 validity-time checking is ON (wall clock from SNTP)");
    }

    // ---- BLE (init before Wi-Fi, as in the esp-hal coex example) ---------
    let ble_controller: Option<ExternalController<_, 1>> = if BLE_ENABLED {
        match BleConnector::new(peripherals.BT, Default::default()) {
            Ok(c) => Some(ExternalController::new(c)),
            Err(e) => panic!("BLE init failed: {:?}", e),
        }
    } else {
        warn!("BLE disabled at build time (SPIKE_BLE=0): A/B baseline run");
        None
    };

    // ---- Wi-Fi STA -------------------------------------------------------
    let station_config = station_config(None);
    let mut controller = WifiController::new(
        peripherals.WIFI,
        ControllerConfig::default().with_initial_config(station_config),
    )
    .expect("wifi controller");
    if let Err(e) = controller.set_max_tx_power(WIFI_TX_POWER as i8) {
        warn!("set_max_tx_power failed: {:?}", e);
    }
    let ps = match WIFI_PS {
        "min" => PowerSaveMode::Minimum,
        "max" => PowerSaveMode::Maximum,
        _ => PowerSaveMode::None,
    };
    match controller.set_power_saving(ps) {
        Ok(()) => info!("wifi power save = {:?}", ps),
        Err(e) => warn!(
            "wifi power save {:?} rejected: {:?} (coex may require modem sleep)",
            ps, e
        ),
    }

    // ---- ESP-NOW on the same radio ---------------------------------------
    let esp_now = controller.esp_now();
    info!("esp-now version {:?}", esp_now.version());

    // ---- network stack ---------------------------------------------------
    let (stack, runner) = embassy_net::new(
        Interface::station(),
        embassy_net::Config::dhcpv4(Default::default()),
        mk_static!(StackResources<4>, StackResources::<4>::new()),
        seed,
    );

    spawner.spawn(wifi_task(controller).unwrap());
    spawner.spawn(net_task(runner).unwrap());
    if let Some(c) = ble_controller {
        spawner.spawn(ble_task(c).unwrap());
    }
    spawner.spawn(esp_now_task(esp_now).unwrap());
    spawner.spawn(heartbeat_task().unwrap());
    spawner.spawn(stats_task().unwrap());

    // The HTTPS worker runs on the main task (it owns the `Tls` instance).
    https_worker(stack, tls, rtc).await
}

// ---------------------------------------------------------------------------
// Wi-Fi
// ---------------------------------------------------------------------------

#[embassy_executor::task]
async fn wifi_task(mut controller: WifiController<'static>) {
    let mut roamed = false;
    loop {
        info!("WIFI connecting to '{}'", SSID);
        match controller.connect_async().await {
            Ok(i) => {
                WIFI_CONNECTS.fetch_add(1, Relaxed);
                WIFI_CHANNEL.store(i.channel as u32, Relaxed);
                WIFI_UP.store(true, Relaxed);
                info!(
                    "WIFI associated bssid={} channel={} auth={:?} (connect #{})",
                    Mac(&i.bssid),
                    i.channel,
                    i.authmode,
                    WIFI_CONNECTS.load(Relaxed)
                );
                // Wait for disconnect, sampling RSSI periodically.
                loop {
                    match select(
                        controller.wait_for_disconnect_async(),
                        Timer::after(Duration::from_secs(5)),
                    )
                    .await
                    {
                        Either::First(r) => {
                            WIFI_UP.store(false, Relaxed);
                            WIFI_DISCONNECTS.fetch_add(1, Relaxed);
                            warn!("WIFI disconnected: {:?}", r);
                            break;
                        }
                        Either::Second(()) => {
                            if !roamed
                                && ROAM_AT_S > 0
                                && Instant::now().as_secs() >= ROAM_AT_S as u64
                            {
                                roamed = true;
                                if let Some(mac) = ROAM_BSSID.and_then(parse_mac) {
                                    warn!("WIFI forced roam to bssid={} (uptime {} s)", Mac(&mac), Instant::now().as_secs());
                                    if let Err(e) = controller.set_config(&station_config(Some(mac))) {
                                        warn!("WIFI roam set_config failed: {:?}", e);
                                    }
                                    let _ = controller.disconnect_async().await;
                                    WIFI_UP.store(false, Relaxed);
                                    WIFI_DISCONNECTS.fetch_add(1, Relaxed);
                                    break;
                                } else {
                                    warn!("WIFI roam requested but SPIKE_ROAM_BSSID missing/invalid");
                                }
                            }
                            if let Ok(rssi) = controller.rssi() {
                                WIFI_RSSI.store(rssi, Relaxed);
                            }
                            if let Ok((ch, _)) = controller.channel() {
                                let old = WIFI_CHANNEL.swap(ch as u32, Relaxed);
                                if old != ch as u32 {
                                    warn!("WIFI channel changed {} -> {}", old, ch);
                                }
                            }
                        }
                    }
                }
            }
            Err(e) => {
                WIFI_UP.store(false, Relaxed);
                warn!("WIFI connect failed: {:?}", e);
            }
        }
        Timer::after(Duration::from_secs(2)).await;
    }
}

#[embassy_executor::task]
async fn net_task(mut runner: Runner<'static, Interface>) {
    runner.run().await
}

// ---------------------------------------------------------------------------
// ESP-NOW
// ---------------------------------------------------------------------------

#[embassy_executor::task]
async fn esp_now_task(mut esp_now: EspNow) {
    let mut buf = [0u8; 64];
    loop {
        let r = esp_now.receive_async().await;
        let t_rx = Instant::now();
        NOW_RX.fetch_add(1, Relaxed);
        let src = r.info.src_address;
        let Some(frame) = Frame::decode(r.data()) else {
            continue;
        };

        if !esp_now.peer_exists(&src) {
            let res = esp_now.add_peer(PeerInfo {
                interface: EspNowWifiInterface::Station,
                peer_address: src,
                lmk: None,
                channel: None, // follow the STA's current channel
                encrypt: false,
            });
            info!("ESPNOW new peer {} ({:?})", Mac(&src), res);
        }

        match frame {
            Frame::Probe { nonce, channel } => {
                NOW_PROBES.fetch_add(1, Relaxed);
                let ch = WIFI_CHANNEL.load(Relaxed) as u8;
                info!(
                    "ESPNOW probe from {} (node thinks ch {}, hub ch {})",
                    Mac(&src),
                    channel,
                    ch
                );
                let len = Frame::ProbeReply { nonce, channel: ch }.encode(&mut buf);
                if esp_now.send_async(&src, &buf[..len]).await.is_err() {
                    NOW_SEND_FAIL.fetch_add(1, Relaxed);
                }
            }
            Frame::Data { seq } => {
                NOW_DATA.fetch_add(1, Relaxed);
                let immediate = ACK_IMMEDIATE.load(Relaxed);
                let (mode, server) = if immediate {
                    (ACK_MODE_IMMEDIATE, SERVER_SKIPPED)
                } else {
                    (ACK_MODE_SERVER, relay_to_server(seq).await)
                };
                if server == SERVER_FAILED {
                    NOW_RELAY_FAIL.fetch_add(1, Relaxed);
                }
                let hub_us = t_rx.elapsed().as_micros() as u32;
                let len = Frame::Ack {
                    seq,
                    mode,
                    server,
                    hub_us,
                }
                .encode(&mut buf);
                match esp_now.send_async(&src, &buf[..len]).await {
                    Ok(()) => {
                        NOW_ACKS.fetch_add(1, Relaxed);
                    }
                    Err(e) => {
                        NOW_SEND_FAIL.fetch_add(1, Relaxed);
                        warn!("ESPNOW ack seq={} send failed: {:?}", seq, e);
                    }
                }
                let total_us = t_rx.elapsed().as_micros() as u32;
                ACK_LAT.record(total_us / 1000);
                info!(
                    "ESPNOW data seq={} from {} rssi={} mode={} server={} rx_to_ack_us={}",
                    seq,
                    Mac(&src),
                    r.info.rx_control.rssi,
                    mode_name(mode),
                    server,
                    total_us
                );
            }
            _ => {}
        }
    }
}

/// Performs the "server round trip" for one Node frame via the HTTPS worker.
async fn relay_to_server(seq: u32) -> u8 {
    RELAY_DONE.reset();
    if HTTP_REQS
        .try_send(HttpReq {
            kind: ReqKind::Relay,
            seq,
        })
        .is_err()
    {
        warn!("RELAY seq={} dropped: HTTPS queue full", seq);
        return SERVER_FAILED;
    }
    let deadline = Instant::now() + RELAY_TIMEOUT;
    loop {
        let Some(left) = deadline.checked_duration_since(Instant::now()) else {
            warn!(
                "RELAY seq={} timed out after {} ms",
                seq,
                RELAY_TIMEOUT.as_millis()
            );
            return SERVER_FAILED;
        };
        match with_timeout(left, RELAY_DONE.wait()).await {
            Ok((s, ok)) if s == seq => return if ok { SERVER_OK } else { SERVER_FAILED },
            Ok(_) => continue, // stale completion from an earlier timed-out relay
            Err(_) => continue,
        }
    }
}

// ---------------------------------------------------------------------------
// Heartbeat + stats
// ---------------------------------------------------------------------------

#[embassy_executor::task]
async fn heartbeat_task() {
    let mut ticker = Ticker::every(HEARTBEAT_EVERY);
    loop {
        ticker.next().await;
        if HTTP_REQS
            .try_send(HttpReq {
                kind: ReqKind::Heartbeat,
                seq: 0,
            })
            .is_err()
        {
            HB_FAIL.fetch_add(1, Relaxed);
            warn!("HEARTBEAT dropped: HTTPS queue full");
        }
    }
}

#[embassy_executor::task]
async fn stats_task() {
    let mut ticker = Ticker::every(STATS_EVERY);
    loop {
        ticker.next().await;
        let hs = esp_alloc::HEAP.stats();
        let min_free = hs.size.saturating_sub(hs.max_usage);
        let ack = ACK_LAT.summary();
        let warm = HTTPS_WARM.summary();
        let cold = HTTPS_COLD.summary();
        let hsl = HANDSHAKE_LAT.summary();
        let unix_s = if SNTP_OK.load(Relaxed) {
            UNIX_S_AT_BOOT.load(Relaxed) as u64 + Instant::now().as_secs()
        } else {
            0
        };
        info!(
            "STATS hub up_s={} unix_s={} heap_size={} heap_free={} heap_used={} heap_min_free={} \
             wifi_up={} wifi_ch={} wifi_rssi={} wifi_connects={} wifi_disconnects={} \
             now_rx={} now_data={} now_probes={} now_acks={} now_send_fail={} now_relay_fail={} ack_mode={} \
             ble_connected={} ble_conns={} ble_writes={} ble_disconnects={} ble_adv_err={} \
             tls_ok={} tls_fail={} tls_handshakes={} tls_handshake_fail={} tls_hs_last_ms={} tls_hs_p50_ms={} tls_hs_max_ms={} \
             hb_ok={} hb_fail={} http_last_status={} http_reconnects={} ble_enabled={} \
             ack_n={} ack_p50_ms={} ack_p95_ms={} ack_max_ms={} \
             https_warm_n={} https_warm_p50_ms={} https_warm_p95_ms={} https_warm_max_ms={} \
             https_cold_n={} https_cold_p50_ms={} https_cold_p95_ms={} https_cold_max_ms={}",
            Instant::now().as_secs(),
            unix_s,
            hs.size,
            hs.size - hs.current_usage,
            hs.current_usage,
            min_free,
            WIFI_UP.load(Relaxed) as u8,
            WIFI_CHANNEL.load(Relaxed),
            WIFI_RSSI.load(Relaxed),
            WIFI_CONNECTS.load(Relaxed),
            WIFI_DISCONNECTS.load(Relaxed),
            NOW_RX.load(Relaxed),
            NOW_DATA.load(Relaxed),
            NOW_PROBES.load(Relaxed),
            NOW_ACKS.load(Relaxed),
            NOW_SEND_FAIL.load(Relaxed),
            NOW_RELAY_FAIL.load(Relaxed),
            if ACK_IMMEDIATE.load(Relaxed) {
                "immediate"
            } else {
                "server"
            },
            BLE_CONNECTED.load(Relaxed) as u8,
            BLE_CONNS.load(Relaxed),
            BLE_WRITES.load(Relaxed),
            BLE_DISCONNECTS.load(Relaxed),
            BLE_ADV_ERRORS.load(Relaxed),
            TLS_OK.load(Relaxed),
            TLS_FAIL.load(Relaxed),
            TLS_HANDSHAKES.load(Relaxed),
            TLS_HANDSHAKE_FAIL.load(Relaxed),
            TLS_HANDSHAKE_MS_LAST.load(Relaxed),
            hsl.p50,
            hsl.max_all,
            HB_OK.load(Relaxed),
            HB_FAIL.load(Relaxed),
            HTTP_LAST_STATUS.load(Relaxed),
            HTTP_RECONNECTS.load(Relaxed),
            BLE_ENABLED as u8,
            ack.n,
            ack.p50,
            ack.p95,
            ack.max_all,
            warm.n,
            warm.p50,
            warm.p95,
            warm.max_all,
            cold.n,
            cold.p50,
            cold.p95,
            cold.max_all,
        );
    }
}

// ---------------------------------------------------------------------------
// BLE
// ---------------------------------------------------------------------------

#[embassy_executor::task]
async fn ble_task(controller: ExternalController<BleConnector<'static>, 1>) {
    let address = Address::random([0xc0, 0xde, 0xf4, 0x1d, 0xc0, 0xff]);
    let mut resources: HostResources<_, DefaultPacketPool, CONNECTIONS_MAX, L2CAP_CHANNELS_MAX> =
        HostResources::new();
    let stack = trouble_host::new(controller, &mut resources)
        .set_random_address(address)
        .build();
    let mut peripheral = stack.peripheral();
    let mut runner = stack.runner();

    let mut adv_data = [0; 31];
    let adv_len = AdStructure::encode_slice(
        &[
            AdStructure::Flags(LE_GENERAL_DISCOVERABLE | BR_EDR_NOT_SUPPORTED),
            AdStructure::CompleteLocalName(b"CF-HUB-SPIKE"),
        ],
        &mut adv_data[..],
    )
    .unwrap();

    let server = Server::new_with_config(GapConfig::Peripheral(PeripheralConfig {
        name: "CF-HUB-SPIKE",
        appearance: &appearance::UNKNOWN,
    }))
    .unwrap();

    info!("BLE advertising as CF-HUB-SPIKE ({:?})", address);
    let _ = join(runner.run(), async {
        let mut params = AdvertisementParameters::default();
        params.interval_min = Duration::from_millis(100);
        params.interval_max = Duration::from_millis(150);
        loop {
            let adv = match peripheral
                .advertise(
                    &params,
                    Advertisement::ConnectableScannableUndirected {
                        adv_data: &adv_data[..adv_len],
                        scan_data: &[],
                    },
                )
                .await
            {
                Ok(a) => a,
                Err(e) => {
                    BLE_ADV_ERRORS.fetch_add(1, Relaxed);
                    warn!("BLE advertise error: {:?}", e);
                    Timer::after(Duration::from_secs(1)).await;
                    continue;
                }
            };
            let conn = match adv.accept().await {
                Ok(c) => c,
                Err(e) => {
                    BLE_ADV_ERRORS.fetch_add(1, Relaxed);
                    warn!("BLE accept error: {:?}", e);
                    continue;
                }
            };
            match conn.with_attribute_server(&server) {
                Ok(conn) => {
                    BLE_CONNS.fetch_add(1, Relaxed);
                    BLE_CONNECTED.store(true, Relaxed);
                    info!("BLE connected (#{})", BLE_CONNS.load(Relaxed));
                    gatt_events(&server, &conn).await;
                    BLE_CONNECTED.store(false, Relaxed);
                    BLE_DISCONNECTS.fetch_add(1, Relaxed);
                }
                Err(e) => warn!("BLE attribute server error: {:?}", e),
            }
        }
    })
    .await;
    error!("BLE runner exited");
}

async fn gatt_events<P: PacketPool>(server: &Server<'_>, conn: &GattConnection<'_, '_, P>) {
    let creds = &server.prov.creds;
    let status = &server.prov.status;
    let reason = loop {
        match conn.next().await {
            GattConnectionEvent::Disconnected { reason } => break reason,
            GattConnectionEvent::Gatt { event } => {
                let mut wrote = false;
                if let GattEvent::Write(w) = &event
                    && w.handle() == creds.handle
                {
                    wrote = true;
                    let n = BLE_WRITES.fetch_add(1, Relaxed) + 1;
                    w.with_data(|_offset, data| match data.first() {
                        Some(b'I') | Some(b'i') => {
                            ACK_IMMEDIATE.store(true, Relaxed);
                            info!("BLE write #{}: ack mode -> immediate", n);
                        }
                        Some(b'S') | Some(b's') => {
                            ACK_IMMEDIATE.store(false, Relaxed);
                            info!("BLE write #{}: ack mode -> server", n);
                        }
                        _ => info!("BLE write #{} ({} bytes)", n, data.len()),
                    });
                }
                match event.accept() {
                    Ok(reply) => reply.send().await,
                    Err(e) => warn!("BLE gatt reply error: {:?}", e),
                }
                if wrote {
                    let n = BLE_WRITES.load(Relaxed).to_le_bytes();
                    let v = [
                        n[0],
                        n[1],
                        n[2],
                        n[3],
                        if ACK_IMMEDIATE.load(Relaxed) {
                            ACK_MODE_IMMEDIATE
                        } else {
                            ACK_MODE_SERVER
                        },
                        WIFI_UP.load(Relaxed) as u8,
                        WIFI_CHANNEL.load(Relaxed) as u8,
                        0,
                    ];
                    // Not subscribed is fine; notify errors are only logged.
                    if let Err(e) = status.notify(conn, &v, true).await {
                        info!("BLE notify skipped: {:?}", e);
                    }
                }
            }
            _ => {}
        }
    };
    info!("BLE disconnected: {:?}", reason);
}

// ---------------------------------------------------------------------------
// HTTPS worker (TLS client, keep-alive) + SNTP
// ---------------------------------------------------------------------------

struct Target {
    host: &'static str,
    port: u16,
    path: &'static str,
}

fn parse_url(url: &'static str) -> Target {
    let rest = url.strip_prefix("https://").unwrap_or(url);
    let (hostport, path) = match rest.find('/') {
        Some(i) => (&rest[..i], &rest[i..]),
        None => (rest, "/"),
    };
    let (host, port) = match hostport.rfind(':') {
        Some(i) => (&hostport[..i], hostport[i + 1..].parse().unwrap_or(443)),
        None => (hostport, 443),
    };
    Target { host, port, path }
}

#[derive(Debug)]
#[allow(dead_code)] // fields are read through Debug in logs
enum HttpError {
    Dns,
    Connect,
    Tls(SessionError),
    Timeout,
    Protocol,
    NoNetwork,
}

async fn https_worker(stack: Stack<'static>, tls: Tls<'static>, rtc: &'static Rtc<'static>) -> ! {
    let target = parse_url(URL);
    let mut sni: heapless::String<128> = heapless::String::new();
    let _ = sni.push_str(target.host);
    let _ = sni.push('\0');
    let sni = CStr::from_bytes_with_nul(sni.as_bytes()).expect("host");

    // Wait for DHCP, then SNTP before the first TLS connection (AD-11).
    stack.wait_config_up().await;
    if let Some(c) = stack.config_v4() {
        info!("NET got ip {}", c.address);
    }
    match sntp(stack).await {
        Some(unix_ms) => rtc.set_current_time_us(unix_ms * 1000),
        None if cfg!(feature = "tls-time-check") => {
            error!("SNTP failed: with tls-time-check on, certificate validation will fail closed")
        }
        None => {}
    }
    #[cfg(not(feature = "tls-time-check"))]
    warn!(
        "TLS X.509 validity-time checking is OFF: built without `tls-time-check` (mbedtls-rs `hook-wall-clock`); \
         chain, signature and hostname are still verified. See docs/spikes/hub-radio-coexistence.md."
    );

    let ca = Certificate::new(X509::PEM(CA_BUNDLE)).expect("CA bundle parse");
    let conf = SessionConfig::Client(ClientSessionConfig {
        ca_chain: Some(ca),
        server_name: Some(sni),
        ..ClientSessionConfig::new()
    });

    let mut rx_buf = [0u8; 4096];
    let mut tx_buf = [0u8; 2048];
    let mut io_buf = [0u8; 1024];
    let mut pending: Option<HttpReq> = None;

    loop {
        // ---- wait for work -----------------------------------------------
        let req = match pending.take() {
            Some(r) => r,
            None => HTTP_REQS.receive().await,
        };
        if !stack.is_config_up() {
            finish(req, Err(HttpError::NoNetwork), false, 0);
            continue;
        }

        // ---- cold connection: DNS + TCP + TLS ----------------------------
        let t_cold = Instant::now();
        let ip =
            match with_timeout(HTTP_TIMEOUT, stack.dns_query(target.host, DnsQueryType::A)).await {
                Ok(Ok(v)) if !v.is_empty() => v[0],
                _ => {
                    finish(req, Err(HttpError::Dns), false, 0);
                    continue;
                }
            };
        let mut socket = TcpSocket::new(stack, &mut rx_buf, &mut tx_buf);
        socket.set_timeout(Some(HTTP_TIMEOUT));
        let remote = (ip, target.port);
        if !matches!(
            with_timeout(HTTP_TIMEOUT, socket.connect(remote)).await,
            Ok(Ok(()))
        ) {
            finish(req, Err(HttpError::Connect), false, 0);
            continue;
        }
        let mut session = match Session::new(tls.reference(), socket, &conf) {
            Ok(s) => s,
            Err(e) => {
                finish(req, Err(HttpError::Tls(e)), false, 0);
                continue;
            }
        };
        let t_hs = Instant::now();
        match with_timeout(HTTP_TIMEOUT, session.connect()).await {
            Ok(Ok(())) => {
                let ms = t_hs.elapsed().as_millis() as u32;
                TLS_HANDSHAKES.fetch_add(1, Relaxed);
                TLS_HANDSHAKE_MS_LAST.store(ms, Relaxed);
                HANDSHAKE_LAT.record(ms);
                info!(
                    "TLS handshake ok in {} ms ({:?}, heap_free={})",
                    ms,
                    session.tls_version(),
                    esp_alloc::HEAP.free()
                );
            }
            Ok(Err(e)) => {
                TLS_HANDSHAKE_FAIL.fetch_add(1, Relaxed);
                error!(
                    "TLS handshake failed: {:?} (verify flags 0x{:x})",
                    e,
                    session.tls_verification_details()
                );
                finish(req, Err(HttpError::Tls(e)), false, 0);
                continue;
            }
            Err(_) => {
                TLS_HANDSHAKE_FAIL.fetch_add(1, Relaxed);
                finish(req, Err(HttpError::Timeout), false, 0);
                continue;
            }
        }

        // ---- request loop on this connection -----------------------------
        let mut current = Some(req);
        let mut cold = true;
        loop {
            let req = match current.take() {
                Some(r) => r,
                // Idle on a kept-alive connection; servers usually close idle
                // connections after some time, which surfaces on the next write.
                None => HTTP_REQS.receive().await,
            };
            let t0 = if cold { t_cold } else { Instant::now() };
            let res = with_timeout(
                HTTP_TIMEOUT,
                exchange(&mut session, &target, req, &mut io_buf),
            )
            .await;
            let res = match res {
                Ok(r) => r,
                Err(_) => Err(HttpError::Timeout),
            };
            let ms = t0.elapsed().as_millis() as u32;
            match res {
                Ok((status, keep_alive)) => {
                    finish(req, Ok(status), cold, ms);
                    if !keep_alive {
                        info!("HTTP server closed the connection (Connection: close)");
                        break;
                    }
                }
                Err(e) if !cold => {
                    // A warm connection that fails was most likely closed by the
                    // server while idle: retry once on a fresh connection.
                    HTTP_RECONNECTS.fetch_add(1, Relaxed);
                    info!("HTTP warm connection failed ({:?}); reconnecting", e);
                    pending = Some(req);
                    break;
                }
                Err(e) => {
                    finish(req, Err(e), cold, ms);
                    break;
                }
            }
            cold = false;
        }
        let _ = with_timeout(Duration::from_secs(2), session.close()).await;
    }
}

fn finish(req: HttpReq, res: Result<u16, HttpError>, cold: bool, ms: u32) {
    let ok = res.is_ok();
    match &res {
        Ok(status) => {
            TLS_OK.fetch_add(1, Relaxed);
            HTTP_LAST_STATUS.store(*status as u32, Relaxed);
            if cold {
                HTTPS_COLD.record(ms);
            } else {
                HTTPS_WARM.record(ms);
            }
            info!(
                "HTTPS {:?} seq={} status={} {} latency_ms={}",
                req.kind,
                req.seq,
                status,
                if cold { "cold" } else { "warm" },
                ms
            );
        }
        Err(e) => {
            TLS_FAIL.fetch_add(1, Relaxed);
            warn!("HTTPS {:?} seq={} failed: {:?}", req.kind, req.seq, e);
        }
    }
    match req.kind {
        ReqKind::Heartbeat => {
            if ok {
                HB_OK.fetch_add(1, Relaxed);
            } else {
                HB_FAIL.fetch_add(1, Relaxed);
            }
        }
        ReqKind::Relay => RELAY_DONE.signal((req.seq, ok)),
    }
}

/// One HTTP/1.1 request/response on an established TLS session.
/// Returns (status, keep_alive).
async fn exchange<T: Read + Write>(
    session: &mut Session<'_, T>,
    target: &Target,
    req: HttpReq,
    buf: &mut [u8],
) -> Result<(u16, bool), HttpError> {
    let mut body: heapless::String<192> = heapless::String::new();
    let _ = write!(
        body,
        "{{\"hub\":\"spike\",\"kind\":\"{}\",\"seq\":{},\"uptimeMs\":{}}}",
        match req.kind {
            ReqKind::Relay => "relay",
            ReqKind::Heartbeat => "heartbeat",
        },
        req.seq,
        Instant::now().as_millis()
    );
    let post = !str_eq(HTTP_METHOD, "GET");
    let mut head: heapless::String<384> = heapless::String::new();
    let _ = write!(
        head,
        "{} {} HTTP/1.1\r\nHost: {}\r\nUser-Agent: coldframe-hub-spike\r\nAccept: */*\r\nConnection: keep-alive\r\n",
        if post { "POST" } else { "GET" },
        target.path,
        target.host
    );
    if post {
        let _ = write!(
            head,
            "Content-Type: application/json\r\nContent-Length: {}\r\n",
            body.len()
        );
    }
    let _ = head.push_str("\r\n");

    session
        .write_all(head.as_bytes())
        .await
        .map_err(HttpError::Tls)?;
    if post {
        session
            .write_all(body.as_bytes())
            .await
            .map_err(HttpError::Tls)?;
    }
    session.flush().await.map_err(HttpError::Tls)?;

    // ---- read headers ----
    let mut filled = 0;
    let header_end = loop {
        if filled == buf.len() {
            return Err(HttpError::Protocol);
        }
        let n = session
            .read(&mut buf[filled..])
            .await
            .map_err(HttpError::Tls)?;
        if n == 0 {
            return Err(HttpError::Protocol);
        }
        filled += n;
        if let Some(i) = find(&buf[..filled], b"\r\n\r\n") {
            break i + 4;
        }
    };
    let head = core::str::from_utf8(&buf[..header_end]).map_err(|_| HttpError::Protocol)?;
    let mut lines = head.split("\r\n");
    let status: u16 = lines
        .next()
        .and_then(|l| l.split(' ').nth(1))
        .and_then(|s| s.parse().ok())
        .ok_or(HttpError::Protocol)?;
    let mut content_length: Option<usize> = None;
    let mut chunked = false;
    let mut keep_alive = true;
    for l in lines {
        let Some((k, v)) = l.split_once(':') else {
            continue;
        };
        let v = v.trim();
        if k.eq_ignore_ascii_case("content-length") {
            content_length = v.parse().ok();
        } else if k.eq_ignore_ascii_case("transfer-encoding") && v.eq_ignore_ascii_case("chunked") {
            chunked = true;
        } else if k.eq_ignore_ascii_case("connection") && v.eq_ignore_ascii_case("close") {
            keep_alive = false;
        }
    }

    // ---- drain body ----
    let already = filled - header_end;
    if let Some(len) = content_length {
        let mut remaining = len.saturating_sub(already);
        while remaining > 0 {
            let n = session.read(buf).await.map_err(HttpError::Tls)?;
            if n == 0 {
                return Err(HttpError::Protocol);
            }
            remaining = remaining.saturating_sub(n);
        }
    } else if chunked {
        // Minimal: read until the terminating zero-length chunk.
        let mut tail = [0u8; 7];
        let mut tail_len = 0;
        let feed = |data: &[u8], tail: &mut [u8; 7], tail_len: &mut usize| -> bool {
            for &b in data {
                if *tail_len < 7 {
                    tail[*tail_len] = b;
                    *tail_len += 1;
                } else {
                    tail.copy_within(1.., 0);
                    tail[6] = b;
                }
                if *tail_len == 7 && &tail[..] == b"\r\n0\r\n\r\n" {
                    return true;
                }
            }
            false
        };
        // The body may start with "0\r\n\r\n" directly (empty chunked body).
        let first = &buf[header_end..filled];
        let mut done = first.starts_with(b"0\r\n\r\n") || feed(first, &mut tail, &mut tail_len);
        while !done {
            let n = session.read(buf).await.map_err(HttpError::Tls)?;
            if n == 0 {
                return Err(HttpError::Protocol);
            }
            done = feed(&buf[..n], &mut tail, &mut tail_len);
        }
    } else if status != 204 && status != 304 {
        // No length: body runs until close.
        loop {
            let n = session.read(buf).await.map_err(HttpError::Tls)?;
            if n == 0 {
                break;
            }
        }
        keep_alive = false;
    }
    Ok((status, keep_alive))
}

fn find(hay: &[u8], needle: &[u8]) -> Option<usize> {
    hay.windows(needle.len()).position(|w| w == needle)
}

/// Minimal SNTP client (RFC 4330): one request, sets `UNIX_MS_AT_BOOT`.
async fn sntp(stack: Stack<'static>) -> Option<u64> {
    const NTP_UNIX_OFFSET: u64 = 2_208_988_800;
    for attempt in 1..=3 {
        let addrs = match stack.dns_query(NTP_SERVER, DnsQueryType::A).await {
            Ok(a) if !a.is_empty() => a,
            _ => {
                warn!("SNTP dns failed for {} (attempt {})", NTP_SERVER, attempt);
                Timer::after(Duration::from_secs(2)).await;
                continue;
            }
        };
        let mut rx_meta = [PacketMetadata::EMPTY; 2];
        let mut rx_buf = [0u8; 128];
        let mut tx_meta = [PacketMetadata::EMPTY; 2];
        let mut tx_buf = [0u8; 128];
        let mut sock = UdpSocket::new(stack, &mut rx_meta, &mut rx_buf, &mut tx_meta, &mut tx_buf);
        if sock.bind(0).is_err() {
            continue;
        }
        let mut pkt = [0u8; 48];
        pkt[0] = 0x23; // LI=0, VN=4, Mode=3 (client)
        let t0 = Instant::now();
        if sock.send_to(&pkt, (addrs[0], 123)).await.is_err() {
            continue;
        }
        let mut resp = [0u8; 48];
        match with_timeout(Duration::from_secs(3), sock.recv_from(&mut resp)).await {
            Ok(Ok((n, _))) if n >= 48 => {
                let rtt_ms = t0.elapsed().as_millis();
                let secs = u32::from_be_bytes([resp[40], resp[41], resp[42], resp[43]]) as u64;
                let frac = u32::from_be_bytes([resp[44], resp[45], resp[46], resp[47]]) as u64;
                let unix_ms = (secs.saturating_sub(NTP_UNIX_OFFSET)) * 1000
                    + ((frac * 1000) >> 32)
                    + rtt_ms / 2;
                UNIX_S_AT_BOOT.store(
                    ((unix_ms / 1000).saturating_sub(Instant::now().as_secs())) as u32,
                    Relaxed,
                );
                SNTP_OK.store(true, Relaxed);
                info!(
                    "SNTP ok: unix_s={} (server {}, rtt {} ms)",
                    unix_ms / 1000,
                    NTP_SERVER,
                    rtt_ms
                );
                return Some(unix_ms);
            }
            _ => warn!("SNTP no/short reply (attempt {})", attempt),
        }
    }
    warn!("SNTP failed; continuing without wall clock");
    None
}
