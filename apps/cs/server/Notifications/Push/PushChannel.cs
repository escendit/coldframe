using Coldframe.Contracts.Notifications;
using Microsoft.Extensions.Options;

namespace Coldframe.Server.Notifications.Push;

/// <summary>
/// How one request to a push provider ended.
/// </summary>
public enum PushSendOutcome
{
    /// <summary>
    /// The provider accepted the push for the device.
    /// </summary>
    Delivered = 0,

    /// <summary>
    /// The provider reports the token as invalid or unregistered: the registration is to be removed. No failure.
    /// </summary>
    InvalidToken = 1,

    /// <summary>
    /// The provider could not take the push now (unreachable, overloaded, timed out): worth another try.
    /// </summary>
    Transient = 2,

    /// <summary>
    /// The provider refused the push for a reason another try does not change (the credentials, the payload).
    /// </summary>
    Refused = 3,
}

/// <summary>
/// The outcome of one request, with what the provider said when it did not deliver.
/// </summary>
/// <param name="Outcome">How the request ended.</param>
/// <param name="Detail">The provider's status and reason, never a token.</param>
public readonly record struct PushSendResult(PushSendOutcome Outcome, string? Detail = null);

/// <summary>
/// A push provider took a notification for no device of the User and failed transiently for at least one: the
/// delivery stays due.
/// </summary>
public sealed class PushProviderException : Exception, IInvalidInstallationsSource
{
    /// <summary>
    /// Creates the exception.
    /// </summary>
    public PushProviderException()
    {
    }

    /// <summary>
    /// Creates the exception with <paramref name="message"/>.
    /// </summary>
    public PushProviderException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates the exception with <paramref name="message"/> and its cause.
    /// </summary>
    public PushProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates the exception with <paramref name="message"/> and the registrations the provider reported as
    /// invalid in the same send.
    /// </summary>
    public PushProviderException(string message, IReadOnlyList<string> invalidInstallations)
        : base(message)
    {
        InvalidInstallations = invalidInstallations;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> InvalidInstallations { get; } = [];
}

/// <summary>
/// What the two push channels share (Story 6.5): which registrations are theirs, the content, the bounded retry
/// and the result the Notifier seam takes. A channel holds no timing, window, mute or cadence logic.
/// </summary>
/// <remarks>
/// <para>
/// A User without a registration for the channel is a success with nothing sent. A token the provider reports
/// as invalid goes back through the seam and is no failure. A transient failure is tried again
/// (<see cref="PushOptions.MaxAttempts"/>, <see cref="PushOptions.RetryDelay"/>) within
/// <see cref="PushOptions.SendBudget"/>. The channel throws, and the delivery stays due, only when it delivered
/// to no device and one failed transiently; when a device has the push, a sibling that failed is logged and
/// not sent to again, because the repeat would reach the device that has it. A refusal another try does not
/// change (the credentials, the payload) is logged as an error and never leaves the delivery due. Every send
/// carries <see cref="PushContent.CollapseId"/>, so a repeated one replaces the earlier one on the device.
/// </para>
/// <para>
/// The channel runs inside the notified User grain's turn and never calls that grain.
/// </para>
/// </remarks>
public abstract partial class PushChannel(PushContentBuilder content, IOptions<PushOptions> options, TimeProvider clock, ILogger logger) : INotificationChannel
{
    /// <summary>
    /// The provider this channel sends to.
    /// </summary>
    protected abstract PushPlatform Platform { get; }

    /// <summary>
    /// The settings.
    /// </summary>
    protected PushOptions Options => options.Value;

    /// <summary>
    /// The clock every provider token is timed with.
    /// </summary>
    protected TimeProvider Clock => clock;

