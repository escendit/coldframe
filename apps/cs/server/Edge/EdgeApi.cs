using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Lots;
using Coldframe.Contracts.Sensors;
using Coldframe.Contracts.Sites;
using Coldframe.Crypto;
using Coldframe.Protocol.Device.V1;
using Coldframe.Server.Devices;
using Coldframe.Server.Identity;
using Coldframe.Server.Lots;
using Coldframe.Server.Sensors;
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
/// <param name="StatusSince">When the Lot got that status, ISO-8601 UTC.</param>
/// <param name="LastReadingAt">The newest Reading time of the Lot's Node since it took the Lot, ISO-8601 UTC; omitted without one.</param>
/// <param name="UnknownCause"><c>node</c> or <c>hub</c>; sent only for an <c>unknown</c> Lot.</param>
/// <param name="PausedBy"><c>device</c> and/or <c>site</c>; sent only for a <c>paused</c> Lot.</param>
/// <param name="PausedUntil">When the Pause ends, ISO-8601 UTC; omitted when the Lot is not paused or the Pause has no end.</param>
/// <param name="Removed"><see langword="true"/> for a removed Lot; omitted otherwise.</param>
/// <param name="Node">The Lot's Node with its battery and last seen; sent only by <c>GET /sites/{siteId}/lots/{lotId}</c> while the Lot holds a Node.</param>
/// <param name="Sensors">The newest Reading of each Sensor of the Node, converted; sent with <paramref name="Node"/>.</param>
/// <param name="MoisturePercent">The soil moisture of the newest Reading in percent (a multiple of 5), sent only when that Reading was stored with a Calibration.</param>
/// <param name="LowThresholdPercent">The effective low Threshold of that soil-moisture Sensor in percent, sent with <paramref name="MoisturePercent"/> when the Sensor has one.</param>
public sealed record LotResponse(
    string Id,
    string Name,
    string Status,
    string StatusSince,
    string? LastReadingAt = null,
    string? UnknownCause = null,
    IReadOnlyList<string>? PausedBy = null,
    string? PausedUntil = null,
    bool? Removed = null,
    NodeStatusResponse? Node = null,
    IReadOnlyList<SensorReadingResponse>? Sensors = null,
    int? MoisturePercent = null,
    int? LowThresholdPercent = null);

/// <summary>
/// A Lot's Node as Lot detail returns it.
/// </summary>
/// <param name="DeviceId">The Node's Device ID.</param>
/// <param name="BatteryPercent">The battery charge of the newest device report; omitted when unknown.</param>
/// <param name="Charging"><c>charging</c> or <c>notCharging</c>; omitted when unknown.</param>
/// <param name="LastSeenAt">The <c>measured_at</c> of the newest device report, ISO-8601 UTC; omitted without a report.</param>
public sealed record NodeStatusResponse(string DeviceId, int? BatteryPercent = null, string? Charging = null, string? LastSeenAt = null);

/// <summary>
/// The newest Reading of one Sensor, converted by the Server.
/// </summary>
/// <param name="Quantity">The quantity token.</param>
/// <param name="Value">The converted value.</param>
/// <param name="Unit"><c>raw</c>, <c>°C</c>, <c>%</c> or <c>kΩ</c>.</param>
/// <param name="MeasuredAt">When the Reading was taken, ISO-8601 UTC.</param>
/// <param name="SensorId">The Sensor ID, which names the Sensor in <c>/sites/{siteId}/sensors/{sensorId}/calibration</c>.</param>
/// <param name="Calibratable">Whether the Sensor's Specification calls for Calibration; clients show Calibrate only then.</param>
public sealed record SensorReadingResponse(string Quantity, double Value, string Unit, string MeasuredAt, string? SensorId = null, bool? Calibratable = null);

/// <summary>
/// One recent stored Reading of a Sensor a Calibration can be taken from.
/// </summary>
/// <param name="ReadingSeq">The Reading's <c>reading_seq</c>, which the Calibration request names.</param>
/// <param name="RawValue">The stored raw value.</param>
/// <param name="MeasuredAt">When the Reading was taken, ISO-8601 UTC.</param>
public sealed record CalibrationReadingResponse(ulong ReadingSeq, long RawValue, string MeasuredAt);

/// <summary>
/// The body of <c>GET /sites/{siteId}/sensors/{sensorId}/calibration</c>: where the Sensor's Calibration stands
/// (as the Calibration endpoint's 200 carries it) and the recent stored Readings to pick a point from.
/// </summary>
/// <param name="Calibrated">Whether a Calibration is in force.</param>
/// <param name="Readings">The recent stored Readings, newest first; empty for a Sensor that cannot be calibrated.</param>
/// <param name="CalibrationId">The Calibration ID in force; omitted while the Sensor is uncalibrated.</param>
/// <param name="Dry">The dry point of the Calibration in force.</param>
/// <param name="Wet">The wet point of the Calibration in force.</param>
/// <param name="PendingDry">A dry point kept while the wet one is missing.</param>
/// <param name="PendingWet">A wet point kept while the dry one is missing.</param>
public sealed record CalibrationStateResponse(
    bool Calibrated,
    IReadOnlyList<CalibrationReadingResponse> Readings,
    string? CalibrationId = null,
    CalibrationPointResponse? Dry = null,
    CalibrationPointResponse? Wet = null,
    CalibrationPointResponse? PendingDry = null,
    CalibrationPointResponse? PendingWet = null);

