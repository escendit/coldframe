//! [`SetupLink`] over trouble-host: the AD-25 setup GATT service on the ESP32-S3 BLE controller.
//!
//! A copy of the Hub's adapter (`apps/rs/hub/src/board/ble.rs`, a shared board crate is a deferred
//! item) with two differences: the name is `Coldframe Node XXXX`, and the host runs as the future
//! [`run`] instead of a spawned task, so setup mode drops it (and with it the BLE controller) when
//! the window closes. BLE runs in setup mode only.
//!
//! [`run`] owns the BLE host and the GATT server. It advertises only when the setup service
//! asks ([`SetupLink::accept_within`]), serves one connection at a time, and bridges it to
//! [`BoardSetupLink`] through embassy-sync channels:
//!
//! - writes to the write characteristic go to the link as [`Event::Write`];
//! - payloads the link sends become notifications on the notify characteristic;
//! - [`SetupLink::disconnect`] asks the task to drop the connection.
//!
//! Advertising: flags plus the setup service UUID in the advertising data, the name
//! `Coldframe Node XXXX` (the first four Device ID hex digits, uppercase) in the scan response.
//! Payloads are never logged.

use core::sync::atomic::{AtomicBool, AtomicU16, Ordering};

use coldframe_hal::{LinkError, SetupLink};
use coldframe_setup::SERVICE_UUID;
use embassy_futures::join::join;
use embassy_futures::select::{Either, select};
use embassy_sync::blocking_mutex::raw::CriticalSectionRawMutex;
use embassy_sync::channel::Channel;
use embassy_sync::signal::Signal;
use embassy_time::{Duration, Timer, with_timeout};
use esp_radio::ble::controller::BleConnector;
use log::{info, warn};
use trouble_host::prelude::*;

/// The largest payload one write or notification carries: ATT MTU 251 minus 3.
pub const MAX_PAYLOAD: usize = 248;

/// Length of the advertised name, `Coldframe Node XXXX`.
pub const NAME_LENGTH: usize = 19;

type Payload = heapless::Vec<u8, MAX_PAYLOAD>;

/// What the BLE task reports to the link.
#[allow(
    clippy::large_enum_variant,
    reason = "lives in a static channel; boxing the payload would need the heap"
)]
enum Event {
    /// A central connected.
    Connected,
    /// The app wrote a payload.
    Write(Payload),
    /// The connection ended.
    Disconnected,
}

/// The link asks the task to advertise.
static ADVERTISE: Signal<CriticalSectionRawMutex, ()> = Signal::new();
/// Task → link.
static EVENTS: Channel<CriticalSectionRawMutex, Event, 16> = Channel::new();
/// Link → task: notifications to send.
static NOTIFY: Channel<CriticalSectionRawMutex, Payload, 4> = Channel::new();
/// Link → task: drop the connection.
static DROP_LINK: Signal<CriticalSectionRawMutex, ()> = Signal::new();
/// The current ATT MTU.
static ATT_MTU: AtomicU16 = AtomicU16::new(23);
/// Whether a connection is open.
static CONNECTED: AtomicBool = AtomicBool::new(false);

const CONNECTIONS_MAX: usize = 1;
const L2CAP_CHANNELS_MAX: usize = 2; // signal + att

#[gatt_server]
struct SetupServer {
    setup: SetupService,
}

/// The AD-25 setup service. The UUIDs are `coldframe_setup::{SERVICE_UUID,
/// WRITE_CHARACTERISTIC_UUID, NOTIFY_CHARACTERISTIC_UUID}`.
#[gatt_service(uuid = "c01d0001-5e70-4c0d-8f00-00000000c0de")]
struct SetupService {
    /// App → Device: `header ‖ fragment`.
    #[characteristic(uuid = "c01d0002-5e70-4c0d-8f00-00000000c0de", write)]
    write: heapless::Vec<u8, MAX_PAYLOAD>,
    /// Device → app: `header ‖ fragment`.
    #[characteristic(uuid = "c01d0003-5e70-4c0d-8f00-00000000c0de", notify)]
    notify: heapless::Vec<u8, MAX_PAYLOAD>,
}

/// The advertised name for a Device ID in hex: `Coldframe Node` and its first four digits,
/// uppercase.
pub fn advertised_name(device_id_hex: &[u8; 16]) -> [u8; NAME_LENGTH] {
    let mut name = *b"Coldframe Node XXXX";
    for (slot, digit) in name[15..].iter_mut().zip(device_id_hex) {
        *slot = digit.to_ascii_uppercase();
    }
    name
}

