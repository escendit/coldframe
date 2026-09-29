//! `coldframe-setup-client`: the app side of the Hub BLE setup session (AD-25), for the bench
//! checklist (`docs/bench/hub-setup-checklist.md`).
//!
//! ```text
//! coldframe-setup-client scan [--seconds N]
//! coldframe-setup-client setup --code CODE --ssid SSID --password PASSWORD --site SITE_ID \
//!     --enrolment-key BASE64URL [--fingerprint HEX] [--address ADDRESS] [--seconds N]
//! ```
//!
//! `setup` runs identity, the Wi-Fi scan list, the Site binding, enrolment and the Wi-Fi config,
//! prints each result, and prints the `EnrolDeviceRequest` body for `POST /sites/{siteId}/devices`.
//! A wrong code exits non-zero with `wrong setup code`. BlueZ negotiates the ATT MTU on connect;
//! the client fragments to whatever was negotiated.

use std::fmt::Write as _;
use std::io::Read as _;
use std::pin::Pin;
use std::process::ExitCode;
use std::time::Duration;

use btleplug::api::{
    Central as _, Characteristic, Manager as _, Peripheral as _, ScanFilter, ValueNotification,
    WriteType,
};
use btleplug::platform::{Adapter, Manager, Peripheral};
use coldframe_crypto::spec::X25519_KEY_LENGTH;
use coldframe_protocol::setup_v1::SetupMessage_::Body;
use coldframe_protocol::setup_v1::{SetupMessage, WifiSecurity, WifiStatus};
use coldframe_setup::app::{
    AppClient, AppError, enrolment_request, identity_request, site_binding, wifi_config,
    wifi_scan_request,
};
use coldframe_setup::framing::{Reassembler, fragments};
use coldframe_setup::{
    MAX_FRAME, NOTIFY_CHARACTERISTIC_UUID, SERVICE_UUID, WRITE_CHARACTERISTIC_UUID,
};
use futures::{Stream, StreamExt as _};
use uuid::Uuid;

/// How long to wait for one reply frame.
const REPLY_TIMEOUT: Duration = Duration::from_secs(60);

type Failure = Box<dyn std::error::Error>;

fn usage() -> &'static str {
    "usage:\n  coldframe-setup-client scan [--seconds N]\n  coldframe-setup-client setup --code CODE --ssid SSID --password PASSWORD --site SITE_ID --enrolment-key BASE64URL [--fingerprint HEX] [--address ADDRESS] [--seconds N]"
}

/// Parsed `--name value` options.
struct Options(Vec<(String, String)>);

impl Options {
    fn parse(args: &[String]) -> Result<Self, Failure> {
        let mut pairs = Vec::new();
        let mut iter = args.iter();
        while let Some(name) = iter.next() {
            let name = name
                .strip_prefix("--")
                .ok_or_else(|| format!("unexpected argument {name}"))?;
            let value = iter
                .next()
                .ok_or_else(|| format!("--{name} needs a value"))?;
            pairs.push((name.to_owned(), value.clone()));
        }
        Ok(Self(pairs))
    }

    fn get(&self, name: &str) -> Option<&str> {
        self.0
            .iter()
            .find(|(key, _)| key == name)
            .map(|(_, value)| value.as_str())
    }

    fn require(&self, name: &str) -> Result<&str, Failure> {
        self.get(name)
            .ok_or_else(|| format!("--{name} is required").into())
    }

    fn seconds(&self) -> Result<Duration, Failure> {
        let seconds = self.get("seconds").unwrap_or("5").parse::<u64>()?;
        Ok(Duration::from_secs(seconds))
    }
}

const BASE64URL: &[u8; 64] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

