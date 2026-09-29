using System.Text.Json;
using Coldframe.Contracts.Lots;
using Coldframe.Contracts.Sites;
using Coldframe.Server.Identity;
using Coldframe.Server.Lots;
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
/// The body of <c>GET /sites</c>: the caller's Sites in the Server's order.
/// </summary>
/// <param name="Sites">The Sites, oldest first, then by Site ID.</param>
public sealed record SiteListResponse(IReadOnlyList<SiteResponse> Sites);

/// <summary>
/// The body of <c>PATCH /sites/{siteId}</c>.
/// </summary>
/// <param name="Name">The new Site name.</param>
public sealed record RenameSiteRequest(string? Name);

/// <summary>
/// The body of <c>POST /sites/{siteId}/lots</c>.
/// </summary>
/// <param name="Name">The Lot name.</param>
public sealed record CreateLotRequest(string? Name);

/// <summary>
/// The body of <c>PATCH /sites/{siteId}/lots/{lotId}</c>.
/// </summary>
/// <param name="Name">The new Lot name.</param>
public sealed record RenameLotRequest(string? Name);

/// <summary>
/// A Lot as the Edge API returns it.
/// </summary>
/// <param name="Id">The Lot ID.</param>
/// <param name="Name">The Lot name.</param>
/// <param name="Status">The Server's status (AD-14), a <c>LotStatus</c> of the contract.</param>
/// <param name="Removed"><see langword="true"/> for a removed Lot; omitted otherwise.</param>
public sealed record LotResponse(string Id, string Name, string Status, bool? Removed = null);

/// <summary>
/// The body of <c>GET /sites/{siteId}/lots</c>: the Site's live Lots in the Server's order.
/// </summary>
/// <param name="Lots">The Lots: status, then creation time, then Lot ID.</param>
public sealed record LotListResponse(IReadOnlyList<LotResponse> Lots);

/// <summary>
/// The Edge API endpoints, contract-first from <c>packages/openapi/coldframe.openapi.json</c> (AD-10).
/// </summary>
/// <remarks>
/// Every endpoint declares exactly one access rule (<see cref="EdgeAccessRuleExtensions.RequireSiteRole{TBuilder}"/>
/// or <see cref="EdgeAccessRuleExtensions.RequireAuthenticatedCaller{TBuilder}"/>). Handlers change state only
/// through grains and read only the read models.
/// </remarks>
public static class EdgeApi
{
    /// <summary>
    /// The header that makes a creating request idempotent.
    /// </summary>
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    /// <summary>
    /// The route value that names the Lot.
    /// </summary>
    public const string LotIdRouteValue = "lotId";

    /// <summary>
    /// Maps every Edge API endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapEdgeApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/sites", ListSitesAsync)
            .WithName("listSites")
            .RequireAuthenticatedCaller();

        endpoints.MapPost("/sites", CreateSiteAsync)
            .WithName("createSite")
            .RequireAuthenticatedCaller();

        endpoints.MapGet("/sites/{siteId}", GetSiteAsync)
            .WithName("getSite")
            .RequireSiteRole(SiteRole.Member);

        endpoints.MapPatch("/sites/{siteId}", RenameSiteAsync)
            .WithName("renameSite")
            .RequireSiteRole(SiteRole.Owner);

        endpoints.MapGet("/sites/{siteId}/lots", ListLotsAsync)
            .WithName("listLots")
            .RequireSiteRole(SiteRole.Member);

        endpoints.MapPost("/sites/{siteId}/lots", CreateLotAsync)
            .WithName("createLot")
            .RequireSiteRole(SiteRole.Administrator);

        endpoints.MapGet("/sites/{siteId}/lots/{lotId}", GetLotAsync)
            .WithName("getLot")
            .RequireSiteRole(SiteRole.Member);

        endpoints.MapPatch("/sites/{siteId}/lots/{lotId}", RenameLotAsync)
            .WithName("renameLot")
            .RequireSiteRole(SiteRole.Administrator);

