//! Integration tests for `coldframe-hal`: they see only its public API.

use coldframe_hal::{NAME, VERSION};

#[test]
fn version_is_a_semantic_version() {
    // MAJOR.MINOR.PATCH, optionally followed by a pre-release ("-rc.1") or build ("+sha") part.
    let core = VERSION.split(['-', '+']).next().unwrap_or_default();
    let parts: Vec<&str> = core.split('.').collect();

    assert_eq!(parts.len(), 3, "{NAME} has version {VERSION}");
    assert!(
        parts.iter().all(|part| part.parse::<u32>().is_ok()),
        "{NAME} has version {VERSION}"
    );
}