/// Base64url without padding.
fn base64url(bytes: &[u8]) -> String {
    let mut out = String::new();
    for chunk in bytes.chunks(3) {
        let mut block = [0u8; 3];
        block[..chunk.len()].copy_from_slice(chunk);
        let value = u32::from(block[0]) << 16 | u32::from(block[1]) << 8 | u32::from(block[2]);
        for index in 0..=chunk.len() {
            let sextet = (value >> (18 - 6 * index)) & 0x3F;
            out.push(char::from(BASE64URL[usize::try_from(sextet).unwrap_or(0)]));
        }
    }
    out
}

/// Decodes base64url, with or without padding.
fn from_base64url(text: &str) -> Result<Vec<u8>, Failure> {
    let mut bits = 0u32;
    let mut count = 0;
    let mut out = Vec::new();
    for byte in text.trim_end_matches('=').bytes() {
        let value = BASE64URL
            .iter()
            .position(|&c| c == byte)
            .ok_or("the enrolment key is not base64url")?;
        bits = bits << 6 | u32::try_from(value)?;
        count += 6;
        if count >= 8 {
            count -= 8;
            out.push(u8::try_from((bits >> count) & 0xFF)?);
        }
    }
    Ok(out)
}

fn hex(bytes: &[u8]) -> String {
    bytes.iter().fold(String::new(), |mut out, byte| {
        let _ = write!(out, "{byte:02x}");
        out
    })
}

fn random_key() -> Result<[u8; X25519_KEY_LENGTH], Failure> {
    let mut key = [0u8; X25519_KEY_LENGTH];
    std::fs::File::open("/dev/urandom")?.read_exact(&mut key)?;
    Ok(key)
}

async fn adapter() -> Result<Adapter, Failure> {
    let manager = Manager::new().await?;
    manager
        .adapters()
        .await?
        .into_iter()
        .next()
        .ok_or_else(|| "no Bluetooth adapter".into())
}

/// Scans for `duration` and returns the Hubs advertising the setup service, strongest first.
async fn find_hubs(adapter: &Adapter, duration: Duration) -> Result<Vec<Hub>, Failure> {
    let service = Uuid::from_u128(SERVICE_UUID);
    adapter
        .start_scan(ScanFilter {
            services: vec![service],
        })
        .await?;
    tokio::time::sleep(duration).await;
    adapter.stop_scan().await?;
    let mut hubs = Vec::new();
    for peripheral in adapter.peripherals().await? {
        let Some(properties) = peripheral.properties().await? else {
            continue;
        };
        if !properties.services.contains(&service) {
            continue;
        }
        hubs.push(Hub {
            address: peripheral.address().to_string(),
            name: properties.local_name.unwrap_or_default(),
            rssi: properties.rssi,
            peripheral,
        });
    }
    hubs.sort_by_key(|hub| std::cmp::Reverse(hub.rssi.unwrap_or(i16::MIN)));
    Ok(hubs)
}

struct Hub {
    address: String,
    name: String,
    rssi: Option<i16>,
    peripheral: Peripheral,
}

async fn scan(options: &Options) -> Result<ExitCode, Failure> {
    let adapter = adapter().await?;
    let hubs = find_hubs(&adapter, options.seconds()?).await?;
    if hubs.is_empty() {
        println!("no Hub advertising the setup service");
    }
    for hub in hubs {
        let rssi = hub
            .rssi
            .map_or_else(|| "?".to_owned(), |rssi| rssi.to_string());
        println!("{}  {}  rssi={rssi}", hub.address, hub.name);
    }
    Ok(ExitCode::SUCCESS)
}

type Notifications = Pin<Box<dyn Stream<Item = ValueNotification> + Send>>;

/// One BLE connection carrying setup frames.
struct Link {
    peripheral: Peripheral,
    write: Characteristic,
    notifications: Notifications,
    reassembler: Reassembler,
}