/// <summary>
/// One UTC day of a Lot's history.
/// </summary>
/// <param name="Day">The UTC date, <c>yyyy-MM-dd</c>.</param>
/// <param name="Low">The day's lowest converted value.</param>
/// <param name="High">The day's highest converted value.</param>
/// <param name="ReadingCount">How many Readings the day has.</param>
public sealed record LotHistoryDayResponse(string Day, double Low, double High, int ReadingCount);

/// <summary>
/// The body of <c>GET /sites/{siteId}/lots/{lotId}/history</c>.
/// </summary>
/// <param name="Quantity">The quantity read.</param>
/// <param name="Unit">The unit of the values.</param>
/// <param name="Days">The days with Readings, ascending.</param>
/// <param name="NextCursor">Opaque; present only when more days follow.</param>
public sealed record LotHistoryResponse(string Quantity, string Unit, IReadOnlyList<LotHistoryDayResponse> Days, string? NextCursor = null);

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
/// The body of <c>POST /sites/{siteId}/devices/{deviceId}/move</c>.
/// </summary>
/// <param name="LotId">The Lot of the Site to move the Node to.</param>
public sealed record MoveDeviceRequest(string? LotId);

/// <summary>
/// One reference point in the body of <c>POST /sites/{siteId}/sensors/{sensorId}/calibration</c>.
/// </summary>
/// <param name="ReadingSeq">The <c>reading_seq</c> of a stored Reading of the Sensor.</param>
public sealed record CalibrationPointRequest(ulong? ReadingSeq);

/// <summary>
/// The body of <c>POST /sites/{siteId}/sensors/{sensorId}/calibration</c>: at least one point.
/// </summary>
/// <param name="Dry">The dry point (the probe in dry soil).</param>
/// <param name="Wet">The wet point (the probe in water).</param>
public sealed record CalibrateSensorRequest(CalibrationPointRequest? Dry, CalibrationPointRequest? Wet);

/// <summary>
/// A reference point of a Calibration as the Edge API returns it.
/// </summary>
/// <param name="RawValue">The raw value of the stored Reading the point was taken from.</param>
public sealed record CalibrationPointResponse(long RawValue);

/// <summary>
/// The body of <c>POST /sites/{siteId}/sensors/{sensorId}/calibration</c>'s 200: where the Sensor's Calibration
/// stands after the call.
/// </summary>
/// <param name="Calibrated">Whether a Calibration is in force.</param>
/// <param name="CalibrationId">The Calibration ID in force; omitted while the Sensor is uncalibrated.</param>
/// <param name="Dry">The dry point of the Calibration in force.</param>
/// <param name="Wet">The wet point of the Calibration in force.</param>
/// <param name="PendingDry">A dry point kept while the wet one is missing.</param>
/// <param name="PendingWet">A wet point kept while the dry one is missing.</param>
public sealed record CalibrationResponse(
    bool Calibrated,
    string? CalibrationId = null,
    CalibrationPointResponse? Dry = null,
    CalibrationPointResponse? Wet = null,
    CalibrationPointResponse? PendingDry = null,
    CalibrationPointResponse? PendingWet = null);

/// <summary>
/// One side of the body of <c>PUT /sites/{siteId}/sensors/{sensorId}/thresholds</c>.
/// </summary>
/// <param name="Kind"><c>default</c>, <c>override</c> or <c>cleared</c>.</param>
/// <param name="Value">The value in display units; only with <c>override</c>.</param>
public sealed record ThresholdSideRequest(string? Kind, decimal? Value = null);

/// <summary>
/// The body of <c>PUT /sites/{siteId}/sensors/{sensorId}/thresholds</c>: at least one side; a side that is
/// absent stays as it is.
/// </summary>
/// <param name="Low">The low side.</param>
/// <param name="High">The high side.</param>
public sealed record SetThresholdsRequest(ThresholdSideRequest? Low, ThresholdSideRequest? High);

/// <summary>
/// One side of a Sensor's Thresholds as the Edge API returns it.
/// </summary>
/// <param name="Kind"><c>default</c>, <c>override</c> or <c>cleared</c>.</param>
/// <param name="Value">The effective Threshold in display units; omitted when the side has none.</param>
public sealed record ThresholdSideResponse(string Kind, decimal? Value = null);

