//! The building blocks: BSSID choice, backoff, the heartbeat schedule, server URLs, RFC 3339
//! times and monotonic stamps.

mod common;

use coldframe_hal::mock::{MockRadio, MockTrng};
use coldframe_hal::{AccessPoint, Security, TrngError};
use coldframe_uplink::url::{DEFAULT_PORT, SERVER_URL_MAX_LENGTH};
use coldframe_uplink::{
    BACKOFF_INITIAL_MS, BACKOFF_MAX_MS, Backoff, HEARTBEAT_MAX_INTERVAL_MS,
    HEARTBEAT_MIN_INTERVAL_MS, HeartbeatSchedule, MonotonicStamp, SelectError, ServerUrl, UrlError,
    parse_rfc3339_ms, select_bssid,
};
use common::{STRONG, scan, trng};

fn ap(ssid: &[u8], last: u8, rssi: i8, security: Security) -> AccessPoint {
    AccessPoint::new(ssid, [0x02, 0, 0, 0, 0, last], last, rssi, security)
}

#[test]
fn the_strongest_bssid_for_the_ssid_wins() {
    let heard = scan();
    let chosen = select_bssid(&heard, b"garden").unwrap();
    assert_eq!(
        (chosen.bssid, chosen.channel, chosen.rssi),
        (STRONG, 6, -50)
    );
    // Scan order does not matter.
    let reversed: Vec<_> = heard.iter().rev().copied().collect();
    assert_eq!(select_bssid(&reversed, b"garden").unwrap().bssid, STRONG);
}

#[test]
fn equally_strong_bssids_keep_the_first_heard() {
    let heard = [
        ap(b"garden", 1, -60, Security::Wpa2Personal),
        ap(b"garden", 2, -60, Security::Wpa2Personal),
        ap(b"garden", 3, -61, Security::Wpa2Personal),
    ];
    assert_eq!(select_bssid(&heard, b"garden").unwrap().bssid[5], 1);
    let swapped = [heard[1], heard[0], heard[2]];
    assert_eq!(select_bssid(&swapped, b"garden").unwrap().bssid[5], 2);
}

#[test]
fn an_absent_ssid_is_not_heard() {
    assert_eq!(
        select_bssid(&scan(), b"absent").err(),
        Some(SelectError::NotHeard)
    );
    assert_eq!(
        select_bssid(&[], b"garden").err(),
        Some(SelectError::NotHeard)
    );
    // A prefix is another SSID.
    assert_eq!(
        select_bssid(&scan(), b"gard").err(),
        Some(SelectError::NotHeard)
    );
}

#[test]
fn unsupported_bssids_are_skipped_and_refused_when_alone() {
    let heard = [
        ap(b"mesh", 1, -40, Security::Wpa3Only),
        ap(b"mesh", 2, -70, Security::Wpa3Transition),
        ap(b"corp", 3, -50, Security::Other),
        ap(b"cafe", 4, -80, Security::Open),
    ];
    assert_eq!(select_bssid(&heard, b"mesh").unwrap().bssid[5], 2);
    assert_eq!(
        select_bssid(&heard, b"corp").err(),
        Some(SelectError::Unsupported)
    );
    assert_eq!(select_bssid(&heard, b"cafe").unwrap().bssid[5], 4);
}

#[test]
fn backoff_doubles_to_sixty_seconds_and_resets() {
    let mut backoff = Backoff::new();
    let waits: Vec<u32> = (0..9).map(|_| backoff.fail()).collect();
    assert_eq!(
        waits,
        [
            1_000, 2_000, 4_000, 8_000, 16_000, 32_000, 60_000, 60_000, 60_000
        ]
    );
    backoff.reset();
    assert_eq!(backoff.fail(), BACKOFF_INITIAL_MS);
    assert_eq!(backoff.fail(), 2 * BACKOFF_INITIAL_MS);
    assert_eq!(BACKOFF_MAX_MS, 60_000);
    // It never overflows.
    let mut backoff = Backoff::new();
    for _ in 0..100 {
        assert!(backoff.fail() <= BACKOFF_MAX_MS);
    }
}