/// A static random BLE address from the Device ID (the two top bits set, as the spec requires).
pub fn address(device_id: &[u8; 8]) -> [u8; 6] {
    let mut address = [0u8; 6];
    address.copy_from_slice(&device_id[..6]);
    address[5] |= 0xC0;
    address
}

/// Runs the BLE host: advertises the setup service whenever the link asks, and serves one
/// connection at a time. Returns only if the host fails; dropping the future stops BLE.
pub async fn run(
    controller: ExternalController<BleConnector<'_>, 1>,
    name: [u8; NAME_LENGTH],
    address: [u8; 6],
) {
    let mut resources: HostResources<_, DefaultPacketPool, CONNECTIONS_MAX, L2CAP_CHANNELS_MAX> =
        HostResources::new();
    let stack = trouble_host::new(controller, &mut resources)
        .set_random_address(Address::random(address))
        .build();
    let mut peripheral = stack.peripheral();
    let mut runner = stack.runner();
    let name_str = core::str::from_utf8(&name).unwrap_or("Coldframe Node");

    let server = match SetupServer::new_with_config(GapConfig::Peripheral(PeripheralConfig {
        name: name_str,
        appearance: &appearance::UNKNOWN,
    })) {
        Ok(server) => server,
        Err(error) => {
            warn!("ble gatt server failed: {error:?}");
            return;
        }
    };

    let uuids = [SERVICE_UUID.to_le_bytes()];
    let mut adv_data = [0u8; 31];
    let mut scan_data = [0u8; 31];
    let (Ok(adv_length), Ok(scan_length)) = (
        AdStructure::encode_slice(
            &[
                AdStructure::Flags(LE_GENERAL_DISCOVERABLE | BR_EDR_NOT_SUPPORTED),
                AdStructure::CompleteServiceUuids128(&uuids),
            ],
            &mut adv_data,
        ),
        AdStructure::encode_slice(&[AdStructure::CompleteLocalName(&name)], &mut scan_data),
    ) else {
        warn!("ble advertising data does not fit");
        return;
    };

    let _ = join(runner.run(), async {
        let params = AdvertisementParameters {
            interval_min: Duration::from_millis(100),
            interval_max: Duration::from_millis(150),
            ..AdvertisementParameters::default()
        };
        loop {
            ADVERTISE.wait().await;
            info!("ble advertising setup service as {name_str}");
            let connection = loop {
                let advertiser = match peripheral
                    .advertise(
                        &params,
                        Advertisement::ConnectableScannableUndirected {
                            adv_data: &adv_data[..adv_length],
                            scan_data: &scan_data[..scan_length],
                        },
                    )
                    .await
                {
                    Ok(advertiser) => advertiser,
                    Err(error) => {
                        warn!("ble advertise failed: {error:?}");
                        Timer::after(Duration::from_secs(1)).await;
                        continue;
                    }
                };
                match advertiser.accept().await {
                    Ok(connection) => break connection,
                    Err(error) => warn!("ble accept failed: {error:?}"),
                }
            };
            let connection = match connection.with_attribute_server(&server) {
                Ok(connection) => connection,
                Err(error) => {
                    warn!("ble attribute server failed: {error:?}");
                    // The link is still waiting in `accept_within`: advertise again for it.
                    ADVERTISE.signal(());
                    continue;
                }
            };
            ATT_MTU.store(connection.raw().att_mtu(), Ordering::Relaxed);
            DROP_LINK.reset();
            while NOTIFY.try_receive().is_ok() {}
            CONNECTED.store(true, Ordering::Relaxed);
            EVENTS.send(Event::Connected).await;
            info!("ble connected");
            select(
                gatt_events(&server, &connection, &stack),
                notifications(&server, &connection),
            )
            .await;
            CONNECTED.store(false, Ordering::Relaxed);
            // Nothing queued for this connection may block the link or reach the next one.
            while NOTIFY.try_receive().is_ok() {}
            EVENTS.send(Event::Disconnected).await;
            info!("ble disconnected");
        }
    })
    .await;
    warn!("ble runner exited");
}

/// Handles GATT events until the connection ends.
async fn gatt_events<C: Controller, P: PacketPool>(
    server: &SetupServer<'_>,
    connection: &GattConnection<'_, '_, P>,
    stack: &Stack<'_, C, P>,
) {
    let write_handle = server.setup.write.handle;
    loop {
        match connection.next().await {
            GattConnectionEvent::Disconnected { .. } => return,
            GattConnectionEvent::RequestConnectionParams(request) => {
                let _ = request.accept(None, stack).await;
            }
            GattConnectionEvent::Gatt { event } => {
                ATT_MTU.store(connection.raw().att_mtu(), Ordering::Relaxed);
                if let GattEvent::Write(write) = &event
                    && write.handle() == write_handle
                {
                    let payload = write.with_data(|_offset, data| {
                        let length = data.len().min(MAX_PAYLOAD);
                        Payload::from_slice(&data[..length]).unwrap_or_default()
                    });
                    if EVENTS.try_send(Event::Write(payload)).is_err() {
                        warn!("ble write dropped: link queue full");
                    }
                }
                match event.accept() {
                    Ok(reply) => reply.send().await,
                    Err(error) => warn!("ble gatt reply failed: {error:?}"),
                }
            }
            _ => {}
        }
    }
}

