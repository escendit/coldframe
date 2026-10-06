//! The datagram radio mock: it records what is sent and on which channel, lets a peer answer, and
//! reports a send status that says nothing about delivery (N-1).

use std::future::Future;
use std::pin::pin;
use std::task::{Context, Poll, Waker};

use coldframe_hal::mock::{MockDatagramRadio, MockFlash};
use coldframe_hal::{BROADCAST, DATAGRAM_MAX, Datagram, DatagramError, DatagramRadio, Flash};

const HUB: [u8; 6] = [0x02, 0, 0, 0, 0, 0x01];

fn block_on<F: Future>(future: F) -> F::Output {
    let mut future = pin!(future);
    let mut context = Context::from_waker(Waker::noop());
    loop {
        if let Poll::Ready(output) = future.as_mut().poll(&mut context) {
            return output;
        }
    }
}

#[test]
fn a_send_is_recorded_with_its_channel_and_the_peer_answers() {
    let mut radio = MockDatagramRadio::new();
    radio.set_peer(|sent| {
        if sent.channel == 6 {
            vec![(HUB, sent.payload.iter().rev().copied().collect())]
        } else {
            Vec::new()
        }
    });
    let mut buffer = [0u8; DATAGRAM_MAX];

    radio.set_channel(1).unwrap();
    assert_eq!(block_on(radio.send(&BROADCAST, &[1, 2, 3])), Ok(()));
    assert_eq!(block_on(radio.receive(&mut buffer, 120)), None);

    radio.set_channel(6).unwrap();
    assert_eq!(block_on(radio.send(&BROADCAST, &[1, 2, 3])), Ok(()));
    assert_eq!(
        block_on(radio.receive(&mut buffer, 120)),
        Some(Datagram {
            source: HUB,
            len: 3
        })
    );
    assert_eq!(buffer[..3], [3, 2, 1]);
    assert_eq!(block_on(radio.receive(&mut buffer, 300)), None);

    assert_eq!(radio.channels(), [1, 6]);
    assert_eq!(radio.channel(), 6);
    assert_eq!(radio.timeouts(), [120, 300]);
    let sent = radio.sent();
    assert_eq!(sent.len(), 2);
    assert_eq!((sent[0].channel, sent[0].to), (1, BROADCAST));
    assert_eq!(
        (sent[1].channel, sent[1].payload.as_slice()),
        (6, &[1u8, 2, 3][..])
    );
}

#[test]
fn the_send_status_is_independent_of_delivery() {
    let mut radio = MockDatagramRadio::new();
    radio.set_peer(|_| vec![(HUB, vec![0xAA])]);
    radio.report_sends_as(Err(DatagramError::Send));
    let mut buffer = [0u8; 4];
    // The radio says "failed", and the answer arrives all the same.
    assert_eq!(block_on(radio.send(&HUB, &[1])), Err(DatagramError::Send));
    assert!(block_on(radio.receive(&mut buffer, 300)).is_some());

    assert_eq!(
        block_on(radio.send(&HUB, &[0; DATAGRAM_MAX + 1])),
        Err(DatagramError::TooLong)
    );
    assert_eq!(radio.sent().len(), 1);
}

#[test]
fn unsolicited_datagrams_and_channel_failures() {
    let mut radio = MockDatagramRadio::new();
    radio.push_incoming(HUB, &[9, 8, 7, 6]);
    let mut small = [0u8; 2];
    // A payload longer than the buffer is cut to fit.
    assert_eq!(
        block_on(radio.receive(&mut small, 10)),
        Some(Datagram {
            source: HUB,
            len: 2
        })
    );
    assert_eq!(small, [9, 8]);

    radio.fail_channel(Some(DatagramError::Channel));
    assert_eq!(radio.set_channel(3), Err(DatagramError::Channel));
    assert_eq!(radio.channel(), 0);
    radio.fail_channel(None);
    assert_eq!(radio.set_channel(3), Ok(()));
}

#[test]
fn a_borrowed_flash_is_a_flash() {
    fn fill<F: Flash>(mut flash: F) -> usize {
        flash.write(0, &[0x12, 0x34]).unwrap();
        flash.erase(4096, 4096).unwrap();
        let mut back = [0u8; 2];
        flash.read(0, &mut back).unwrap();
        assert_eq!(back, [0x12, 0x34]);
        flash.capacity()
    }
    let mut flash = MockFlash::new(8192);
    assert_eq!(fill(&mut flash), 8192);
    assert_eq!(flash.contents()[..2], [0x12, 0x34]);
    assert_eq!((flash.write_count(), flash.erase_count()), (1, 1));
}
