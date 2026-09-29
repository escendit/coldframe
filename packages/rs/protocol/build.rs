//! Generates the micropb types of `packages/proto/coldframe/setup/v1/setup.proto`.
//!
//! protox compiles the file into a descriptor set in pure Rust (no `protoc`), and micropb-gen
//! turns that into no_std Rust with heapless containers. Every string, bytes and repeated field
//! gets a fixed capacity, so each message has a compile-time `MAX_SIZE`.

use std::path::PathBuf;
use std::{env, fmt};

use micropb_gen::{Config, Generator};

const SETUP_PROTO: &str = "coldframe/setup/v1/setup.proto";
const PKG: &str = ".coldframe.setup.v1";

/// A `SetupMessage` is sealed into `SealedSetupMessage.ciphertext` with a 16-byte tag: its largest
/// encoding (1081 bytes, a full 16-network scan list) plus 16. `src/lib.rs` asserts that it fits,
/// so a capacity change above that fails the build instead of truncating messages.
const SEALED_CIPHERTEXT_CAPACITY: u32 = 1081 + 16;

fn main() {
    let manifest = PathBuf::from(env::var("CARGO_MANIFEST_DIR").expect("set by cargo"));
    let proto_root = manifest.join("../../proto");
    let out_dir = PathBuf::from(env::var("OUT_DIR").expect("set by cargo"));
    println!(
        "cargo:rerun-if-changed={}",
        proto_root.join(SETUP_PROTO).display()
    );

    let fdset = protox::Compiler::new([&proto_root])
        .expect("proto root exists")
        .include_source_info(false)
        .open_file(SETUP_PROTO)
        .expect("setup.proto compiles")
        .encode_file_descriptor_set();
    let fdset_path = out_dir.join("setup.fdset");
    std::fs::write(
        out_dir.join("sealed_capacity.rs"),
        SEALED_CIPHERTEXT_CAPACITY.to_string(),
    )
    .expect("OUT_DIR is writable");
    std::fs::write(&fdset_path, fdset).expect("OUT_DIR is writable");

    let mut generator = Generator::with_warning_callback(warn);
    generator.use_container_heapless().calculate_max_size(true);
    let field = |path: &str| format!("{PKG}.{path}");
    let bytes = |n: u32| Config::new().max_bytes(n);
    for (path, n) in [
        ("SessionHello.app_public_key", 32),
        ("SessionHelloReply.device_public_key", 32),
        ("SealedSetupMessage.ciphertext", SEALED_CIPHERTEXT_CAPACITY),
        ("Identity.device_id", 8),
        ("Identity.firmware_version", 32),
        ("WifiNetwork.ssid", 32),
        ("WifiNetwork.bssid", 6),
        ("WifiConfig.ssid", 32),
        ("WifiConfig.password", 64),
        ("SiteBinding.site_id", 36),
        ("SiteBinding.lot_id", 36),
        ("SiteBinding.server_url", 100),
        ("EnrolmentRequest.server_public_key", 32),
        ("EnrolmentRequest.fingerprint", 64),
        ("EnrolmentResponse.device_id", 8),
        ("EnrolmentResponse.enc", 32),
        ("EnrolmentResponse.ciphertext", 48),
    ] {
        generator.configure(&field(path), bytes(n));
    }
    generator.configure(&field("WifiScanList.networks"), Config::new().max_len(16));
    // Types that can hold a Wi-Fi password never implement `Debug`.
    for path in ["WifiConfig", "SetupMessage", "SetupMessage.body"] {
        generator.configure(&field(path), Config::new().no_debug_impl(true));
    }
    generator
        .compile_fdset_file(&fdset_path, out_dir.join("setup.rs"))
        .expect("micropb-gen generates setup.rs");
}

/// Forwards micropb-gen warnings to cargo, except the notes that confirm a `no_debug_impl` took
/// effect (those are expected). An unused configuration path still warns.
fn warn(args: fmt::Arguments) {
    let message = args.to_string();
    if !message.starts_with("Disable Debug for ") {
        println!("cargo::warning={message}");
    }
}