impl Link {
    async fn open(peripheral: Peripheral) -> Result<Self, Failure> {
        peripheral.connect().await?;
        peripheral.discover_services().await?;
        let characteristics = peripheral.characteristics();
        let find = |uuid: u128| {
            characteristics
                .iter()
                .find(|c| c.uuid == Uuid::from_u128(uuid))
                .cloned()
                .ok_or_else(|| format!("characteristic {} missing", Uuid::from_u128(uuid)))
        };
        let write = find(WRITE_CHARACTERISTIC_UUID)?;
        let notify = find(NOTIFY_CHARACTERISTIC_UUID)?;
        peripheral.subscribe(&notify).await?;
        let notifications = peripheral.notifications().await?;
        Ok(Self {
            peripheral,
            write,
            notifications,
            reassembler: Reassembler::new(),
        })
    }

    async fn send(&self, frame: &[u8]) -> Result<(), Failure> {
        // The ATT MTU BlueZ negotiated; a write carries MTU - 3 bytes.
        let max_payload = usize::from(self.peripheral.mtu()).saturating_sub(3).max(20);
        for fragment in fragments(frame, max_payload) {
            let mut payload = vec![0u8; fragment.data.len() + 1];
            fragment.write_to(&mut payload);
            self.peripheral
                .write(&self.write, &payload, WriteType::WithResponse)
                .await?;
        }
        Ok(())
    }

    async fn receive(&mut self) -> Result<Vec<u8>, Failure> {
        loop {
            let notification = tokio::time::timeout(REPLY_TIMEOUT, self.notifications.next())
                .await
                .map_err(|_| "no reply from the Hub")?
                .ok_or("the Hub disconnected")?;
            if notification.uuid != Uuid::from_u128(NOTIFY_CHARACTERISTIC_UUID) {
                continue;
            }
            if let Some(frame) = self
                .reassembler
                .push(&notification.value)
                .map_err(|error| format!("bad notification: {error}"))?
            {
                return Ok(frame.to_vec());
            }
        }
    }
}

/// Sends a sealed request and reads the sealed reply.
async fn request(
    link: &mut Link,
    client: &mut AppClient,
    body: Body,
) -> Result<SetupMessage, Failure> {
    let mut frame = vec![0u8; MAX_FRAME];
    let length = client.seal(body, &mut frame)?;
    link.send(&frame[..length]).await?;
    let reply = link.receive().await?;
    Ok(client.open(&reply)?)
}

/// Sends a sealed message that has no reply on success.
async fn post(link: &Link, client: &mut AppClient, body: Body) -> Result<(), Failure> {
    let mut frame = vec![0u8; MAX_FRAME];
    let length = client.seal(body, &mut frame)?;
    link.send(&frame[..length]).await
}

fn security(security: WifiSecurity) -> &'static str {
    match security {
        WifiSecurity::Open => "open",
        WifiSecurity::Wpa2Personal => "wpa2",
        WifiSecurity::Wpa3Transition => "wpa2/wpa3",
        WifiSecurity::Wpa3Only => "wpa3-only (unsupported)",
        _ => "other (unsupported)",
    }
}

fn status(status: WifiStatus) -> &'static str {
    match status {
        WifiStatus::Connected => "CONNECTED",
        WifiStatus::WrongPassword => "WRONG_PASSWORD",
        WifiStatus::NetworkNotFound => "NETWORK_NOT_FOUND",
        WifiStatus::UnsupportedSecurity => "UNSUPPORTED_SECURITY",
        WifiStatus::NoServer => "NO_SERVER",
        _ => "UNSPECIFIED",
    }
}

/// The reply body, or an error naming the `SetupError` the Hub sent instead.
fn body(message: SetupMessage, step: &str) -> Result<Body, Failure> {
    match message.body {
        Some(Body::Error(error)) => {
            Err(format!("{step}: the Hub answered SetupError {}", error.code.0).into())
        }
        Some(body) => Ok(body),
        None => Err(format!("{step}: empty reply").into()),
    }
}

