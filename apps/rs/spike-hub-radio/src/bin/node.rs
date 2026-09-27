//! Spike Node (test harness, second board).
//!
//! ESP-NOW only, no Wi-Fi association. Finds the Hub by probing channels 1–13,
//! then sends one DATA frame every `SPIKE_NODE_INTERVAL_MS` and waits up to
//! `SPIKE_NODE_ACK_TIMEOUT_MS` for the matching ACK. Logs every frame and a
//! machine-parsable `STATS node ...` line every 10 s.
//!
//! After `SPIKE_NODE_RESCAN_AFTER` consecutive failures (MAC send failure or ack
//! timeout) it re-scans all channels: this prototypes FR-4 channel following.

#![no_std]
#![no_main]

use embassy_executor::Spawner;
use embassy_time::{Duration, Instant, Timer, with_timeout};
use esp_backtrace as _;
use esp_hal::{clock::CpuClock, timer::timg::TimerGroup};
use esp_radio::esp_now::{BROADCAST_ADDRESS, EspNowWifiInterface, PeerInfo};
use log::{info, warn};
use spike_hub_radio::{
    ACK_MODE_IMMEDIATE, ACK_MODE_SERVER, Frame, Latency, Mac, SERVER_OK, mode_name, parse_u32,
};

esp_bootloader_esp_idf::esp_app_desc!();

const INTERVAL_MS: u32 = parse_u32(option_env!("SPIKE_NODE_INTERVAL_MS"), 1000);
const ACK_TIMEOUT_MS: u32 = parse_u32(option_env!("SPIKE_NODE_ACK_TIMEOUT_MS"), 10000);
const RESCAN_AFTER: u32 = parse_u32(option_env!("SPIKE_NODE_RESCAN_AFTER"), 3);
/// How long to listen for a probe reply on each channel.
const PROBE_WAIT_MS: u64 = 120;
const STATS_EVERY: Duration = Duration::from_secs(10);

#[derive(Default)]
struct Counters {
    sent: u32,
    mac_fail: u32,
    acked: u32,
    lost: u32,
    late_or_dup: u32,
    server_fail_acks: u32,
    scans: u32,
    scan_ms_last: u32,
    channel: u8,
}