#[test]
fn the_schedule_stays_within_thirty_to_sixty_seconds() {
    assert_eq!(
        (HEARTBEAT_MIN_INTERVAL_MS, HEARTBEAT_MAX_INTERVAL_MS),
        (30_000, 60_000)
    );
    let mut trng = trng(&[]);
    let draws: Vec<u32> = (0..10_000)
        .map(|_| HeartbeatSchedule.next_delay_ms(&mut trng))
        .collect();
    assert!(
        draws
            .iter()
            .all(|&d| (HEARTBEAT_MIN_INTERVAL_MS..=HEARTBEAT_MAX_INTERVAL_MS).contains(&d))
    );
    // The draws spread over the range rather than sitting on one value.
    let low = draws.iter().filter(|&&d| d < 37_500).count();
    let high = draws.iter().filter(|&&d| d > 52_500).count();
    assert!(low > 2_000 && high > 2_000, "low={low} high={high}");
    let mean = draws.iter().map(|&d| u64::from(d)).sum::<u64>() / draws.len() as u64;
    assert!((43_500..=46_500).contains(&mean), "mean={mean}");

    // The extremes of the TRNG map onto the bounds.
    let mut edges = common::trng(&[0, 0, 0, 0, 0x30, 0x75, 0, 0]);
    assert_eq!(HeartbeatSchedule.next_delay_ms(&mut edges), 30_000);
    assert_eq!(HeartbeatSchedule.next_delay_ms(&mut edges), 60_000);
}

#[test]
fn a_failed_trng_falls_back_to_forty_five_seconds() {
    let radio = MockRadio::new();
    let mut off = MockTrng::new(&radio);
    assert_eq!(HeartbeatSchedule.next_delay_ms(&mut off), 45_000);
    let mut failing = trng(&[]);
    failing.fail_with(Some(TrngError::Hardware));
    assert_eq!(HeartbeatSchedule.next_delay_ms(&mut failing), 45_000);
}

#[test]
fn server_urls_that_are_accepted() {
    for (text, host, port) in [
        (
            "https://coldframe.example.org",
            "coldframe.example.org",
            443,
        ),
        (
            "https://coldframe.example.org:8443",
            "coldframe.example.org",
            8443,
        ),
        ("https://a.example.org:1", "a.example.org", 1),
        ("https://a.example.org:65535", "a.example.org", 65535),
        (
            "https://hub-1.lan.example.org",
            "hub-1.lan.example.org",
            443,
        ),
        ("https://localhost", "localhost", 443),
        ("https://x1.example.org", "x1.example.org", 443),
        ("https://1x.example.org", "1x.example.org", 443),
    ] {
        let url = ServerUrl::parse(text).unwrap_or_else(|error| panic!("{text}: {error}"));
        assert_eq!((url.host(), url.port(), url.as_str()), (host, port, text));
    }
    assert_eq!(DEFAULT_PORT, 443);
    // Exactly 100 bytes, with a 63-byte label, is accepted.
    let label = "b".repeat(63);
    let long = format!("https://{label}.{}", "c".repeat(100 - 8 - 64));
    assert_eq!(long.len(), 100);
    assert!(ServerUrl::parse(&long).is_ok(), "{long}");
}

#[test]
fn server_urls_that_are_refused() {
    let long_label = format!("https://{}.example.org", "a".repeat(64));
    let too_long = format!("https://{}.org", "a".repeat(SERVER_URL_MAX_LENGTH));
    for (text, error) in [
        ("", UrlError::Length),
        (too_long.as_str(), UrlError::Length),
        ("http://coldframe.example.org", UrlError::Scheme),
        ("HTTPS://coldframe.example.org", UrlError::Scheme),
        ("coldframe.example.org", UrlError::Scheme),
        ("ftp://coldframe.example.org", UrlError::Scheme),
        ("https://coldframe.example.org/", UrlError::NotBare),
        ("https://coldframe.example.org/api", UrlError::NotBare),
        ("https://coldframe.example.org?x=1", UrlError::NotBare),
        ("https://coldframe.example.org#top", UrlError::NotBare),
        ("https://user@coldframe.example.org", UrlError::NotBare),
        ("https://user:pw@coldframe.example.org", UrlError::NotBare),
        ("https://192.168.1.10", UrlError::Host),
        ("https://192.168.1.10:8443", UrlError::Host),
        ("https://10.0.0.1", UrlError::Host),
        ("https://[::1]", UrlError::Host),
        ("https://[fe80::1]:443", UrlError::Host),
        ("https://Coldframe.Example.org", UrlError::Host),
        ("https://coldframe_example.org", UrlError::Host),
        ("https://-coldframe.example.org", UrlError::Host),
        ("https://coldframe-.example.org", UrlError::Host),
        ("https://coldframe..example.org", UrlError::Host),
        ("https://coldframe.example.org.", UrlError::Host),
        ("https://.example.org", UrlError::Host),
        ("https://", UrlError::Host),
        ("https://:443", UrlError::Host),
        (long_label.as_str(), UrlError::Host),
        ("https://coldframe.example.org:", UrlError::Port),
        ("https://coldframe.example.org:0", UrlError::Port),
        ("https://coldframe.example.org:0443", UrlError::Port),
        ("https://coldframe.example.org:65536", UrlError::Port),
        ("https://coldframe.example.org:99999", UrlError::Port),
        ("https://coldframe.example.org:123456", UrlError::Port),
        ("https://coldframe.example.org:+443", UrlError::Port),
        ("https://coldframe.example.org:44a", UrlError::Port),
        ("https://coldframe.example.org:443:1", UrlError::Port),
        ("https://coldframe.exämple.org", UrlError::Host),
        ("https://coldframe example.org", UrlError::Host),
    ] {
        assert_eq!(ServerUrl::parse(text).err(), Some(error), "{text:?}");
    }
}