/// The `EnrolDeviceRequest` body (OpenAPI) for `POST /sites/{siteId}/devices`: the Device ID in
/// lowercase hex, `kind` `hub`, and `enc` and `ciphertext` in base64url without padding.
fn enrol_device_request(device_id: &[u8], enc: &[u8], ciphertext: &[u8]) -> String {
    format!(
        "{{\"deviceId\":\"{}\",\"kind\":\"hub\",\"enc\":\"{}\",\"ciphertext\":\"{}\"}}",
        hex(device_id),
        base64url(enc),
        base64url(ciphertext)
    )
}

async fn setup(options: &Options) -> Result<ExitCode, Failure> {
    let code = options.require("code")?;
    let ssid = options.require("ssid")?;
    let password = options.require("password")?;
    let site = options.require("site")?;
    let server_key: [u8; X25519_KEY_LENGTH] = from_base64url(options.require("enrolment-key")?)?
        .try_into()
        .map_err(|_| "the enrolment key is not 32 bytes")?;

    let adapter = adapter().await?;
    let hubs = find_hubs(&adapter, options.seconds()?).await?;
    let hub = match options.get("address") {
        Some(address) => hubs
            .into_iter()
            .find(|hub| hub.address.eq_ignore_ascii_case(address)),
        None => hubs.into_iter().next(),
    }
    .ok_or("no Hub advertising the setup service")?;
    println!("hub {} ({})", hub.address, hub.name);

    let mut link = Link::open(hub.peripheral).await?;
    let result = run_session(&mut link, code, ssid, password, site, &server_key, options).await;
    let _ = link.peripheral.disconnect().await;
    result
}

async fn run_session(
    link: &mut Link,
    code: &str,
    ssid: &str,
    password: &str,
    site: &str,
    server_key: &[u8; X25519_KEY_LENGTH],
    options: &Options,
) -> Result<ExitCode, Failure> {
    let mut client = AppClient::new(random_key()?);
    let mut frame = vec![0u8; MAX_FRAME];
    let length = client.hello(&mut frame)?;
    link.send(&frame[..length]).await?;
    let reply = link.receive().await?;
    client.on_hello_reply(&reply, code)?;
    println!("session open, mtu={}", link.peripheral.mtu());

    let identity = match request(link, &mut client, identity_request()).await {
        Ok(message) => body(message, "identity")?,
        Err(error) if error.downcast_ref::<AppError>() == Some(&AppError::WrongSetupCode) => {
            eprintln!("wrong setup code");
            return Ok(ExitCode::FAILURE);
        }
        Err(error) => return Err(error),
    };
    let Body::Identity(identity) = identity else {
        return Err("identity: unexpected reply".into());
    };
    let device_id = hex(&identity.device_id);
    println!(
        "identity device_id={device_id} kind={} firmware={}",
        identity.kind.0, identity.firmware_version
    );

    if let Body::WifiScanList(list) = body(
        request(link, &mut client, wifi_scan_request()).await?,
        "scan",
    )? {
        println!("networks ({}):", list.networks.len());
        for network in &list.networks {
            println!(
                "  {:<32} rssi={:>4} ch={:>2} bssid={} {}",
                network.ssid,
                network.rssi,
                network.channel,
                hex(&network.bssid),
                security(network.security)
            );
        }
    }

    post(link, &mut client, site_binding(site)?).await?;
    println!("site binding sent site={site}");

    let enrolment = enrolment_request(server_key, options.get("fingerprint"))?;
    let Body::EnrolmentResponse(enrolled) =
        body(request(link, &mut client, enrolment).await?, "enrolment")?
    else {
        return Err("enrolment: unexpected reply".into());
    };
    println!("enrolment sealed device_id={}", hex(&enrolled.device_id));

    let Body::WifiResult(result) = body(
        request(link, &mut client, wifi_config(ssid, password)?).await?,
        "wifi",
    )?
    else {
        return Err("wifi: unexpected reply".into());
    };
    println!("wifi result {}", status(result.status));

    println!("POST /sites/{site}/devices");
    println!(
        "{}",
        enrol_device_request(&enrolled.device_id, &enrolled.enc, &enrolled.ciphertext)
    );
    Ok(if result.status == WifiStatus::Connected {
        ExitCode::SUCCESS
    } else {
        ExitCode::FAILURE
    })
}

