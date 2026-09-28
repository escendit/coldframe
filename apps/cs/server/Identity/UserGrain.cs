using Coldframe.Contracts.Sites;
using Coldframe.Server.Journal;
using Microsoft.Extensions.Options;

namespace Coldframe.Server.Identity;

/// <summary>
/// A User, keyed by the OIDC <c>sub</c>. Creates Sites idempotently per key (AD-3): it persists the
/// request before calling Keycloak, creates the tagged Phase Two Organization, and hands the Site to
/// its Site grain. It creates the Organization and writes nothing else to Keycloak (AD-1).
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
        await ConfirmEvents();

        return Created(State.SiteCreations[idempotencyKey]);
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