    /// <inheritdoc />
    public async Task<ChannelDelivery> SendAsync(Notification notification, DateTimeOffset sentAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var registrations = (notification.Registrations ?? []).Where(registration => registration.Platform == Platform).ToList();

        if (registrations.Count == 0 || await content.BuildAsync(notification, sentAt).ConfigureAwait(false) is not { } push)
        {
            return ChannelDelivery.Nothing;
        }

        using var budget = new CancellationTokenSource(Options.SendBudget, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(budget.Token, cancellationToken);

        var results = await Task
            .WhenAll(registrations.Select(registration => SendWithRetriesAsync(registration, push, sentAt, linked.Token)))
            .ConfigureAwait(false);

        var delivered = results.Count(result => result.Outcome == PushSendOutcome.Delivered);
        IReadOnlyList<string> invalid =
            [.. registrations.Where((_, index) => results[index].Outcome == PushSendOutcome.InvalidToken).Select(registration => registration.InstallationId)];

        // Another try does not change a refusal: it is an operator's matter and never leaves the delivery due.
        var refused = results.Where(result => result.Outcome == PushSendOutcome.Refused).ToList();

        if (refused.Count > 0)
        {
            LogRefused(logger, Platform, push.Kind, refused.Count, registrations.Count, Details(refused));
        }

        var transient = results.Where(result => result.Outcome == PushSendOutcome.Transient).ToList();

        if (transient.Count > 0)
        {
            if (delivered == 0)
            {
                throw new PushProviderException(
                    $"{Platform} did not take the {push.Kind} notification for {transient.Count} of {registrations.Count} device(s): {Details(transient)}",
                    invalid);
            }

            // A phone has the push: sending again would reach it too.
            LogPartlyDelivered(logger, Platform, push.Kind, transient.Count, registrations.Count, Details(transient));
        }

        return new ChannelDelivery(delivered, invalid);
    }

    /// <summary>
    /// Sends <paramref name="push"/> to one device, once. A request that cannot be made or times out may throw
    /// <see cref="HttpRequestException"/> or <see cref="OperationCanceledException"/>: both count as transient.
    /// </summary>
    /// <param name="registration">The device.</param>
    /// <param name="push">What to send.</param>
    /// <param name="sentAt">When the Notifier sent the notification.</param>
    /// <param name="cancellationToken">Ends with the channel's budget.</param>
    protected abstract Task<PushSendResult> SendOnceAsync(
        PushRegistration registration,
        PushContent push,
        DateTimeOffset sentAt,
        CancellationToken cancellationToken);

    private async Task<PushSendResult> SendWithRetriesAsync(
        PushRegistration registration,
        PushContent push,
        DateTimeOffset sentAt,
        CancellationToken cancellationToken)
    {
        var attempts = Math.Max(1, Options.MaxAttempts);
        var delay = Options.RetryDelay;
        var result = new PushSendResult(PushSendOutcome.Transient, "not sent");

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                result = await SendOnceAsync(registration, push, sentAt, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException)
            {
                result = new PushSendResult(PushSendOutcome.Transient, exception.GetType().Name);
            }

            if (result.Outcome != PushSendOutcome.Transient || attempt == attempts || cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, clock, cancellationToken).ConfigureAwait(false);
                    delay *= 2;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return result;
    }

    // The provider's status and reason, never a token.
    private static string Details(IEnumerable<PushSendResult> results) =>
        string.Join("; ", results.Select(result => result.Detail).Distinct(StringComparer.Ordinal));

    [LoggerMessage(EventId = 1, EventName = "PushRefused", Level = LogLevel.Error, Message = "{Platform} refused the {Kind} notification for {Refused} of {Devices} device(s) and another try does not change that; it is not sent again: {Detail}")]
    private static partial void LogRefused(ILogger logger, PushPlatform platform, string kind, int refused, int devices, string detail);

    [LoggerMessage(EventId = 2, EventName = "PushPartlyDelivered", Level = LogLevel.Warning, Message = "{Platform} did not take the {Kind} notification for {Failed} of {Devices} device(s); the others have it, so it is not sent again: {Detail}")]
    private static partial void LogPartlyDelivered(ILogger logger, PushPlatform platform, string kind, int failed, int devices, string detail);
}
