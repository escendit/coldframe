//! The IP uplink of the Hub: DHCP, SNTP and one HTTPS request at a time.
//!
//! The Hub talks to its Server over TLS with public roots only (AD-13): an implementation opens a
//! fresh TLS connection per [`Net::post`], sends `Connection: close` and closes it after the
//! exchange, so at most one connection is ever open. It verifies the certificate chain, the host
//! name (SNI) and the validity dates, which is why [`Net::sntp`] must have set the clock first
//! (AD-11).
//!
//! No error carries a header value, a body or an address.

use core::fmt;

/// Why a network operation failed. Carries no request or response content.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum NetError {
    /// No IP address (DHCP has not completed, or the link is down).
    NoIp,
    /// The host name did not resolve.
    Dns,
    /// SNTP gave no usable time.
    Sntp,
    /// The TCP connection or the TLS handshake failed (including certificate validation).
    Tls,
    /// The HTTP exchange failed: a malformed response, or a body larger than the buffer.
    Http,
    /// The operation did not finish in time.
    Timeout,
}

impl NetError {
    /// A short, stable name for logs.
    #[must_use]
    pub const fn kind(self) -> &'static str {
        match self {
            Self::NoIp => "no-ip",
            Self::Dns => "dns",
            Self::Sntp => "sntp",
            Self::Tls => "tls",
            Self::Http => "http",
            Self::Timeout => "timeout",
        }
    }
}

impl fmt::Display for NetError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(match self {
            Self::NoIp => "no IP address",
            Self::Dns => "DNS lookup failed",
            Self::Sntp => "SNTP failed",
            Self::Tls => "TLS connection failed",
            Self::Http => "HTTP exchange failed",
            Self::Timeout => "network timeout",
        })
    }
}

impl core::error::Error for NetError {}

/// One HTTPS `POST`.
#[derive(Clone, Copy)]
pub struct HttpRequest<'a> {
    /// The server host name; also the TLS server name (SNI) and the `Host` header.
    pub host: &'a str,
    /// The TCP port.
    pub port: u16,
    /// The absolute request path, such as `/device/heartbeat`.
    pub path: &'a str,
    /// Extra headers, sent as given. `Host`, `Content-Type: application/json`, `Content-Length`
    /// and `Connection: close` are added by the implementation.
    pub headers: &'a [(&'a str, &'a str)],
    /// The exact body bytes.
    pub body: &'a [u8],
}

/// The answer to a [`Net::post`].
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct HttpResponse {
    /// The HTTP status code.
    pub status: u16,
    /// How many body bytes were written into the caller's buffer.
    pub body_len: usize,
}

/// The IP stack of a station.
///
/// `async` for a single-threaded executor, like [`crate::Wifi`].
#[allow(async_fn_in_trait)]
pub trait Net {
    /// Waits up to `timeout_ms` for an IP address (DHCP). Returns at once when one is configured.
    ///
    /// # Errors
    ///
    /// [`NetError::NoIp`] when none arrives in time.
    async fn wait_ip(&mut self, timeout_ms: u32) -> Result<(), NetError>;

    /// Asks an SNTP server for the time within `timeout_ms`; returns Unix time in milliseconds,
    /// UTC.
    ///
    /// # Errors
    ///
    /// [`NetError::NoIp`], [`NetError::Dns`], [`NetError::Sntp`] or [`NetError::Timeout`].
    async fn sntp(&mut self, timeout_ms: u32) -> Result<u64, NetError>;

    /// Sends `request` as an HTTPS `POST` on a fresh connection, closed after the exchange, and
    /// copies the response body into `response_body`.
    ///
    /// # Errors
    ///
    /// [`NetError::NoIp`], [`NetError::Dns`], [`NetError::Tls`], [`NetError::Http`] (also for a
    /// body larger than `response_body`) or [`NetError::Timeout`].
    async fn post(
        &mut self,
        request: &HttpRequest<'_>,
        response_body: &mut [u8],
    ) -> Result<HttpResponse, NetError>;
}
