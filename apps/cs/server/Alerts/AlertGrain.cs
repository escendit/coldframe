using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Sites;
using Coldframe.Server.Journal;

namespace Coldframe.Server.Alerts;

/// <summary>
/// An Alert, keyed by its Alert ID (Story 6.1). The only writer of the Alert's state: it journals
/// <see cref="AlertOpened"/> and <see cref="AlertClosed"/> once each, whatever is retried, and records the
/// side, Site, Lot, Sensor, Device and quantity it was opened with.
/// </summary>
/// <remarks>
/// <para>
/// Who may close: only the Sensor grain that opened the Alert. The grain reads the caller of every call from the
/// call itself (it is its own incoming call filter), so a close from a client, from another grain or from
/// another Sensor is refused and the Alert stays open.
/// </para>
/// <para>
/// Fan-out (AD-5: grain calls and the journal only): every open and close is reported to the Site grain of the
/// Alert, the open before the close. The Site's acknowledgement is journaled as <see cref="AlertSiteNotified"/>;
/// until then the report is made again from state, by a grain timer while the grain is active, by the
/// <c>report-alert</c> reminder, on activation, and whenever the Sensor repeats its call. The Site grain is
/// idempotent, so a report made twice changes nothing.
/// </para>
/// </remarks>
[GrainType("alert")]
public sealed partial class AlertGrain : JournaledStreamGrain<AlertState>, IAlertGrain, IRemindable, IIncomingGrainCallFilter
{
    private const string ReportReminderName = "report-alert";

    private const string SensorGrainType = "sensor";

    private static readonly TimeSpan ReportTimerDue = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan ReportTimerPeriod = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan ReportReminderPeriod = TimeSpan.FromMinutes(1);

    private IGrainTimer? _reportTimer;

    // The caller of the call this turn runs; the grain is not reentrant, so one call at a time.
    private GrainId? _caller;

    private string AlertId => this.GetPrimaryKeyString();

    private ILogger Logger => ServiceProvider.GetRequiredService<ILogger<AlertGrain>>();

    /// <inheritdoc />
    public async Task Invoke(IIncomingGrainCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _caller = context.SourceId;

        try
        {
            await context.Invoke();
        }
        finally
        {
            _caller = null;
        }
    }