#[test]
fn rfc3339_times_to_unix_milliseconds() {
    for (text, millis) in [
        ("1970-01-01T00:00:00Z", 0),
        ("1970-01-01T00:00:00.001Z", 1),
        ("2026-09-29T12:34:56Z", 1_790_685_296_000),
        ("2026-09-29T12:34:56.789Z", 1_790_685_296_789),
        ("2026-09-29T12:34:56.7Z", 1_790_685_296_700),
        ("2026-09-29T12:34:56.78Z", 1_790_685_296_780),
        ("2026-09-29T12:34:56.789999999Z", 1_790_685_296_789),
        ("2000-02-29T00:00:00Z", 951_782_400_000),
        ("2024-02-29T23:59:59.999Z", 1_709_251_199_999),
        ("2100-03-01T00:00:00Z", 4_107_542_400_000),
        ("1999-12-31T23:59:59Z", 946_684_799_000),
    ] {
        assert_eq!(parse_rfc3339_ms(text), Ok(millis), "{text}");
    }
}

#[test]
fn rfc3339_times_that_are_refused() {
    for text in [
        "",
        "2026-09-29",
        "2026-09-29T12:34:56",
        "2026-09-29T12:34:56+00:00",
        "2026-09-29T12:34:56.789+02:00",
        "2026-09-29t12:34:56Z",
        "2026-09-29T12:34:56z",
        "2026-09-29 12:34:56Z",
        "2026-09-29T12:34:56.Z",
        "2026-09-29T12:34:56.1234567890Z",
        "2026-09-29T12:34:56.78aZ",
        "2026-09-29T12:34:60Z",
        "2026-09-29T12:60:00Z",
        "2026-09-29T24:00:00Z",
        "2026-13-01T00:00:00Z",
        "2026-00-01T00:00:00Z",
        "2026-09-00T00:00:00Z",
        "2026-09-31T00:00:00Z",
        "2023-02-29T00:00:00Z",
        "2100-02-29T00:00:00Z",
        "1969-12-31T23:59:59Z",
        "+2026-09-29T12:34:56Z",
        "2026-9-29T12:34:56Z",
        "2026-09-29T12:34:5Z",
        "20260929T123456Z",
        "２026-09-29T12:34:56Z",
    ] {
        assert!(parse_rfc3339_ms(text).is_err(), "{text:?}");
    }
}

#[test]
fn stamps_strictly_increase_even_when_the_clock_steps_back() {
    let mut stamp = MonotonicStamp::new();
    assert_eq!(stamp.last(), 0);
    assert_eq!(stamp.next(1_000), 1_000);
    assert_eq!(stamp.next(1_000), 1_001, "the same millisecond");
    assert_eq!(stamp.next(500), 1_002, "the clock stepped back");
    assert_eq!(stamp.next(5_000), 5_000, "the clock moved on");
    assert_eq!(stamp.last(), 5_000);
    let mut after = MonotonicStamp::after(9_000);
    assert_eq!(after.next(8_000), 9_001);
    let mut top = MonotonicStamp::after(u64::MAX);
    assert_eq!(top.next(0), u64::MAX, "saturates instead of wrapping");
}
