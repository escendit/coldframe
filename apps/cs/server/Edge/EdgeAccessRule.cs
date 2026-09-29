using Coldframe.Contracts.Sites;

namespace Coldframe.Server.Edge;

/// <summary>
/// The access rule an Edge API endpoint declares in its metadata: a minimum <see cref="SiteRole"/> on the
/// Site named by the route value <c>siteId</c>, any authenticated User, or an authenticated Device. Every
/// endpoint declares exactly one; a test rejects an endpoint without one.
/// </summary>
/// <param name="MinimumRole">The minimum Role, or <see langword="null"/> for any authenticated User or a Device.</param>
/// <param name="IsDevice">
/// <see langword="true"/> for a <c>/device/*</c> endpoint: no Keycloak token, the Device signs the request
/// (AD-12) and the handler checks it.
/// </param>
public sealed record EdgeAccessRule(SiteRole? MinimumRole, bool IsDevice = false)
{
    /// <summary>
    /// The name of the "any authenticated User" rule, as the contract's <c>x-coldframe-minimum-role</c> writes it.
    /// </summary>
    public const string AuthenticatedName = "Authenticated";

    /// <summary>
    /// The name of the "authenticated Device" rule, as the contract's <c>x-coldframe-minimum-role</c> writes it.
    /// </summary>
    public const string DeviceName = "Device";

    /// <summary>
    /// Any authenticated User may call the endpoint.
    /// </summary>
    public static EdgeAccessRule AuthenticatedCaller { get; } = new((SiteRole?)null);

    /// <summary>
    /// A Device that signs its request may call the endpoint; a Keycloak token neither helps nor hurts.
    /// </summary>
    public static EdgeAccessRule DeviceCaller { get; } = new((SiteRole?)null, IsDevice: true);

    /// <summary>
    /// The rule as the contract writes it: a <see cref="SiteRole"/> name, <see cref="AuthenticatedName"/> or
    /// <see cref="DeviceName"/>.
    /// </summary>
    public override string ToString() => IsDevice ? DeviceName : MinimumRole?.ToString() ?? AuthenticatedName;
}

/// <summary>
/// Declares an endpoint's access rule and attaches the policy that enforces it.
/// </summary>
public static class EdgeAccessRuleExtensions
{
    /// <summary>
    /// The policy that requires the caller's Role on the route's Site to be at least the declared minimum.
    /// </summary>
    public const string SiteRolePolicy = "coldframe:site-role";

    /// <summary>
    /// The policy that requires an authenticated caller.
    /// </summary>
    public const string AuthenticatedCallerPolicy = "coldframe:authenticated";

    /// <summary>
    /// The caller needs at least <paramref name="minimum"/> on the Site named by the route value <c>siteId</c>.
    /// </summary>
    public static TBuilder RequireSiteRole<TBuilder>(this TBuilder builder, SiteRole minimum)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithMetadata(new EdgeAccessRule(minimum)).RequireAuthorization(SiteRolePolicy);
    }

    /// <summary>
    /// Any authenticated caller may call the endpoint.
    /// </summary>
    public static TBuilder RequireAuthenticatedCaller<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithMetadata(EdgeAccessRule.AuthenticatedCaller).RequireAuthorization(AuthenticatedCallerPolicy);
    }

    /// <summary>
    /// A Device calls the endpoint (<c>/device/*</c>): it is anonymous to JWT, and its handler authenticates the
    /// Device's signature itself, answering 401 <c>device-unauthorized</c> when it does not hold.
    /// </summary>
    public static TBuilder RequireDevice<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithMetadata(EdgeAccessRule.DeviceCaller).AllowAnonymous();
    }
}
