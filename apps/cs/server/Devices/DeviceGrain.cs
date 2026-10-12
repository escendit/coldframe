using System.Security.Cryptography;
using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Lots;
using Coldframe.Contracts.Sensors;
using Coldframe.Contracts.Sites;
using Coldframe.Crypto;
using Coldframe.Protocol.Device.V1;
using Coldframe.Server.Alerts;
using Coldframe.Server.Journal;
using Google.Protobuf;

namespace Coldframe.Server.Devices;

/// <summary>
/// A Device, keyed by its Device ID. The only writer of the Device's state and the owner of its Site
/// (AD-1, AD-18): it joins a Site by calling <see cref="ISiteGrain.RegisterDevice"/>, and a Node claims its
/// Lot with <see cref="ILotGrain.Claim"/>, before it journals <see cref="DeviceEnrolled"/> and
/// <see cref="DeviceAssigned"/>. It is the only source of which Lot a Node is on. It only ever receives
/// <c>K_dev</c> wrapped, and unwraps it only inside itself: to verify a Hub's signature
/// (<see cref="Heartbeat"/>, <see cref="AuthenticateRelay"/>) and to open a Node's frame and seal its
/// acknowledgement (<see cref="Ingest"/>), zeroing the keys again each time. It is the only writer of the
/// Readings, device-report, Reading-key and replay tables (AD-9), through <see cref="DeviceIngestionStore"/>.
/// It keeps a Node's known <c>spec_hash</c> and Sensor list (AD-19), and declares each Sensor of an accepted
/// Specification set to its <see cref="ISensorGrain"/>. After a frame is committed it hands every declared
/// Sensor of an assigned Node its Reading to evaluate (Story 6.1). It measures its own silence against a
/// journaled expiry and opens and closes its Health Alert of kind <c>silent</c> (Story 7.1).
/// </summary>
/// <remarks>
/// <para>
/// Silence (Story 7.1, AD-6, AD-7): the expiry is <see cref="DeviceState.SilenceDueAt"/>, the journaled clock
/// start plus the Silence Window. The <c>silence-window</c> reminder only wakes the grain; every wake, every
/// activation and every accepted heartbeat or frame processes what is overdue (<see cref="EvaluateSilenceAsync"/>).
/// The episode is journaled before <see cref="IAlertGrain.OpenForDevice"/> is called, and an open or a close the
/// Alert grain did not acknowledge is delivered again from state, the open before the close. A Node whose window
/// expired asks its last relay Hub first and opens nothing while that Hub is silent. A paused Device opens nothing.
/// </para>
/// <para>
/// Frames (AD-9, AD-17): the replay window is loaded from <c>device_replay</c> on first use and commits with
/// the Readings in one transaction; the downlink counter is reserved in that transaction and never kept in
/// memory, so it is never reused after a restart or a restore. A failed transaction drops the in-memory
/// window, which is read again for the next frame. No event is journaled per frame.
/// </para>
/// <para>
/// Specifications (AD-19): a frame whose <c>spec_hash</c> is not the known one is answered with
/// <c>specifications_unknown</c>. When it carries a valid set, every Sensor is declared first (idempotent),
/// and only then <see cref="DeviceSpecificationsDeclared"/> makes the hash known; a failure in between
/// answers retry and leaves the hash unknown, so the Node is asked again. A paused or unassigned Node
/// declares too: only Readings pass the Pause gate.
/// </para>
/// <para>
/// Hub requests (AD-12): Orleans keeps one activation per Device, so the nonces seen while it is active are
/// authoritative in memory; they are pruned once older than the skew window, beyond which the timestamp
/// check refuses them anyway. The persisted <see cref="DeviceState.LastHeartbeatTimestampMs"/> closes the gap
/// after a reactivation for heartbeats: a heartbeat must be above it, and the Hub's stamps strictly increase.
/// </para>
/// </remarks>
[GrainType("device")]
public sealed partial class DeviceGrain : JournaledStreamGrain<DeviceState>, IDeviceGrain, IRemindable
{
    // Pending Lot releases (AD-18) are retried by a grain timer while the grain is active, and by a grain
    // reminder, which survives a deactivation or a silo restart.
    private const string ReleaseReminderName = "release-pending-lots";

    private static readonly TimeSpan ReleaseTimerPeriod = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan ReleaseReminderPeriod = TimeSpan.FromMinutes(1);

    private IGrainTimer? _releaseTimer;

    /// <summary>
    /// The name of the reminder that wakes the grain to look at its Silence Window (Story 7.1). Every enrolled
    /// Device keeps it registered; its period is <see cref="SilenceRule.WakePeriod"/>.
    /// </summary>
    public const string SilenceReminderName = "silence-window";

    // How long a call waits for the silence reminder to be registered before it goes on without it.
    private static readonly TimeSpan SilenceReminderPatience = TimeSpan.FromSeconds(5);

    // Whether this activation registered the silence reminder already, and the registration under way.
    private bool _silenceReminderRegistered;
    private Task<IGrainReminder>? _silenceReminderRegistration;

    // The length of an HMAC-SHA256 signature.
    private const int SignatureLength = 32;

    // Nonce (hex) → the timestamp it was accepted with.
    private readonly Dictionary<string, long> _nonces = new(StringComparer.Ordinal);

    // The replay window of a Node's frames, or null until it is loaded from device_replay.
    private ReplayWindow? _replay;

    private string DeviceId => this.GetPrimaryKeyString();

    /// <inheritdoc />
    public async Task<DeviceEnrolmentResult> Enrol(EnrolDevice request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SiteId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CallerId);
        ArgumentException.ThrowIfNullOrEmpty(request.IdempotencyKey);
        ArgumentNullException.ThrowIfNull(request.WrappedKey);

        // Only a Node is put in a Lot; the Edge API refuses a Hub with a Lot before it gets here.
        var kind = State.SiteId is null ? request.Kind : State.Kind;
        if (request.LotId is not null && (request.Kind != DeviceKind.Node || kind != DeviceKind.Node))
        {
            throw new ArgumentException("Only a Node is assigned to a Lot.", nameof(request));
        }