#[tokio::main]
async fn main() -> ExitCode {
    let args: Vec<String> = std::env::args().skip(1).collect();
    let Some((command, rest)) = args.split_first() else {
        eprintln!("{}", usage());
        return ExitCode::from(2);
    };
    let options = match Options::parse(rest) {
        Ok(options) => options,
        Err(error) => {
            eprintln!("{error}\n{}", usage());
            return ExitCode::from(2);
        }
    };
    let result = match command.as_str() {
        "scan" => scan(&options).await,
        "setup" => setup(&options).await,
        _ => {
            eprintln!("{}", usage());
            return ExitCode::from(2);
        }
    };
    match result {
        Ok(code) => code,
        Err(error) => {
            eprintln!("error: {error}");
            ExitCode::FAILURE
        }
    }
}

#[cfg(test)]
mod tests {
    use super::{base64url, from_base64url};

    #[test]
    fn base64url_round_trips_without_padding() {
        for length in 0..70 {
            let bytes: Vec<u8> = (0..length)
                .map(|i| u8::try_from(i * 7 % 256).unwrap())
                .collect();
            let text = base64url(&bytes);
            assert!(!text.contains('='));
            assert_eq!(from_base64url(&text).unwrap(), bytes);
        }
        assert_eq!(base64url(&[0xFB, 0xFF]), "-_8");
        assert_eq!(base64url(&[0u8; 32]).len(), 43);
        assert_eq!(base64url(&[0u8; 48]).len(), 64);
    }
}

#[cfg(test)]
mod enrol_body_tests {
    use super::{enrol_device_request, from_base64url};

    fn vector(field: &str) -> Vec<u8> {
        let path = concat!(
            env!("CARGO_MANIFEST_DIR"),
            "/../../../packages/crypto-spec/vectors.json"
        );
        let text = std::fs::read_to_string(path).expect("vectors.json is readable");
        let vectors: serde_json::Value = serde_json::from_str(&text).expect("JSON");
        let hex = vectors["enrolment"][0][field].as_str().expect("hex field");
        (0..hex.len())
            .step_by(2)
            .map(|i| u8::from_str_radix(&hex[i..i + 2], 16).expect("hex"))
            .collect()
    }

    fn base64url_field(value: &serde_json::Value, length: usize) -> Vec<u8> {
        let text = value.as_str().expect("a string");
        assert_eq!(text.len(), length);
        assert!(
            text.bytes()
                .all(|b| b.is_ascii_alphanumeric() || b == b'-' || b == b'_')
        );
        from_base64url(text).expect("base64url")
    }

    #[test]
    fn the_enrolment_body_matches_the_openapi_shape_and_the_vector() {
        let (device_id, enc, ciphertext) =
            (vector("deviceId"), vector("enc"), vector("ciphertext"));
        let body = enrol_device_request(&device_id, &enc, &ciphertext);
        let json: serde_json::Value = serde_json::from_str(&body).expect("valid JSON");
        let object = json.as_object().expect("an object");
        assert_eq!(object.len(), 4);

        let id = json["deviceId"].as_str().expect("deviceId");
        assert_eq!(id.len(), 16);
        assert!(
            id.bytes()
                .all(|b| b.is_ascii_digit() || (b'a'..=b'f').contains(&b))
        );
        assert_eq!(id, "92064422c012f481");
        assert_eq!(json["kind"], "hub");
        assert_eq!(base64url_field(&json["enc"], 43), enc);
        assert_eq!(base64url_field(&json["ciphertext"], 64), ciphertext);
    }
}
