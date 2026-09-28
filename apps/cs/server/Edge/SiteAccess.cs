using Coldframe.Contracts.Sites;

namespace Coldframe.Server.Edge;

/// <summary>
/// What the shared policy decides for a Site-scoped request.
/// </summary>
public enum SiteAccessDecision
{
    /// <summary>
    /// The caller's Role meets the minimum.
    /// </summary>
    Allow,

    /// <summary>
    /// The Site exists but the caller has no Role on it, or a lower one: 403.
    /// </summary>
    Forbidden,

    /// <summary>
    /// The Site does not exist or is not active: 404.
    /// </summary>
    NotFound,
}

/// <summary>
/// The one rule behind every Site-scoped endpoint (AD-4).
/// </summary>
public static class SiteAccess
{
    /// <summary>
    /// Decides a request from the declared minimum, whether the Site exists, and the caller's Role on it.
    /// </summary>
    public static SiteAccessDecision Decide(SiteRole minimum, bool siteExists, SiteRole? callerRole)
    {
        if (!siteExists)
        {
            return SiteAccessDecision.NotFound;
        }

        return callerRole is { } role && role >= minimum ? SiteAccessDecision.Allow : SiteAccessDecision.Forbidden;
    }
}
