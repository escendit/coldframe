//! Generates the micropb types of `packages/proto/coldframe/setup/v1/setup.proto` and
//! `packages/proto/coldframe/device/v1/envelope.proto`.
//!
//! protox compiles the file into a descriptor set in pure Rust (no `protoc`), and micropb-gen
//! turns that into no_std Rust with heapless containers. Every string, bytes and repeated field
//! gets a fixed capacity, so each message has a compile-time `MAX_SIZE`.

use std::path::PathBuf;
use std::{env, fmt};

use micropb_gen::{Config, Generator};

const SETUP_PROTO: &str = "coldframe/setup/v1/setup.proto";
const DEVICE_PROTO: &str = "coldframe/device/v1/envelope.proto";
const PKG: &str = ".coldframe.setup.v1";
const DEVICE_PKG: &str = ".coldframe.device.v1";

/// A `SetupMessage` is sealed into `SealedSetupMessage.ciphertext` with a 16-byte tag: its largest
/// encoding (1081 bytes, a full 16-network scan list) plus 16. `src/lib.rs` asserts that it fits,
/// so a capacity change above that fails the build instead of truncating messages.
const SEALED_CIPHERTEXT_CAPACITY: u32 = 1081 + 16;

/// Capacity of `SealedEnvelope.ciphertext`: more than any envelope that fits one ESP-NOW payload
/// (250 bytes, less the kind byte and the envelope's own fields). `src/lib.rs` asserts that.
const ENVELOPE_CIPHERTEXT_CAPACITY: u32 = 240;

/// Readings of one `NodeFrame`, and Specifications of one set: one per Sensor of a Node.
const SENSORS_PER_NODE: u32 = 4;

/// `Downlink.acked_readings`: one range per reading_seq of a frame at worst (four Readings and
/// the report), with room to spare.
const ACKED_RANGES: u32 = 8;

/// `Downlink.commands`: V1 defines none; a few fit, so a later Server does not break decoding.
const COMMANDS: u32 = 4;

fn main() {
    let manifest = PathBuf::from(env::var("CARGO_MANIFEST_DIR").expect("set by cargo"));
    let proto_root = manifest.join("../../proto");
    let out_dir = PathBuf::from(env::var("OUT_DIR").expect("set by cargo"));
    for proto in [SETUP_PROTO, DEVICE_PROTO] {
        println!(
            "cargo:rerun-if-changed={}",
            proto_root.join(proto).display()
        );
    }

    let mut compiler = protox::Compiler::new([&proto_root]).expect("proto root exists");
    compiler.include_source_info(false);
    compiler
        .open_file(SETUP_PROTO)
        .expect("setup.proto compiles");
    compiler
        .open_file(DEVICE_PROTO)
        .expect("envelope.proto compiles");
    let fdset = compiler.encode_file_descriptor_set();
    let fdset_path = out_dir.join("protocol.fdset");
    std::fs::write(
        out_dir.join("sealed_capacity.rs"),
        SEALED_CIPHERTEXT_CAPACITY.to_string(),
    )
    .expect("OUT_DIR is writable");
    std::fs::write(
        out_dir.join("envelope_capacity.rs"),
        ENVELOPE_CIPHERTEXT_CAPACITY.to_string(),
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

    let device = |path: &str| format!("{DEVICE_PKG}.{path}");
    for (path, n) in [
        ("SealedEnvelope.device_id", 8),
        ("SealedEnvelope.ciphertext", ENVELOPE_CIPHERTEXT_CAPACITY),
        ("NodeFrame.spec_hash", 32),
    ] {
        generator.configure(&device(path), bytes(n));
    }
    for (path, n) in [
        ("Downlink.commands", COMMANDS),
        ("Downlink.acked_readings", ACKED_RANGES),
        ("NodeFrame.readings", SENSORS_PER_NODE),
        ("SpecificationSet.specifications", SENSORS_PER_NODE),
    ] {
        generator.configure(&device(path), Config::new().max_len(n));
    }
    generator
        .compile_fdset_file(&fdset_path, out_dir.join("protocol.rs"))
        .expect("micropb-gen generates protocol.rs");
}

/// Forwards micropb-gen warnings to cargo, except the notes that confirm a `no_debug_impl` took
/// effect (those are expected). An unused configuration path still warns.
fn warn(args: fmt::Arguments) {
    let message = args.to_string();
    if !message.starts_with("Disable Debug for ") {
        println!("cargo::warning={message}");
    }
}
