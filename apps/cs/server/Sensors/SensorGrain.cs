using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Sensors;
using Coldframe.Crypto;
using Coldframe.Server.Alerts;
using Coldframe.Server.Journal;
using Coldframe.Server.Lots;

namespace Coldframe.Server.Sensors;

/// <summary>
/// A Sensor, keyed by its Sensor ID. The only writer of the Sensor's state (AD-19): the Specification its
/// Node declared and its Thresholds, each side <c>Default | Override(value) | Cleared</c>. A declaration
/// replaces the Specification only.
/// </summary>
/// <remarks>
/// <para>
/// Evaluation (Story 6.1): the Device grain hands the Sensor every Reading of an assigned Node after its frame
/// is committed. <see cref="Evaluate"/> compares it with the effective Thresholds, counts the streak with
/// <see cref="ThresholdStreakRule"/>, and journals only when the streak, the episode or the Alert changes. An
/// episode is journaled before <see cref="IAlertGrain.Open"/> is called, so its Alert ID never changes on a
/// retry. An open or a close the Alert grain did not acknowledge is delivered again from state, in order, by a
/// grain timer while the grain is active, by the <c>deliver-alerts</c> reminder, on activation, and by the next
/// evaluation. The Sensor grain never calls the Device grain from <see cref="Evaluate"/>.
/// </para>
/// <para>
/// Calibration (Story 5.1, AD-9): the Sensor grain is the only validator and the only writer of a Calibration.
/// <see cref="Calibrate"/> checks the named Readings against the stored ones, keeps a single point until the
/// other one arrives, and journals <see cref="SensorCalibrated"/> once both are known and distinct. Only then
/// does it set the Calibration in force on the Device grain, which merely caches it: the Device's
/// acknowledgement is journaled as <see cref="SensorCalibrationDelivered"/>. When that call fails the persisted
/// Calibration is delivered again from state, by a grain timer while the grain is active, by the
/// <c>deliver-calibration</c> reminder, and on activation, until the Device holds it.
/// </para>
/// </remarks>
[GrainType("sensor")]
public sealed partial class SensorGrain : JournaledStreamGrain<SensorState>, ISensorGrain, IRemindable
{
    private const string DeliveryReminderName = "deliver-calibration";

    private static readonly TimeSpan DeliveryTimerDue = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan DeliveryTimerPeriod = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan DeliveryReminderPeriod = TimeSpan.FromMinutes(1);

    private const string AlertReminderName = "deliver-alerts";

    private IGrainTimer? _deliveryTimer;

    private IGrainTimer? _alertTimer;

    // The newest Reading this activation evaluated. A steady Reading journals nothing, so it runs ahead of
    // State.LastEvaluatedAt; after a reactivation it starts again from the journaled one.
    private DateTimeOffset? _lastEvaluatedAt;

    private string SensorId => this.GetPrimaryKeyString();

    private ILogger Logger => ServiceProvider.GetRequiredService<ILogger<SensorGrain>>();

    /// <inheritdoc />
    public async Task<SensorDeclarationResult> Declare(DeclareSensor request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Specification);
        ArgumentException.ThrowIfNullOrEmpty(request.DeviceId);
        ArgumentException.ThrowIfNullOrEmpty(request.Specification.Quantity);

        // The Sensor ID is its Device, slot and quantity (AD-19): a request for another Sensor is refused.
        if (request.Slot < 0 || !Derives(request))
        {
            throw new ArgumentException("The request does not derive this Sensor ID.", nameof(request));
        }

        if (State.Specification is not { } current)
        {
            RaiseEvent(new SensorDeclared(request.DeviceId, request.Slot, request.Specification, Clock.GetUtcNow()));
            await ConfirmEvents();
            return new SensorDeclarationResult(SensorDeclarationOutcome.Declared);
        }

        if (current == request.Specification)
        {
            return new SensorDeclarationResult(SensorDeclarationOutcome.Unchanged);
        }

