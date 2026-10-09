using Coldframe.Contracts.Sites;
using Coldframe.Server.Journal;
using Microsoft.Extensions.Options;

namespace Coldframe.Server.Identity;

/// <summary>
/// A User, keyed by the OIDC <c>sub</c>. Creates Sites idempotently per key (AD-3): it persists the
/// request before calling Keycloak, creates the tagged Phase Two Organization, and hands the Site to
/// its Site grain. It creates the Organization and writes nothing else to Keycloak (AD-1). It also holds
/// the User's Role on each Site, as the Site grains report it, and the User's notification settings
/// (Story 6.3): the Notification Window, the time zone, and per Site the mute, the User's own Reminder
/// cadence and a copy of the Site's.
/// </summary>
[GrainType("user")]
public sealed partial class UserGrain(
    IPhaseTwoOrganizations organizations,
    IOptions<KeycloakOptions> options,
    ILogger<UserGrain> logger) : JournaledStreamGrain<UserState>, IUserGrain
{
    private string UserId => this.GetPrimaryKeyString();

    /// <inheritdoc />
    public async Task<SiteCreationResult> CreateSite(string idempotencyKey, string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var now = Clock.GetUtcNow();
        var creation = State.FindLive(idempotencyKey, now);

        if (creation is not null && !string.Equals(creation.Name, name, StringComparison.Ordinal))
        {
            return new SiteCreationResult(SiteCreationOutcome.IdempotencyKeyReused);
        }

        if (creation is { Completed: true })
        {
            return Created(creation);
        }

        if (creation is null)
        {
            // Persisted before any Keycloak call, so a retry resumes with the same Site ID.
            var requested = new SiteCreationRequested(idempotencyKey, Guid.CreateVersion7(now).ToString(), name, now);
            RaiseEvent(requested);
            await ConfirmEvents();
            creation = State.SiteCreations[idempotencyKey];
        }

        using var budget = new CancellationTokenSource(options.Value.OperationBudget, Clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(budget.Token, cancellationToken);

        try
        {
            await EnsureOrganizationAsync(idempotencyKey, creation, linked.Token);

            var initialized = await GrainFactory
                .GetGrain<ISiteGrain>(creation.SiteId)
                .Initialize(creation.Name, UserId, linked.Token);

            switch (initialized.Outcome)
            {
                case SiteInitializationOutcome.Initialized:
                    break;
                case SiteInitializationOutcome.IdentityProviderUnavailable:
                    return new SiteCreationResult(SiteCreationOutcome.IdentityProviderUnavailable);
                default:
                    throw new InvalidOperationException(
                        $"Site {creation.SiteId} refused to be initialized for its creator: {initialized.Outcome}.");
            }
        }
        catch (Exception exception) when (
            exception is IdentityProviderUnavailableException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogIdentityProviderUnavailable(logger, creation.SiteId, exception);
            return new SiteCreationResult(SiteCreationOutcome.IdentityProviderUnavailable);
        }

        RaiseEvent(new SiteCreationCompleted(idempotencyKey, creation.SiteId));

        // A reconciliation may have recorded a Role for this Site already; it is newer than the creation.
        if (!State.Sites.ContainsKey(creation.SiteId))
        {
            RaiseEvent(new SiteMembershipChanged(creation.SiteId, SiteRole.Owner));
        }

        await ConfirmEvents();

        return Created(State.SiteCreations[idempotencyKey]);
    }

    /// <inheritdoc />
    public async Task SyncSiteMembership(string siteId, SiteRole? role, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteId);

        SiteRole? current = State.Sites.TryGetValue(siteId, out var held) ? held : null;

        // A User may hold settings for a Site whose Membership this grain never heard of (the Edge policy
        // reads the projection): the end of the Membership is journaled then too, which drops them.
        if (current == role && (role is not null || !State.SiteNotifications.ContainsKey(siteId)))
        {
            return;
        }

        RaiseEvent(new SiteMembershipChanged(siteId, role));
        await ConfirmEvents();
    }

    /// <inheritdoc />
    public Task<UserNotificationSettings> GetNotificationSettings(CancellationToken cancellationToken = default) =>
        Task.FromResult(Settings());

    /// <inheritdoc />
    public async Task<UpdateNotificationSettingsResult> UpdateNotificationSettings(
        UpdateNotificationSettings update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        // Everything is checked before anything is journaled: a refusal changes nothing.
        if (update.Window is { IsValid: false })
        {
            return new UpdateNotificationSettingsResult(NotificationSettingsOutcome.InvalidWindow, Settings());
        }

        if (!IsTimeZone(update.TimeZone) || !IsTimeZone(update.DetectedTimeZone))
        {
            return new UpdateNotificationSettingsResult(NotificationSettingsOutcome.InvalidTimeZone, Settings());
        }

        var now = Clock.GetUtcNow();
        var changed = false;

        if (update.Window is { } window && window != State.NotificationWindow)
        {
            RaiseEvent(new NotificationWindowChanged(window.FromMinutes, window.ToMinutes, now));
            changed = true;
        }

        if (update.TimeZone is { } chosen)
        {
            if (!string.Equals(State.ChosenTimeZone, chosen, StringComparison.Ordinal))
            {
                RaiseEvent(new TimeZoneChosen(chosen, now));
                changed = true;
            }
        }
        else if (update.DetectedTimeZone is { } detected
            && State.ChosenTimeZone is null
            && !string.Equals(State.DetectedTimeZone, detected, StringComparison.Ordinal))
        {
            // Only a proposal: it never replaces a zone the User chose, and never confirms one.
            RaiseEvent(new TimeZoneDetected(detected, now));
            changed = true;
        }

        if (changed)
        {
            await ConfirmEvents();
        }

        return new UpdateNotificationSettingsResult(
            changed ? NotificationSettingsOutcome.Changed : NotificationSettingsOutcome.Unchanged,
            Settings());
    }

    /// <inheritdoc />
    public Task<UserSiteNotificationSettings> GetSiteNotificationSettings(string siteId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteId);

        return Task.FromResult(SiteSettings(siteId));
    }

    /// <inheritdoc />
    public async Task<UserSiteNotificationSettings> SetSiteNotificationSettings(
        string siteId,
        bool muted,
        ReminderCadence? reminderCadence,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteId);

        if (reminderCadence is { } requested && !Enum.IsDefined(requested))
        {
            throw new ArgumentOutOfRangeException(nameof(reminderCadence), reminderCadence, "Unknown Reminder cadence.");
        }

        var current = State.SiteNotificationsOf(siteId);
        var now = Clock.GetUtcNow();
        var changed = false;

        if (current.Muted != muted)
        {
            RaiseEvent(new SiteMuteChanged(siteId, muted, now));
            changed = true;
        }

        if (current.ReminderCadence != reminderCadence)
        {
            RaiseEvent(new PersonalReminderCadenceChanged(siteId, reminderCadence, now));
            changed = true;
        }

        if (changed)
        {
            await ConfirmEvents();
        }

        return SiteSettings(siteId);
    }

    /// <inheritdoc />
    public async Task SyncSiteReminderCadence(string siteId, ReminderCadence cadence, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteId);

        if (!Enum.IsDefined(cadence))
        {
            throw new ArgumentOutOfRangeException(nameof(cadence), cadence, "Unknown Reminder cadence.");
        }

        // A Site the grain never heard a cadence of reminds daily, so daily is no change for it.
        if ((State.SiteNotificationsOf(siteId).SiteReminderCadence ?? ReminderCadence.Daily) == cadence)
        {
            return;
        }

        RaiseEvent(new SiteReminderCadenceSynced(siteId, cadence));
        await ConfirmEvents();
    }

    private static bool IsTimeZone(string? timeZone) =>
        timeZone is null || (timeZone.Length is > 0 and <= UserNotificationLimits.MaxTimeZoneLength && !string.IsNullOrWhiteSpace(timeZone));

    private UserNotificationSettings Settings() =>
        new(State.NotificationWindow, State.TimeZone, State.ChosenTimeZone is not null);

    private UserSiteNotificationSettings SiteSettings(string siteId)
    {
        var settings = State.SiteNotificationsOf(siteId);
        return new UserSiteNotificationSettings(settings.Muted, settings.ReminderCadence, settings.ResolvedReminderCadence);
    }

    private static SiteCreationResult Created(SiteCreation creation) =>
        new(SiteCreationOutcome.Created, new SiteSummary(creation.SiteId, creation.Name, SiteRole.Owner));

    // Recovery first looks the Organization up by its tag; creation then uses the persisted ID, so a
    // 409 resolves by reading that ID. Both paths converge on one Organization.
    private async Task EnsureOrganizationAsync(string idempotencyKey, SiteCreation creation, CancellationToken cancellationToken)
    {
        var tag = $"{UserId}:{idempotencyKey}";

        var tagged = await organizations.FindByAttributeAsync(IPhaseTwoOrganizations.IdempotencyKeyAttribute, tag, cancellationToken);

        // A key reused after 24 h tags a second Organization; only the one with this request's ID counts.
        if (tagged.Any(organization => string.Equals(organization.Id, creation.SiteId, StringComparison.Ordinal)))
        {
            return;
        }

        var organization = new PhaseTwoOrganization(
            creation.SiteId,
            creation.SiteId,
            creation.Name,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                [IPhaseTwoOrganizations.IdempotencyKeyAttribute] = [tag],
            });

        if (await organizations.CreateAsync(organization, cancellationToken))
        {
            return;
        }

        _ = await organizations.GetAsync(creation.SiteId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Phase Two reports a conflict for Organization {creation.SiteId} but has no Organization with that ID.");
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Keycloak is unavailable while creating Site {SiteId}; the request stays pending.")]
    private static partial void LogIdentityProviderUnavailable(ILogger logger, string siteId, Exception exception);
}
