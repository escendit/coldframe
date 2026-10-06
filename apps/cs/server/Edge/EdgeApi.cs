using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Lots;
using Coldframe.Contracts.Sites;
using Coldframe.Crypto;
using Coldframe.Protocol.Device.V1;
using Coldframe.Server.Devices;
using Coldframe.Server.Identity;
using Coldframe.Server.Lots;
using Google.Protobuf;
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
/// The body of <c>GET /enrolment-key</c>.
/// </summary>
/// <param name="PublicKey">The raw 32-byte X25519 enrolment public key, base64url without padding.</param>
/// <param name="Fingerprint">Lowercase hex SHA-256 of the raw public key.</param>
public sealed record EnrolmentKeyResponse(string PublicKey, string Fingerprint);

/// <summary>
/// The body of <c>POST /sites/{siteId}/devices</c>: a Device's sealed enrolment, relayed unread.
/// </summary>
/// <param name="DeviceId">The Device ID, 16 lowercase hex digits; also the HPKE associated data.</param>
/// <param name="Kind"><c>hub</c> or <c>node</c>.</param>
/// <param name="Enc">The HPKE encapsulated key (32 bytes), base64url without padding.</param>
/// <param name="Ciphertext">The sealed <c>K_dev</c> (48 bytes), base64url without padding.</param>
/// <param name="LotId">The Lot a Node is put in, or <see langword="null"/>; never sent for a Hub.</param>
public sealed record EnrolDeviceRequest(string? DeviceId, string? Kind, string? Enc, string? Ciphertext, string? LotId = null);

/// <summary>
/// A Device as the Edge API returns it.
/// </summary>
/// <param name="Id">The Device ID.</param>
/// <param name="Kind"><c>hub</c> or <c>node</c>.</param>
/// <param name="SiteId">The Site the Device is enrolled on.</param>
/// <param name="LotId">The Lot a Node is on; omitted otherwise.</param>
public sealed record DeviceResponse(string Id, string Kind, string SiteId, string? LotId = null);

/// <summary>
/// A Device in the body of <c>GET /sites/{siteId}/devices</c>.
/// </summary>
/// <param name="Id">The Device ID.</param>
/// <param name="Kind"><c>hub</c> or <c>node</c>.</param>
/// <param name="Online">Whether the Device is online, computed when the Server answers; never stored.</param>
/// <param name="LotId">The Lot a Node is on; omitted otherwise.</param>
/// <param name="LastSeenAt">When the last heartbeat was accepted, <c>yyyy-MM-ddTHH:mm:ss.fffZ</c>; omitted before the first.</param>
public sealed record DeviceListItemResponse(string Id, string Kind, bool Online, string? LotId = null, string? LastSeenAt = null);

/// <summary>
/// The body of <c>GET /sites/{siteId}/devices</c>: every enrolled Device of the Site.
/// </summary>
/// <param name="Devices">The Devices, ordered by Device ID.</param>
public sealed record DeviceListResponse(IReadOnlyList<DeviceListItemResponse> Devices);

/// <summary>
/// The body of <c>POST /device/heartbeat</c>'s 200.
/// </summary>
/// <param name="ServerTime">The Server clock, <c>yyyy-MM-ddTHH:mm:ss.fffZ</c>.</param>
public sealed record HeartbeatResponse(string ServerTime);

/// <summary>
/// A heartbeat request after the Edge API's checks: either the problem to answer, or the Device and what
/// to hand its grain.
/// </summary>
/// <param name="Problem">The response for a request that failed a check, or <see langword="null"/>.</param>
/// <param name="DeviceId">The Device the request claims to be from.</param>
/// <param name="Request">The request for <see cref="IDeviceGrain.Heartbeat"/>.</param>
internal sealed record HeartbeatRead(IResult? Problem, DeviceId? DeviceId = null, DeviceHeartbeat? Request = null);

/// <summary>
/// The body of <c>POST /device/ingest</c>'s 200: one result per frame, in request order (AD-9).
/// </summary>
/// <param name="Results">The frames' results.</param>
public sealed record IngestResponse(IReadOnlyList<IngestFrameResponse> Results);

/// <summary>
/// How one frame of an ingest envelope ended.
/// </summary>
/// <param name="Status">The contract's <c>IngestFrameStatus</c>, such as <c>stored</c>.</param>
/// <param name="Downlink">The sealed acknowledgement in base64, only for <c>stored</c> and <c>duplicate</c>.</param>
public sealed record IngestFrameResponse(string Status, string? Downlink = null);