    /// <inheritdoc />
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);

        // A report the Site never acknowledged (the call failed, or the silo went down): keep reporting. Not
        // inline, so a Site that cannot be reached never fails the activation.
        if (State.ReportPending)
        {
            await StartReportingAsync();
        }
    }

    /// <inheritdoc />
    public async Task<AlertResult> Open(OpenAlert request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SiteId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.LotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DeviceId);
        ArgumentException.ThrowIfNullOrEmpty(request.Quantity);

        // The Alert ID is its Sensor and episode: a request for another Alert is refused.
        if (request.Episode < 1
            || !string.Equals(AlertIds.Threshold(request.SensorId, request.Episode).ToString("D"), AlertId, StringComparison.Ordinal))
        {
            return new AlertResult(AlertOutcome.Refused);
        }

        if (State.Lifecycle == AlertLifecycle.None)
        {
            RaiseEvent(new AlertOpened(
                AlertKind.Threshold,
                request.Side,
                request.SiteId,
                request.LotId,
                request.SensorId,
                request.DeviceId,
                request.Quantity,
                request.Episode,
                request.OpenedAt));
            await ConfirmEvents();
        }

        // Opened again, also after it closed: nothing is journaled, only the report is finished.
        var reported = await ReportAsync();

        return new AlertResult(State.Lifecycle == AlertLifecycle.Closed ? AlertOutcome.Closed : AlertOutcome.Open, reported);
    }

    /// <inheritdoc />
    public async Task<AlertResult> Close(CloseAlert request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Read before the first await: the caller of this call.
        var caller = _caller;

        if (State.Lifecycle == AlertLifecycle.None)
        {
            return new AlertResult(AlertOutcome.NotOpened);
        }

        // Only the grain that opened the Alert closes it.
        if (caller is not { } source || source != GrainId.Create(SensorGrainType, State.SensorId.ToString("D")))
        {
            LogCloseRefused(Logger, AlertId, caller?.ToString() ?? "an unknown caller");
            return new AlertResult(AlertOutcome.Refused);
        }

        if (State.Lifecycle == AlertLifecycle.Open)
        {
            RaiseEvent(new AlertClosed(request.Reason, request.ClosedAt));
            await ConfirmEvents();
        }

        return new AlertResult(AlertOutcome.Closed, await ReportAsync());
    }

    /// <inheritdoc />
    public Task<AlertSnapshot?> Describe(CancellationToken cancellationToken = default) =>
        Task.FromResult(
            State.Lifecycle == AlertLifecycle.None
                ? null
                : new AlertSnapshot(
                    Guid.Parse(AlertId),
                    State.Kind,
                    State.Lifecycle,
                    State.Side,
                    State.SiteId!,
                    State.LotId!,
                    State.SensorId,
                    State.DeviceId!,
                    State.Quantity!,
                    State.OpenedAt,
                    State.Reason,
                    State.ClosedAt));

    /// <inheritdoc />
    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (string.Equals(reminderName, ReportReminderName, StringComparison.Ordinal))
        {
            await ReportAsync();

            // A stray reminder (its unregister failed earlier) finds nothing pending and removes itself.
            if (!State.ReportPending)
            {
                await UnregisterReminderAsync();
            }
        }
    }

    // Tells the Site grain what it has not acknowledged yet, the open before the close, and journals each
    // acknowledgement before the next report. Never throws: a failure leaves the report pending, and the timer,
    // the reminder, the next activation and the Sensor's next call try again.
    private async Task<bool> ReportAsync()
    {
        if (!State.ReportPending || State.SiteId is not { } siteId)
        {
            await StopReportingAsync();
            return true;
        }

        try
        {
            var site = GrainFactory.GetGrain<ISiteGrain>(siteId);
            var alertId = Guid.Parse(AlertId);

            if (!State.OpenReported)
            {
                // Not cancelled by the caller: the journaled Alert is reported until the Site holds it.
                await site.AlertOpened(
                    new SiteAlert(alertId, State.Kind, State.Side, State.LotId!, State.SensorId, State.DeviceId!, State.Quantity!, State.OpenedAt),
                    CancellationToken.None);

                RaiseEvent(new AlertSiteNotified(AlertLifecycle.Open, Clock.GetUtcNow()));
                await ConfirmEvents();
            }

            if (State.Lifecycle == AlertLifecycle.Closed && !State.CloseReported)
            {
                await site.AlertClosed(alertId, State.Reason ?? AlertCloseReason.Recovered, State.ClosedAt ?? Clock.GetUtcNow(), CancellationToken.None);

                RaiseEvent(new AlertSiteNotified(AlertLifecycle.Closed, Clock.GetUtcNow()));
                await ConfirmEvents();
            }
        }
#pragma warning disable CA1031 // Whatever failed the report, it stays pending and is made again.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogReportFailed(Logger, AlertId, siteId, exception);
            await StartReportingAsync();
            return false;
        }

        await StopReportingAsync();
        return true;
    }

    // The timer and the reminder are created together, once; later ticks touch neither. Awaited in the grain turn.
    private async Task StartReportingAsync()
    {
        if (_reportTimer is not null)
        {
            return;
        }

        _reportTimer = this.RegisterGrainTimer(
            async () => await ReportAsync(),
            new GrainTimerCreationOptions(ReportTimerDue, ReportTimerPeriod));

        try
        {
            await this.RegisterOrUpdateReminder(ReportReminderName, ReportReminderPeriod, ReportReminderPeriod);
        }
#pragma warning disable CA1031 // Without a reminder the timer and the next activation still retry.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogReportFailed(Logger, AlertId, "reminder", exception);
        }
    }

    private async Task StopReportingAsync()
    {
        if (_reportTimer is null)
        {
            return;
        }

        _reportTimer.Dispose();
        _reportTimer = null;
        await UnregisterReminderAsync();
    }

    private async Task UnregisterReminderAsync()
    {
        try
        {
            if (await this.GetReminder(ReportReminderName) is { } reminder)
            {
                await this.UnregisterReminder(reminder);
            }
        }
#pragma warning disable CA1031 // A stray reminder only finds nothing pending and unregisters itself next time.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogReportFailed(Logger, AlertId, "reminder", exception);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Reporting Alert {AlertId} to Site {SiteId} failed; it stays pending and is reported again.")]
    private static partial void LogReportFailed(ILogger logger, string alertId, string siteId, Exception exception);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "A close of Alert {AlertId} from {Caller} was refused: only the Sensor that opened an Alert closes it.")]
    private static partial void LogCloseRefused(ILogger logger, string alertId, string caller);
}
