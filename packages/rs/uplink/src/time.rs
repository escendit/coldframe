//! Time on the wire (AD-11): the Server's `serverTime` and the Hub's request timestamps.

/// Why an RFC 3339 time was refused.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct TimeError;

impl core::fmt::Display for TimeError {
    fn fmt(&self, f: &mut core::fmt::Formatter<'_>) -> core::fmt::Result {
        f.write_str("not an RFC 3339 UTC time")
    }
}

impl core::error::Error for TimeError {}

/// Parses `yyyy-MM-ddTHH:mm:ss[.fraction]Z` (UTC only, upper-case `T` and `Z`, 1 to 9 fraction
/// digits, year 1970 or later) into Unix milliseconds. Fraction digits past the third are cut.
///
/// # Errors
///
/// [`TimeError`] for anything else: an offset, a leap second, an impossible date.
pub fn parse_rfc3339_ms(text: &str) -> Result<u64, TimeError> {
    let bytes = text.as_bytes();
    if bytes.len() < 20
        || bytes[4] != b'-'
        || bytes[7] != b'-'
        || bytes[10] != b'T'
        || bytes[13] != b':'
        || bytes[16] != b':'
        || bytes[bytes.len() - 1] != b'Z'
    {
        return Err(TimeError);
    }
    let year = digits(&bytes[0..4])?;
    let month = digits(&bytes[5..7])?;
    let day = digits(&bytes[8..10])?;
    let hour = digits(&bytes[11..13])?;
    let minute = digits(&bytes[14..16])?;
    let second = digits(&bytes[17..19])?;
    let fraction = &bytes[19..bytes.len() - 1];
    let millis = match fraction {
        [] => 0,
        [b'.', rest @ ..] if (1..=9).contains(&rest.len()) => {
            let mut value = digits(&rest[..rest.len().min(3)])?;
            // Every digit must be a digit, including the ones that are cut.
            digits(rest)?;
            for _ in rest.len()..3 {
                value *= 10;
            }
            value
        }
        _ => return Err(TimeError),
    };
    if year < 1970
        || !(1..=12).contains(&month)
        || day < 1
        || day > days_in_month(year, month)
        || hour > 23
        || minute > 59
        || second > 59
    {
        return Err(TimeError);
    }
    let days = days_from_civil(year, month, day);
    let seconds = days * 86_400 + hour * 3_600 + minute * 60 + second;
    Ok(seconds * 1_000 + millis)
}

fn digits(bytes: &[u8]) -> Result<u64, TimeError> {
    let mut value: u64 = 0;
    for &byte in bytes {
        if !byte.is_ascii_digit() {
            return Err(TimeError);
        }
        value = value * 10 + u64::from(byte - b'0');
    }
    Ok(value)
}

const fn is_leap(year: u64) -> bool {
    (year.is_multiple_of(4) && !year.is_multiple_of(100)) || year.is_multiple_of(400)
}

const fn days_in_month(year: u64, month: u64) -> u64 {
    match month {
        2 if is_leap(year) => 29,
        2 => 28,
        4 | 6 | 9 | 11 => 30,
        _ => 31,
    }
}

/// Days since 1970-01-01 of a proleptic Gregorian date from 1970 on (Howard Hinnant's
/// `days_from_civil`, unsigned).
const fn days_from_civil(year: u64, month: u64, day: u64) -> u64 {
    let year = if month <= 2 { year - 1 } else { year };
    let era = year / 400;
    let year_of_era = year - era * 400;
    let month_index = if month > 2 { month - 3 } else { month + 9 };
    let day_of_year = (153 * month_index + 2) / 5 + day - 1;
    let day_of_era = year_of_era * 365 + year_of_era / 4 - year_of_era / 100 + day_of_year;
    era * 146_097 + day_of_era - 719_468
}

/// Strictly increasing request timestamps: the Server refuses a heartbeat whose timestamp is not
/// above the last one it accepted, so a clock that steps back (a `serverTime` behind SNTP) must
/// never produce a repeat.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct MonotonicStamp {
    last: u64,
}

impl MonotonicStamp {
    /// A stamp that has issued nothing.
    #[must_use]
    pub const fn new() -> Self {
        Self { last: 0 }
    }

    /// A stamp whose last issued value was `last`.
    #[must_use]
    pub const fn after(last: u64) -> Self {
        Self { last }
    }

    /// The next timestamp: `max(now_unix_ms, last + 1)`.
    pub fn next(&mut self, now_unix_ms: u64) -> u64 {
        self.last = now_unix_ms.max(self.last.saturating_add(1));
        self.last
    }

    /// The last value issued, 0 before the first.
    #[must_use]
    pub const fn last(&self) -> u64 {
        self.last
    }
}