        endpoints.MapDelete("/sites/{siteId}/lots/{lotId}", RemoveLotAsync)
            .WithName("removeLot")
            .RequireSiteRole(SiteRole.Administrator);

        return endpoints;
    }

    private static async Task<IResult> CreateSiteAsync(
        HttpContext httpContext,
        [FromServices] IGrainFactory grains,
        [FromServices] IOptions<HttpJsonOptions> jsonOptions)
    {
        if (CheckIdempotencyKey(httpContext, "Site") is { } keyProblem)
        {
            return keyProblem;
        }

        var name = EdgeValidation.NormalizeSiteName(await ReadNameAsync(httpContext, jsonOptions.Value).ConfigureAwait(false));

        if (name is null)
        {
            return InvalidName("Site");
        }

        var result = await grains
            .GetGrain<IUserGrain>(CallerId(httpContext))
            .CreateSite(httpContext.Request.Headers[IdempotencyKeyHeader][0]!, name, httpContext.RequestAborted)
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

    private static async Task<IResult> ListSitesAsync(
        HttpContext httpContext,
        [FromServices] IdentityReadModel readModel)
    {
        var sites = await readModel.ListSitesAsync(CallerId(httpContext), httpContext.RequestAborted).ConfigureAwait(false);

        return TypedResults.Ok(new SiteListResponse([.. sites.Select(site => new SiteResponse(site.SiteId, site.Name, site.Role))]));
    }

    private static async Task<IResult> GetSiteAsync(
        string siteId,
        HttpContext httpContext,
        [FromServices] IdentityReadModel readModel)
    {
        var canonical = SiteAccessHandler.Canonicalize(siteId);
        var site = canonical is null ? null : await readModel.FindSiteAsync(canonical, CallerId(httpContext), httpContext.RequestAborted).ConfigureAwait(false);

        // The policy has allowed the request; the Site can only have vanished in between.
        return site is { Lifecycle: SiteLifecycle.Active, CallerRole: { } role }
            ? TypedResults.Ok(new SiteResponse(site.SiteId, site.Name, role))
            : SiteNotFound();
    }

    private static async Task<IResult> RenameSiteAsync(
        string siteId,
        HttpContext httpContext,
        [FromServices] IGrainFactory grains,
        [FromServices] IdentityReadModel readModel,
        [FromServices] IOptions<HttpJsonOptions> jsonOptions)
    {
        var name = EdgeValidation.NormalizeSiteName(await ReadNameAsync(httpContext, jsonOptions.Value).ConfigureAwait(false));

        if (name is null)
        {
            return InvalidName("Site");
        }

        // The policy has allowed the request, so the Site ID is canonical.
        var canonical = SiteAccessHandler.Canonicalize(siteId)!;
        var result = await grains.GetGrain<ISiteGrain>(canonical).Rename(name, httpContext.RequestAborted).ConfigureAwait(false);

        if (result.Outcome is not (SiteRenameOutcome.Renamed or SiteRenameOutcome.Unchanged))
        {
            return ToHttpResult(result.Outcome, site: null);
        }

        // The Site grain brought the identity projection up to date before it returned.
        var site = await readModel.FindSiteAsync(canonical, CallerId(httpContext), httpContext.RequestAborted).ConfigureAwait(false);

        return ToHttpResult(
            result.Outcome,
            site is { Lifecycle: SiteLifecycle.Active, CallerRole: { } role } ? new SiteResponse(site.SiteId, site.Name, role) : null);
    }

    /// <summary>
    /// How <c>PATCH /sites/{siteId}</c> answers an outcome of <see cref="ISiteGrain.Rename"/>: the renamed
    /// (or unchanged) Site as read afterwards, 503 when Keycloak did not answer, 404 otherwise.
    /// </summary>
    /// <param name="outcome">The Site grain's outcome.</param>
    /// <param name="site">The Site as the caller now reads it, or <see langword="null"/> when it vanished.</param>
    /// <returns>The HTTP result.</returns>
    internal static IResult ToHttpResult(SiteRenameOutcome outcome, SiteResponse? site) => outcome switch
    {
        SiteRenameOutcome.Renamed or SiteRenameOutcome.Unchanged when site is not null => TypedResults.Ok(site),
        SiteRenameOutcome.IdentityProviderUnavailable => EdgeProblems.Result(
            StatusCodes.Status503ServiceUnavailable,
            EdgeProblems.IdentityProviderUnavailable,
            "The sign-in service is unavailable.",
            "The Site was not renamed. Try again."),
        _ => SiteNotFound(),
    };

    private static async Task<IResult> ListLotsAsync(
        string siteId,
        HttpContext httpContext,
        [FromServices] LotsReadModel lots)
    {
        var canonical = SiteAccessHandler.Canonicalize(siteId)!;
        var views = await lots.ListLotsAsync(canonical, httpContext.RequestAborted).ConfigureAwait(false);

        return TypedResults.Ok(new LotListResponse([.. views.Select(ToLotResponse)]));
    }

    private static async Task<IResult> CreateLotAsync(
        string siteId,
        HttpContext httpContext,
        [FromServices] IGrainFactory grains,
        [FromServices] LotsReadModel lots,
        [FromServices] IOptions<HttpJsonOptions> jsonOptions)
    {
        if (CheckIdempotencyKey(httpContext, "Lot") is { } keyProblem)
        {
            return keyProblem;
        }

        var name = EdgeValidation.NormalizeLotName(await ReadNameAsync(httpContext, jsonOptions.Value).ConfigureAwait(false));

        if (name is null)
        {
            return InvalidName("Lot");
        }

        var canonical = SiteAccessHandler.Canonicalize(siteId)!;
        var result = await grains
            .GetGrain<ISiteGrain>(canonical)
            .CreateLot(CallerId(httpContext), httpContext.Request.Headers[IdempotencyKeyHeader][0]!, name, httpContext.RequestAborted)
            .ConfigureAwait(false);

        switch (result)
        {
            case { Outcome: LotCreationOutcome.Created, Lot: { } lot }:
                var view = await FindLotAsync(lots, canonical, lot.Id, httpContext).ConfigureAwait(false);
                return TypedResults.Created($"/sites/{canonical}/lots/{lot.Id}", ToLotResponse(view));
            case { Outcome: LotCreationOutcome.IdempotencyKeyReused }:
                return EdgeProblems.Result(
                    StatusCodes.Status422UnprocessableEntity,
                    EdgeProblems.IdempotencyKeyReused,
                    "The Idempotency-Key was used for a different request.",
                    "Nothing was created. Use a new key for a new Lot.");
            default:
                return SiteNotFound();
        }
    }

    private static async Task<IResult> GetLotAsync(
        string siteId,
        string lotId,
        HttpContext httpContext,
        [FromServices] LotsReadModel lots)
    {
        var canonicalLot = CanonicalizeLotId(lotId);
        var view = canonicalLot is null
            ? null
            : await lots.FindLotAsync(SiteAccessHandler.Canonicalize(siteId)!, canonicalLot, httpContext.RequestAborted).ConfigureAwait(false);

        return view is null ? LotNotFound() : TypedResults.Ok(ToLotResponse(view));
    }

    private static async Task<IResult> RenameLotAsync(
        string siteId,
        string lotId,
        HttpContext httpContext,
        [FromServices] IGrainFactory grains,
        [FromServices] LotsReadModel lots,
        [FromServices] IOptions<HttpJsonOptions> jsonOptions)
    {
        var name = EdgeValidation.NormalizeLotName(await ReadNameAsync(httpContext, jsonOptions.Value).ConfigureAwait(false));

        if (name is null)
        {
            return InvalidName("Lot");
        }

        if (CanonicalizeLotId(lotId) is not { } canonicalLot)
        {
            return LotNotFound();
        }

        var canonical = SiteAccessHandler.Canonicalize(siteId)!;
        var result = await grains.GetGrain<ILotGrain>(canonicalLot).Rename(canonical, name, httpContext.RequestAborted).ConfigureAwait(false);

        if (result.Outcome is not (LotOutcome.Renamed or LotOutcome.Unchanged))
        {
            return LotNotFound();
        }

        return TypedResults.Ok(ToLotResponse(await FindLotAsync(lots, canonical, canonicalLot, httpContext).ConfigureAwait(false)));
    }

    private static async Task<IResult> RemoveLotAsync(
        string siteId,
        string lotId,
        HttpContext httpContext,
        [FromServices] IGrainFactory grains)
    {
        if (CanonicalizeLotId(lotId) is not { } canonicalLot)
        {
            return LotNotFound();
        }

        var result = await grains
            .GetGrain<ILotGrain>(canonicalLot)
            .Remove(SiteAccessHandler.Canonicalize(siteId)!, httpContext.RequestAborted)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            LotOutcome.Removed or LotOutcome.AlreadyRemoved => TypedResults.NoContent(),
            LotOutcome.Claimed => EdgeProblems.Result(
                StatusCodes.Status409Conflict,
                EdgeProblems.LotClaimed,
                "A Node is assigned to the Lot.",
                "Nothing changed. Move or unassign the Node first."),
            _ => LotNotFound(),
        };
    }

    /// <summary>
    /// Returns the canonical form of a Lot ID, or <see langword="null"/> when it is not a Lot ID at all.
    /// </summary>
    internal static string? CanonicalizeLotId(string? lotId) =>
        Guid.TryParse(lotId, out var id) ? id.ToString() : null;

    /// <summary>
    /// Maps a Lot of the read model to the response: <c>removed</c> only when true.
    /// </summary>
    internal static LotResponse ToLotResponse(LotView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new LotResponse(view.LotId, view.Name, view.Status, view.Removed ? true : null);
    }

    // The Lot grain brought the lots projection up to date before it returned (read-your-writes).
    private static async Task<LotView> FindLotAsync(LotsReadModel lots, string siteId, string lotId, HttpContext httpContext) =>
        await lots.FindLotAsync(siteId, lotId, httpContext.RequestAborted).ConfigureAwait(false)
        ?? throw new InvalidOperationException($"The lots projection has no Lot {lotId} after its grain returned.");

    private static string CallerId(HttpContext httpContext) =>
        httpContext.User.FindFirst(EdgeAuthentication.UserIdClaim)!.Value;

    private static IResult? CheckIdempotencyKey(HttpContext httpContext, string resource) =>
        EdgeValidation.CheckIdempotencyKey([.. httpContext.Request.Headers[IdempotencyKeyHeader]]) switch
        {
            EdgeValidation.KeyCheck.Missing => EdgeProblems.Result(
                StatusCodes.Status400BadRequest,
                EdgeProblems.IdempotencyKeyMissing,
                "The request has no Idempotency-Key.",
                $"Send an {IdempotencyKeyHeader} header so a retry cannot create a second {resource}."),
            EdgeValidation.KeyCheck.Invalid => EdgeProblems.Result(
                StatusCodes.Status400BadRequest,
                EdgeProblems.Validation,
                "The Idempotency-Key is not valid.",
                $"Send one key of 1 to {EdgeValidation.MaxIdempotencyKeyLength} printable ASCII characters."),
            _ => null,
        };

    private static IResult InvalidName(string resource) =>
        EdgeProblems.Result(
            StatusCodes.Status400BadRequest,
            EdgeProblems.Validation,
            $"The {resource} name is not valid.",
            $"Send a JSON body with a name of 1 to {EdgeValidation.MaxSiteNameLength} characters.");

    private static IResult SiteNotFound() =>
        EdgeProblems.Result(StatusCodes.Status404NotFound, EdgeProblems.SiteNotFound, "The Site does not exist.");

    private static IResult LotNotFound() =>
        EdgeProblems.Result(StatusCodes.Status404NotFound, EdgeProblems.LotNotFound, "The Site has no such Lot.");

    // Every body of this API that names something is {"name": …}.
    private static async Task<string?> ReadNameAsync(HttpContext httpContext, HttpJsonOptions jsonOptions)
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