/// <summary>
/// A Sensor's Thresholds as the Edge API returns them (Story 5.3).
/// </summary>
/// <param name="Unit">The display unit of every value: <c>%</c>, <c>°C</c>, <c>kΩ</c> or <c>raw</c>.</param>
/// <param name="Low">The low side.</param>
/// <param name="High">The high side.</param>
/// <param name="ProposedLow">The low the Server proposes; omitted when the Specification has a default low. There is no proposed high.</param>
public sealed record ThresholdsResponse(
    string Unit,
    ThresholdSideResponse Low,
    ThresholdSideResponse High,
    decimal? ProposedLow = null);

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
/// <param name="LastSeenAt">A Hub's last accepted heartbeat, or a Node's newest device report, <c>yyyy-MM-ddTHH:mm:ss.fffZ</c>; omitted before the first.</param>
/// <param name="LotName">The name of the Lot a Node is on; omitted otherwise.</param>
/// <param name="BatteryPercent">A Node's battery charge from its newest device report; omitted when unknown.</param>
/// <param name="Charging">A Node's <c>charging</c> or <c>notCharging</c>; omitted when unknown.</param>
public sealed record DeviceListItemResponse(
    string Id,
    string Kind,
    bool Online,
    string? LotId = null,
    string? LastSeenAt = null,
    string? LotName = null,
    int? BatteryPercent = null,
    string? Charging = null);