        RaiseEvent(new SensorSpecificationChanged(request.Specification, Clock.GetUtcNow()));
        await ConfirmEvents();
        return new SensorDeclarationResult(SensorDeclarationOutcome.SpecificationChanged);
    }

    /// <inheritdoc />
    public Task<SensorSnapshot?> Describe(CancellationToken cancellationToken = default) =>
        Task.FromResult(
            State.Specification is { } specification
                ? new SensorSnapshot(
                    SensorId,
                    State.DeviceId!,
                    State.Slot,
                    specification,
                    new ThresholdSetting(State.Low.Kind, State.EffectiveLow),
                    new ThresholdSetting(State.High.Kind, State.EffectiveHigh),
                    State.Calibration,
                    State.PendingDryRaw,
                    State.PendingWetRaw)
                : null);

    /// <inheritdoc />
    public Task<SensorThresholds?> GetThresholds(CancellationToken cancellationToken = default) =>
        Task.FromResult(CurrentThresholds());

    /// <inheritdoc />
    public async Task<SetSensorThresholdsResult> SetThresholds(SetSensorThresholds request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (State.Specification is not { } specification)
        {
            return new SetSensorThresholdsResult(SensorThresholdsOutcome.NotDeclared);
        }

        var change = ThresholdRules.Evaluate(specification, State.Low, State.High, request.Low, request.High);

        // A refusal and a request that changes nothing journal nothing: only a real change resets the evaluation streak.
        if (change.Outcome == SensorThresholdsOutcome.Changed)
        {
            RaiseEvent(new SensorThresholdsChanged(change.Low, change.High, Clock.GetUtcNow()));
            await ConfirmEvents();
        }

        return new SetSensorThresholdsResult(change.Outcome, CurrentThresholds());
    }

    /// <inheritdoc />
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);

        // A Calibration the Device never acknowledged (the call failed, or the silo went down): keep delivering
        // it. Not inline, so a Device that cannot be reached never fails the activation.
        if (State.DeliveryPending)
        {
            await StartDeliveringAsync();
        }

        // The same for an open or a close the Alert grain never acknowledged.
        if (State.PendingAlertDeliveries.Count > 0)
        {
            await StartDeliveringAlertsAsync();
        }
    }

    /// <inheritdoc />
    public async Task<SensorEvaluationResult> Evaluate(EvaluateReading request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Context);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Context.SiteId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Context.LotId);

        // 1. What an earlier evaluation left undelivered comes first, also for a frame that is sent again. A
        // failure does not stop this Reading from being evaluated: it only keeps the frame unacknowledged.
        var delivered = await DeliverAlertsAsync();

        // 2. Only an alerting Sensor: declared, calibrated when it calibrates, and with an effective low.
        if (State.Specification is not { } specification
            || State.EffectiveLow is not { } low
            || (specification.Calibration && !State.Calibrated))
        {
            // The Reading still counts as seen: a backlog Reading older than it is not evaluated once the
            // Sensor alerts.
            if (LastEvaluatedAt is not { } seen || request.MeasuredAt > seen)
            {
                _lastEvaluatedAt = request.MeasuredAt;
            }

            return Result(SensorEvaluationOutcome.NotEligible, delivered);
        }

        // 3. Monotonic in measured_at: a backlog Reading and a frame sent again change nothing.
        if (LastEvaluatedAt is { } last && request.MeasuredAt <= last)
        {
            return Result(SensorEvaluationOutcome.NotNewer, delivered);
        }

        // 4. The value as the User reads it: the rounded percentage of the current Calibration, else the
        // Specification's unit.
        var value = State.Calibration is { } calibration && specification.Calibration
            ? CalibrationMath.Percent(request.RawValue, calibration.DryRaw, calibration.WetRaw)
            : request.RawValue;
        var high = State.EffectiveHigh;
        var position = ThresholdStreakRule.Classify(value, low, high);

        // 5. A streak never runs across two evaluation epochs.
        var epoch = request.Context.Epoch;
        var newEpoch = epoch != State.EvaluationEpoch;
        var step = ThresholdStreakRule.Advance(newEpoch ? ThresholdStreakRule.Reset(State.Streak) : State.Streak, position);

        // 6. Journal only what changes: a steady Reading leaves no event (the journal replays in full).
        var now = Clock.GetUtcNow();

        if (step.Closes && State.OpenAlert is { } open)
        {
            RaiseEvent(new SensorThresholdEpisodeClosed(open.Episode, open.AlertId, AlertCloseReason.Recovered, epoch, request.MeasuredAt, now));
        }

        if (step.Opens is { } side)
        {
            // The episode counter is persisted here, before the Alert grain hears of it.
            var episode = State.Episode + 1;
            RaiseEvent(new SensorThresholdEpisodeOpened(
                episode,
                AlertIds.Threshold(Guid.Parse(SensorId), episode),
                side,
                request.Context.SiteId,
                request.Context.LotId,
                value,
                side == ThresholdSide.Low ? low : high!.Value,
                epoch,
                request.MeasuredAt,
                now));
        }
        else if (!step.Closes && (step.Next != State.Streak || (newEpoch && step.Next.Count > 0)))
        {
            RaiseEvent(new SensorStreakChanged(step.Next.Position, step.Next.Count, epoch, request.MeasuredAt));
        }

        await ConfirmEvents();
        _lastEvaluatedAt = request.MeasuredAt;

        // 7. The episode is journaled: now the Alert grain, which is idempotent. Not when the delivery just
        // failed: one failing Alert call per evaluation is enough, and the timer and the reminder try again.
        if (delivered && (step.Closes || step.Opens is not null))
        {
            delivered = await DeliverAlertsAsync();
        }

        return Result(SensorEvaluationOutcome.Evaluated, delivered);
    }

    /// <inheritdoc />
    public async Task<SensorCalibrationResult> Calibrate(CalibrateSensor request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (State.Specification is not { Calibration: true })
        {
            return new SensorCalibrationResult(SensorCalibrationOutcome.NotCalibratable);
        }

        if (request.Dry is null && request.Wet is null)
        {
            return new SensorCalibrationResult(SensorCalibrationOutcome.NoPoint);
        }

        // 1. Every named Reading must be stored for this Sensor; the raw value is read from it, never sent.
        var sensorId = Guid.Parse(SensorId);
        var readings = ServiceProvider.GetRequiredService<SensorReadings>();
        long? dry = null;
        long? wet = null;

        if (request.Dry is { } dryPoint)
        {
            dry = await readings.FindRawValueAsync(sensorId, dryPoint.ReadingSeq, cancellationToken);
            if (dry is null)
            {
                return new SensorCalibrationResult(SensorCalibrationOutcome.UnknownReading);
            }
        }

        if (request.Wet is { } wetPoint)
        {
            wet = await readings.FindRawValueAsync(sensorId, wetPoint.ReadingSeq, cancellationToken);
            if (wet is null)
            {
                return new SensorCalibrationResult(SensorCalibrationOutcome.UnknownReading);
            }
        }

        // 2. With both points known (this request's, else the one kept), they must be distinct.
        var dryRaw = dry ?? State.PendingDryRaw;
        var wetRaw = wet ?? State.PendingWetRaw;

        if (dryRaw is { } knownDry && wetRaw is { } knownWet)
        {
            if (!CalibrationMath.AreDistinct(knownDry, knownWet))
            {
                return new SensorCalibrationResult(SensorCalibrationOutcome.IndistinctPoints);
            }

            // The same pair again (a retry after a failed delivery) is the Calibration in force: no new ID.
            var nothingPending = State.PendingDryRaw is null && State.PendingWetRaw is null;
            if (nothingPending && State.Calibration is { } current && current.DryRaw == knownDry && current.WetRaw == knownWet)
            {
                return await ResultAsync(await DeliverAsync());
            }

            // 3. Persist first: the Calibration is the Sensor's, whatever happens to the Device call.
            var now = Clock.GetUtcNow();
            RaiseEvent(new SensorCalibrated(Guid.CreateVersion7(now), knownDry, knownWet, now));
            await ConfirmEvents();

            // Read-your-writes: the Lot leaves needs calibration, and the points are there to derive a percentage.
            await CatchUpLotsAsync();

            // 4. Set in force on the Device grain before the request is confirmed.
            return await ResultAsync(await DeliverAsync());
        }

        // Only one point is known: keep it. The Sensor stays as calibrated as it was.
        var (point, seq, raw) = request.Dry is { } onlyDry
            ? (CalibrationPoint.Dry, onlyDry.ReadingSeq, dry!.Value)
            : (CalibrationPoint.Wet, request.Wet!.ReadingSeq, wet!.Value);
        var pending = point == CalibrationPoint.Dry ? State.PendingDryRaw : State.PendingWetRaw;

        if (pending != raw)
        {
            RaiseEvent(new SensorCalibrationPointRecorded(point, seq, raw, Clock.GetUtcNow()));
            await ConfirmEvents();
        }

        return new SensorCalibrationResult(
            SensorCalibrationOutcome.PointRecorded,
            State.Calibration,
            State.PendingDryRaw,
            State.PendingWetRaw);
    }

    /// <inheritdoc />
    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (string.Equals(reminderName, DeliveryReminderName, StringComparison.Ordinal))
        {
            await DeliverAsync();

            // A stray reminder (its unregister failed earlier) finds nothing pending and removes itself.
            if (!State.DeliveryPending)
            {
                await UnregisterReminderAsync(DeliveryReminderName);
            }
        }
        else if (string.Equals(reminderName, AlertReminderName, StringComparison.Ordinal))
        {
            await DeliverAlertsAsync();

            if (State.PendingAlertDeliveries.Count == 0)
            {
                await UnregisterReminderAsync(AlertReminderName);
            }
        }
    }

    // While an open or a close is undelivered the Device grain must not acknowledge the frame.
    private static SensorEvaluationResult Result(SensorEvaluationOutcome outcome, bool delivered) =>
        new(delivered ? outcome : SensorEvaluationOutcome.NotDelivered);

    // The newest Reading evaluated: in this activation, else as journaled.
    private DateTimeOffset? LastEvaluatedAt =>
        _lastEvaluatedAt is { } inMemory && (State.LastEvaluatedAt is not { } journaled || inMemory > journaled)
            ? inMemory
            : State.LastEvaluatedAt;

    // Delivers every open and close the Alert grain has not acknowledged, oldest first, and journals each
    // acknowledgement. It stops at the first one that fails, so a close never overtakes its open. Never throws:
    // a failure leaves the rest pending, and the timer, the reminder, the next activation and the next
    // evaluation try again. An Alert whose Site has not acknowledged it yet counts as not delivered.
    private async Task<bool> DeliverAlertsAsync()
    {
        foreach (var delivery in State.PendingAlertDeliveries.ToList())
        {
            try
            {
                var alert = GrainFactory.GetGrain<IAlertGrain>(delivery.AlertId.ToString("D"));

                // Not cancelled by the caller: the journaled episode is delivered until the Alert grain holds it.
                var result = delivery.Change == AlertLifecycle.Open
                    ? await alert.Open(
                        new OpenAlert(
                            Guid.Parse(SensorId),
                            delivery.Episode,
                            delivery.Side,
                            delivery.SiteId!,
                            delivery.LotId!,
                            State.DeviceId!,
                            State.Specification!.Quantity,
                            delivery.At),
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

                RaiseEvent(new SensorAlertDelivered(delivery.AlertId, delivery.Change, Clock.GetUtcNow()));
                await ConfirmEvents();
            }
#pragma warning disable CA1031 // Whatever failed the delivery, it stays pending and is delivered again.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                LogAlertDeliveryFailed(Logger, SensorId, delivery.AlertId, exception);
                await StartDeliveringAlertsAsync();
                return false;
            }
        }

        await StopDeliveringAlertsAsync();
        return true;
    }

    private async Task StartDeliveringAlertsAsync()
    {
        if (_alertTimer is not null)
        {
            return;
        }

        _alertTimer = this.RegisterGrainTimer(
            async () => await DeliverAlertsAsync(),
            new GrainTimerCreationOptions(DeliveryTimerDue, DeliveryTimerPeriod));

        try
        {
            await this.RegisterOrUpdateReminder(AlertReminderName, DeliveryReminderPeriod, DeliveryReminderPeriod);
        }
#pragma warning disable CA1031 // Without a reminder the timer and the next activation still retry.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogDeliveryFailed(Logger, SensorId, "reminder", exception);
        }
    }

    private async Task StopDeliveringAlertsAsync()
    {
        if (_alertTimer is null)
        {
            return;
        }

        _alertTimer.Dispose();
        _alertTimer = null;
        await UnregisterReminderAsync(AlertReminderName);
    }

    private SensorThresholds? CurrentThresholds() =>
        State.Specification is { } specification
            ? new SensorThresholds(
                specification,
                new ThresholdSetting(State.Low.Kind, State.EffectiveLow),
                new ThresholdSetting(State.High.Kind, State.EffectiveHigh),
                ThresholdRules.ProposedLow(specification))
            : null;

    private Task<SensorCalibrationResult> ResultAsync(bool delivered) =>
        Task.FromResult(new SensorCalibrationResult(
            delivered ? SensorCalibrationOutcome.Calibrated : SensorCalibrationOutcome.NotDelivered,
            State.Calibration,
            State.PendingDryRaw,
            State.PendingWetRaw));

    // Sets the Calibration in force on the Device grain and journals that it acknowledged. Never throws: a
    // failure leaves the Calibration pending, and the timer, the reminder and the next activation try again.
    private async Task<bool> DeliverAsync()
    {
        if (State.Calibration is not { } calibration || State.DeviceId is not { } deviceId)
        {
            await StopDeliveringAsync();
            return true;
        }

        if (!State.DeliveryPending)
        {
            await StopDeliveringAsync();
            return true;
        }

        try
        {
            await GrainFactory
                .GetGrain<IDeviceGrain>(deviceId)
                // Not cancelled by the caller: the persisted Calibration is delivered until the Device holds it.
                .SetCalibration(new SetCalibration(Guid.Parse(SensorId), calibration.Id, calibration.Revision), CancellationToken.None);

            RaiseEvent(new SensorCalibrationDelivered(calibration.Id, Clock.GetUtcNow()));
            await ConfirmEvents();
        }
#pragma warning disable CA1031 // Whatever failed the delivery, the Calibration stays pending and is delivered again.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogDeliveryFailed(Logger, SensorId, deviceId, exception);
            await StartDeliveringAsync();
            return false;
        }

        await StopDeliveringAsync();
        return true;
    }

    // Read-your-writes for the Lot status. The events are journaled, so a failure here only delays the status.
    private async Task CatchUpLotsAsync()
    {
        try
        {
            await ServiceProvider.GetProjectionRunner<LotsProjector>().CatchUpAsync(CancellationToken.None);
        }
#pragma warning disable CA1031 // The Lot status follows on the next poll or hint.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogCatchUpFailed(Logger, SensorId, exception);
        }
    }

    // The timer and the reminder are created together, once; later ticks touch neither. Awaited in the grain turn.
    private async Task StartDeliveringAsync()
    {
        if (_deliveryTimer is not null)
        {
            return;
        }

        _deliveryTimer = this.RegisterGrainTimer(
            async () => await DeliverAsync(),
            new GrainTimerCreationOptions(DeliveryTimerDue, DeliveryTimerPeriod));

        try
        {
            await this.RegisterOrUpdateReminder(DeliveryReminderName, DeliveryReminderPeriod, DeliveryReminderPeriod);
        }
#pragma warning disable CA1031 // Without a reminder the timer and the next activation still retry.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogDeliveryFailed(Logger, SensorId, "reminder", exception);
        }
    }

    private async Task StopDeliveringAsync()
    {
        if (_deliveryTimer is null)
        {
            return;
        }

        _deliveryTimer.Dispose();
        _deliveryTimer = null;
        await UnregisterReminderAsync(DeliveryReminderName);
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
            LogDeliveryFailed(Logger, SensorId, "reminder", exception);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Setting the Calibration of Sensor {SensorId} in force on Device {DeviceId} failed; it stays pending and is delivered again.")]
    private static partial void LogDeliveryFailed(ILogger logger, string sensorId, string deviceId, Exception exception);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "The Lot status could not be brought up to date after Sensor {SensorId} was calibrated; it follows on the next poll.")]
    private static partial void LogCatchUpFailed(ILogger logger, string sensorId, Exception exception);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Delivering Alert {AlertId} of Sensor {SensorId} failed; it stays pending and is delivered again.")]
    private static partial void LogAlertDeliveryFailed(ILogger logger, string sensorId, Guid alertId, Exception exception);

    private bool Derives(DeclareSensor request)
    {
        DeviceId deviceId;
        try
        {
            deviceId = DeviceId.Parse(request.DeviceId);
        }
        catch (CryptoFailureException)
        {
            return false;
        }

        return string.Equals(
            SensorIds.Derive(deviceId, (uint)request.Slot, request.Specification.Quantity).ToString("D"),
            SensorId,
            StringComparison.Ordinal);
    }
}
