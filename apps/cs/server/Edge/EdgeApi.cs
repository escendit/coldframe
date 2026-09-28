using System.Text.Json;
using Coldframe.Contracts.Sites;
using Coldframe.Server.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Coldframe.Server.Edge;

/// <summary>
/// The body of <c>POST /sites</c>.
/// </summary>
/// <param name="Name">The Site name.</param>
public sealed record CreateSiteRequest(string? Name);

/// <summary>
/// A Site as the Edge API returns it.
/// </summary>
/// <param name="Id">The Site ID.</param>
/// <param name="Name">The Site name.</param>
/// <param name="Role">The caller's Role on the Site.</param>
public sealed record SiteResponse(string Id, string Name, SiteRole Role);

/// <summary>
/// The Edge API endpoints, contract-first from <c>packages/openapi/coldframe.openapi.json</c> (AD-10).
/// </summary>
/// <remarks>
/// Every endpoint declares exactly one access rule (<see cref="EdgeAccessRuleExtensions.RequireSiteRole{TBuilder}"/>
/// or <see cref="EdgeAccessRuleExtensions.RequireAuthenticatedCaller{TBuilder}"/>). Handlers change state only
/// through grains and read only the identity read model.
/// </remarks>
public static class EdgeApi
{
    /// <summary>
    /// The header that makes a creating request idempotent.
    /// </summary>
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    /// <summary>
    /// Maps every Edge API endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapEdgeApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost("/sites", CreateSiteAsync)
            .WithName("createSite")
            .RequireAuthenticatedCaller();

        endpoints.MapGet("/sites/{siteId}", GetSiteAsync)
            .WithName("getSite")
            .RequireSiteRole(SiteRole.Member);

        return endpoints;
    }

    private static async Task<IResult> CreateSiteAsync(
        HttpContext httpContext,
        [FromServices] IGrainFactory grains,
        [FromServices] IOptions<HttpJsonOptions> jsonOptions)
    {
        var headerValues = httpContext.Request.Headers[IdempotencyKeyHeader];

        switch (EdgeValidation.CheckIdempotencyKey([.. headerValues]))
        {
            case EdgeValidation.KeyCheck.Missing:
                return EdgeProblems.Result(
                    StatusCodes.Status400BadRequest,
                    EdgeProblems.IdempotencyKeyMissing,
                    "The request has no Idempotency-Key.",
                    $"Send an {IdempotencyKeyHeader} header so a retry cannot create a second Site.");
            case EdgeValidation.KeyCheck.Invalid:
                return EdgeProblems.Result(
                    StatusCodes.Status400BadRequest,
                    EdgeProblems.Validation,
                    "The Idempotency-Key is not valid.",
                    $"Send one key of 1 to {EdgeValidation.MaxIdempotencyKeyLength} printable ASCII characters.");
        }

        var name = EdgeValidation.NormalizeSiteName(await ReadSiteNameAsync(httpContext, jsonOptions.Value).ConfigureAwait(false));

        if (name is null)
        {
            return EdgeProblems.Result(
                StatusCodes.Status400BadRequest,
                EdgeProblems.Validation,
                "The Site name is not valid.",
                $"Send a JSON body with a name of 1 to {EdgeValidation.MaxSiteNameLength} characters.");
        }

        var userId = httpContext.User.FindFirst(EdgeAuthentication.UserIdClaim)!.Value;
        var result = await grains
            .GetGrain<IUserGrain>(userId)
            .CreateSite(headerValues[0]!, name, httpContext.RequestAborted)
            .ConfigureAwait(false);

        return ToHttpResult(result);
    }

    /// <summary>
    /// Maps the User grain's answer to the HTTP response: 201 with <c>Location</c> and the Site, 422
    /// <c>idempotency-key-reused</c>, or 503 <c>identity-provider-unavailable</c>.
    /// </summary>
    internal static IResult ToHttpResult(SiteCreationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result switch
        {
            { Outcome: SiteCreationOutcome.Created, Site: { } site } =>
                TypedResults.Created($"/sites/{site.Id}", new SiteResponse(site.Id, site.Name, site.Role)),
            { Outcome: SiteCreationOutcome.IdempotencyKeyReused } => EdgeProblems.Result(
                StatusCodes.Status422UnprocessableEntity,
                EdgeProblems.IdempotencyKeyReused,
                "The Idempotency-Key was used for a different request.",
                "Nothing was created. Use a new key for a new Site."),
            { Outcome: SiteCreationOutcome.IdentityProviderUnavailable } => EdgeProblems.Result(
                StatusCodes.Status503ServiceUnavailable,
                EdgeProblems.IdentityProviderUnavailable,
                "The sign-in service is unavailable.",
                "The Site was not created yet. Try again with the same Idempotency-Key."),
            _ => throw new InvalidOperationException($"Unexpected Site creation result {result}."),
        };
    }

    private static async Task<IResult> GetSiteAsync(
        string siteId,
        HttpContext httpContext,
        [FromServices] IdentityReadModel readModel)
    {
        var userId = httpContext.User.FindFirst(EdgeAuthentication.UserIdClaim)!.Value;
        var canonical = SiteAccessHandler.Canonicalize(siteId);
        var site = canonical is null ? null : await readModel.FindSiteAsync(canonical, userId, httpContext.RequestAborted).ConfigureAwait(false);

        // The policy has allowed the request; the Site can only have vanished in between.
        return site is { Lifecycle: SiteLifecycle.Active, CallerRole: { } role }
            ? TypedResults.Ok(new SiteResponse(site.SiteId, site.Name, role))
            : EdgeProblems.Result(StatusCodes.Status404NotFound, EdgeProblems.SiteNotFound, "The Site does not exist.");
    }

    private static async Task<string?> ReadSiteNameAsync(HttpContext httpContext, HttpJsonOptions jsonOptions)
    {
        if (!httpContext.Request.HasJsonContentType())
        {
            return null;
        }

        try
        {
            var request = await httpContext.Request
                .ReadFromJsonAsync<CreateSiteRequest>(jsonOptions.SerializerOptions, httpContext.RequestAborted)
                .ConfigureAwait(false);

            return request?.Name;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
