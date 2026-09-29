//! BLE framing: `header(1) ‖ fragment`, fragments of at most MTU − 4 bytes, reassembly and its
//! errors.

mod common;

use coldframe_setup::MAX_FRAME;
use coldframe_setup::framing::{FrameError, HEADER_LAST, HEADER_MORE, Reassembler, fragments};
use common::{PAYLOAD_MTU_23, PAYLOAD_MTU_251, payloads};

fn frame(length: usize) -> Vec<u8> {
    (0..length)
        .map(|i| u8::try_from(i % 251).unwrap())
        .collect()
}

#[test]
fn fragments_fit_the_mtu_and_reassemble() {
    for (max_payload, mtu) in [(PAYLOAD_MTU_23, 23), (PAYLOAD_MTU_251, 251)] {
        for length in [0, 1, mtu - 4, mtu - 3, 100, 1117, MAX_FRAME] {
            let original = frame(length);
            let parts = payloads(&original, max_payload);
            assert!(
                parts
                    .iter()
                    .all(|p| p.len() <= mtu - 3 && p.len() - 1 <= mtu - 4)
            );
            assert!(parts[..parts.len() - 1].iter().all(|p| p[0] == HEADER_MORE));
            assert_eq!(parts.last().unwrap()[0], HEADER_LAST);
            assert_eq!(parts.len(), length.div_ceil(mtu - 4).max(1));

            let mut reassembler = Reassembler::new();
            let mut done = None;
            for (index, part) in parts.iter().enumerate() {
                let result = reassembler.push(part).unwrap();
                if index + 1 < parts.len() {
                    assert!(result.is_none());
                } else {
                    done = result.map(<[u8]>::to_vec);
                }
            }
            assert_eq!(done.unwrap(), original);
        }
    }
}

#[test]
fn an_empty_frame_is_one_last_fragment() {
    let parts: Vec<_> = fragments(&[], PAYLOAD_MTU_23).collect();
    assert_eq!(parts.len(), 1);
    assert_eq!(parts[0].header, HEADER_LAST);
    assert!(parts[0].data.is_empty());
}

#[test]
fn a_bad_header_drops_the_partial_frame() {
    let mut reassembler = Reassembler::new();
    assert_eq!(reassembler.push(&[HEADER_MORE, 1, 2]), Ok(None));
    assert_eq!(reassembler.push(&[0x07, 3]), Err(FrameError::BadHeader));
    assert_eq!(reassembler.push(&[]), Err(FrameError::Empty));
    // The next frame starts clean.
    assert_eq!(reassembler.push(&[HEADER_LAST, 9]), Ok(Some(&[9u8][..])));
}

#[test]
fn an_oversize_frame_is_dropped_up_to_its_last_fragment() {
    let mut reassembler = Reassembler::new();
    let chunk = [0xAB; 200];
    let mut fragment = vec![HEADER_MORE];
    fragment.extend_from_slice(&chunk);
    let mut errors = 0;
    for _ in 0..8 {
        if reassembler.push(&fragment) == Err(FrameError::Oversize) {
            errors += 1;
        }
    }
    assert_eq!(errors, 1, "one error per oversize frame");
    // Its last fragment is swallowed too.
    assert_eq!(reassembler.push(&[HEADER_LAST, 1]), Ok(None));
    // Then frames reassemble again.
    assert_eq!(reassembler.push(&[HEADER_LAST, 2]), Ok(Some(&[2u8][..])));
}

#[test]
fn a_frame_of_exactly_max_frame_is_accepted() {
    let original = frame(MAX_FRAME);
    let mut reassembler = Reassembler::new();
    let mut last = None;
    for part in payloads(&original, PAYLOAD_MTU_251) {
        last = reassembler.push(&part).unwrap().map(<[u8]>::to_vec);
    }
    assert_eq!(last.unwrap(), original);

    let too_long = frame(MAX_FRAME + 1);
    let mut reassembler = Reassembler::new();
    let results: Vec<_> = payloads(&too_long, PAYLOAD_MTU_251)
        .iter()
        .map(|part| reassembler.push(part).map(|f| f.map(<[u8]>::to_vec)))
        .collect();
    assert!(results.contains(&Err(FrameError::Oversize)));
    assert!(!results.iter().any(|r| matches!(r, Ok(Some(_)))));
}
