using Coldframe.Server.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Coldframe.Server.Edge;

/// <summary>
/// The requirement of <see cref="EdgeAccessRuleExtensions.SiteRolePolicy"/>: the endpoint's declared
/// <see cref="EdgeAccessRule"/> holds for the caller on the route's Site.
/// </summary>
public sealed class SiteAccessRequirement : IAuthorizationRequirement;

/// <summary>
/// Why a Site-scoped request was refused.
/// </summary>
public sealed class SiteAccessFailureReason(IAuthorizationHandler handler, SiteAccessDecision decision)
    : AuthorizationFailureReason(handler, decision.ToString())
{
    /// <summary>
    /// The decision: <see cref="SiteAccessDecision.Forbidden"/> or <see cref="SiteAccessDecision.NotFound"/>.
    /// </summary>
    public SiteAccessDecision Decision { get; } = decision;
}

/// <summary>
/// The one shared policy (AD-4): reads the <c>siteId</c> route value and the caller's Role from the
/// identity projection, never from token claims, and decides with <see cref="SiteAccess.Decide"/>.
/// </summary>
public sealed class SiteAccessHandler(IdentityReadModel readModel) : AuthorizationHandler<SiteAccessRequirement>
{
    /// <summary>
    /// The route value that names the Site.
    /// </summary>
    public const string SiteIdRouteValue = "siteId";

    /// <summary>
    /// Returns the canonical form of a Site ID, or <see langword="null"/> when it is not a Site ID at all.
    /// </summary>
    public static string? Canonicalize(string? siteId) =>
        Guid.TryParse(siteId, out var id) ? id.ToString() : null;

    /// <inheritdoc />
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, SiteAccessRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Resource is not HttpContext httpContext
            || context.User.Identity?.IsAuthenticated != true
            || context.User.FindFirst(EdgeAuthentication.UserIdClaim)?.Value is not { } userId
            || httpContext.GetEndpoint()?.Metadata.GetMetadata<EdgeAccessRule>() is not { MinimumRole: { } minimum })
        {
            // Unauthenticated: the policy's authenticated-user requirement challenges.
            return;
        }

        var siteId = Canonicalize(httpContext.GetRouteValue(SiteIdRouteValue) as string);
        var site = siteId is null ? null : await readModel.FindSiteAsync(siteId, userId, httpContext.RequestAborted).ConfigureAwait(false);

        var decision = SiteAccess.Decide(
            minimum,
            site is { Lifecycle: Coldframe.Contracts.Sites.SiteLifecycle.Active },
            site?.CallerRole);

        if (decision == SiteAccessDecision.Allow)
        {
            context.Succeed(requirement);
        }
        else
        {
            context.Fail(new SiteAccessFailureReason(this, decision));
        }
    }
}

/// <summary>
/// Answers a refused request with Problem Details: 404 <c>site-not-found</c> when the Site does not exist,
/// 403 <c>forbidden</c> otherwise. Challenges go to the bearer handler, which answers 401.
/// </summary>
public sealed class EdgeAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    /// <inheritdoc />
    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorizeResult);

        if (!authorizeResult.Forbidden)
        {
            return _default.HandleAsync(next, context, policy, authorizeResult);
        }

        var notFound = authorizeResult.AuthorizationFailure?.FailureReasons
            .OfType<SiteAccessFailureReason>()
            .Any(reason => reason.Decision == SiteAccessDecision.NotFound) == true;

        return notFound
            ? EdgeProblems.WriteAsync(context, StatusCodes.Status404NotFound, EdgeProblems.SiteNotFound, "The Site does not exist.")
            : EdgeProblems.WriteAsync(context, StatusCodes.Status403Forbidden, EdgeProblems.Forbidden, "Your Role on this Site does not allow this.");
    }
}