/// Sends the link's notifications; drops the connection when the link asks.
async fn notifications<P: PacketPool>(
    server: &SetupServer<'_>,
    connection: &GattConnection<'_, '_, P>,
) {
    loop {
        match select(NOTIFY.receive(), DROP_LINK.wait()).await {
            Either::First(payload) => {
                if let Err(error) = server
                    .setup
                    .notify
                    .notify_raw(connection, &payload, false)
                    .await
                {
                    warn!("ble notify failed: {error:?}");
                }
            }
            Either::Second(()) => {
                connection.raw().disconnect();
                // The GATT event loop sees the disconnect and ends the connection.
                core::future::pending::<()>().await;
            }
        }
    }
}

/// The link side of [`run`].
pub struct BoardSetupLink {
    connected: bool,
}

impl BoardSetupLink {
    /// The link. Run [`run`] alongside it, from before the first accept.
    pub fn new() -> Self {
        Self { connected: false }
    }
}

impl SetupLink for BoardSetupLink {
    async fn accept(&mut self) -> Result<(), LinkError> {
        while EVENTS.try_receive().is_ok() {}
        ADVERTISE.signal(());
        loop {
            if let Event::Connected = EVENTS.receive().await {
                self.connected = true;
                return Ok(());
            }
        }
    }

    async fn accept_within(&mut self, timeout_ms: u32) -> Result<(), LinkError> {
        let timeout = Duration::from_millis(u64::from(timeout_ms));
        with_timeout(timeout, self.accept())
            .await
            .unwrap_or(Err(LinkError::Timeout))
    }

    async fn receive(&mut self, buffer: &mut [u8], timeout_ms: u32) -> Result<usize, LinkError> {
        if !self.connected {
            return Err(LinkError::Disconnected);
        }
        let timeout = Duration::from_millis(u64::from(timeout_ms));
        loop {
            match with_timeout(timeout, EVENTS.receive()).await {
                Err(_) => return Err(LinkError::Timeout),
                Ok(Event::Write(payload)) => {
                    let length = payload.len().min(buffer.len());
                    buffer[..length].copy_from_slice(&payload[..length]);
                    return Ok(length);
                }
                Ok(Event::Disconnected) => {
                    self.connected = false;
                    return Err(LinkError::Disconnected);
                }
                Ok(Event::Connected) => {}
            }
        }
    }

    async fn send(&mut self, payload: &[u8]) -> Result<(), LinkError> {
        if !self.connected || !CONNECTED.load(Ordering::Relaxed) {
            return Err(LinkError::Disconnected);
        }
        let payload = Payload::from_slice(payload).map_err(|_| LinkError::Transport)?;
        // The queue drains only while the connection lives: stop waiting as soon as it ends.
        let ended = async {
            while CONNECTED.load(Ordering::Relaxed) {
                Timer::after(Duration::from_millis(20)).await;
            }
        };
        match select(NOTIFY.send(payload), ended).await {
            Either::First(()) => Ok(()),
            Either::Second(()) => {
                self.connected = false;
                Err(LinkError::Disconnected)
            }
        }
    }

    fn max_payload(&self) -> usize {
        usize::from(ATT_MTU.load(Ordering::Relaxed))
            .saturating_sub(3)
            .clamp(20, MAX_PAYLOAD)
    }

    async fn disconnect(&mut self) {
        if !self.connected {
            return;
        }
        self.connected = false;
        // Let queued notifications (a wrong-code SetupError, say) reach the app first.
        let _ = with_timeout(Duration::from_secs(1), async {
            while !NOTIFY.is_empty() {
                Timer::after(Duration::from_millis(10)).await;
            }
        })
        .await;
        Timer::after(Duration::from_millis(200)).await;
        DROP_LINK.signal(());
        // Wait for the task to report the end of the connection, but not forever.
        let _ = with_timeout(Duration::from_secs(5), async {
            loop {
                if let Event::Disconnected = EVENTS.receive().await {
                    return;
                }
            }
        })
        .await;
    }
}
