using System.Security.Cryptography;
using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Sites;
using Coldframe.Crypto;
using Coldframe.Server.Journal;

namespace Coldframe.Server.Devices;

/// <summary>
/// A Device, keyed by its Device ID. The only writer of the Device's state and the owner of its Site
/// (AD-1, AD-18): it joins a Site by calling <see cref="ISiteGrain.RegisterDevice"/> before it journals
/// <see cref="DeviceEnrolled"/>. It only ever receives <c>K_dev</c> wrapped, and unwraps it only inside
/// <see cref="Heartbeat"/>, where it verifies the Hub's signature and zeroes the keys again.
/// </summary>
/// <remarks>
/// Replay (AD-12): Orleans keeps one activation per Device, so the nonces seen while it is active are
/// authoritative in memory; they are pruned once older than the skew window, beyond which the timestamp
/// check refuses them anyway. The persisted <see cref="DeviceState.LastHeartbeatTimestampMs"/> closes the gap
/// after a reactivation: a heartbeat must be above it, and the Hub's stamps strictly increase.
/// </remarks>
[GrainType("device")]
public sealed class DeviceGrain : JournaledStreamGrain<DeviceState>, IDeviceGrain
{
    // The length of an HMAC-SHA256 signature.
    private const int SignatureLength = 32;

    // Nonce (hex) → the timestamp it was accepted with.
    private readonly Dictionary<string, long> _nonces = new(StringComparer.Ordinal);

    private string DeviceId => this.GetPrimaryKeyString();

    /// <inheritdoc />
    public async Task<DeviceEnrolmentResult> Enrol(EnrolDevice request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SiteId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CallerId);
        ArgumentException.ThrowIfNullOrEmpty(request.IdempotencyKey);
        ArgumentNullException.ThrowIfNull(request.WrappedKey);

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

        // 3. Only after the Site has registered it. Enrolling again on the same Site journals nothing.
        if (State.SiteId is null)
        {
            RaiseEvent(new DeviceEnrolled(request.SiteId, request.Kind, request.WrappedKey, Clock.GetUtcNow()));
            await ConfirmEvents();
        }

        return new DeviceEnrolmentResult(
            DeviceEnrolmentOutcome.Enrolled,
            new DeviceSummary(DeviceId, State.Kind, State.SiteId!));
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
        if (_nonces.ContainsKey(nonce) || !Verify(wrapped, request))
        {
            return refused;
        }

        RaiseEvent(new DeviceSeen(now, request.TimestampMs, request.UptimeMs));
        await ConfirmEvents();

        // Only once the heartbeat is journaled: a failed write accepted nothing.
        _nonces[nonce] = request.TimestampMs;

        return new DeviceHeartbeatResult(DeviceHeartbeatOutcome.Accepted);
    }

    // Unwraps K_dev, derives the hub-auth/v1 key, verifies, and zeroes both keys on every path.
    private bool Verify(WrappedDeviceKey wrapped, DeviceHeartbeat request)
    {
        var vault = ServiceProvider.GetRequiredService<DeviceKeyVault>();
        byte[]? deviceKey = null;
        byte[]? hubAuthKey = null;

        try
        {
            deviceKey = vault.Unwrap(Coldframe.Crypto.DeviceId.Parse(DeviceId), wrapped);
            hubAuthKey = KeyHierarchy.DerivePurposeKey(deviceKey, CryptoSpec.HubAuthLabel);

            return Coldframe.Crypto.Heartbeat.Verify(
                hubAuthKey,
                request.Method,
                request.Path,
                request.Body,
                request.TimestampMs,
                request.Nonce,
                request.Signature);
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
