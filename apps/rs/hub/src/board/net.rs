//! [`Net`] over embassy-net (DHCP, DNS, UDP, TCP), SNTP over UDP, and HTTPS through an mbedtls-rs
//! session with reqwless writing the request and parsing the response.
//!
//! - One TLS connection at a time, opened per request and closed after it (`Connection: close`);
//!   there is no keep-alive pool (AD-13, spike F-4: about 40 KiB of heap per connection).
//! - The certificate chain, the host name (SNI) and the validity dates are verified: the dates
//!   against the RTC through `hook-wall-clock` (see [`super::rtc`]), with public roots only
//!   ([`super::roots`]).
//! - No header value, body, address or nonce is logged; failures log their kind.

use core::ffi::CStr;

use coldframe_hal::{HttpRequest, HttpResponse, Net, NetError};
use coldframe_uplink::url::SERVER_URL_MAX_LENGTH;
use coldframe_uplink::{INGEST_RESPONSE_MAX, SNTP_SERVER};
use embassy_net::dns::DnsQueryType;
use embassy_net::tcp::TcpSocket;
use embassy_net::udp::{PacketMetadata, UdpSocket};
use embassy_net::{IpAddress, Runner, Stack, StackResources};
use embassy_time::{Duration, Instant, with_timeout};
use esp_radio::wifi::Interface;
use log::warn;
use mbedtls_rs::io::{Read, Write};
use mbedtls_rs::{Certificate, ClientSessionConfig, Session, SessionConfig, Tls, TlsError, X509};
use reqwless::headers::ContentType;
use reqwless::request::{Method, Request, RequestBuilder};
use reqwless::response::Response;
use static_cell::StaticCell;

use super::roots::ROOTS_PEM;

/// The sockets the stack holds at once: DHCP, DNS, one TCP or UDP exchange, and a spare.
const SOCKETS: usize = 4;

/// One whole HTTPS exchange: DNS, TCP, TLS handshake, request and response.
const HTTP_TIMEOUT: Duration = Duration::from_secs(20);

/// Idle limit on the TCP socket under TLS.
const SOCKET_TIMEOUT: Duration = Duration::from_secs(15);

/// How long a TLS close may take before the connection is dropped.
const CLOSE_TIMEOUT: Duration = Duration::from_secs(2);

/// Seconds from 1900 (NTP) to 1970 (Unix).
const NTP_UNIX_OFFSET: u64 = 2_208_988_800;

/// Room for the response headers.
const RESPONSE_HEADERS: usize = 1_024;

/// The response headers plus the largest body the uplink reads: an ingest answer with one
/// downlink per frame of a full batch (Story 4.4). A heartbeat answer is far smaller.
const RESPONSE_BUFFER: usize = RESPONSE_HEADERS + INGEST_RESPONSE_MAX;

/// The TCP send buffer: a whole ingest request of a full batch (2.7 kB of body) with its headers.
const TCP_TX_BUFFER: usize = 4_096;

/// The TCP receive buffer.
const TCP_RX_BUFFER: usize = 4_096;

/// Creates the embassy-net stack over the Wi-Fi station interface, with DHCP. `seed` comes from the
/// TRNG. The returned runner must be spawned with [`net_task`].
pub fn stack(interface: Interface, seed: u64) -> (Stack<'static>, Runner<'static, Interface>) {
    static RESOURCES: StaticCell<StackResources<SOCKETS>> = StaticCell::new();
    embassy_net::new(
        interface,
        embassy_net::Config::dhcpv4(embassy_net::DhcpConfig::default()),
        RESOURCES.init(StackResources::new()),
        seed,
    )
}

/// Runs the network stack forever.
#[embassy_executor::task]
pub async fn net_task(mut runner: Runner<'static, Interface>) -> ! {
    runner.run().await
}

/// Why the TLS client could not start.
#[derive(Debug)]
pub enum TlsSetupError {
    /// The TRNG has no entropy source.
    Trng,
    /// mbedTLS refused to start.
    Tls(TlsError),
    /// The root bundle does not parse.
    Roots,
}

impl core::fmt::Display for TlsSetupError {
    fn fmt(&self, f: &mut core::fmt::Formatter<'_>) -> core::fmt::Result {
        match self {
            Self::Trng => f.write_str("TRNG unavailable"),
            Self::Tls(error) => write!(f, "mbedTLS: {error:?}"),
            Self::Roots => f.write_str("root bundle does not parse"),
        }
    }
}

