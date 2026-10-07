using Microsoft.AspNetCore.Mvc;

namespace Coldframe.Server.Edge;

/// <summary>
/// The RFC 9457 Problem Details the Edge API answers with. Each <c>type</c> is stable.
/// </summary>
public static class EdgeProblems
{
    /// <summary>
    /// The media type of every error response.
    /// </summary>
    public const string ContentType = "application/problem+json";

    /// <summary>
    /// 401: no valid access token.
    /// </summary>
    public const string Unauthorized = "urn:coldframe:problem:unauthorized";

    /// <summary>
    /// 403: the caller's Role on the Site is missing or too low.
    /// </summary>
    public const string Forbidden = "urn:coldframe:problem:forbidden";

    /// <summary>
    /// 404: the Site does not exist.
    /// </summary>
    public const string SiteNotFound = "urn:coldframe:problem:site-not-found";

    /// <summary>
    /// 404: the Site has no such Lot (or, for a rename, the Lot was removed).
    /// </summary>
    public const string LotNotFound = "urn:coldframe:problem:lot-not-found";

    /// <summary>
    /// 404: the Site has no such Node (unknown, a Hub, or of another Site).
    /// </summary>
    public const string DeviceNotFound = "urn:coldframe:problem:device-not-found";

    /// <summary>
    /// 404: the Site has no such Sensor (unknown, never declared, or of another Site).
    /// </summary>
    public const string SensorNotFound = "urn:coldframe:problem:sensor-not-found";

    /// <summary>
    /// 409: a Node is assigned to the Lot, so it cannot be removed or take another Node.
    /// </summary>
    public const string LotClaimed = "urn:coldframe:problem:lot-claimed";

    /// <summary>
    /// 409: the Node is assigned to another Lot already; enrolment does not move it.
    /// </summary>
    public const string DeviceAssigned = "urn:coldframe:problem:device-assigned";

    /// <summary>
    /// 409: the Device is already enrolled on another Site.
    /// </summary>
    public const string DeviceOnAnotherSite = "urn:coldframe:problem:device-on-another-site";

    /// <summary>
    /// 401: the Device authentication failed (unknown Device, missing or malformed headers, a bad signature,
    /// skew, a replayed nonce or an old timestamp). The reason is never told.
    /// </summary>
    public const string DeviceUnauthorized = "urn:coldframe:problem:device-unauthorized";

    /// <summary>
    /// 400: the request is malformed.
    /// </summary>
    public const string Validation = "urn:coldframe:problem:validation";

    /// <summary>
    /// 400: a creating request has no <c>Idempotency-Key</c>.
    /// </summary>
    public const string IdempotencyKeyMissing = "urn:coldframe:problem:idempotency-key-missing";

    /// <summary>
    /// 422: the <c>Idempotency-Key</c> was used within 24 h for a different request.
    /// </summary>
    public const string IdempotencyKeyReused = "urn:coldframe:problem:idempotency-key-reused";

    /// <summary>
    /// 503: Keycloak could not be reached.
    /// </summary>
    public const string IdentityProviderUnavailable = "urn:coldframe:problem:identity-provider-unavailable";

    /// <summary>
    /// 503: the ingest envelope parsed, but no frame of it could be committed.
    /// </summary>
    public const string IngestUnavailable = "urn:coldframe:problem:ingest-unavailable";

    /// <summary>
    /// 503: the Calibration is saved, but the Device grain did not acknowledge it yet; it is delivered again.
    /// </summary>
    public const string CalibrationNotDelivered = "urn:coldframe:problem:calibration-not-delivered";

    /// <summary>
    /// Creates the Problem Details of a type.
    /// </summary>
    public static ProblemDetails Create(int status, string type, string title, string? detail = null) =>
        new() { Status = status, Type = type, Title = title, Detail = detail };

    /// <summary>
    /// A result that writes the Problem Details of a type.
    /// </summary>
    public static IResult Result(int status, string type, string title, string? detail = null) =>
        TypedResults.Problem(Create(status, type, title, detail));

    /// <summary>
    /// Writes the Problem Details of a type straight to the response, whatever the request accepts.
    /// </summary>
    public static Task WriteAsync(HttpContext httpContext, int status, string type, string title, string? detail = null)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        httpContext.Response.StatusCode = status;
        return httpContext.Response.WriteAsJsonAsync(
            Create(status, type, title, detail),
            options: null,
            contentType: ContentType,
            cancellationToken: httpContext.RequestAborted);
    }
}
