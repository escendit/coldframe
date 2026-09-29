//! The Server address the app binds a Hub to (`SiteBinding.server_url`).
//!
//! Accepted: `https://` + a lowercase DNS host + an optional `:port` (1–65535, no leading zero),
//! at most [`SERVER_URL_MAX_LENGTH`] bytes. Refused: any other scheme, a path (even `/`), a query,
//! a fragment, userinfo, an IP literal (IPv4 or `[…]`), upper case, and empty or malformed labels.
//! The host is what TLS checks the certificate against (AD-13), so it must be a name.

use core::fmt;

/// Longest server URL, in bytes.
pub const SERVER_URL_MAX_LENGTH: usize = 100;

/// Longest DNS label, in bytes.
const LABEL_MAX_LENGTH: usize = 63;

const SCHEME: &str = "https://";

/// The default HTTPS port.
pub const DEFAULT_PORT: u16 = 443;

/// Why a server URL was refused. Carries none of the text.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum UrlError {
    /// Longer than [`SERVER_URL_MAX_LENGTH`] bytes, or empty.
    Length,
    /// Not `https://`.
    Scheme,
    /// A path, query, fragment or userinfo.
    NotBare,
    /// The host is not a lowercase DNS name, or is an IP literal.
    Host,
    /// The port is not 1–65535 in plain digits.
    Port,
}

impl fmt::Display for UrlError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(match self {
            Self::Length => "server URL length",
            Self::Scheme => "server URL is not https",
            Self::NotBare => "server URL has a path, query, fragment or userinfo",
            Self::Host => "server URL host is not a lowercase DNS name",
            Self::Port => "server URL port is invalid",
        })
    }
}

impl core::error::Error for UrlError {}

/// A parsed server URL.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct ServerUrl {
    text: heapless::String<SERVER_URL_MAX_LENGTH>,
    host_end: usize,
    port: u16,
}

impl ServerUrl {
    /// Parses `text`; the port defaults to 443.
    ///
    /// # Errors
    ///
    /// See [`UrlError`].
    pub fn parse(text: &str) -> Result<Self, UrlError> {
        if text.is_empty() || text.len() > SERVER_URL_MAX_LENGTH {
            return Err(UrlError::Length);
        }
        let rest = text.strip_prefix(SCHEME).ok_or(UrlError::Scheme)?;
        if rest
            .bytes()
            .any(|byte| matches!(byte, b'/' | b'?' | b'#' | b'@'))
        {
            return Err(UrlError::NotBare);
        }
        if rest.bytes().any(|byte| matches!(byte, b'[' | b']')) {
            // An IPv6 literal.
            return Err(UrlError::Host);
        }
        let (host, port) = match rest.split_once(':') {
            Some((host, port)) => (host, parse_port(port)?),
            None => (rest, DEFAULT_PORT),
        };
        check_host(host)?;
        let text = text.try_into().map_err(|_| UrlError::Length)?;
        Ok(Self {
            text,
            host_end: SCHEME.len() + host.len(),
            port,
        })
    }

    /// The host: the TLS server name and the `Host` header.
    #[must_use]
    pub fn host(&self) -> &str {
        &self.text[SCHEME.len()..self.host_end]
    }

    /// The TCP port.
    #[must_use]
    pub const fn port(&self) -> u16 {
        self.port
    }

    /// The URL as given.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.text
    }
}

fn parse_port(text: &str) -> Result<u16, UrlError> {
    let digits = text.as_bytes();
    if digits.is_empty()
        || digits.len() > 5
        || digits[0] == b'0'
        || !digits.iter().all(u8::is_ascii_digit)
    {
        return Err(UrlError::Port);
    }
    text.parse::<u16>().map_err(|_| UrlError::Port)
}

fn check_host(host: &str) -> Result<(), UrlError> {
    if host.is_empty() {
        return Err(UrlError::Host);
    }
    let mut last = "";
    for label in host.split('.') {
        let bytes = label.as_bytes();
        let valid = !bytes.is_empty()
            && bytes.len() <= LABEL_MAX_LENGTH
            && bytes
                .iter()
                .all(|&byte| byte.is_ascii_lowercase() || byte.is_ascii_digit() || byte == b'-')
            && bytes[0] != b'-'
            && bytes[bytes.len() - 1] != b'-';
        if !valid {
            return Err(UrlError::Host);
        }
        last = label;
    }
    // A top-level label is never all digits; a host whose last label is all digits is an IPv4
    // literal (or a mistyped one).
    if last.bytes().all(|byte| byte.is_ascii_digit()) {
        return Err(UrlError::Host);
    }
    Ok(())
}