/// <summary>
/// The body of <c>GET /sites/{siteId}/devices</c>: every enrolled Device of the Site.
/// </summary>
/// <param name="Devices">The Devices: Hubs by Device ID, then Nodes by Lot name (unassigned last) and Device ID.</param>
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

        endpoints.MapPost("/sites/{siteId}/devices/{deviceId}/move", MoveDeviceAsync)
            .WithName("moveDevice")
            .RequireSiteRole(SiteRole.Administrator);

        endpoints.MapPost("/sites/{siteId}/devices/{deviceId}/unassign", UnassignDeviceAsync)
            .WithName("unassignDevice")
            .RequireSiteRole(SiteRole.Administrator);

        endpoints.MapPost("/sites/{siteId}/sensors/{sensorId}/calibration", CalibrateSensorAsync)
            .WithName("calibrateSensor")
            .RequireSiteRole(SiteRole.Administrator);

        endpoints.MapGet("/sites/{siteId}/sensors/{sensorId}/calibration", GetSensorCalibrationAsync)
            .WithName("getSensorCalibration")
            .RequireSiteRole(SiteRole.Administrator);

        endpoints.MapGet("/sites/{siteId}/sensors/{sensorId}/thresholds", GetSensorThresholdsAsync)
            .WithName("getSensorThresholds")
            .RequireSiteRole(SiteRole.Member);

        endpoints.MapPut("/sites/{siteId}/sensors/{sensorId}/thresholds", SetSensorThresholdsAsync)
            .WithName("setSensorThresholds")
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

        endpoints.MapGet("/sites/{siteId}/lots/{lotId}/history", GetLotHistoryAsync)
            .WithName("getLotHistory")
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
        [FromServices] LotsReadModel lots,
        [FromServices] IGrainFactory grains)
    {
        var canonical = SiteAccessHandler.Canonicalize(siteId)!;
        var views = await lots.ListLotsAsync(canonical, httpContext.RequestAborted).ConfigureAwait(false);

        return TypedResults.Ok(new LotListResponse(await WithLowThresholdsAsync(views, grains, httpContext.RequestAborted).ConfigureAwait(false)));
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
        [FromServices] LotsReadModel lots,
        [FromServices] LotDetailReadModel detail,
        [FromServices] IGrainFactory grains)
    {
        var canonicalSite = SiteAccessHandler.Canonicalize(siteId)!;
        var canonicalLot = CanonicalizeLotId(lotId);
        var view = canonicalLot is null
            ? null
            : await lots.FindLotAsync(canonicalSite, canonicalLot, httpContext.RequestAborted).ConfigureAwait(false);

        if (view is null)
        {
            return LotNotFound();
        }

        var response = (await WithLowThresholdsAsync([view], grains, httpContext.RequestAborted).ConfigureAwait(false))[0];

        if (await detail.FindClaimAsync(canonicalSite, canonicalLot!, httpContext.RequestAborted).ConfigureAwait(false) is not { } claim)
        {
            return TypedResults.Ok(response);
        }

        var readings = await detail.LatestReadingsAsync(claim.NodeId, claim.ClaimedAt, httpContext.RequestAborted).ConfigureAwait(false);
        var report = await detail.LatestReportAsync(claim.NodeId, httpContext.RequestAborted).ConfigureAwait(false);

        return TypedResults.Ok(response with
        {
            Node = new NodeStatusResponse(
                claim.NodeId,
                report?.BatteryPercent,
                ToChargeState(report?.Charging),
                report is null ? null : ToServerTime(report.MeasuredAt)),
            Sensors = await ToSensorReadingsAsync(readings, grains, httpContext.RequestAborted).ConfigureAwait(false),
        });
    }

    // The percentage and its low Threshold belong to the list and the detail; a create or rename answer carries neither.
    // The low Threshold comes from the Sensor grain (AD-19) and only for a Lot that shows a percentage; the grains are
    // asked once per Lot and all at the same time. A Sensor that cannot be described shows no Threshold line.
    private static async Task<IReadOnlyList<LotResponse>> WithLowThresholdsAsync(
        IReadOnlyList<LotView> views,
        IGrainFactory grains,
        CancellationToken cancellationToken)
    {
        var lows = await Task.WhenAll(views.Select(view => EffectiveLowPercentAsync(view, grains, cancellationToken))).ConfigureAwait(false);

        return [.. views.Select((view, index) => ToLotResponse(view) with { MoisturePercent = view.MoisturePercent, LowThresholdPercent = lows[index] })];
    }

    private static async Task<int?> EffectiveLowPercentAsync(LotView view, IGrainFactory grains, CancellationToken cancellationToken)
    {
        if (view is not { MoisturePercent: not null, SoilSensorId: { } sensorId })
        {
            return null;
        }

        try
        {
            var snapshot = await grains.GetGrain<ISensorGrain>(sensorId.ToString("D")).Describe(cancellationToken).ConfigureAwait(false);

            return snapshot is { Specification.Calibration: true, Low.Value: { } low } ? (int)low : null;
        }
#pragma warning disable CA1031 // One Sensor that cannot be described must not fail the whole Lot list or detail.
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    private static async Task<IReadOnlyList<SensorReadingResponse>> ToSensorReadingsAsync(
        IReadOnlyList<StoredReading> readings,
        IGrainFactory grains,
        CancellationToken cancellationToken)
    {
        // A Sensor is calibratable when its Specification says so; a Sensor no Node declared is not. The grains
        // are asked once per Sensor and all at the same time, not one after the other per Reading.
        var calibratable = readings
            .Where(reading => reading.SensorId is not null)
            .Select(reading => reading.SensorId!.Value)
            .Distinct()
            .ToDictionary(
                sensorId => sensorId,
                sensorId => IsCalibratableAsync(grains, sensorId, cancellationToken));

        await Task.WhenAll(calibratable.Values).ConfigureAwait(false);

        var responses = new List<SensorReadingResponse>(readings.Count);

        foreach (var reading in readings)
        {
            if (SensorConversion.Convert(reading.Quantity, reading.RawValue, reading.Calibration) is not { } converted)
            {
                continue;
            }

            responses.Add(new SensorReadingResponse(
                reading.Quantity,
                converted.Value,
                converted.Unit,
                ToServerTime(reading.MeasuredAt),
                reading.SensorId?.ToString("D"),
                reading.SensorId is { } sensorId && await calibratable[sensorId].ConfigureAwait(false)));
        }

        return responses;
    }

    private static async Task<bool> IsCalibratableAsync(IGrainFactory grains, Guid sensorId, CancellationToken cancellationToken) =>
        await grains.GetGrain<ISensorGrain>(sensorId.ToString("D")).Describe(cancellationToken).ConfigureAwait(false) is { Specification.Calibration: true };

    /// <summary>
    /// Maps a stored charger token to the contract's <c>ChargeState</c>; <see langword="null"/> when it is unknown or absent.
    /// </summary>
    internal static string? ToChargeState(string? stored) => stored switch
    {
        "charging" => "charging",
        "not_charging" => "notCharging",
        _ => null,
    };

    private const int DefaultHistoryLimit = 31;

    private const int MaxHistoryLimit = 366;

    private const int DefaultHistoryDays = 30;

    private static readonly string[] HistoryTimeFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ssK",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
        "yyyy-MM-dd",
    ];

    private static async Task<IResult> GetLotHistoryAsync(
        string siteId,
        string lotId,
        HttpContext httpContext,
        [FromServices] LotsReadModel lots,
        [FromServices] LotDetailReadModel detail,
        [FromServices] TimeProvider timeProvider)
    {
        var canonicalSite = SiteAccessHandler.Canonicalize(siteId)!;
        var canonicalLot = CanonicalizeLotId(lotId);
        var view = canonicalLot is null
            ? null
            : await lots.FindLotAsync(canonicalSite, canonicalLot, httpContext.RequestAborted).ConfigureAwait(false);

        if (view is null)
        {
            return LotNotFound();
        }

        var query = httpContext.Request.Query;
        var now = timeProvider.GetUtcNow();

        if (SingleValue(query, "quantity") is not { } quantity || !SensorConversion.Quantities.Contains(quantity)
            || !TryReadTime(query, "from", out var from)
            || !TryReadTime(query, "to", out var to, endOfDay: true)
            || !TryReadLimit(query, out var limit)
            || !TryReadCursor(query, out var after))
        {
            return InvalidHistoryQuery();
        }

        var end = to ?? now;
        var start = from ?? end.AddDays(-DefaultHistoryDays);

        if (start > end)
        {
            return InvalidHistoryQuery();
        }

        var unit = SensorConversion.Convert(quantity, 0)!.Value.Unit;
        var claim = await detail.FindClaimAsync(canonicalSite, canonicalLot!, httpContext.RequestAborted).ConfigureAwait(false);

        if (claim is null)
        {
            return TypedResults.Ok(new LotHistoryResponse(quantity, unit, []));
        }

        // Whole UTC days, so the first bar of the window is not a partial day; only Readings since the claim count.
        var windowStart = new DateTimeOffset(start.UtcDateTime.Date, TimeSpan.Zero);
        var since = windowStart > claim.ClaimedAt ? windowStart : claim.ClaimedAt;

        var (days, hasMore) = await detail
            .HistoryAsync(claim.NodeId, quantity, since, end, after, limit, httpContext.RequestAborted)
            .ConfigureAwait(false);

        // A window with any calibrated soil-moisture Reading is in percent (AD-14: the Server converts, the band is in
        // percent), and then only counts the Readings that have a Calibration: a day of raw counts cannot sit beside
        // it. The window decides, not the page, so every page of a paged History has the same unit. A window without
        // one is raw as before. The cursor still names the last day of the page.
        var inPercent = quantity == "soil_moisture"
            && await detail.HasCalibratedAsync(claim.NodeId, quantity, since, end, httpContext.RequestAborted).ConfigureAwait(false);

        return TypedResults.Ok(new LotHistoryResponse(
            quantity,
            inPercent ? "%" : unit,
            [.. days.Where(day => !inPercent || day.CalibratedCount > 0).Select(day => ToHistoryDay(quantity, day, inPercent))],
            hasMore ? LotDetailReadModel.EncodeCursor(days[^1].Day) : null));
    }

    private static LotHistoryDayResponse ToHistoryDay(string quantity, StoredDay day, bool inPercent) =>
        inPercent
            ? new(day.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), day.CalibratedLow!.Value, day.CalibratedHigh!.Value, day.CalibratedCount)
            : new(
                day.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                SensorConversion.Convert(quantity, day.Low)!.Value.Value,
                SensorConversion.Convert(quantity, day.High)!.Value.Value,
                day.ReadingCount);

    // One value, or null when the parameter is absent or repeated.
    private static string? SingleValue(IQueryCollection query, string name) =>
        query.TryGetValue(name, out var values) && values.Count == 1 ? values[0] : null;

    private static bool TryReadTime(IQueryCollection query, string name, out DateTimeOffset? time, bool endOfDay = false)
    {
        time = null;

        if (!query.TryGetValue(name, out var values))
        {
            return true;
        }

        if (values.Count != 1
            || !DateTimeOffset.TryParseExact(values[0], HistoryTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return false;
        }

        // A date-only end names the whole UTC day: it ends one tick before the next midnight.
        time = endOfDay && values[0]!.Length == 10 ? parsed.AddDays(1).AddTicks(-1) : parsed;
        return true;
    }

    private static bool TryReadLimit(IQueryCollection query, out int limit)
    {
        limit = DefaultHistoryLimit;

        if (!query.TryGetValue("limit", out var values))
        {
            return true;
        }

        return values.Count == 1
            && int.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out limit)
            && limit is >= 1 and <= MaxHistoryLimit;
    }

    private static bool TryReadCursor(IQueryCollection query, out DateOnly? after)
    {
        after = null;

        if (!query.TryGetValue("cursor", out var values))
        {
            return true;
        }

        after = values.Count == 1 ? LotDetailReadModel.DecodeCursor(values[0]!) : null;
        return after is not null;
    }

    private static IResult InvalidHistoryQuery() =>
        EdgeProblems.Result(
            StatusCodes.Status400BadRequest,
            EdgeProblems.Validation,
            "The history query is not valid.",
            "Send quantity (soil_moisture, air_temperature, relative_humidity or gas_resistance); optionally from and to as ISO-8601 UTC times with from not after to, limit from 1 to 366, and the cursor of the previous page unchanged.");

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
            (view.Kind == "node" ? view.ReportedAt ?? view.LastSeenAt : view.LastSeenAt) is { } seen ? ToServerTime(seen) : null,
            view.LotName,
            view.BatteryPercent,
            ToChargeState(view.Charging));
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

    private static async Task<IResult> MoveDeviceAsync(
        string siteId,
        string deviceId,
        HttpContext httpContext,
        [FromServices] IGrainFactory grains,
        [FromServices] IOptions<HttpJsonOptions> jsonOptions)
    {
        var request = await ReadJsonAsync<MoveDeviceRequest>(httpContext, jsonOptions.Value).ConfigureAwait(false);

        if (request?.LotId is not { } requested || CanonicalizeLotId(requested) is not { } lotId)
        {
            return EdgeProblems.Result(
                StatusCodes.Status400BadRequest,
                EdgeProblems.Validation,
                "The move request is not valid.",
                "Send a JSON body with lotId, the Lot ID (a UUID) to move the Node to.");
        }

        if (EdgeValidation.NormalizeDeviceId(deviceId) is not { } id)
        {
            return DeviceNotFound();
        }

        var result = await grains
            .GetGrain<IDeviceGrain>(id.ToString())
            .Move(SiteAccessHandler.Canonicalize(siteId)!, lotId, httpContext.RequestAborted)
            .ConfigureAwait(false);

        return ToHttpResult(result);
    }

    private static async Task<IResult> UnassignDeviceAsync(
        string siteId,
        string deviceId,
        HttpContext httpContext,
        [FromServices] IGrainFactory grains)
    {
        if (EdgeValidation.NormalizeDeviceId(deviceId) is not { } id)
        {
            return DeviceNotFound();
        }

        var result = await grains
            .GetGrain<IDeviceGrain>(id.ToString())
            .Unassign(SiteAccessHandler.Canonicalize(siteId)!, httpContext.RequestAborted)
            .ConfigureAwait(false);

        return ToHttpResult(result);
    }

    /// <summary>
    /// Maps the Device grain's answer to a move or unassign to the HTTP response: 200 with the Node, 404
    /// <c>device-not-found</c> or <c>lot-not-found</c>, or 409 <c>lot-claimed</c>.
    /// </summary>
    internal static IResult ToHttpResult(DeviceAssignmentResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result switch
        {
            { Outcome: DeviceAssignmentOutcome.Moved or DeviceAssignmentOutcome.Unassigned or DeviceAssignmentOutcome.Unchanged, Device: { } device } =>
                TypedResults.Ok(new DeviceResponse(device.Id, EdgeValidation.DeviceKindName(device.Kind), device.SiteId, device.LotId)),
            { Outcome: DeviceAssignmentOutcome.NotFound } => DeviceNotFound(),
            { Outcome: DeviceAssignmentOutcome.LotNotFound } => LotNotFound(),
            { Outcome: DeviceAssignmentOutcome.LotOccupied } => EdgeProblems.Result(
                StatusCodes.Status409Conflict,
                EdgeProblems.LotClaimed,
                "This Lot already has a Node.",
                "Nothing changed. Choose another Lot."),
            _ => throw new InvalidOperationException($"Unexpected Device assignment result {result.Outcome}."),
        };
    }

    private static async Task<IResult> CalibrateSensorAsync(
        string siteId,
        string sensorId,
        HttpContext httpContext,
        [FromServices] IGrainFactory grains,
        [FromServices] IOptions<HttpJsonOptions> jsonOptions)
    {
        var request = await ReadJsonAsync<CalibrateSensorRequest>(httpContext, jsonOptions.Value).ConfigureAwait(false);

        if (request is not { } body
            || (body.Dry is null && body.Wet is null)
            || (body.Dry is not null && body.Dry.ReadingSeq is null)
            || (body.Wet is not null && body.Wet.ReadingSeq is null))
        {
            return EdgeProblems.Result(
                StatusCodes.Status400BadRequest,
                EdgeProblems.Validation,
                "The Calibration request is not valid.",
                "Send a JSON body with dry and/or wet, each {readingSeq}, the reading_seq of a stored Reading of the Sensor.");
        }

        if (await FindSiteSensorAsync(siteId, sensorId, grains, httpContext.RequestAborted).ConfigureAwait(false) is not { } found)
        {
            return SensorNotFound();
        }

        var sensor = found.Grain;

        var result = await sensor
            .Calibrate(
                new CalibrateSensor(
                    body.Dry is { ReadingSeq: { } dry } ? new CalibrationPointRef(dry) : null,
                    body.Wet is { ReadingSeq: { } wet } ? new CalibrationPointRef(wet) : null),
                httpContext.RequestAborted)
            .ConfigureAwait(false);

        return ToHttpResult(result);
    }

    private static async Task<IResult> GetSensorCalibrationAsync(
        string siteId,
        string sensorId,
        HttpContext httpContext,
        [FromServices] IGrainFactory grains,
        [FromServices] SensorReadings sensorReadings,
        [FromServices] TimeProvider time)
    {
        if (await FindSiteSensorAsync(siteId, sensorId, grains, httpContext.RequestAborted).ConfigureAwait(false) is not { } found)
        {
            return SensorNotFound();
        }

        var snapshot = found.Snapshot;
        var recent = snapshot.Specification.Calibration
            ? await sensorReadings
                .RecentAsync(found.Id, time.GetUtcNow() - CalibrationReadingWindow, CalibrationReadingLimit, httpContext.RequestAborted)
                .ConfigureAwait(false)
            : [];

        var state = ToCalibrationResponse(new SensorCalibrationResult(SensorCalibrationOutcome.Calibrated, snapshot.Calibration, snapshot.PendingDryRaw, snapshot.PendingWetRaw));

        return TypedResults.Ok(new CalibrationStateResponse(
            state.Calibrated,
            [.. recent.Select(reading => new CalibrationReadingResponse(reading.ReadingSeq, reading.RawValue, ToServerTime(reading.MeasuredAt)))],
            state.CalibrationId,
            state.Dry,
            state.Wet,
            state.PendingDry,
            state.PendingWet));
    }

    private static async Task<IResult> GetSensorThresholdsAsync(
        string siteId,
        string sensorId,
        HttpContext httpContext,
        [FromServices] IGrainFactory grains)
    {
        if (await FindSiteSensorAsync(siteId, sensorId, grains, httpContext.RequestAborted).ConfigureAwait(false) is not { } found
            || await found.Grain.GetThresholds(httpContext.RequestAborted).ConfigureAwait(false) is not { } thresholds)
        {
            return SensorNotFound();
        }

        return TypedResults.Ok(ToThresholdsResponse(thresholds));
    }

    private static async Task<IResult> SetSensorThresholdsAsync(
        string siteId,
        string sensorId,
        HttpContext httpContext,
        [FromServices] IGrainFactory grains,
        [FromServices] IOptions<HttpJsonOptions> jsonOptions)
    {
        var request = await ReadJsonAsync<SetThresholdsRequest>(httpContext, jsonOptions.Value).ConfigureAwait(false);

        if (request is not { } body || (body.Low is null && body.High is null))
        {
            return InvalidThresholds("Send a JSON body with low and/or high, each {kind: default | override | cleared, value?}.");
        }

        if (await FindSiteSensorAsync(siteId, sensorId, grains, httpContext.RequestAborted).ConfigureAwait(false) is not { } found)
        {
            return SensorNotFound();
        }

        var specification = found.Snapshot.Specification;
        if (!TryStoredSide(body.Low, specification, out var low) || !TryStoredSide(body.High, specification, out var high))
        {
            return InvalidThresholds(
                specification.Calibration
                    ? "A side is default, override with a whole percent from 0 to 100, or cleared. Nothing changed."
                    : "A side is default, override with a value the Sensor can report, or cleared. Nothing changed.");
        }

        var result = await found.Grain
            .SetThresholds(new SetSensorThresholds(low, high), httpContext.RequestAborted)
            .ConfigureAwait(false);

        return ToHttpResult(result);
    }

    /// <summary>
    /// Maps the Sensor grain's answer to a Threshold change to the HTTP response: 200 with the Thresholds in force
    /// (also when nothing changed), 404 <c>sensor-not-found</c> for a Sensor never declared, or 400
    /// <c>validation</c> with what was wrong and that nothing changed.
    /// </summary>
    internal static IResult ToHttpResult(SetSensorThresholdsResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Outcome switch
        {
            SensorThresholdsOutcome.Changed or SensorThresholdsOutcome.Unchanged when result.Thresholds is { } thresholds =>
                TypedResults.Ok(ToThresholdsResponse(thresholds)),
            SensorThresholdsOutcome.NotDeclared => SensorNotFound(),
            SensorThresholdsOutcome.MalformedSide => InvalidThresholds("A side is default, override with a value, or cleared. Nothing changed."),
            SensorThresholdsOutcome.OutOfRange => InvalidThresholds("Choose a value the Sensor can report (a calibrating Sensor: a whole percent from 0 to 100). Nothing changed."),
            SensorThresholdsOutcome.LowRequired => InvalidThresholds("A high Threshold needs a low one. Set a low or clear the high. Nothing changed."),
            SensorThresholdsOutcome.LowNotBelowHigh => InvalidThresholds("Low must stay below high. Lower the low or raise the high. Nothing changed."),
            _ => throw new InvalidOperationException($"Unexpected Threshold outcome {result.Outcome}."),
        };
    }

    private static IResult InvalidThresholds(string detail) =>
        EdgeProblems.Result(
            StatusCodes.Status400BadRequest,
            EdgeProblems.Validation,
            "The Thresholds are not valid.",
            detail);

    // Display units to the Specification's unit (a calibrating Sensor: percent, whole). false when the side is unreadable.
    private static bool TryStoredSide(ThresholdSideRequest? side, SensorSpecification specification, out ThresholdSetting? stored)
    {
        stored = null;
        if (side is null)
        {
            return true;
        }

        ThresholdKind kind;
        switch (side.Kind)
        {
            case "default":
                kind = ThresholdKind.Default;
                break;
            case "override":
                kind = ThresholdKind.Override;
                break;
            case "cleared":
                kind = ThresholdKind.Cleared;
                break;
            default:
                return false;
        }

        if (side.Value is not { } display)
        {
            stored = new ThresholdSetting(kind);
            return true;
        }

        var factor = DisplayFactor(specification);
        if (Math.Abs(display) > MaximumDisplayValue || (specification.Calibration && display != decimal.Truncate(display)))
        {
            return false;
        }

        // A value finer than the stored unit does not convert exactly: refused, never rounded.
        var scaled = display * factor;
        if (scaled != decimal.Truncate(scaled))
        {
            return false;
        }

        stored = new ThresholdSetting(kind, (long)scaled);
        return true;
    }

    private const decimal MaximumDisplayValue = 1_000_000_000_000m;

    // How many stored units make one display unit: a calibrating Sensor is percent already.
    private static decimal DisplayFactor(SensorSpecification specification) =>
        specification.Calibration ? 1m : specification.Unit switch
        {
            SensorUnit.MilliDegreeCelsius or SensorUnit.MilliPercent or SensorUnit.Ohm => 1000m,
            _ => 1m,
        };

    private static string DisplayUnit(SensorSpecification specification) =>
        specification.Calibration ? "%" : specification.Unit switch
        {
            SensorUnit.MilliDegreeCelsius => "°C",
            SensorUnit.MilliPercent => "%",
            SensorUnit.Ohm => "kΩ",
            _ => "raw",
        };

    private static ThresholdsResponse ToThresholdsResponse(SensorThresholds thresholds)
    {
        var factor = DisplayFactor(thresholds.Specification);

        // Dividing by a decimal with many places drops trailing zeros: 25 and not 25.000.
        decimal? Shown(long? stored) => stored is { } value ? value / factor / 1.0000000000000000000000000000m : null;

        return new ThresholdsResponse(
            DisplayUnit(thresholds.Specification),
            new ThresholdSideResponse(KindName(thresholds.Low.Kind), Shown(thresholds.Low.Value)),
            new ThresholdSideResponse(KindName(thresholds.High.Kind), Shown(thresholds.High.Value)),
            Shown(thresholds.ProposedLow));
    }

    private static string KindName(ThresholdKind kind) => kind switch
    {
        ThresholdKind.Default => "default",
        ThresholdKind.Override => "override",
        _ => "cleared",
    };

    private static readonly TimeSpan CalibrationReadingWindow = TimeSpan.FromDays(2);

    private const int CalibrationReadingLimit = 20;

    // The Sensor must be a Sensor of a Node of this Site: another Site's Sensor is not disclosed.
    private static async Task<(Guid Id, ISensorGrain Grain, SensorSnapshot Snapshot)?> FindSiteSensorAsync(
        string siteId,
        string sensorId,
        IGrainFactory grains,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(sensorId, out var id))
        {
            return null;
        }

        var canonical = SiteAccessHandler.Canonicalize(siteId)!;
        var grain = grains.GetGrain<ISensorGrain>(id.ToString("D"));
        var snapshot = await grain.Describe(cancellationToken).ConfigureAwait(false);
        var device = snapshot is null
            ? null
            : await grains.GetGrain<IDeviceGrain>(snapshot.DeviceId).Describe(cancellationToken).ConfigureAwait(false);

        return snapshot is not null && device is { Kind: DeviceKind.Node } node && string.Equals(node.SiteId, canonical, StringComparison.Ordinal)
            ? (id, grain, snapshot)
            : null;
    }

    /// <summary>
    /// Maps the Sensor grain's answer to a Calibration to the HTTP response: 200 with where the Calibration
    /// stands, 400 <c>validation</c> for a Sensor that cannot be calibrated, a Reading it has not stored, or
    /// indistinct points, or 503 <c>calibration-not-delivered</c> while the Device has not acknowledged it.
    /// </summary>
    internal static IResult ToHttpResult(SensorCalibrationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Outcome switch
        {
            SensorCalibrationOutcome.Calibrated or SensorCalibrationOutcome.PointRecorded => TypedResults.Ok(ToCalibrationResponse(result)),
            SensorCalibrationOutcome.NotCalibratable => EdgeProblems.Result(
                StatusCodes.Status400BadRequest,
                EdgeProblems.Validation,
                "The Sensor cannot be calibrated.",
                "Only a Sensor whose Specification calls for Calibration, such as soil moisture, is calibrated. Nothing changed."),
            SensorCalibrationOutcome.NoPoint => EdgeProblems.Result(
                StatusCodes.Status400BadRequest,
                EdgeProblems.Validation,
                "The Calibration request names no point.",
                "Send dry and/or wet. Nothing changed."),
            SensorCalibrationOutcome.UnknownReading => EdgeProblems.Result(
                StatusCodes.Status400BadRequest,
                EdgeProblems.Validation,
                "The Sensor has no such Reading.",
                "Name the reading_seq of a Reading the Server stored for this Sensor. Nothing changed."),
            SensorCalibrationOutcome.IndistinctPoints => EdgeProblems.Result(
                StatusCodes.Status400BadRequest,
                EdgeProblems.Validation,
                "The dry and wet points are too close to tell apart.",
                $"Choose Readings whose raw values are at least {SensorCalibrationLimits.MinimumSpan} apart. Nothing changed."),
            SensorCalibrationOutcome.NotDelivered => EdgeProblems.Result(
                StatusCodes.Status503ServiceUnavailable,
                EdgeProblems.CalibrationNotDelivered,
                "The Calibration is saved but not in force yet.",
                "The Server keeps trying. Send the same request again to see whether it is in force."),
            _ => throw new InvalidOperationException($"Unexpected Calibration outcome {result.Outcome}."),
        };
    }

    private static CalibrationResponse ToCalibrationResponse(SensorCalibrationResult result) =>
        new(
            result.Calibration is not null,
            result.Calibration?.Id.ToString("D"),
            result.Calibration is { } calibration ? new CalibrationPointResponse(calibration.DryRaw) : null,
            result.Calibration is { } current ? new CalibrationPointResponse(current.WetRaw) : null,
            result.PendingDryRaw is { } pendingDry ? new CalibrationPointResponse(pendingDry) : null,
            result.PendingWetRaw is { } pendingWet ? new CalibrationPointResponse(pendingWet) : null);

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
    /// Maps a Lot of the read model to the response: every time in UTC, the optional fields only when set, and
    /// <c>removed</c> only when true.
    /// </summary>
    internal static LotResponse ToLotResponse(LotView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new LotResponse(
            view.LotId,
            view.Name,
            view.Status,
            ToServerTime(view.StatusSince),
            view.LastReadingAt is { } lastReadingAt ? ToServerTime(lastReadingAt) : null,
            view.UnknownCause,
            view.PausedBy is { Count: > 0 } ? view.PausedBy : null,
            view.PausedUntil is { } pausedUntil ? ToServerTime(pausedUntil) : null,
            view.Removed ? true : null);
    }

    private static string ToServerTime(DateTimeOffset time) =>
        time.UtcDateTime.ToString(ServerTimeFormat, CultureInfo.InvariantCulture);

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

    private static IResult DeviceNotFound() =>
        EdgeProblems.Result(StatusCodes.Status404NotFound, EdgeProblems.DeviceNotFound, "The Site has no such Node.");

    private static IResult SensorNotFound() =>
        EdgeProblems.Result(StatusCodes.Status404NotFound, EdgeProblems.SensorNotFound, "The Site has no such Sensor.");

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
