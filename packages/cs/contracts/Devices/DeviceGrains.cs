using Orleans.Concurrency;

namespace Coldframe.Contracts.Devices;

/// <summary>
/// A Device, keyed by its Device ID (16 lowercase hex digits). The only writer of the Device's state and
/// the owner of its <c>siteId</c> (AD-1, AD-18).
/// </summary>
[Alias("coldframe.device")]
public interface IDeviceGrain : IGrainWithStringKey
{
    /// <summary>
    /// Enrols the Device on a Site. A Device enrolled on another Site is refused first. Then the Site grain
    /// registers the Device (<c>ISiteGrain.RegisterDevice</c>). A Node given a Lot then claims it
    /// (<c>ILotGrain.Claim</c>); a Node on another Lot already is refused. Only after all of that, the Device
    /// journals <see cref="DeviceEnrolled"/> (when new) and <see cref="DeviceAssigned"/> (when it claimed the
    /// Lot now) together. Enrolling again on the same Site and Lot journals nothing and answers the same. The
    /// Device persists nothing on a refusal; after a refused claim only the Site's idempotent registration
    /// stays.
    /// </summary>
    /// <param name="request">What to enrol, with the wrapped key only.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("enrol")]
    Task<DeviceEnrolmentResult> Enrol(EnrolDevice request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a Node to another Lot of its Site (Story 4.9, AD-18). In order: the new Lot is claimed
    /// (<c>ILotGrain.Claim</c>), the Device journals <see cref="DeviceMoved"/> (or <see cref="DeviceAssigned"/>
    /// for a Node on no Lot), and only then the old Lot is released, from persisted pending state that is
    /// retried until it succeeds. A refusal changes nothing. The Node's current Lot answers
    /// <see cref="DeviceAssignmentOutcome.Unchanged"/>.
    /// </summary>
    /// <param name="siteId">The canonical Site ID the caller reached the Node through.</param>
    /// <param name="lotId">The canonical Lot ID to move to.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("move")]
    Task<DeviceAssignmentResult> Move(string siteId, string lotId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unassigns a Node from its Lot (Story 4.9, AD-18): the Device journals <see cref="DeviceUnassigned"/>,
    /// then releases the Lot from persisted pending state that is retried until it succeeds. A Node on no Lot
    /// answers <see cref="DeviceAssignmentOutcome.Unchanged"/>.
    /// </summary>
    /// <param name="siteId">The canonical Site ID the caller reached the Node through.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("unassign")]
    Task<DeviceAssignmentResult> Unassign(string siteId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies a Hub's signed heartbeat (AD-12, FR-13) and, when it holds, journals <see cref="DeviceSeen"/>.
    /// The grain verifies inside itself, so the plaintext <c>K_dev</c> never crosses a grain boundary: it
    /// unwraps <c>K_dev</c>, derives the <c>hub-auth/v1</c> key, checks the signature and zeroes both. It
    /// refuses an unenrolled Device, a bad signature, a timestamp more than
    /// <c>HeartbeatMaxSkewMs</c> off its clock, a nonce it has seen, and a timestamp not above the last
    /// one it accepted. Nothing is journaled on a refusal.
    /// </summary>
    /// <param name="request">The request as the Hub signed it.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("heartbeat")]
    Task<DeviceHeartbeatResult> Heartbeat(DeviceHeartbeat request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Authenticates a Hub's signed <c>POST /device/ingest</c> (AD-9, AD-12), called on the grain of the Hub
    /// that signed it. It is the heartbeat's check (the <c>hub-auth/v1</c> HMAC over method, path, body hash,
    /// timestamp and nonce; a timestamp within <c>HeartbeatMaxSkewMs</c> of the clock; a nonce not seen while
    /// the grain is active), and it also refuses a Device that is not an enrolled Hub. It journals nothing and
    /// does not move the heartbeat's timestamp rule: the frames carry their own replay protection (AD-17).
    /// </summary>
    /// <param name="request">The request as the Hub signed it.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("authenticate-relay")]
    Task<DeviceRelayAuthenticationResult> AuthenticateRelay(DeviceRelayAuthentication request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ingests one sealed Node frame (AD-9, AD-17), called on the grain of the Node that sealed it. In order:
    /// open the seal with the <c>seal/v1</c> key, check the replay window, decode the Node frame, check its
    /// time, apply the Pause gate (AD-8), write the Readings, the device report, the replay window and the
    /// reserved downlink counter in one PostgreSQL transaction, and only after that commit seal the
    /// acknowledgement with the <c>ack/v1</c> key. A failed transaction changes neither the stored nor the
    /// in-memory replay state and answers <see cref="DeviceIngestStatus.Retry"/>.
    /// </summary>
    /// <param name="request">The frame as the Edge API decoded its <c>SealedEnvelope</c>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("ingest")]
    Task<DeviceIngestResult> Ingest(DeviceIngest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the Device without its key, or <see langword="null"/> before it is enrolled. Read only.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    [Alias("describe")]
    Task<DeviceSummary?> Describe(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a Sensor's Calibration in force on the Node (Story 5.1, AD-9). Only the Sensor grain calls it, once it
    /// journaled <c>SensorCalibrated</c>: the Device grain only caches which Calibration ID to stamp on the
    /// Sensor's later Readings and never validates a Calibration. It journals <see cref="DeviceCalibrationSet"/>
    /// and returns when that is persisted. A Calibration whose revision is not above the one held, which is what a
    /// redelivery is, journals nothing and returns as well.
    /// </summary>
    /// <remarks>
    /// It interleaves with the Device grain's other calls. The Device grain awaits
    /// <c>ISensorGrain.Evaluate</c> while it ingests a frame (Story 6.1), and the Sensor grain awaits this call
    /// while it calibrates: without interleaving the two would wait for each other until a call times out. The
    /// method only journals one event that no other call reads halfway.
    /// </remarks>
    /// <param name="request">The Sensor and the Calibration in force.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="InvalidOperationException">The Device is not an enrolled Node. Nothing is journaled.</exception>
    [Alias("set-calibration")]
    [AlwaysInterleave]
    Task SetCalibration(SetCalibration request, CancellationToken cancellationToken = default);
}

/// <summary>
/// The Calibration the Sensor grain sets in force on a Node's Device grain.
/// </summary>
/// <param name="SensorId">The Sensor ID.</param>
/// <param name="CalibrationId">The Calibration ID to stamp on the Sensor's later Readings.</param>
/// <param name="Revision">1 for the Sensor's first Calibration, then one more each time; the Device keeps the highest.</param>
[GenerateSerializer]
[Alias("coldframe.set-calibration")]
public sealed record SetCalibration(
    [property: Id(0)] Guid SensorId,
    [property: Id(1)] Guid CalibrationId,
    [property: Id(2)] int Revision);

/// <summary>
/// A Hub's signed ingest request as the Edge API hands it to the Hub's Device grain: parsed, not yet verified.
/// </summary>
/// <param name="Method">The HTTP method, uppercase.</param>
/// <param name="Path">The request path, exactly as sent.</param>
/// <param name="Body">The exact body bytes (at most 16 KiB).</param>
/// <param name="TimestampMs">The <c>X-Coldframe-Timestamp</c>, Unix milliseconds.</param>
/// <param name="Nonce">The <c>X-Coldframe-Nonce</c>, 16 bytes.</param>
/// <param name="Signature">The <c>X-Coldframe-Signature</c>, 32 bytes.</param>
[GenerateSerializer]
[Alias("coldframe.device-relay-authentication")]
public sealed record DeviceRelayAuthentication(
    [property: Id(0)] string Method,
    [property: Id(1)] string Path,
    [property: Id(2)] byte[] Body,
    [property: Id(3)] long TimestampMs,
    [property: Id(4)] byte[] Nonce,
    [property: Id(5)] byte[] Signature);

/// <summary>
/// The result of <see cref="IDeviceGrain.AuthenticateRelay"/>.
/// </summary>
/// <param name="Authenticated">
/// Whether the request is an authentic, new request of an enrolled Hub. The reason of a refusal is not told.
/// </param>
[GenerateSerializer]
[Alias("coldframe.device-relay-authentication-result")]
public sealed record DeviceRelayAuthenticationResult([property: Id(0)] bool Authenticated);

/// <summary>
/// One sealed Node frame as the Edge API hands it to the Node's Device grain: the fields of its
/// <c>SealedEnvelope</c>, and the Hub that relayed it.
/// </summary>
/// <param name="ProtocolVersion">The envelope's wire major.</param>
/// <param name="Counter">The frame counter.</param>
/// <param name="Ciphertext">The sealed Node frame followed by its tag.</param>
/// <param name="RelayHubId">The Device ID of the authenticated Hub that relayed the frame.</param>
[GenerateSerializer]
[Alias("coldframe.device-ingest")]
public sealed record DeviceIngest(
    [property: Id(0)] uint ProtocolVersion,
    [property: Id(1)] ulong Counter,
    [property: Id(2)] byte[] Ciphertext,
    [property: Id(3)] string RelayHubId);

/// <summary>
/// How one frame ended (AD-9). The contract's <c>IngestFrameStatus</c> is its snake-case name.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.device-ingest-status")]
public enum DeviceIngestStatus
{
    /// <summary>
    /// At least one Reading or the device report was new and is committed; or the Device is paused and the
    /// frame was acknowledged and discarded (AD-8).
    /// </summary>
    Stored = 0,

    /// <summary>
    /// Everything in the frame was stored before. It is acknowledged again and adds no row.
    /// </summary>
    Duplicate = 1,

    /// <summary>
    /// The frame has an unsupported protocol version, does not open with the Node's key, or its plaintext
    /// is not a valid Node frame.
    /// </summary>
    RejectedAuth = 2,

    /// <summary>
    /// The frame is authentic, but its counter is below the replay window or was seen.
    /// </summary>
    RejectedReplay = 3,

    /// <summary>
    /// A synced <c>measured_at</c> is more than 5 min after the Server clock. The counter is consumed.
    /// </summary>
    RejectedTime = 4,

    /// <summary>
    /// The Device is not an enrolled Node.
    /// </summary>
    UnknownDevice = 5,

    /// <summary>
    /// The frame could not be finished and is not acknowledged; the Node resends. Its rows may already be
    /// committed (when only the relay Hub could not be journaled), in which case the resend is a duplicate.
    /// </summary>
    Retry = 6,
}

/// <summary>
/// The result of <see cref="IDeviceGrain.Ingest"/>.
/// </summary>
/// <param name="Status">How the frame ended.</param>
/// <param name="Downlink">
/// The serialized <c>SealedEnvelope</c> of the acknowledgement, sealed with the Node's <c>ack/v1</c> key; set
/// exactly when <paramref name="Status"/> is <see cref="DeviceIngestStatus.Stored"/> or
/// <see cref="DeviceIngestStatus.Duplicate"/>.
/// </param>
[GenerateSerializer]
[Alias("coldframe.device-ingest-result")]
public sealed record DeviceIngestResult(
    [property: Id(0)] DeviceIngestStatus Status,
    [property: Id(1)] byte[]? Downlink = null);

/// <summary>
/// A Hub's heartbeat as the Edge API hands it to the Device grain: the parts of the signed request, parsed
/// but not yet verified.
/// </summary>
/// <param name="Method">The HTTP method, uppercase.</param>
/// <param name="Path">The request path, exactly as sent.</param>
/// <param name="Body">The exact body bytes (at most 4 KiB).</param>
/// <param name="TimestampMs">The <c>X-Coldframe-Timestamp</c>, Unix milliseconds.</param>
/// <param name="Nonce">The <c>X-Coldframe-Nonce</c>, 16 bytes.</param>
/// <param name="Signature">The <c>X-Coldframe-Signature</c>, 32 bytes.</param>
/// <param name="UptimeMs">The body's <c>uptimeMs</c>, when sent.</param>
[GenerateSerializer]
[Alias("coldframe.device-heartbeat")]
public sealed record DeviceHeartbeat(
    [property: Id(0)] string Method,
    [property: Id(1)] string Path,
    [property: Id(2)] byte[] Body,
    [property: Id(3)] long TimestampMs,
    [property: Id(4)] byte[] Nonce,
    [property: Id(5)] byte[] Signature,
    [property: Id(6)] long? UptimeMs);

/// <summary>
/// How a heartbeat ended.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.device-heartbeat-outcome")]
public enum DeviceHeartbeatOutcome
{
    /// <summary>
    /// The heartbeat is authentic and new; <see cref="DeviceSeen"/> was journaled.
    /// </summary>
    Accepted = 0,

    /// <summary>
    /// The Device authentication failed: unknown Device, bad signature, skew, a replayed nonce or an old
    /// timestamp. Nothing was journaled; the reason is not told to the caller.
    /// </summary>
    Unauthorized = 1,
}

/// <summary>
/// The result of <see cref="IDeviceGrain.Heartbeat"/>.
/// </summary>
/// <param name="Outcome">How the heartbeat ended.</param>
[GenerateSerializer]
[Alias("coldframe.device-heartbeat-result")]
public sealed record DeviceHeartbeatResult([property: Id(0)] DeviceHeartbeatOutcome Outcome);

/// <summary>
/// An enrolment request as the Edge API hands it to the Device grain, after it opened and wrapped <c>K_dev</c>.
/// </summary>
/// <param name="SiteId">The canonical Site ID.</param>
/// <param name="Kind">Hub or Node.</param>
/// <param name="WrappedKey">The Device's <c>K_dev</c>, wrapped under the key-encryption key.</param>
/// <param name="CallerId">The caller's User ID (the OIDC <c>sub</c>).</param>
/// <param name="IdempotencyKey">The request's <c>Idempotency-Key</c>, already validated.</param>
/// <param name="LotId">The canonical Lot ID a Node is assigned to, or <see langword="null"/>; never set for a Hub.</param>
[GenerateSerializer]
[Alias("coldframe.enrol-device")]
public sealed record EnrolDevice(
    [property: Id(0)] string SiteId,
    [property: Id(1)] DeviceKind Kind,
    [property: Id(2)] WrappedDeviceKey WrappedKey,
    [property: Id(3)] string CallerId,
    [property: Id(4)] string IdempotencyKey,
    [property: Id(5)] string? LotId = null);

/// <summary>
/// How an enrolment ended.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.device-enrolment-outcome")]
public enum DeviceEnrolmentOutcome
{
    /// <summary>
    /// The Device is enrolled on the Site, now or already; <see cref="DeviceEnrolmentResult.Device"/> describes it.
    /// </summary>
    Enrolled = 0,

    /// <summary>
    /// The Site is not active. Nothing was persisted.
    /// </summary>
    SiteNotFound = 1,

    /// <summary>
    /// The Device is enrolled on another Site. Nothing was persisted.
    /// </summary>
    OnAnotherSite = 2,

    /// <summary>
    /// The caller used the key within 24 h to enrol another Device on the Site. Nothing was persisted.
    /// </summary>
    IdempotencyKeyReused = 3,

    /// <summary>
    /// The Lot is not on the Site: uncreated, removed, or of another Site. The Device journaled nothing.
    /// </summary>
    LotNotFound = 4,

    /// <summary>
    /// Another Node holds the Lot. The Device journaled nothing.
    /// </summary>
    LotOccupied = 5,

    /// <summary>
    /// The Node is assigned to another Lot already; moving it is a separate operation. Nothing was persisted.
    /// </summary>
    AlreadyAssigned = 6,
}

/// <summary>
/// A Device as the grain holds it, without its key.
/// </summary>
/// <param name="Id">The Device ID.</param>
/// <param name="Kind">Hub or Node.</param>
/// <param name="SiteId">The Site the Device is enrolled on.</param>
/// <param name="LotId">The Lot a Node is assigned to, or <see langword="null"/>.</param>
[GenerateSerializer]
[Alias("coldframe.device-summary")]
public sealed record DeviceSummary(
    [property: Id(0)] string Id,
    [property: Id(1)] DeviceKind Kind,
    [property: Id(2)] string SiteId,
    [property: Id(3)] string? LotId = null);

/// <summary>
/// The result of <see cref="IDeviceGrain.Enrol"/>.
/// </summary>
/// <param name="Outcome">How the enrolment ended.</param>
/// <param name="Device">The Device, when <paramref name="Outcome"/> is <see cref="DeviceEnrolmentOutcome.Enrolled"/>.</param>
[GenerateSerializer]
[Alias("coldframe.device-enrolment-result")]
public sealed record DeviceEnrolmentResult(
    [property: Id(0)] DeviceEnrolmentOutcome Outcome,
    [property: Id(1)] DeviceSummary? Device = null);

/// <summary>
/// How <see cref="IDeviceGrain.Move"/> or <see cref="IDeviceGrain.Unassign"/> ended.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.device-assignment-outcome")]
public enum DeviceAssignmentOutcome
{
    /// <summary>
    /// The Node is on the requested Lot now (moved, or assigned when it was on none).
    /// </summary>
    Moved = 0,

    /// <summary>
    /// The Node is on no Lot now.
    /// </summary>
    Unassigned = 1,

    /// <summary>
    /// Nothing needed to change: the Node was on the requested Lot, or on none. Nothing was journaled.
    /// </summary>
    Unchanged = 2,

    /// <summary>
    /// The Device is not an enrolled Node of this Site. Nothing was journaled.
    /// </summary>
    NotFound = 3,

    /// <summary>
    /// The Lot is not on the Site: uncreated, removed, or of another Site. Nothing was journaled.
    /// </summary>
    LotNotFound = 4,

    /// <summary>
    /// Another Node holds the Lot. Nothing was journaled.
    /// </summary>
    LotOccupied = 5,
}

/// <summary>
/// The result of <see cref="IDeviceGrain.Move"/> or <see cref="IDeviceGrain.Unassign"/>.
/// </summary>
/// <param name="Outcome">How the call ended.</param>
/// <param name="Device">The Node afterwards, unless <paramref name="Outcome"/> is a refusal.</param>
[GenerateSerializer]
[Alias("coldframe.device-assignment-result")]
public sealed record DeviceAssignmentResult(
    [property: Id(0)] DeviceAssignmentOutcome Outcome,
    [property: Id(1)] DeviceSummary? Device = null);