/// <summary>
/// An ingest request after the Edge API's checks: either the problem to answer, or the Hub that signed it,
/// what to hand its grain, and the frames.
/// </summary>
/// <param name="Problem">The response for a request that failed a check, or <see langword="null"/>.</param>
/// <param name="HubId">The Hub the request claims to be from.</param>
/// <param name="Request">The request for <see cref="IDeviceGrain.AuthenticateRelay"/>.</param>
/// <param name="Frames">The frames of the envelope, still base64.</param>
internal sealed record IngestRead(
    IResult? Problem,
    DeviceId? HubId = null,
    DeviceRelayAuthentication? Request = null,
    IReadOnlyList<string>? Frames = null);

/// <summary>
/// The Edge API endpoints, contract-first from <c>packages/openapi/coldframe.openapi.json</c> (AD-10).
/// </summary>
/// <remarks>
/// Every endpoint declares exactly one access rule (<see cref="EdgeAccessRuleExtensions.RequireSiteRole{TBuilder}"/>,
/// <see cref="EdgeAccessRuleExtensions.RequireAuthenticatedCaller{TBuilder}"/> or
/// <see cref="EdgeAccessRuleExtensions.RequireDevice{TBuilder}"/>). Handlers change state only through grains and
/// read only the read models.
/// </remarks>
public static partial class EdgeApi
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
    /// The heartbeat operation's path, which the Hub signs.
    /// </summary>
    public const string HeartbeatPath = "/device/heartbeat";

    /// <summary>
    /// The ingest operation's path, which the Hub signs.
    /// </summary>
    public const string IngestPath = "/device/ingest";

    /// <summary>
    /// How <c>serverTime</c> is written: ISO-8601 UTC with milliseconds and <c>Z</c> (AD-11).
    /// </summary>
    public const string ServerTimeFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    /// <summary>
    /// Maps every Edge API endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapEdgeApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost(HeartbeatPath, DeviceHeartbeatAsync)
            .WithName("deviceHeartbeat")
            .RequireDevice();

        endpoints.MapPost(IngestPath, DeviceIngestAsync)
            .WithName("deviceIngest")
            .RequireDevice();

        endpoints.MapGet("/enrolment-key", GetEnrolmentKey)
            .WithName("getEnrolmentKey")
            .RequireAuthenticatedCaller();

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

        endpoints.MapGet("/sites/{siteId}/devices", ListDevicesAsync)
            .WithName("listDevices")
            .RequireSiteRole(SiteRole.Member);

        endpoints.MapPost("/sites/{siteId}/devices", EnrolDeviceAsync)
            .WithName("enrolDevice")
            .RequireSiteRole(SiteRole.Administrator);

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

    private static async Task<IResult> DeviceHeartbeatAsync(
        HttpContext httpContext,
        [FromServices] IGrainFactory grains,
        [FromServices] TimeProvider time)
    {
        var read = await ReadHeartbeatAsync(httpContext).ConfigureAwait(false);

        if (read is not { Problem: null, DeviceId: { } deviceId, Request: { } request })
        {
            return read.Problem!;
        }

        var result = await grains
            .GetGrain<IDeviceGrain>(deviceId.ToString())
            .Heartbeat(request, httpContext.RequestAborted)
            .ConfigureAwait(false);

        return ToHttpResult(result, time);
    }

    /// <summary>
    /// Checks a heartbeat request before any grain sees it: the Device headers (401 <c>device-unauthorized</c>
    /// when one is missing or malformed), then the raw body, read up to
    /// <see cref="EdgeValidation.MaxHeartbeatBodyLength"/> bytes (400 <c>validation</c> when larger, not JSON,
    /// or not <c>protocolVersion</c> 1). Nothing is verified here: the Device grain checks the signature.
    /// </summary>
    internal static async Task<HeartbeatRead> ReadHeartbeatAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (EdgeValidation.ParseDeviceHeaders(httpContext.Request.Headers) is not { } authentication)
        {
            return new HeartbeatRead(DeviceUnauthorized());
        }

        var body = await ReadCappedBodyAsync(httpContext.Request, EdgeValidation.MaxHeartbeatBodyLength, httpContext.RequestAborted)
            .ConfigureAwait(false);

        if (body is null || EdgeValidation.ParseHeartbeatBody(body) is not { } heartbeat)
        {
            return new HeartbeatRead(EdgeProblems.Result(
                StatusCodes.Status400BadRequest,
                EdgeProblems.Validation,
                "The heartbeat is not valid.",
                $"Send a JSON body of at most {EdgeValidation.MaxHeartbeatBodyLength} bytes with protocolVersion {EdgeValidation.HeartbeatProtocolVersion} and, optionally, uptimeMs."));
        }

        return new HeartbeatRead(
            null,
            authentication.DeviceId,
            new DeviceHeartbeat(
                httpContext.Request.Method.ToUpperInvariant(),
                httpContext.Request.Path.Value ?? HeartbeatPath,
                body,
                authentication.TimestampMs,
                authentication.Nonce,
                authentication.Signature,
                heartbeat.UptimeMs));
    }

    /// <summary>
    /// Maps the Device grain's answer to the HTTP response: 200 with the Server's time, or 401
    /// <c>device-unauthorized</c> without saying why.
    /// </summary>
    internal static IResult ToHttpResult(DeviceHeartbeatResult result, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(time);

        return result.Outcome switch
        {
            DeviceHeartbeatOutcome.Accepted => TypedResults.Ok(new HeartbeatResponse(
                time.GetUtcNow().UtcDateTime.ToString(ServerTimeFormat, CultureInfo.InvariantCulture))),
            DeviceHeartbeatOutcome.Unauthorized => DeviceUnauthorized(),
            _ => throw new InvalidOperationException($"Unexpected heartbeat result {result.Outcome}."),
        };
    }

    // Parses the envelope and calls grains, nothing else (AD-9): the Hub's grain authenticates the request,
    // each Node's grain opens, stores and acknowledges its own frames.
    private static async Task<IResult> DeviceIngestAsync(
        HttpContext httpContext,
        [FromServices] IGrainFactory grains,
        [FromServices] ILoggerFactory loggers)
    {
        var read = await ReadIngestAsync(httpContext).ConfigureAwait(false);

        if (read is not { Problem: null, HubId: { } hubId, Request: { } request, Frames: { } frames })
        {
            return read.Problem!;
        }

        var authentication = await grains
            .GetGrain<IDeviceGrain>(hubId.ToString())
            .AuthenticateRelay(request, httpContext.RequestAborted)
            .ConfigureAwait(false);

        if (!authentication.Authenticated)
        {
            return DeviceUnauthorized();
        }

        var results = await IngestFramesAsync(grains, frames, hubId.ToString(), loggers.CreateLogger(IngestLogCategory))
            .ConfigureAwait(false);

        return ToHttpResult(results);
    }

    /// <summary>
    /// Hands each frame of an authenticated envelope to its Node's grain, in request order and one after
    /// the other, so the frames of one Node are never reordered. A frame whose grain cannot answer is
    /// <c>retry</c> for that frame only.
    /// </summary>
    internal static async Task<IReadOnlyList<DeviceIngestResult>> IngestFramesAsync(
        IGrainFactory grains,
        IReadOnlyList<string> frames,
        string hubId,
        ILogger logger)
    {
        var results = new List<DeviceIngestResult>(frames.Count);
        foreach (var frame in frames)
        {
            results.Add(await IngestFrameAsync(grains, frame, hubId, logger).ConfigureAwait(false));
        }

        return results;
    }

    /// <summary>
    /// Checks an ingest request before any grain sees it: the Device headers (401 <c>device-unauthorized</c>
    /// when one is missing or malformed), then the raw body, read up to
    /// <see cref="EdgeValidation.MaxIngestBodyLength"/> bytes (400 <c>validation</c> when larger, not JSON, or
    /// not an envelope of at most <see cref="EdgeValidation.MaxIngestFrames"/> frames). Nothing is verified
    /// here: the Hub's Device grain checks the signature.
    /// </summary>
    internal static async Task<IngestRead> ReadIngestAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (EdgeValidation.ParseDeviceHeaders(httpContext.Request.Headers) is not { } authentication)
        {
            return new IngestRead(DeviceUnauthorized());
        }

        var body = await ReadCappedBodyAsync(httpContext.Request, EdgeValidation.MaxIngestBodyLength, httpContext.RequestAborted)
            .ConfigureAwait(false);

        if (body is null || EdgeValidation.ParseIngestBody(body) is not { } frames)
        {
            return new IngestRead(EdgeProblems.Result(
                StatusCodes.Status400BadRequest,
                EdgeProblems.Validation,
                "The ingest envelope is not valid.",
                $"Send a JSON body of at most {EdgeValidation.MaxIngestBodyLength} bytes with frames, an array of at most {EdgeValidation.MaxIngestFrames} base64 strings of at most {EdgeValidation.MaxIngestFrameLength} characters."));
        }

        return new IngestRead(
            null,
            authentication.DeviceId,
            new DeviceRelayAuthentication(
                httpContext.Request.Method.ToUpperInvariant(),
                httpContext.Request.Path.Value ?? IngestPath,
                body,
                authentication.TimestampMs,
                authentication.Nonce,
                authentication.Signature),
            frames);
    }

    /// <summary>
    /// Decodes one frame of the envelope into the request its Node's grain takes, or <see langword="null"/>
    /// when it is not base64, not a <c>SealedEnvelope</c>, or names no Device ID: <c>rejected_auth</c>.
    /// </summary>
    internal static (string DeviceId, DeviceIngest Request)? DecodeFrame(string frame, string hubId)
    {
        if (EdgeValidation.DecodeBase64(frame) is not { } bytes)
        {
            return null;
        }

        SealedEnvelope envelope;
        try
        {
            envelope = SealedEnvelope.Parser.ParseFrom(bytes);
        }
        catch (InvalidProtocolBufferException)
        {
            return null;
        }

        if (envelope.DeviceId.Length != CryptoSpec.DeviceIdLength)
        {
            return null;
        }

        return (
            DeviceId.FromBytes(envelope.DeviceId.Span).ToString(),
            new DeviceIngest(envelope.ProtocolVersion, envelope.Counter, envelope.Ciphertext.ToByteArray(), hubId));
    }

    private static async Task<DeviceIngestResult> IngestFrameAsync(IGrainFactory grains, string frame, string hubId, ILogger logger)
    {
        if (DecodeFrame(frame, hubId) is not var (deviceId, request))
        {
            return new DeviceIngestResult(DeviceIngestStatus.RejectedAuth);
        }

        try
        {
            // Not cancelled by the caller: a Hub that hangs up does not abandon a commit halfway.
            return await grains.GetGrain<IDeviceGrain>(deviceId).Ingest(request, CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A grain that cannot answer (timeout, a failed activation) stored nothing it acknowledged: the Node resends.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogIngestGrainFailed(logger, deviceId, exception);
            return new DeviceIngestResult(DeviceIngestStatus.Retry);
        }
    }

    private const string IngestLogCategory = "Coldframe.Server.Edge.Ingest";

    // Never the frame or its payload: only the Device and the failure.
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "The grain of Device {DeviceId} did not answer a frame; the frame is answered with retry.")]
    private static partial void LogIngestGrainFailed(ILogger logger, string deviceId, Exception exception);

    /// <summary>
    /// Maps the frames' results to the HTTP response (AD-9): 200 with one result per frame in request order,
    /// a downlink only on <c>stored</c> and <c>duplicate</c>; 503 <c>ingest-unavailable</c> only when there
    /// were frames and every one ended in <c>retry</c>.
    /// </summary>
    internal static IResult ToHttpResult(IReadOnlyList<DeviceIngestResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        if (results.Count > 0 && results.All(result => result.Status == DeviceIngestStatus.Retry))
        {
            return EdgeProblems.Result(
                StatusCodes.Status503ServiceUnavailable,
                EdgeProblems.IngestUnavailable,
                "No frame could be stored.",
                "Nothing was acknowledged. Send the frames again.");
        }

        return TypedResults.Ok(new IngestResponse(
        [
            .. results.Select(result => new IngestFrameResponse(
                EdgeValidation.IngestStatusName(result.Status),
                result is { Status: DeviceIngestStatus.Stored or DeviceIngestStatus.Duplicate, Downlink: { } downlink }
                    ? Convert.ToBase64String(downlink)
                    : null)),
        ]));
    }

    private static IResult DeviceUnauthorized() =>
        EdgeProblems.Result(
            StatusCodes.Status401Unauthorized,
            EdgeProblems.DeviceUnauthorized,
            "The Device is not authenticated.",
            "Sign the request with the Device's hub-auth key, a fresh nonce and the current time.");

    // Reads the whole body, or null once it exceeds `limit` bytes.
    private static async Task<byte[]?> ReadCappedBodyAsync(HttpRequest request, int limit, CancellationToken cancellationToken)
    {
        if (request.ContentLength > limit)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[1024];
        int read;

        while ((read = await request.Body.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > limit)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static Microsoft.AspNetCore.Http.HttpResults.Ok<EnrolmentKeyResponse> GetEnrolmentKey([FromServices] EnrolmentKeyring keyring) =>
        TypedResults.Ok(new EnrolmentKeyResponse(Base64Url.EncodeToString(keyring.PublicKey.Span), keyring.Fingerprint));

    private static async Task<IResult> ListDevicesAsync(
        string siteId,
        HttpContext httpContext,
        [FromServices] DevicesReadModel devices,
        [FromServices] TimeProvider timeProvider)
    {
        var canonical = SiteAccessHandler.Canonicalize(siteId)!;
        var views = await devices.ListDevicesAsync(canonical, httpContext.RequestAborted).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();

        return TypedResults.Ok(new DeviceListResponse([.. views.Select(view => ToDeviceListItem(view, now))]));
    }

    /// <summary>
    /// Maps a Device of the read model to the list item: <c>online</c> from the Server clock
    /// (<see cref="DeviceLiveness.IsOnline"/>), <c>lotId</c> and <c>lastSeenAt</c> only when present.
    /// </summary>
    /// <param name="view">The Device as the devices projection holds it.</param>
    /// <param name="now">The Server clock.</param>
    internal static DeviceListItemResponse ToDeviceListItem(DeviceView view, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new DeviceListItemResponse(
            view.DeviceId,
            view.Kind,
            DeviceLiveness.IsOnline(view.LastSeenAt, now),
            view.LotId,
            view.LastSeenAt?.UtcDateTime.ToString(ServerTimeFormat, CultureInfo.InvariantCulture));
    }

    private static async Task<IResult> EnrolDeviceAsync(
        string siteId,
        HttpContext httpContext,
        [FromServices] IGrainFactory grains,
        [FromServices] EnrolmentKeyring keyring,
        [FromServices] DeviceKeyVault vault,
        [FromServices] IOptions<HttpJsonOptions> jsonOptions)
    {
        if (CheckIdempotencyKey(httpContext, "enrolment") is { } keyProblem)
        {
            return keyProblem;
        }

        var request = await ReadJsonAsync<EnrolDeviceRequest>(httpContext, jsonOptions.Value).ConfigureAwait(false);

        if (EdgeValidation.NormalizeDeviceId(request?.DeviceId) is not { } deviceId
            || EdgeValidation.NormalizeDeviceKind(request?.Kind) is not { } kind
            || EdgeValidation.DecodeBase64Url(request?.Enc, CryptoSpec.HpkeEncLength) is not { } enc
            || EdgeValidation.DecodeBase64Url(request?.Ciphertext, CryptoSpec.DeviceKeyLength + CryptoSpec.AeadTagLength) is not { } ciphertext)
        {
            return EdgeProblems.Result(
                StatusCodes.Status400BadRequest,
                EdgeProblems.Validation,
                "The enrolment request is not valid.",
                "Send a JSON body with deviceId (16 lowercase hex digits), kind (hub or node), and enc (32 bytes) and ciphertext (48 bytes), both base64url without padding.");
        }

        // Only a Node is put in a Lot (AD-18), and only a Lot ID names one.
        if (request!.LotId is not null && kind != DeviceKind.Node)
        {
            return EdgeProblems.Result(
                StatusCodes.Status400BadRequest,
                EdgeProblems.Validation,
                "The enrolment request is not valid.",
                "A Hub is never put in a Lot. Send lotId only for a Node.");
        }

        var lotId = request.LotId is { } requested ? CanonicalizeLotId(requested) : null;
        if (request.LotId is not null && lotId is null)
        {
            return EdgeProblems.Result(
                StatusCodes.Status400BadRequest,
                EdgeProblems.Validation,
                "The enrolment request is not valid.",
                "Send lotId as a Lot ID (a UUID).");
        }

        // K_dev exists in plaintext only here: it is wrapped before any grain sees it, and never logged.
        WrappedDeviceKey wrapped;
        byte[]? deviceKey = null;

        try
        {
            deviceKey = keyring.Open(deviceId, enc, ciphertext);

            // The Device ID is the one K_dev derives (AD-12), not merely the one the request claims.
            if (KeyHierarchy.DeriveDeviceId(deviceKey) != deviceId)
            {
                return NotSealedToThisServer();
            }

            wrapped = vault.Wrap(deviceId, deviceKey);
        }
        catch (CryptoFailureException)
        {
            // Never echoed: the reason could help an attacker probe the key.
            return NotSealedToThisServer();
        }
        finally
        {
            if (deviceKey is not null)
            {
                CryptographicOperations.ZeroMemory(deviceKey);
            }
        }

        var canonical = SiteAccessHandler.Canonicalize(siteId)!;
        var result = await grains
            .GetGrain<IDeviceGrain>(deviceId.ToString())
            .Enrol(
                new EnrolDevice(canonical, kind, wrapped, CallerId(httpContext), httpContext.Request.Headers[IdempotencyKeyHeader][0]!, lotId),
                httpContext.RequestAborted)
            .ConfigureAwait(false);

        return ToHttpResult(result);
    }

    /// <summary>
    /// Maps the Device grain's answer to the HTTP response: 201 with the Device, 404 <c>site-not-found</c> or
    /// <c>lot-not-found</c>, 409 <c>device-on-another-site</c>, <c>lot-claimed</c> or <c>device-assigned</c>,
    /// or 422 <c>idempotency-key-reused</c>.
    /// </summary>
    internal static IResult ToHttpResult(DeviceEnrolmentResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result switch
        {
            { Outcome: DeviceEnrolmentOutcome.Enrolled, Device: { } device } => TypedResults.Created(
                (string?)null,
                new DeviceResponse(device.Id, EdgeValidation.DeviceKindName(device.Kind), device.SiteId, device.LotId)),
            { Outcome: DeviceEnrolmentOutcome.OnAnotherSite } => EdgeProblems.Result(
                StatusCodes.Status409Conflict,
                EdgeProblems.DeviceOnAnotherSite,
                "The Device is enrolled on another Site.",
                "Nothing was enrolled."),
            { Outcome: DeviceEnrolmentOutcome.IdempotencyKeyReused } => EdgeProblems.Result(
                StatusCodes.Status422UnprocessableEntity,
                EdgeProblems.IdempotencyKeyReused,
                "The Idempotency-Key was used for a different request.",
                "Nothing was enrolled. Use a new key for another Device."),
            { Outcome: DeviceEnrolmentOutcome.SiteNotFound } => SiteNotFound(),
            { Outcome: DeviceEnrolmentOutcome.LotNotFound } => LotNotFound(),
            { Outcome: DeviceEnrolmentOutcome.LotOccupied } => EdgeProblems.Result(
                StatusCodes.Status409Conflict,
                EdgeProblems.LotClaimed,
                "This Lot already has a Node.",
                "Nothing was assigned. Choose another Lot."),
            { Outcome: DeviceEnrolmentOutcome.AlreadyAssigned } => EdgeProblems.Result(
                StatusCodes.Status409Conflict,
                EdgeProblems.DeviceAssigned,
                "The Node is in another Lot.",
                "Nothing changed. Move the Node instead."),
            _ => throw new InvalidOperationException($"Unexpected Device enrolment result {result.Outcome}."),
        };
    }

    private static IResult NotSealedToThisServer() =>
        EdgeProblems.Result(
            StatusCodes.Status400BadRequest,
            EdgeProblems.Validation,
            "The enrolment does not open.",
            "Seal the Device key to the key from GET /enrolment-key, with the Device's own ID.");

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
    private static async Task<string?> ReadNameAsync(HttpContext httpContext, HttpJsonOptions jsonOptions) =>
        (await ReadJsonAsync<CreateSiteRequest>(httpContext, jsonOptions).ConfigureAwait(false))?.Name;

    // A body that is not JSON, or not the expected shape, reads as null: the handler answers 400.
    private static async Task<T?> ReadJsonAsync<T>(HttpContext httpContext, HttpJsonOptions jsonOptions)
        where T : class
    {
        if (!httpContext.Request.HasJsonContentType())
        {
            return null;
        }

        try
        {
            return await httpContext.Request
                .ReadFromJsonAsync<T>(jsonOptions.SerializerOptions, httpContext.RequestAborted)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