#[esp_rtos::main]
async fn main(_spawner: Spawner) -> ! {
    esp_println::logger::init_logger_from_env();
    let peripherals = esp_hal::init(esp_hal::Config::default().with_cpu_clock(CpuClock::max()));

    esp_alloc::heap_allocator!(size: 72 * 1024);

    let timg0 = TimerGroup::new(peripherals.TIMG0);
    esp_rtos::start(timg0.timer0, peripherals.FROM_CPU_INTR0);

    info!(
        "SPIKE node starting: interval={}ms ack_timeout={}ms rescan_after={}",
        INTERVAL_MS, ACK_TIMEOUT_MS, RESCAN_AFTER
    );

    let controller = esp_radio::wifi::WifiController::new(peripherals.WIFI, Default::default())
        .expect("wifi controller");
    let mut esp_now = controller.esp_now();
    // The controller is not needed any more: no association on the Node.
    core::mem::forget(controller);
    info!("esp-now version {:?}", esp_now.version());

    let mut c = Counters::default();
    let mut rtt_server = Latency::new();
    let mut rtt_immediate = Latency::new();
    let mut hub_mac: [u8; 6];
    let mut seq: u32 = 0;
    let mut consecutive_fail: u32;
    let mut last_stats = Instant::now();
    let mut buf = [0u8; 64];

    'scan: loop {
        // ---- channel scan -------------------------------------------------
        let scan_start = Instant::now();
        c.scans += 1;
        let mut round: u32 = 0;
        hub_mac = loop {
            let mut found = None;
            for ch in 1u8..=13 {
                if esp_now.set_channel(ch).is_err() {
                    warn!("set_channel({}) failed", ch);
                    continue;
                }
                let nonce = (scan_start.as_ticks() as u32) ^ (ch as u32) ^ (round << 8);
                let len = Frame::Probe { nonce, channel: ch }.encode(&mut buf);
                let _ = esp_now.send_async(&BROADCAST_ADDRESS, &buf[..len]).await;
                let deadline = Instant::now() + Duration::from_millis(PROBE_WAIT_MS);
                while let Some(left) = deadline.checked_duration_since(Instant::now()) {
                    match with_timeout(left, esp_now.receive_async()).await {
                        Ok(r) => {
                            if let Some(Frame::ProbeReply { nonce: n, channel }) =
                                Frame::decode(r.data())
                                && n == nonce
                            {
                                found = Some((r.info.src_address, channel));
                                break;
                            }
                        }
                        Err(_) => break,
                    }
                }
                if found.is_some() {
                    break;
                }
            }
            if let Some((mac, ch)) = found {
                c.channel = ch;
                c.scan_ms_last = scan_start.elapsed().as_millis() as u32;
                info!(
                    "SCAN found hub {} on channel {} after {} ms ({} rounds)",
                    Mac(&mac),
                    ch,
                    c.scan_ms_last,
                    round + 1
                );
                break mac;
            }
            round += 1;
            if round % 5 == 0 {
                warn!("SCAN no hub found after {} rounds, still scanning", round);
            }
            Timer::after(Duration::from_millis(200)).await;
        };

        if !esp_now.peer_exists(&hub_mac) {
            let _ = esp_now.add_peer(PeerInfo {
                interface: EspNowWifiInterface::Station,
                peer_address: hub_mac,
                lmk: None,
                channel: None,
                encrypt: false,
            });
        }
        consecutive_fail = 0;

        // ---- data loop ----------------------------------------------------
        let mut next_send = Instant::now();
        loop {
            Timer::at(next_send).await;
            next_send += Duration::from_millis(INTERVAL_MS as u64);

            seq = seq.wrapping_add(1);
            let len = Frame::Data { seq }.encode(&mut buf);
            let t0 = Instant::now();
            c.sent += 1;
            let mac_ok = esp_now.send_async(&hub_mac, &buf[..len]).await.is_ok();
            let mut ok = false;
            if !mac_ok {
                c.mac_fail += 1;
                c.lost += 1;
                warn!("FRAME seq={} result=mac_fail", seq);
            } else {
                // Wait for the matching ack.
                let deadline = t0 + Duration::from_millis(ACK_TIMEOUT_MS as u64);
                loop {
                    let Some(left) = deadline.checked_duration_since(Instant::now()) else {
                        c.lost += 1;
                        warn!(
                            "FRAME seq={} result=timeout after {} ms",
                            seq, ACK_TIMEOUT_MS
                        );
                        break;
                    };
                    match with_timeout(left, esp_now.receive_async()).await {
                        Ok(r) => match Frame::decode(r.data()) {
                            Some(Frame::Ack {
                                seq: s,
                                mode,
                                server,
                                hub_us,
                            }) if s == seq => {
                                let rtt_us = t0.elapsed().as_micros() as u32;
                                match mode {
                                    ACK_MODE_SERVER => rtt_server.record(rtt_us / 1000),
                                    ACK_MODE_IMMEDIATE => rtt_immediate.record(rtt_us / 1000),
                                    _ => {}
                                }
                                if server != SERVER_OK && mode == ACK_MODE_SERVER {
                                    c.server_fail_acks += 1;
                                }
                                c.acked += 1;
                                ok = true;
                                info!(
                                    "FRAME seq={} result=acked rtt_us={} hub_us={} mode={} server={} rssi={}",
                                    seq,
                                    rtt_us,
                                    hub_us,
                                    mode_name(mode),
                                    server,
                                    r.info.rx_control.rssi
                                );
                                break;
                            }
                            Some(Frame::Ack { .. }) => c.late_or_dup += 1,
                            _ => {}
                        },
                        Err(_) => {} // loop re-checks deadline
                    }
                }
            }

            if ok {
                consecutive_fail = 0;
            } else {
                consecutive_fail += 1;
            }

            if last_stats.elapsed() >= STATS_EVERY {
                last_stats = Instant::now();
                log_stats(&c, &rtt_server, &rtt_immediate);
            }

            if consecutive_fail >= RESCAN_AFTER {
                warn!(
                    "{} consecutive failures on channel {}: re-scanning",
                    consecutive_fail, c.channel
                );
                continue 'scan;
            }
        }
    }
}

fn log_stats(c: &Counters, srv: &Latency, imm: &Latency) {
    let s = srv.summary();
    let i = imm.summary();
    let loss_permille = if c.sent == 0 {
        0
    } else {
        (c.lost as u64 * 1000 / c.sent as u64) as u32
    };
    info!(
        "STATS node up_s={} ch={} sent={} acked={} lost={} loss_permille={} mac_fail={} late_or_dup={} server_fail_acks={} scans={} scan_ms_last={} \
         rtt_server_n={} rtt_server_p50_ms={} rtt_server_p95_ms={} rtt_server_max_ms={} \
         rtt_immediate_n={} rtt_immediate_p50_ms={} rtt_immediate_p95_ms={} rtt_immediate_max_ms={}",
        Instant::now().as_secs(),
        c.channel,
        c.sent,
        c.acked,
        c.lost,
        loss_permille,
        c.mac_fail,
        c.late_or_dup,
        c.server_fail_acks,
        c.scans,
        c.scan_ms_last,
        s.n,
        s.p50,
        s.p95,
        s.max_all,
        i.n,
        i.p50,
        i.p95,
        i.max_all
    );
}