        // 1. A Device belongs to one Site (AD-18); nothing is asked of either Site.
        if (State.SiteId is { } current && !string.Equals(current, request.SiteId, StringComparison.Ordinal))
        {
            return new DeviceEnrolmentResult(DeviceEnrolmentOutcome.OnAnotherSite);
        }

        // 2. The Site grain owns the roster and the idempotency rule; it persists nothing on a refusal.
        var registration = await GrainFactory
            .GetGrain<ISiteGrain>(request.SiteId)
            // Not cancelled by the caller: an abort between the Site's write and the Device's would split them.
            .RegisterDevice(DeviceId, request.Kind, $"{request.CallerId}:{request.IdempotencyKey}", CancellationToken.None);

        switch (registration.Outcome)
        {
            case DeviceRegistrationOutcome.NotFound:
                return new DeviceEnrolmentResult(DeviceEnrolmentOutcome.SiteNotFound);
            case DeviceRegistrationOutcome.IdempotencyKeyReused:
                return new DeviceEnrolmentResult(DeviceEnrolmentOutcome.IdempotencyKeyReused);
            case DeviceRegistrationOutcome.Registered:
                break;
            default:
                throw new InvalidOperationException($"Unexpected Device registration outcome {registration.Outcome}.");
        }

        // 3. The Lot grain owns occupancy (AD-18): its claim is the only check. A refused claim leaves only
        // the Site's idempotent registration, which a retry with any Lot reuses.
        var claimed = false;
        if (request.LotId is { } lotId && !string.Equals(State.LotId, lotId, StringComparison.Ordinal))
        {
            if (State.LotId is not null)
            {
                // Moving a Node is its own operation (Story 4.9).
                return new DeviceEnrolmentResult(DeviceEnrolmentOutcome.AlreadyAssigned);
            }

            var claim = await GrainFactory
                .GetGrain<ILotGrain>(lotId)
                // Not cancelled by the caller: a claim the Lot journaled must reach the Device's journal too.
                .Claim(request.SiteId, DeviceId, CancellationToken.None);

            switch (claim.Outcome)
            {
                case LotOutcome.NotFound or LotOutcome.AlreadyRemoved:
                    return new DeviceEnrolmentResult(DeviceEnrolmentOutcome.LotNotFound);
                case LotOutcome.Claimed:
                    return new DeviceEnrolmentResult(DeviceEnrolmentOutcome.LotOccupied);
                case LotOutcome.Held:
                    claimed = true;
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected Lot claim outcome {claim.Outcome}.");
            }
        }

        // 4. Only after the Site registered it and the Lot granted the claim, in one write. Enrolling again on
        // the same Site and Lot journals nothing.
        if (State.SiteId is null)
        {
            RaiseEvent(new DeviceEnrolled(request.SiteId, request.Kind, request.WrappedKey, Clock.GetUtcNow()));
        }

        if (claimed)
        {
            RaiseEvent(new DeviceAssigned(request.SiteId, request.LotId!, Clock.GetUtcNow()));
        }

        // 5. No compensating release: Orleans' log-consistency adaptor retries a failed or conflicting write
        // until it lands, so a claim the Lot granted is always recorded while this activation lives.
        if (State.SiteId is null || claimed)
        {
            await ConfirmEvents();
        }

        // 6. From now on the Device is woken to look at its Silence Window (Story 7.1).
        await EnsureSilenceReminderAsync();