/// The Hub's IP uplink.
pub struct BoardNet {
    stack: Stack<'static>,
    tls: Tls<'static>,
    roots: Certificate<'static>,
}

impl BoardNet {
    /// The uplink over `stack`. Starts mbedTLS on the TRNG (the radio must be on) and parses the
    /// roots once.
    ///
    /// # Errors
    ///
    /// See [`TlsSetupError`].
    pub fn new(stack: Stack<'static>) -> Result<Self, TlsSetupError> {
        static TRNG: StaticCell<esp_hal::rng::Trng> = StaticCell::new();
        let trng = esp_hal::rng::Trng::try_new().map_err(|_| TlsSetupError::Trng)?;
        let tls = Tls::new(TRNG.init(trng)).map_err(TlsSetupError::Tls)?;
        let roots = Certificate::new(X509::PEM(ROOTS_PEM)).map_err(|_| TlsSetupError::Roots)?;
        Ok(Self { stack, tls, roots })
    }

    async fn resolve(&self, host: &str) -> Result<IpAddress, NetError> {
        let addresses = self
            .stack
            .dns_query(host, DnsQueryType::A)
            .await
            .map_err(|_| NetError::Dns)?;
        addresses.first().copied().ok_or(NetError::Dns)
    }

    async fn exchange(
        &mut self,
        request: &HttpRequest<'_>,
        response_body: &mut [u8],
    ) -> Result<HttpResponse, NetError> {
        if !self.stack.is_config_up() {
            return Err(NetError::NoIp);
        }
        let address = self.resolve(request.host).await?;

        let mut rx = [0u8; TCP_RX_BUFFER];
        let mut tx = [0u8; TCP_TX_BUFFER];
        let mut socket = TcpSocket::new(self.stack, &mut rx, &mut tx);
        socket.set_timeout(Some(SOCKET_TIMEOUT));
        socket
            .connect((address, request.port))
            .await
            .map_err(|_| NetError::Tls)?;

        let mut name = heapless::String::<{ SERVER_URL_MAX_LENGTH + 1 }>::new();
        name.push_str(request.host).map_err(|_| NetError::Dns)?;
        name.push('\0').map_err(|_| NetError::Dns)?;
        let server_name = CStr::from_bytes_with_nul(name.as_bytes()).map_err(|_| NetError::Dns)?;
        let config = SessionConfig::Client(ClientSessionConfig {
            ca_chain: Some(self.roots.clone()),
            server_name: Some(server_name),
            ..ClientSessionConfig::new()
        });
        let mut session =
            Session::new(self.tls.reference(), socket, &config).map_err(|_| NetError::Tls)?;
        if let Err(error) = session.connect().await {
            warn!(
                "net tls handshake failed: {error:?} verify_flags=0x{:x}",
                session.tls_verification_details()
            );
            return Err(NetError::Tls);
        }

        let result = http(&mut session, request, response_body).await;
        let _ = with_timeout(CLOSE_TIMEOUT, session.close()).await;
        result
    }
}

/// Writes the request with reqwless, then reads the status and the body.
async fn http<C>(
    connection: &mut C,
    request: &HttpRequest<'_>,
    response_body: &mut [u8],
) -> Result<HttpResponse, NetError>
where
    C: Read + Write,
{
    let mut headers: heapless::Vec<(&str, &str), 8> = heapless::Vec::new();
    headers
        .push(("Connection", "close"))
        .map_err(|_| NetError::Http)?;
    for header in request.headers {
        headers.push(*header).map_err(|_| NetError::Http)?;
    }
    // The Host header carries the port unless it is the HTTPS default.
    let mut host = heapless::String::<{ SERVER_URL_MAX_LENGTH + 6 }>::new();
    host.push_str(request.host).map_err(|_| NetError::Http)?;
    if request.port != 443 {
        core::fmt::Write::write_fmt(&mut host, format_args!(":{}", request.port))
            .map_err(|_| NetError::Http)?;
    }
    let outgoing = Request::post(request.path)
        .host(&host)
        .content_type(ContentType::ApplicationJson)
        .headers(&headers)
        .body(request.body)
        .build();
    outgoing
        .write_header(connection)
        .await
        .map_err(|_| NetError::Http)?;
    connection
        .write_all(request.body)
        .await
        .map_err(|_| NetError::Http)?;
    connection.flush().await.map_err(|_| NetError::Http)?;

    let mut buffer = [0u8; RESPONSE_BUFFER];
    let mut incoming = reqwless::client::HttpConnection::Plain(connection);
    let response = Response::read(&mut incoming, Method::POST, &mut buffer)
        .await
        .map_err(|_| NetError::Http)?;
    let status = response.status.0;
    if status != 200 {
        // Only a 200's body is read: a refusal is reported by its status, whatever its body.
        return Ok(HttpResponse {
            status,
            body_len: 0,
        });
    }
    let body = response
        .body()
        .read_to_end()
        .await
        .map_err(|_| NetError::Http)?;
    let target = response_body.get_mut(..body.len()).ok_or(NetError::Http)?;
    target.copy_from_slice(body);
    let body_len = body.len();
    body.fill(0);
    Ok(HttpResponse { status, body_len })
}

