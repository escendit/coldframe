//! The Wi-Fi link, network and timer mocks the Story 3.5 uplink tests rely on.

use std::future::Future;
use std::pin::pin;
use std::task::{Context, Poll, Waker};

use coldframe_hal::mock::{MockNet, MockTimer, MockWifi};
use coldframe_hal::{AccessPoint, HttpRequest, JoinError, Net, NetError, Security, Timer, Wifi};

fn block_on<F: Future>(future: F) -> F::Output {
    let mut future = pin!(future);
    let mut context = Context::from_waker(Waker::noop());
    loop {
        if let Poll::Ready(output) = future.as_mut().poll(&mut context) {
            return output;
        }
    }
}

fn garden() -> Vec<AccessPoint> {
    vec![AccessPoint::new(
        b"garden",
        [2, 0, 0, 0, 0, 1],
        6,
        -50,
        Security::Wpa2Personal,
    )]
}

#[test]
fn a_join_brings_the_link_up_and_leave_or_a_drop_takes_it_down() {
    let mut wifi = MockWifi::new(garden());
    wifi.set_join_outcome("garden", "fixture-pass", Ok(()));
    assert!(!wifi.is_connected());
    assert_eq!(
        block_on(wifi.join("garden", "wrong", [0; 6], 1)),
        Err(JoinError::WrongPassword)
    );
    assert!(!wifi.is_connected());
    assert_eq!(
        block_on(wifi.join("garden", "fixture-pass", [0; 6], 1)),
        Ok(())
    );
    assert!(wifi.is_connected());
    block_on(wifi.leave());
    assert!(!wifi.is_connected());
    assert_eq!(wifi.leave_count(), 1);
    block_on(wifi.join("garden", "fixture-pass", [0; 6], 1)).unwrap();
    wifi.drop_link();
    assert!(!wifi.is_connected());
    // A shared handle sees and drives the same link.
    let link = wifi.link();
    link.set(true);
    assert!(wifi.is_connected());
}

#[test]
fn queued_join_outcomes_come_first_in_order() {
    let mut wifi = MockWifi::new(garden());
    wifi.set_join_outcome("garden", "fixture-pass", Ok(()));
    wifi.push_join_outcome(Err(JoinError::NotFound));
    wifi.push_join_outcome(Err(JoinError::Failed));
    let mut join = || block_on(wifi.join("garden", "fixture-pass", [0; 6], 1));
    assert_eq!(join(), Err(JoinError::NotFound));
    assert_eq!(join(), Err(JoinError::Failed));
    assert_eq!(join(), Ok(()));
    assert_eq!(wifi.join_count(), 3);
    wifi.set_scan(Vec::new());
    let mut out = [AccessPoint::EMPTY; 4];
    assert_eq!(block_on(wifi.scan(&mut out)), Ok(0));
}

#[test]
fn the_network_plays_its_script_and_records_requests() {
    let mut net = MockNet::new();
    assert_eq!(block_on(net.wait_ip(5)), Ok(()));
    assert_eq!(block_on(net.sntp(7)), Err(NetError::Sntp), "unscripted");
    net.push_ip(Err(NetError::NoIp));
    net.push_sntp(Ok(42));
    assert_eq!(block_on(net.wait_ip(6)), Err(NetError::NoIp));
    assert_eq!(block_on(net.sntp(8)), Ok(42));
    assert_eq!(net.ip_timeouts(), [5, 6]);
    assert_eq!(net.sntp_timeouts(), [7, 8]);

    net.push_post(Ok((200, b"{\"a\":1}")));
    net.push_post(Ok((200, &[b'x'; 10])));
    let headers = [("X-Coldframe-Device", "0011223344556677")];
    let request = HttpRequest {
        host: "coldframe.example.org",
        port: 443,
        path: "/device/heartbeat",
        headers: &headers,
        body: b"{}",
    };
    let mut body = [0u8; 8];
    let answer = block_on(net.post(&request, &mut body)).unwrap();
    assert_eq!(
        (answer.status, &body[..answer.body_len]),
        (200, &b"{\"a\":1}"[..])
    );
    assert_eq!(
        block_on(net.post(&request, &mut body)),
        Err(NetError::Http),
        "a body larger than the buffer"
    );
    assert_eq!(
        block_on(net.post(&request, &mut body)),
        Err(NetError::Tls),
        "unscripted"
    );
    assert_eq!(net.requests().len(), 3);
    let recorded = &net.requests()[0];
    assert_eq!(
        recorded.header("x-coldframe-device"),
        Some("0011223344556677")
    );
    assert_eq!(
        (
            recorded.host.as_str(),
            recorded.port,
            recorded.path.as_str()
        ),
        ("coldframe.example.org", 443, "/device/heartbeat")
    );
    assert_eq!(recorded.body, b"{}");
}

#[test]
fn the_network_follows_the_wifi_link() {
    let mut wifi = MockWifi::new(garden());
    let mut net = MockNet::with_link(wifi.link());
    assert_eq!(block_on(net.wait_ip(1)), Err(NetError::NoIp), "not joined");
    wifi.set_connected(true);
    net.push_post_dropping_link(Err(NetError::Tls));
    let request = HttpRequest {
        host: "coldframe.example.org",
        port: 443,
        path: "/",
        headers: &[],
        body: b"",
    };
    assert_eq!(block_on(net.post(&request, &mut [])), Err(NetError::Tls));
    assert!(!wifi.is_connected(), "the post dropped the link");
    wifi.set_connected(true);
    net.push_sntp_dropping_link(Ok(1));
    assert_eq!(block_on(net.sntp(1)), Ok(1));
    assert!(!wifi.is_connected());
    assert_eq!(block_on(net.sntp(1)), Err(NetError::NoIp));
}

#[test]
fn the_timer_records_its_waits() {
    let mut timer = MockTimer::new();
    block_on(timer.sleep_ms(1_000));
    block_on(timer.sleep_ms(30_000));
    assert_eq!(timer.sleeps(), [1_000, 30_000]);
    assert_eq!(NetError::Timeout.kind(), "timeout");
    assert!(!NetError::Tls.to_string().is_empty());
}