        return new DeviceEnrolmentResult(
            DeviceEnrolmentOutcome.Enrolled,
            new DeviceSummary(DeviceId, State.Kind, State.SiteId!, State.LotId));
    }

    /// <inheritdoc />
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);

        // A crash between a move and its release left the old Lot pending: keep releasing it. Not inline, so
        // a Lot that cannot be reached never fails the activation.
        if (State.PendingReleases.Count > 0)
        {
            await StartReleasingAsync();
        }

        // Every activation processes what is overdue (AD-6), and applies the Server start to the silence clock.
        // It never throws, so a grain that cannot be reached never fails the activation.
        await EvaluateSilenceAsync(reported: false);
    }

    /// <inheritdoc />
    public async Task<DeviceAssignmentResult> Move(string siteId, string lotId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteId);
        ArgumentException.ThrowIfNullOrWhiteSpace(lotId);

        // Only a Node of this Site is moved; a Device of another Site is not disclosed.
        if (!IsNodeOf(siteId))
        {
            return new DeviceAssignmentResult(DeviceAssignmentOutcome.NotFound);
        }

        if (string.Equals(State.LotId, lotId, StringComparison.Ordinal))
        {
            await ReleasePendingAsync();
            return new DeviceAssignmentResult(DeviceAssignmentOutcome.Unchanged, Summary());
        }

        // 1. The Lot grain owns occupancy (AD-18): its claim is the only check. A refused claim changes nothing.
        var claim = await GrainFactory
            .GetGrain<ILotGrain>(lotId)
            // Not cancelled by the caller: a claim the Lot journaled must reach the Device's journal too.
            .Claim(siteId, DeviceId, CancellationToken.None);

        switch (claim.Outcome)
        {
            case LotOutcome.NotFound or LotOutcome.AlreadyRemoved:
                return new DeviceAssignmentResult(DeviceAssignmentOutcome.LotNotFound);
            case LotOutcome.Claimed:
                return new DeviceAssignmentResult(DeviceAssignmentOutcome.LotOccupied);
            case LotOutcome.Held:
                break;
            default:
                throw new InvalidOperationException($"Unexpected Lot claim outcome {claim.Outcome}.");
        }

        // 2. Only after the claim: the journal. A Node on no Lot is simply assigned.
        if (State.LotId is { } from)
        {
            RaiseEvent(new DeviceMoved(siteId, from, lotId, Clock.GetUtcNow()));
        }
        else
        {
            RaiseEvent(new DeviceAssigned(siteId, lotId, Clock.GetUtcNow()));
        }

        // No compensating release: the log-consistency adaptor retries a conflicting write until it lands.
        await ConfirmEvents();
        await CatchUpDevicesAsync();

        // 3. Last, the old Lot, from the pending state the event just persisted.
        await ReleasePendingAsync();

        return new DeviceAssignmentResult(DeviceAssignmentOutcome.Moved, Summary());
    }

    /// <inheritdoc />
    public async Task<DeviceAssignmentResult> Unassign(string siteId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteId);

        if (!IsNodeOf(siteId))
        {
            return new DeviceAssignmentResult(DeviceAssignmentOutcome.NotFound);
        }

        if (State.LotId is not { } from)
        {
            await ReleasePendingAsync();
            return new DeviceAssignmentResult(DeviceAssignmentOutcome.Unchanged, Summary());
        }

        RaiseEvent(new DeviceUnassigned(siteId, from, Clock.GetUtcNow()));
        await ConfirmEvents();
        await CatchUpDevicesAsync();
        await ReleasePendingAsync();

        return new DeviceAssignmentResult(DeviceAssignmentOutcome.Unassigned, Summary());
    }

    /// <inheritdoc />
    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (string.Equals(reminderName, ReleaseReminderName, StringComparison.Ordinal))
        {
            await ReleasePendingAsync();

            // A stray reminder (its unregister failed earlier) finds nothing pending and removes itself.
            if (State.PendingReleases.Count == 0)
            {
                await UnregisterReminderAsync(ReleaseReminderName);
            }
        }
        else if (string.Equals(reminderName, SilenceReminderName, StringComparison.Ordinal))
        {
            // Only an enrolled Device has a Silence Window; a stray reminder removes itself.
            if (State.SiteId is null)
            {
                await UnregisterReminderAsync(SilenceReminderName);
                return;
            }

            await EvaluateSilenceAsync(reported: false);
        }
    }

    // Read-your-writes for the Devices list. The events are journaled, so a failure here only delays the list.
    private async Task CatchUpDevicesAsync()
    {
        try
        {
            await ServiceProvider.GetProjectionRunner<DevicesProjector>().CatchUpAsync(CancellationToken.None);
        }
#pragma warning disable CA1031 // The list follows on the next poll or hint.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogCatchUpFailed(Logger, DeviceId, exception);
        }
    }

    private bool IsNodeOf(string siteId) =>
        State.SiteId is { } current && string.Equals(current, siteId, StringComparison.Ordinal) && State.Kind == DeviceKind.Node;

    private DeviceSummary Summary() => new(DeviceId, State.Kind, State.SiteId!, State.LotId);

    // Releases every Lot the Node left, in order. A Lot that answers released or unchanged is done (journaled
    // as DeviceLotReleased); one that throws stays pending and is tried again by the timer and the reminder.
    // Never throws: the move or unassign it follows is already journaled.
    private async Task ReleasePendingAsync()
    {
        if (State.PendingReleases.Count == 0 || State.SiteId is not { } siteId)
        {
            await StopReleasingAsync();
            return;
        }

        foreach (var lotId in State.PendingReleases.ToList())
        {
            try
            {
                // A Lot the Node is on again is never released.
                if (!string.Equals(State.LotId, lotId, StringComparison.Ordinal))
                {
                    await GrainFactory
                        .GetGrain<ILotGrain>(lotId)
                        // Not cancelled by the caller: the release is retried until it lands.
                        .Release(siteId, DeviceId, CancellationToken.None);
                }

                RaiseEvent(new DeviceLotReleased(lotId, Clock.GetUtcNow()));
                await ConfirmEvents();
            }
#pragma warning disable CA1031 // Whatever failed the release, it stays pending and is retried.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                LogReleaseFailed(Logger, DeviceId, lotId, exception);
            }
        }

        if (State.PendingReleases.Count > 0)
        {
            await StartReleasingAsync();
        }
        else
        {
            await StopReleasingAsync();
        }
    }

    // The timer and the reminder are created together, once; later ticks touch neither. Awaited in the
    // grain turn, so a register and an unregister can never reorder.
    private async Task StartReleasingAsync()
    {
        if (_releaseTimer is not null)
        {
            return;
        }

        _releaseTimer = this.RegisterGrainTimer(
            ReleasePendingAsync,
            new GrainTimerCreationOptions(ReleaseTimerPeriod, ReleaseTimerPeriod));

        await RegisterReminderAsync();
    }

    private async Task RegisterReminderAsync()
    {
        try
        {
            await this.RegisterOrUpdateReminder(ReleaseReminderName, ReleaseReminderPeriod, ReleaseReminderPeriod);
        }
#pragma warning disable CA1031 // Without a reminder the timer and the next activation still retry.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogReleaseFailed(Logger, DeviceId, ReleaseReminderName, exception);
        }
    }

    private async Task StopReleasingAsync()
    {
        if (_releaseTimer is null)
        {
            return;
        }

        _releaseTimer.Dispose();
        _releaseTimer = null;
        await UnregisterReminderAsync(ReleaseReminderName);
    }

    private async Task UnregisterReminderAsync(string reminderName)
    {
        try
        {
            if (await this.GetReminder(reminderName) is { } reminder)
            {
                await this.UnregisterReminder(reminder);
            }
        }
#pragma warning disable CA1031 // A stray reminder only finds nothing pending and unregisters itself next time.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogReleaseFailed(Logger, DeviceId, "reminder", exception);
        }
    }

    /// <inheritdoc />
    public async Task<DeviceHeartbeatResult> Heartbeat(DeviceHeartbeat request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var refused = new DeviceHeartbeatResult(DeviceHeartbeatOutcome.Unauthorized);
        var now = Clock.GetUtcNow();
        var nowMs = now.ToUnixTimeMilliseconds();

        if (State.SiteId is null || State.WrappedKey is not { } wrapped)
        {
            return refused;
        }

        if (request.TimestampMs < 0
            || Math.Abs(request.TimestampMs - nowMs) > CryptoSpec.HeartbeatMaxSkewMs
            || request.TimestampMs <= State.LastHeartbeatTimestampMs
            || request.Nonce is not { Length: CryptoSpec.HeartbeatNonceLength }
            || request.Signature is not { Length: SignatureLength })
        {
            return refused;
        }

        PruneNonces(nowMs);
        var nonce = Convert.ToHexStringLower(request.Nonce);
        if (_nonces.ContainsKey(nonce)
            || !Verify(wrapped, request.Method, request.Path, request.Body, request.TimestampMs, request.Nonce, request.Signature))
        {
            return refused;
        }

        RaiseEvent(new DeviceSeen(now, request.TimestampMs, request.UptimeMs));
        await ConfirmEvents();

        // Only once the heartbeat is journaled: a failed write accepted nothing.
        _nonces[nonce] = request.TimestampMs;

        // The heartbeat moved the silence clock: a silent Hub has recovered (Story 7.1). Never throws.
        await EvaluateSilenceAsync(reported: true);

        return new DeviceHeartbeatResult(DeviceHeartbeatOutcome.Accepted);
    }

    /// <inheritdoc />
    public Task<DeviceRelayAuthenticationResult> AuthenticateRelay(DeviceRelayAuthentication request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var refused = Task.FromResult(new DeviceRelayAuthenticationResult(false));
        var nowMs = Clock.GetUtcNow().ToUnixTimeMilliseconds();

        // Only an enrolled Hub relays (AD-18); a Node's key signs nothing here.
        if (State.SiteId is null || State.Kind != DeviceKind.Hub || State.WrappedKey is not { } wrapped)
        {
            return refused;
        }

        if (request.TimestampMs < 0
            || Math.Abs(request.TimestampMs - nowMs) > CryptoSpec.HeartbeatMaxSkewMs
            || request.Body is null
            || request.Nonce is not { Length: CryptoSpec.HeartbeatNonceLength }
            || request.Signature is not { Length: SignatureLength })
        {
            return refused;
        }

        PruneNonces(nowMs);
        var nonce = Convert.ToHexStringLower(request.Nonce);
        if (_nonces.ContainsKey(nonce)
            || !Verify(wrapped, request.Method, request.Path, request.Body, request.TimestampMs, request.Nonce, request.Signature))
        {
            return refused;
        }

        // No event and no timestamp rule: the frames carry their own replay protection (AD-17).
        _nonces[nonce] = request.TimestampMs;

        return Task.FromResult(new DeviceRelayAuthenticationResult(true));
    }

    /// <inheritdoc />
    public async Task<DeviceIngestResult> Ingest(DeviceIngest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(request.RelayHubId);

        // 1. Only an enrolled Node, and only the supported protocol version.
        if (State.SiteId is null || State.Kind != DeviceKind.Node || State.WrappedKey is not { } wrapped)
        {
            return new DeviceIngestResult(DeviceIngestStatus.UnknownDevice);
        }

        if (request.ProtocolVersion != CryptoSpec.ProtocolMajor || request.Ciphertext is null)
        {
            return new DeviceIngestResult(DeviceIngestStatus.RejectedAuth);
        }

        var deviceId = Coldframe.Crypto.DeviceId.Parse(DeviceId);
        var vault = ServiceProvider.GetRequiredService<DeviceKeyVault>();
        var store = ServiceProvider.GetRequiredService<DeviceIngestionStore>();
        byte[]? deviceKey = null;
        byte[]? sealKey = null;
        byte[]? ackKey = null;

        try
        {
            // 2. Open the seal with the seal/v1 key.
            byte[] plaintext;
            try
            {
                deviceKey = vault.Unwrap(deviceId, wrapped);
                sealKey = KeyHierarchy.DerivePurposeKey(deviceKey, CryptoSpec.SealLabel);
                plaintext = Frames.Open(sealKey, deviceId, request.Counter, request.Ciphertext);
            }
            catch (CryptoFailureException)
            {
                return new DeviceIngestResult(DeviceIngestStatus.RejectedAuth);
            }

            // 3. The replay window. The in-memory window moves only once the frame's transaction commits.
            ReplayWindow window;
            try
            {
                _replay ??= await store.LoadReplayAsync(DeviceId, CancellationToken.None) is { } stored
                    ? ReplayWindow.FromState(stored.HighWater, stored.Seen)
                    : new ReplayWindow();
                window = _replay.Clone();
            }
#pragma warning disable CA1031 // Whatever keeps the replay state from being read, the Node resends.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                LogIngestFailed(Logger, DeviceId, exception);
                return new DeviceIngestResult(DeviceIngestStatus.Retry);
            }

            if (!window.WouldAccept(request.Counter))
            {
                return new DeviceIngestResult(DeviceIngestStatus.RejectedReplay);
            }

            window.Accept(request.Counter);
            var replay = new StoredReplay(window.Highest!.Value, window.Seen);

            // 4. Decode the Node frame and check its time. An authentic frame the Server cannot store
            // still consumes its counter, without rows and without a downlink.
            var receivedAt = Clock.GetUtcNow();
            var frame = NodeFrameReader.Read(plaintext, deviceId, receivedAt);

            if (frame.Verdict != NodeFrameVerdict.Valid)
            {
                if (await CommitAsync(store, new FrameCommit(DeviceId, replay, ReserveDownlink: false, Rows: null), window) is null)
                {
                    return new DeviceIngestResult(DeviceIngestStatus.Retry);
                }

                return new DeviceIngestResult(
                    frame.Verdict == NodeFrameVerdict.FutureTime ? DeviceIngestStatus.RejectedTime : DeviceIngestStatus.RejectedAuth);
            }

            // 5. The Pause gate (AD-8): a paused Device's Readings are acknowledged and discarded.
            var paused = State.IsPaused;

            // 6. One transaction: Reading keys, Reading rows, the device report, the replay window and the
            // reserved downlink counter.
            var committed = await CommitAsync(store, new FrameCommit(DeviceId, replay, ReserveDownlink: true, paused ? null : Stamped(frame.Rows)), window);
            if (committed?.DownlinkCounter is not { } downlinkCounter)
            {
                return new DeviceIngestResult(DeviceIngestStatus.Retry);
            }

            // 6a. The readings hint (Story 6.6), only after the commit and only when a row was new: the open
            // web app of the Site's members reads the Lot again. It never fails or repeats the commit.
            if (!paused && committed.NewKeys > 0)
            {
                HintReadings(frame.Rows, committed);
            }

            // 7. The Hub that relayed the frame, journaled only when it changes and only once the frame is
            // committed (AD-18, DW-45), and the Node's liveness (Story 7.1): the frame's measured_at when it is
            // newer than any accepted before, never the arrival time. A paused Node's frame was discarded and is
            // no report. When the write fails the answer is retry: the Node resends, and the resend is a
            // duplicate that records both.
            var relayChanged = !string.Equals(State.LastRelayHubId, request.RelayHubId, StringComparison.Ordinal);
            var liveness = !paused && frame.Rows is { } accepted && (State.LastReportedAt is not { } last || accepted.MeasuredAt > last)
                ? accepted.MeasuredAt
                : (DateTimeOffset?)null;

            if (relayChanged || liveness is not null)
            {
                try
                {
                    if (relayChanged)
                    {
                        RaiseEvent(new DeviceRelayChanged(request.RelayHubId, receivedAt));
                    }

                    if (liveness is { } measuredAt)
                    {
                        RaiseEvent(new DeviceReported(measuredAt, receivedAt));
                    }

                    await ConfirmEvents();
                }
#pragma warning disable CA1031 // The relay Hub or the liveness could not be persisted: no acknowledgement, the Node resends.
                catch (Exception exception)
#pragma warning restore CA1031
                {
                    LogIngestFailed(Logger, DeviceId, exception);
                    return new DeviceIngestResult(DeviceIngestStatus.Retry);
                }
            }

            // 7a. Silence (Story 7.1): an accepted frame may end the Node's silence, and whatever is overdue is
            // processed. It never throws and never changes the frame's answer: an Alert that could not be
            // delivered is delivered again by the next wake.
            if (!paused)
            {
                await EvaluateSilenceAsync(reported: true);
            }

            // 8. The Specification set (AD-19), after the commit and whether or not the Device is paused. A known
            // hash needs nothing, with or without a set attached.
            var specificationsKnown = State.Knows(frame.SpecHash);
            if (!specificationsKnown && frame.Specifications is { } attached)
            {
                if (attached.Valid is not { } specifications)
                {
                    // A firmware fault in metadata must not block Readings: the set is ignored and asked for again.
                    LogInvalidSpecifications(Logger, DeviceId);
                }
                else
                {
                    try
                    {
                        await DeclareAsync(deviceId, frame.SpecHash!, specifications);
                        specificationsKnown = true;
                    }
#pragma warning disable CA1031 // The set could not be declared: no acknowledgement, the Node resends and is asked again.
                    catch (Exception exception)
#pragma warning restore CA1031
                    {
                        LogDeclarationFailed(Logger, DeviceId, exception);
                        return new DeviceIngestResult(DeviceIngestStatus.Retry);
                    }
                }
            }

            // 9. Evaluation (Story 6.1), after the commit and the declaration: every declared Sensor of an
            // assigned Node gets its Reading, on a first delivery and on a resend alike, with the time stored at
            // its first delivery. When an evaluation or the delivery of what it opened or closed fails, the frame
            // is not acknowledged: the Node resends, and the resend, a duplicate, finishes it.
            if (!paused && !await EvaluateAsync(frame.Rows, committed))
            {
                return new DeviceIngestResult(DeviceIngestStatus.Retry);
            }

            // 10. Only after the commit: the acknowledgement, sealed with the ack/v1 key under the fresh counter.
            var downlink = new Downlink
            {
                ProtocolVersion = CryptoSpec.ProtocolMajor,
                AckedCounter = request.Counter,
                ServerTimeMs = Clock.GetUtcNow().ToUnixTimeMilliseconds(),
                SpecificationsUnknown = !specificationsKnown,
            };
            downlink.AckedReadings.Add(frame.Acknowledged);

            ackKey = KeyHierarchy.DerivePurposeKey(deviceKey, CryptoSpec.AckLabel);
            var envelope = new SealedEnvelope
            {
                ProtocolVersion = CryptoSpec.ProtocolMajor,
                DeviceId = ByteString.CopyFrom(deviceId.ToBytes()),
                Counter = downlinkCounter,
                Ciphertext = ByteString.CopyFrom(Frames.Seal(ackKey, deviceId, downlinkCounter, downlink.ToByteArray())),
            };

            return new DeviceIngestResult(
                paused || committed.NewKeys > 0 ? DeviceIngestStatus.Stored : DeviceIngestStatus.Duplicate,
                envelope.ToByteArray());
        }
        finally
        {
            Zero(deviceKey);
            Zero(sealKey);
            Zero(ackKey);
        }
    }

    /// <inheritdoc />
    public Task<DeviceSummary?> Describe(CancellationToken cancellationToken = default) =>
        Task.FromResult<DeviceSummary?>(State.SiteId is null ? null : Summary());

    /// <inheritdoc />
    public async Task SetCalibration(SetCalibration request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (State.SiteId is null || State.Kind != DeviceKind.Node)
        {
            throw new InvalidOperationException("Only an enrolled Node holds a Calibration.");
        }

        // A redelivery, or an older one arriving late: the cache already holds it.
        if (State.HoldsCalibration(request.SensorId, request.Revision))
        {
            return;
        }

        RaiseEvent(new DeviceCalibrationSet(request.SensorId, request.CalibrationId, request.Revision, Clock.GetUtcNow()));
        await ConfirmEvents();
    }

    /// <inheritdoc />
    public async Task<DeviceSilenceWindowResult> SetSilenceWindow(string siteId, TimeSpan? window, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteId);

        // A Device of another Site is not disclosed.
        if (State.SiteId is not { } current || !string.Equals(current, siteId, StringComparison.Ordinal))
        {
            return new DeviceSilenceWindowResult(DeviceSilenceWindowOutcome.NotFound);
        }

        if (window is { } requested && !SilenceRule.IsValid(requested))
        {
            return new DeviceSilenceWindowResult(DeviceSilenceWindowOutcome.OutOfRange, State.SilenceWindow);
        }

        if (State.SilenceWindowOverride == window)
        {
            return new DeviceSilenceWindowResult(DeviceSilenceWindowOutcome.Unchanged, State.SilenceWindow);
        }

        RaiseEvent(new DeviceSilenceWindowSet(window is { } set ? (long)set.TotalSeconds : null, Clock.GetUtcNow()));
        await ConfirmEvents();

        return new DeviceSilenceWindowResult(DeviceSilenceWindowOutcome.Set, State.SilenceWindow);
    }

    /// <inheritdoc />
    public async Task<DeviceSilence?> Silence(CancellationToken cancellationToken = default)
    {
        if (State.SiteId is null || State.SilenceFrom is null)
        {
            return null;
        }

        // First what is overdue: the answer must not depend on whose reminder fired first.
        await EvaluateSilenceAsync(reported: false);

        return new DeviceSilence(
            State.Kind,
            State.SilenceWindow,
            State.SilenceWindowOverride is not null,
            State.SilenceFrom!.Value,
            State.SilenceDueAt!.Value,
            State.IsPaused,
            State.OpenSilentAlert,
            State.LastRecoveredAt);
    }

    private ILogger Logger => ServiceProvider.GetRequiredService<ILogger<DeviceGrain>>();

    // Processes what is overdue of the Device's Silence Window (Story 7.1), in this order: the Server start
    // restarts the clock, an accepted report ends an open silence whose expiry is ahead again, an expiry that
    // passed starts the next episode (unless the Device is paused or its relay Hub is silent), and every open and
    // close the Alert grain has not acknowledged is delivered. `reported` says that the Server just accepted a
    // heartbeat or a frame: only then an open Alert may close, so a Server start or a longer window never closes
    // one. Never throws: whatever failed is state in the journal, and the next wake goes on from there.
    private async Task EvaluateSilenceAsync(bool reported)
    {
        if (State.SiteId is not { } siteId)
        {
            return;
        }

        try
        {
            await EnsureSilenceReminderAsync();

            // 1. Server downtime is never Device silence: the clock restarts at the Server's start. Not while an
            // Alert is open, where only a report of the Device counts.
            var serverStart = ServiceProvider.GetRequiredService<ServerStart>().At;
            if (State.OpenSilentAlert is null && State.SilenceFrom is { } from && serverStart > from)
            {
                RaiseEvent(new DeviceSilenceClockRestarted(serverStart, SilenceClockRestartCause.ServerStarted, HubId: null, Clock.GetUtcNow()));
                await ConfirmEvents();
            }

            var now = Clock.GetUtcNow();

            if (State.OpenSilentAlert is { } open)
            {
                // 2. Open means overdue: a report closes the Alert only when the expiry is ahead again. A Hub's
                // heartbeat always does that; a Node flushing an old backlog does not.
                if (reported && State.SilenceDueAt is { } dueAt && !SilenceRule.IsOverdue(dueAt, now))
                {
                    RaiseEvent(new DeviceSilenceEnded(open.Episode, open.AlertId, AlertCloseReason.Recovered, now));
                    await ConfirmEvents();
                }
            }
            else if (!State.IsPaused
                && State.SilenceDueAt is { } dueAt
                && SilenceRule.IsOverdue(dueAt, now)
                && await RelayHubAllowsAsync(now))
            {
                // 3. The episode first, so a retry derives the same Alert ID.
                var episode = State.SilenceEpisode + 1;
                RaiseEvent(new DeviceSilenceStarted(
                    episode,
                    AlertIds.Silent(DeviceId, episode),
                    siteId,
                    State.LotId,
                    State.SilenceDueAt ?? dueAt,
                    now,
                    State.LastHeardAt));
                await ConfirmEvents();
            }

            // 4. Whatever the Alert grain has not acknowledged, the open before the close.
            await DeliverAlertsAsync();
        }
#pragma warning disable CA1031 // Whatever failed, the journaled state stands and the next wake tries again.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogSilenceEvaluationFailed(Logger, DeviceId, exception);
        }
    }

    // Hub silence is reported as the Hub only (AD-7): a Node whose window expired asks its last relay Hub, which
    // first processes its own expiry. A silent Hub suppresses the Node's Alert. A Hub that recovered after the
    // Node's clock start restarts the Node's clock at its recovery, because until then the Node could not have
    // reported; the Node is silent only when the window counted from there has passed too. A relay Hub that is
    // no enrolled Hub suppresses nothing. A Hub that cannot be reached throws: nothing opens, and the next wake
    // asks again. The Hub relation never decides whether a frame is accepted.
    private async Task<bool> RelayHubAllowsAsync(DateTimeOffset now)
    {
        if (State.Kind != DeviceKind.Node
            || State.LastRelayHubId is not { } hubId
            || string.Equals(hubId, DeviceId, StringComparison.Ordinal))
        {
            return true;
        }

        // Not cancelled by the caller: a wake has no caller to cancel it.
        var hub = await GrainFactory.GetGrain<IDeviceGrain>(hubId).Silence(CancellationToken.None);

        if (hub is not { Kind: DeviceKind.Hub })
        {
            return true;
        }

        if (hub.OpenAlert is not null)
        {
            return false;
        }

        if (hub.LastRecoveredAt is { } recoveredAt && State.SilenceFrom is { } from && recoveredAt > from)
        {
            RaiseEvent(new DeviceSilenceClockRestarted(recoveredAt, SilenceClockRestartCause.RelayHubRecovered, hubId, now));
            await ConfirmEvents();

            return State.SilenceDueAt is { } dueAt && SilenceRule.IsOverdue(dueAt, now);
        }

        return true;
    }

    // Delivers every open and close of the Device's Alerts that the Alert grain has not acknowledged, oldest
    // first, and journals each acknowledgement. It stops at the first one that fails, so a close never overtakes
    // its open. An Alert whose Site has not acknowledged it yet counts as not delivered. Throws when a delivery
    // fails; its caller leaves the rest pending for the next wake.
    private async Task DeliverAlertsAsync()
    {
        foreach (var delivery in State.PendingAlertDeliveries.ToList())
        {
            var alert = GrainFactory.GetGrain<IAlertGrain>(delivery.AlertId.ToString("D"));

            // Not cancelled by the caller: the journaled episode is delivered until the Alert grain holds it.
            var result = delivery.Change == AlertLifecycle.Open
                ? await alert.OpenForDevice(
                    new OpenDeviceAlert(delivery.Kind, DeviceId, delivery.Episode, delivery.SiteId!, delivery.LotId, delivery.At, State.Kind, delivery.SilentSince),
                    CancellationToken.None)
                : await alert.Close(new CloseAlert(delivery.Reason ?? AlertCloseReason.Recovered, delivery.At), CancellationToken.None);

            var accepted = delivery.Change == AlertLifecycle.Open
                ? result.Outcome is AlertOutcome.Open or AlertOutcome.Closed
                : result.Outcome == AlertOutcome.Closed;

            if (!accepted || !result.Reported)
            {
                throw new InvalidOperationException(
                    $"Alert {delivery.AlertId:D} answered {result.Outcome} (reported: {result.Reported}) to {delivery.Change}.");
            }

            RaiseEvent(new DeviceAlertDelivered(delivery.AlertId, delivery.Change, Clock.GetUtcNow()));
            await ConfirmEvents();
        }
    }

    // Registered once per activation; RegisterOrUpdateReminder keeps one reminder however often it is called, so
    // a Device enrolled before Story 7.1 gets its reminder with its next activation.
    private async Task EnsureSilenceReminderAsync()
    {
        if (_silenceReminderRegistered || State.SiteId is null)
        {
            return;
        }

        try
        {
            // A report or a wake never waits long for the reminder service, which can take a while to answer
            // after a silo start: the registration goes on, and the next call looks at it again.
            _silenceReminderRegistration ??=
                this.RegisterOrUpdateReminder(SilenceReminderName, SilenceRule.WakePeriod, SilenceRule.WakePeriod);
            await _silenceReminderRegistration.WaitAsync(SilenceReminderPatience);
            _silenceReminderRegistered = true;
        }
#pragma warning disable CA1031 // Without a reminder the next activation and the next report still evaluate, and register it again.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            if (_silenceReminderRegistration is { IsCompleted: true })
            {
                _silenceReminderRegistration = null;
            }

            LogSilenceReminderFailed(Logger, DeviceId, exception);
        }
    }

    // Stamps each Reading with the Calibration in force for its Sensor (Story 5.1): the Sensor grain set it, and
    // only Readings stored from now on carry it. A Sensor without a Calibration stores none.
    private FrameRows? Stamped(FrameRows? rows) =>
        rows is null
            ? null
            : rows with
            {
                Readings = [.. rows.Readings.Select(reading => reading with { CalibrationId = State.CalibrationOf(reading.SensorId) })],
            };

    // Hands each declared Sensor its Reading with where the Node is (Site, Lot, epoch): the Sensor grain knows
    // only its Device and must not call back. Nothing is evaluated for an unassigned Node (AD-8) or for a slot
    // that is not in the accepted Specification set (AD-19); those Readings are stored all the same. A Reading
    // is evaluated with the measured_at stored with its key at first delivery, never with the time of the frame
    // that carries it again: an unsynced Node's Reading gets a later time on every resend, and the Sensor grain,
    // which only evaluates a Reading newer than the last one, would count it once per resend. False when a
    // Sensor could not evaluate or deliver. Never throws.
    private async Task<bool> EvaluateAsync(FrameRows? rows, FrameCommitted committed)
    {
        if (rows is null || rows.Readings.Count == 0 || State.SiteId is not { } siteId || State.LotId is not { } lotId)
        {
            return true;
        }

        var context = new EvaluationContext(siteId, lotId, State.EvaluationEpoch);
        var declared = State.Sensors.Select(sensor => sensor.SensorId).ToHashSet();

        try
        {
            // One evaluation per Sensor and frame: of two Readings of one Sensor only the newer one can count.
            var evaluations = rows.Readings
                .Where(reading => declared.Contains(reading.SensorId))
                .GroupBy(reading => reading.SensorId)
                .Select(readings => readings.MaxBy(reading => reading.ReadingSeq)!)
                .Select(reading => GrainFactory
                    .GetGrain<ISensorGrain>(reading.SensorId.ToString("D"))
                    // Not cancelled by the caller: the frame is committed, and its evaluation is not abandoned halfway.
                    .Evaluate(
                        new EvaluateReading(
                            reading.RawValue,
                            committed.MeasuredAtOf(reading.SensorId, reading.ReadingSeq, rows.MeasuredAt),
                            context),
                        CancellationToken.None))
                .ToList();

            var results = await Task.WhenAll(evaluations);
            return results.All(result => result.Outcome != SensorEvaluationOutcome.NotDelivered);
        }
#pragma warning disable CA1031 // Whatever failed the evaluation, the answer is retry: the Node resends.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogEvaluationFailed(Logger, DeviceId, exception);
            return false;
        }
    }

    // Tells the hub that the Lot of this Node has new Readings. The version is the newest measured_at of the
    // frame as its keys keep it, in Unix milliseconds; nothing of a Reading is handed on. An unassigned Node has
    // no Lot and hints nothing. Never throws.
    private void HintReadings(FrameRows? rows, FrameCommitted committed)
    {
        if (rows is null || State.SiteId is not { } siteId || State.LotId is not { } lotId)
        {
            return;
        }

        try
        {
            var newest = rows.Readings
                .Select(reading => committed.MeasuredAtOf(reading.SensorId, reading.ReadingSeq, rows.MeasuredAt))
                .Append(rows.MeasuredAt)
                .Max();

            ServiceProvider.GetService<IReadModelHints>()?.Publish(
                [new ReadModelChange(siteId, ReadModelResources.Readings, lotId, Math.Max(1, newest.ToUnixTimeMilliseconds()))]);
        }
#pragma warning disable CA1031 // A hint is best effort: the frame is committed and is acknowledged all the same.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogHintFailed(Logger, DeviceId, exception);
        }
    }

    // Commits a frame and, on success, adopts its replay window. A failed transaction stored nothing: the
    // in-memory window is dropped and read again for the next frame, so it always equals the stored one.
    private async Task<FrameCommitted?> CommitAsync(DeviceIngestionStore store, FrameCommit commit, ReplayWindow window)
    {
        try
        {
            // Not cancelled by the caller: a commit is never abandoned halfway.
            var committed = await store.CommitAsync(commit, CancellationToken.None);
            _replay = window;
            return committed;
        }
#pragma warning disable CA1031 // Whatever failed the transaction, the answer is retry: the Node resends.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _replay = null;
            LogIngestFailed(Logger, DeviceId, exception);
            return null;
        }
    }

    // Every Sensor grain first, then the Device's own event with the hash and the Sensor list (AD-19). A
    // declaration is idempotent, so a set that was declared in part is simply declared again.
    private async Task DeclareAsync(Coldframe.Crypto.DeviceId deviceId, byte[] specHash, IReadOnlyList<SensorSpecification> specifications)
    {
        var sensors = new List<DeclaredSensor>(specifications.Count);
        var declarations = new List<Task>(specifications.Count);

        for (var slot = 0; slot < specifications.Count; slot++)
        {
            var specification = specifications[slot];
            var sensorId = SensorIds.Derive(deviceId, (uint)slot, specification.Quantity);
            sensors.Add(new DeclaredSensor(slot, specification.Quantity, sensorId));
            declarations.Add(GrainFactory
                .GetGrain<ISensorGrain>(sensorId.ToString("D"))
                // Not cancelled by the caller: the frame is committed, and its declaration is not abandoned halfway.
                .Declare(new DeclareSensor(DeviceId, slot, specification), CancellationToken.None));
        }

        await Task.WhenAll(declarations);

        RaiseEvent(new DeviceSpecificationsDeclared(specHash, sensors, Clock.GetUtcNow()));
        await ConfirmEvents();
    }

    private static void Zero(byte[]? key)
    {
        if (key is not null)
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    // Never the frame, its payload or a key: only the Device and the failure.
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "A frame of Device {DeviceId} could not be committed; it is answered with retry.")]
    private static partial void LogIngestFailed(ILogger logger, string deviceId, Exception exception);

    // Never the set or anything in it: only the Device.
    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Device {DeviceId} sent a Specification set that breaks the contract; it is ignored and asked for again.")]
    private static partial void LogInvalidSpecifications(ILogger logger, string deviceId);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Releasing Lot {LotId} for Device {DeviceId} failed; it stays pending and is retried.")]
    private static partial void LogReleaseFailed(ILogger logger, string deviceId, string lotId, Exception exception);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "The Devices list could not be brought up to date after Device {DeviceId} changed Lot; it follows on the next poll.")]
    private static partial void LogCatchUpFailed(ILogger logger, string deviceId, Exception exception);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "The Specification set of Device {DeviceId} could not be declared; the frame is answered with retry.")]
    private static partial void LogDeclarationFailed(ILogger logger, string deviceId, Exception exception);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "A Reading of Device {DeviceId} could not be evaluated; the frame is answered with retry.")]
    private static partial void LogEvaluationFailed(ILogger logger, string deviceId, Exception exception);

    [LoggerMessage(EventId = 7, EventName = "ReadingsHintFailed", Level = LogLevel.Warning, Message = "The readings hint for Device {DeviceId} could not be published; the frame is stored and acknowledged all the same.")]
    private static partial void LogHintFailed(ILogger logger, string deviceId, Exception exception);

    [LoggerMessage(EventId = 8, EventName = "SilenceEvaluationFailed", Level = LogLevel.Warning, Message = "The Silence Window of Device {DeviceId} could not be evaluated or its Alert not delivered; the next wake tries again.")]
    private static partial void LogSilenceEvaluationFailed(ILogger logger, string deviceId, Exception exception);

    [LoggerMessage(EventId = 9, EventName = "SilenceReminderFailed", Level = LogLevel.Warning, Message = "The silence reminder of Device {DeviceId} could not be registered; the next activation tries again.")]
    private static partial void LogSilenceReminderFailed(ILogger logger, string deviceId, Exception exception);

    // Unwraps K_dev, derives the hub-auth/v1 key, verifies, and zeroes both keys on every path.
    private bool Verify(WrappedDeviceKey wrapped, string method, string path, byte[] body, long timestampMs, byte[] nonce, byte[] signature)
    {
        var vault = ServiceProvider.GetRequiredService<DeviceKeyVault>();
        byte[]? deviceKey = null;
        byte[]? hubAuthKey = null;

        try
        {
            deviceKey = vault.Unwrap(Coldframe.Crypto.DeviceId.Parse(DeviceId), wrapped);
            hubAuthKey = KeyHierarchy.DerivePurposeKey(deviceKey, CryptoSpec.HubAuthLabel);

            return Coldframe.Crypto.Heartbeat.Verify(hubAuthKey, method, path, body, timestampMs, nonce, signature);
        }
        catch (CryptoFailureException)
        {
            // A key wrapped under another KEK, or tampered at rest: this Device cannot authenticate.
            return false;
        }
        finally
        {
            if (deviceKey is not null)
            {
                CryptographicOperations.ZeroMemory(deviceKey);
            }

            if (hubAuthKey is not null)
            {
                CryptographicOperations.ZeroMemory(hubAuthKey);
            }
        }
    }

    // A nonce older than the skew window can never come back: its timestamp would be refused first.
    private void PruneNonces(long nowMs)
    {
        foreach (var (nonce, timestampMs) in _nonces.ToList())
        {
            if (timestampMs < nowMs - CryptoSpec.HeartbeatMaxSkewMs)
            {
                _nonces.Remove(nonce);
            }
        }
    }
}