impl Net for BoardNet {
    async fn wait_ip(&mut self, timeout_ms: u32) -> Result<(), NetError> {
        with_timeout(
            Duration::from_millis(u64::from(timeout_ms)),
            self.stack.wait_config_up(),
        )
        .await
        .map_err(|_| NetError::NoIp)
    }

    async fn sntp(&mut self, timeout_ms: u32) -> Result<u64, NetError> {
        let budget = Duration::from_millis(u64::from(timeout_ms));
        match with_timeout(budget, sntp(self.stack)).await {
            Ok(result) => result,
            Err(_) => Err(NetError::Timeout),
        }
    }

    async fn post(
        &mut self,
        request: &HttpRequest<'_>,
        response_body: &mut [u8],
    ) -> Result<HttpResponse, NetError> {
        match with_timeout(HTTP_TIMEOUT, self.exchange(request, response_body)).await {
            Ok(result) => result,
            Err(_) => Err(NetError::Timeout),
        }
    }
}

/// One SNTP exchange (RFC 4330) with [`SNTP_SERVER`]; returns Unix milliseconds, corrected by half
/// the round trip.
async fn sntp(stack: Stack<'static>) -> Result<u64, NetError> {
    if !stack.is_config_up() {
        return Err(NetError::NoIp);
    }
    let addresses = stack
        .dns_query(SNTP_SERVER, DnsQueryType::A)
        .await
        .map_err(|_| NetError::Dns)?;
    let server = addresses.first().copied().ok_or(NetError::Dns)?;

    let mut rx_meta = [PacketMetadata::EMPTY; 2];
    let mut rx = [0u8; 128];
    let mut tx_meta = [PacketMetadata::EMPTY; 2];
    let mut tx = [0u8; 128];
    let mut socket = UdpSocket::new(stack, &mut rx_meta, &mut rx, &mut tx_meta, &mut tx);
    socket.bind(0).map_err(|_| NetError::Sntp)?;

    let mut packet = [0u8; 48];
    // LI 0, version 4, mode 3 (client).
    packet[0] = 0x23;
    let sent = Instant::now();
    socket
        .send_to(&packet, (server, 123))
        .await
        .map_err(|_| NetError::Sntp)?;
    let mut answer = [0u8; 48];
    let (length, from) = socket
        .recv_from(&mut answer)
        .await
        .map_err(|_| NetError::Sntp)?;
    // Only the queried server may set the clock.
    if from.endpoint.addr != server || from.endpoint.port != 123 {
        return Err(NetError::Sntp);
    }
    // A server answer (mode 4), synchronised (LI not 3), not a kiss-o'-death (stratum not 0).
    if length < 48 || answer[0] & 0x07 != 4 || answer[0] >> 6 == 3 || answer[1] == 0 {
        return Err(NetError::Sntp);
    }
    let seconds = u64::from(u32::from_be_bytes([
        answer[40], answer[41], answer[42], answer[43],
    ]));
    let fraction = u64::from(u32::from_be_bytes([
        answer[44], answer[45], answer[46], answer[47],
    ]));
    // NTP era 1 starts in 2036: a seconds field below the Unix offset has wrapped.
    let seconds = if seconds < NTP_UNIX_OFFSET {
        seconds + (1 << 32)
    } else {
        seconds
    };
    let round_trip = sent.elapsed().as_millis();
    Ok((seconds - NTP_UNIX_OFFSET) * 1_000 + ((fraction * 1_000) >> 32) + round_trip / 2)
}
