using System.Net;
using System.Net.Http.Json;
using Coldframe.Contracts.Sites;
using Coldframe.Server.IntegrationTests.Devices;
using Coldframe.Server.Tests.Edge;

namespace Coldframe.Server.IntegrationTests.Edge;

/// <summary>
/// The generated authorization matrix (AD-4, AD-24): every Edge API endpoint × every Role × own Site and
/// other Site, with the expected outcome derived only from the endpoint's declared access rule. A new
/// endpoint joins the matrix on its own and fails it until it has a sample request below. A <c>Device</c>
/// endpoint takes no user token at all: without the Device headers every caller, whatever its Role, gets
/// 401 <c>device-unauthorized</c>.
/// </summary>
/// <remarks>
/// Only Owners can be created through the API before Epic 9, so Site A (Owner, Administrator, Member) and
/// Site B (another Owner) are seeded as <c>site.*</c> events on fresh streams of the Server's journal, and
/// the Lot endpoints get a fresh <c>lot/{id}</c> stream of the targeted Site per call.
/// The projection, and with it the policy, sees exactly what real events produce. The seeded Sites have
/// no Organization, which is harmless: the policy reads only the projection.
/// </remarks>
public sealed class AuthorizationMatrixTests : IClassFixture<EdgeApiFixture>
{
    private const string SiteAName = "Matrix A";

    private const string DeviceUnauthorized = "urn:coldframe:problem:device-unauthorized";

    private static readonly SiteRole[] Roles = [SiteRole.Owner, SiteRole.Administrator, SiteRole.Member];

    private readonly EdgeApiFixture _edge;

    /// <summary>
    /// One request per endpoint, keyed like <see cref="EdgeOperation.Key"/>: the request for a Site, and the
    /// status it answers when the caller is allowed.
    /// </summary>
    private readonly Dictionary<string, Sample> _samples = new(StringComparer.Ordinal)
    {
        ["GET /enrolment-key"] = new(
            (server, _, cancellationToken) => server.GetAsync(new Uri("/enrolment-key", UriKind.Relative), cancellationToken),
            HttpStatusCode.OK),
        ["GET /sites"] = new(
            (server, _, cancellationToken) => server.GetAsync(new Uri("/sites", UriKind.Relative), cancellationToken),
            HttpStatusCode.OK),
        ["POST /sites"] = new(
            (server, _, cancellationToken) => EdgeApiTests.PostSiteAsync(server, Guid.NewGuid().ToString(), new { name = "Matrix" }, cancellationToken),
            HttpStatusCode.Created),
        ["GET /sites/{siteId}"] = new(
            (server, siteId, cancellationToken) => server.GetAsync(new Uri($"/sites/{siteId}", UriKind.Relative), cancellationToken),
            HttpStatusCode.OK,
            async (response, role, cancellationToken) =>
            {
                using var body = await EdgeApiFixture.ReadJsonAsync(response, cancellationToken);
                var actual = body.RootElement.GetProperty("role").GetString();
                return actual == role.ToString() ? null : $"role {actual}, expected {role}";
            }),

        // Site A's current name: the matrix Sites have no Organization, so a real rename would need
        // Keycloak. The same name tests the access rule alone; RenameSiteTests renames for real.
        ["PATCH /sites/{siteId}"] = new(
            (server, siteId, cancellationToken) => LotsTests.PatchNameAsync(server, $"/sites/{siteId}", SiteAName, cancellationToken),
            HttpStatusCode.OK),
        ["GET /sites/{siteId}/devices"] = new(
            (server, siteId, cancellationToken) => server.GetAsync(new Uri($"/sites/{siteId}/devices", UriKind.Relative), cancellationToken),
            HttpStatusCode.OK),
        ["GET /sites/{siteId}/alerts"] = new(
            (server, siteId, cancellationToken) => server.GetAsync(new Uri($"/sites/{siteId}/alerts", UriKind.Relative), cancellationToken),
            HttpStatusCode.OK),

        // Notification settings (Story 6.3). The matrix Users' grains hold no Membership of the matrix Sites, so
        // the per-Site settings must not ask the User grain for one: the policy is the only gate.
        ["GET /me/notification-settings"] = new(
            (server, _, cancellationToken) => server.GetAsync(new Uri("/me/notification-settings", UriKind.Relative), cancellationToken),
            HttpStatusCode.OK),
        ["PATCH /me/notification-settings"] = new(
            (server, _, cancellationToken) =>
                server.PatchAsJsonAsync(new Uri("/me/notification-settings", UriKind.Relative), new { window = new { from = "07:00" } }, cancellationToken),
            HttpStatusCode.OK),

        // Push registrations (Story 6.5): the caller's own, whatever Site the matrix names.
        ["PUT /me/push-registrations/{installationId}"] = new(
            (server, _, cancellationToken) =>
                server.PutAsJsonAsync(
                    new Uri("/me/push-registrations/matrix-installation", UriKind.Relative),
                    new { platform = "fcm", token = "matrix-token" },
                    cancellationToken),
            HttpStatusCode.NoContent),
        ["DELETE /me/push-registrations/{installationId}"] = new(
            (server, _, cancellationToken) => server.DeleteAsync(new Uri("/me/push-registrations/matrix-installation", UriKind.Relative), cancellationToken),
            HttpStatusCode.NoContent),
        ["GET /sites/{siteId}/notification-settings"] = new(
            (server, siteId, cancellationToken) => server.GetAsync(new Uri($"/sites/{siteId}/notification-settings", UriKind.Relative), cancellationToken),
            HttpStatusCode.OK),
        ["PUT /sites/{siteId}/notification-settings"] = new(
            (server, siteId, cancellationToken) =>
                server.PutAsJsonAsync(new Uri($"/sites/{siteId}/notification-settings", UriKind.Relative), new { muted = true }, cancellationToken),
            HttpStatusCode.OK),
        ["GET /sites/{siteId}/reminder-cadence"] = new(
            (server, siteId, cancellationToken) => server.GetAsync(new Uri($"/sites/{siteId}/reminder-cadence", UriKind.Relative), cancellationToken),
            HttpStatusCode.OK),
        ["PUT /sites/{siteId}/reminder-cadence"] = new(
            (server, siteId, cancellationToken) =>
                server.PutAsJsonAsync(new Uri($"/sites/{siteId}/reminder-cadence", UriKind.Relative), new { cadence = "every2Days" }, cancellationToken),
            HttpStatusCode.OK),
        ["GET /sites/{siteId}/lots"] = new(
            (server, siteId, cancellationToken) => server.GetAsync(new Uri($"/sites/{siteId}/lots", UriKind.Relative), cancellationToken),
            HttpStatusCode.OK),
        ["POST /sites/{siteId}/lots"] = new(
            (server, siteId, cancellationToken) => LotsTests.PostLotAsync(server, siteId, Guid.NewGuid().ToString(), new { name = "Matrix Lot" }, cancellationToken),
            HttpStatusCode.Created),
    };

    public AuthorizationMatrixTests(EdgeApiFixture edge)
    {
        _edge = edge;

        // Without Device headers: what every caller of a Device endpoint gets, token or not.
        _samples["POST /device/heartbeat"] = new(
            (server, _, cancellationToken) => HeartbeatTests.PostUnsignedAsync(server, cancellationToken),
            HttpStatusCode.Unauthorized);
        _samples["POST /device/ingest"] = new(
            (server, _, cancellationToken) => IngestTests.PostUnsignedAsync(server, cancellationToken),
            HttpStatusCode.Unauthorized);

        // Each call enrols a fresh simulated Device, sealed to the Server's key, so no call meets an enrolled one.
        _samples["POST /sites/{siteId}/devices"] = new(
            (server, siteId, cancellationToken) => EnrolmentTests.PostFreshDeviceAsync(_edge, server, siteId, cancellationToken),
            HttpStatusCode.Created);

        // A Device no Site has enrolled: an allowed caller passes the access rule and meets 404 device-not-found,
        // which tells it apart from the 403 of a caller below Administrator and the 404 of a missing Site.
        _samples["POST /sites/{siteId}/devices/{deviceId}/move"] = new(
            async (server, siteId, cancellationToken) =>
                await server.PostAsJsonAsync(
                    new Uri($"/sites/{siteId}/devices/{UnknownNodeId()}/move", UriKind.Relative),
                    new { lotId = await SeedLotAsync(siteId, cancellationToken) },
                    cancellationToken),
            HttpStatusCode.NotFound,
            DeviceNotFoundAsync);
        _samples["POST /sites/{siteId}/devices/{deviceId}/unassign"] = new(
            (server, siteId, cancellationToken) =>
                server.PostAsync(new Uri($"/sites/{siteId}/devices/{UnknownNodeId()}/unassign", UriKind.Relative), content: null, cancellationToken),
            HttpStatusCode.NotFound,
            DeviceNotFoundAsync);

        // A Sensor no Site has declared: an allowed caller passes the access rule and meets 404 sensor-not-found,
        // which tells it apart from the 403 of a caller below Administrator and the 404 of a missing Site.
        _samples["POST /sites/{siteId}/sensors/{sensorId}/calibration"] = new(
            async (server, siteId, cancellationToken) =>
                await server.PostAsJsonAsync(
                    new Uri($"/sites/{siteId}/sensors/{Guid.NewGuid()}/calibration", UriKind.Relative),
                    new { dry = new { readingSeq = 1 } },
                    cancellationToken),
            HttpStatusCode.NotFound,
            SensorNotFoundAsync);
        _samples["GET /sites/{siteId}/sensors/{sensorId}/calibration"] = new(
            (server, siteId, cancellationToken) =>
                server.GetAsync(new Uri($"/sites/{siteId}/sensors/{Guid.NewGuid()}/calibration", UriKind.Relative), cancellationToken),
            HttpStatusCode.NotFound,
            SensorNotFoundAsync);

        // Thresholds (Story 5.3): a Member reads (404 here, the Sensor is unknown) but its PUT is a 403.
        _samples["GET /sites/{siteId}/sensors/{sensorId}/thresholds"] = new(
            (server, siteId, cancellationToken) =>
                server.GetAsync(new Uri($"/sites/{siteId}/sensors/{Guid.NewGuid()}/thresholds", UriKind.Relative), cancellationToken),
            HttpStatusCode.NotFound,
            SensorNotFoundAsync);
        _samples["PUT /sites/{siteId}/sensors/{sensorId}/thresholds"] = new(
            async (server, siteId, cancellationToken) =>
                await server.PutAsJsonAsync(
                    new Uri($"/sites/{siteId}/sensors/{Guid.NewGuid()}/thresholds", UriKind.Relative),
                    new { low = new { kind = "override", value = 25 } },
                    cancellationToken),
            HttpStatusCode.NotFound,
            SensorNotFoundAsync);

        // Each call gets a fresh Lot of the Site it targets, so a removal never meets a removed Lot.
        _samples["GET /sites/{siteId}/lots/{lotId}"] = new(
            async (server, siteId, cancellationToken) =>
                await server.GetAsync(new Uri($"/sites/{siteId}/lots/{await SeedLotAsync(siteId, cancellationToken)}", UriKind.Relative), cancellationToken),
            HttpStatusCode.OK);
        _samples["GET /sites/{siteId}/lots/{lotId}/history"] = new(
            async (server, siteId, cancellationToken) =>
                await server.GetAsync(new Uri($"/sites/{siteId}/lots/{await SeedLotAsync(siteId, cancellationToken)}/history?quantity=soil_moisture", UriKind.Relative), cancellationToken),
            HttpStatusCode.OK);
        _samples["PATCH /sites/{siteId}/lots/{lotId}"] = new(
            async (server, siteId, cancellationToken) =>
                await LotsTests.PatchNameAsync(server, $"/sites/{siteId}/lots/{await SeedLotAsync(siteId, cancellationToken)}", "Matrix renamed", cancellationToken),
            HttpStatusCode.OK);
        _samples["DELETE /sites/{siteId}/lots/{lotId}"] = new(
            async (server, siteId, cancellationToken) =>
                await server.DeleteAsync(new Uri($"/sites/{siteId}/lots/{await SeedLotAsync(siteId, cancellationToken)}", UriKind.Relative), cancellationToken),
            HttpStatusCode.NoContent);
    }

    [Fact]
    public void EveryEndpointHasASampleRequest()
    {
        var missing = EdgeEndpointCatalog.Describe()
            .Select(operation => operation.Key)
            .Where(key => !_samples.ContainsKey(key))
            .ToList();

        Assert.True(missing.Count == 0, $"The authorization matrix has no sample request for: {string.Join(", ", missing)}.");
    }

    [Fact]
    public async Task EveryEndpointRoleAndSiteMatchesTheDeclaredMinimum()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var operations = EdgeEndpointCatalog.Describe();

        var users = new Dictionary<SiteRole, TestUser>();
        foreach (var role in Roles)
        {
            users[role] = await _edge.CreateUserAsync(role.ToString().ToLowerInvariant(), cancellationToken);
        }

        var otherOwner = await _edge.CreateUserAsync("other-owner", cancellationToken);

        var siteA = Guid.CreateVersion7().ToString();
        var siteB = Guid.CreateVersion7().ToString();

        await _edge.AppendAsync(
            $"site/{siteA}",
            [
                new SiteCreated(SiteAName, users[SiteRole.Owner].UserId),
                .. Roles.Select(role => new MembershipGranted(users[role].UserId, role)),
            ],
            cancellationToken);
        var last = await _edge.AppendAsync(
            $"site/{siteB}",
            [new SiteCreated("Matrix B", otherOwner.UserId), new MembershipGranted(otherOwner.UserId, SiteRole.Owner)],
            cancellationToken);

        await _edge.WaitForIdentityCheckpointAsync(last);

        var failures = new List<string>();

        foreach (var operation in operations)
        {
            var sample = _samples[operation.Key];
            var minimum = operation.Rule.MinimumRole;

            if (operation.Rule.IsDevice)
            {
                // A user token is no Device authentication, whatever the Role: the Device branch.
                foreach (var role in Roles)
                {
                    using var server = _edge.CreateServerClient(users[role].AccessToken);
                    using var response = await sample.Send(server, siteA, cancellationToken);
                    await EdgeApiTests.AssertProblemAsync(response, HttpStatusCode.Unauthorized, DeviceUnauthorized, cancellationToken);
                }

                continue;
            }

            foreach (var role in Roles)
            {
                using var server = _edge.CreateServerClient(users[role].AccessToken);

                foreach (var (target, own) in new[] { (siteA, true), (siteB, false) })
                {
                    var allowed = minimum is not { } required || (own && role >= required);
                    var expected = allowed ? sample.Allowed : HttpStatusCode.Forbidden;

                    using var response = await sample.Send(server, target, cancellationToken);

                    if (response.StatusCode != expected)
                    {
                        failures.Add(
                            $"{operation.Key} ({operation.Rule}) as {role} on {(own ? "own" : "other")} Site: " +
                            $"expected {(int)expected}, got {(int)response.StatusCode}");
                    }
                    else if (allowed && sample.CheckAllowed is { } check
                        && await check(response, role, cancellationToken) is { } mismatch)
                    {
                        failures.Add($"{operation.Key} as {role} on {(own ? "own" : "other")} Site: {mismatch}");
                    }
                    else if (!allowed)
                    {
                        await EdgeApiTests.AssertProblemAsync(response, HttpStatusCode.Forbidden, "urn:coldframe:problem:forbidden", cancellationToken);
                    }
                }

                // A Site that does not exist is 404 on every Site-scoped endpoint, whatever the caller's Roles.
                if (minimum is not null)
                {
                    using var missing = await sample.Send(server, Guid.CreateVersion7().ToString(), cancellationToken);
                    if (missing.StatusCode != HttpStatusCode.NotFound)
                    {
                        failures.Add($"{operation.Key} as {role} on a missing Site: expected 404, got {(int)missing.StatusCode}");
                    }
                    else
                    {
                        await EdgeApiTests.AssertProblemAsync(missing, HttpStatusCode.NotFound, "urn:coldframe:problem:site-not-found", cancellationToken);
                    }
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public async Task EveryEndpointRefusesACallerWithoutAToken()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var server = _edge.CreateServerClient();

        foreach (var operation in EdgeEndpointCatalog.Describe())
        {
            using var response = await _samples[operation.Key].Send(server, Guid.CreateVersion7().ToString(), cancellationToken);

            // A Device endpoint answers a caller without Device headers in its own terms.
            var type = operation.Rule.IsDevice ? DeviceUnauthorized : "urn:coldframe:problem:unauthorized";
            await EdgeApiTests.AssertProblemAsync(response, HttpStatusCode.Unauthorized, type, cancellationToken);
        }
    }

    private static string UnknownNodeId() => Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8));

    private static async Task<string?> DeviceNotFoundAsync(HttpResponseMessage response, SiteRole role, CancellationToken cancellationToken)
    {
        using var body = await EdgeApiFixture.ReadJsonAsync(response, cancellationToken);
        var type = body.RootElement.GetProperty("type").GetString();
        return type == "urn:coldframe:problem:device-not-found" ? null : $"problem {type}, expected device-not-found";
    }

    private static async Task<string?> SensorNotFoundAsync(HttpResponseMessage response, SiteRole role, CancellationToken cancellationToken)
    {
        using var body = await EdgeApiFixture.ReadJsonAsync(response, cancellationToken);
        var type = body.RootElement.GetProperty("type").GetString();
        return type == "urn:coldframe:problem:sensor-not-found" ? null : $"problem {type}, expected sensor-not-found";
    }

    private Task<string> SeedLotAsync(string siteId, CancellationToken cancellationToken) =>
        _edge.SeedLotAsync(siteId, "Matrix Lot", cancellationToken);

    /// <param name="Send">Calls the endpoint for a Site.</param>
    /// <param name="Allowed">The status the endpoint answers an allowed caller.</param>
    /// <param name="CheckAllowed">Checks an allowed answer against the caller's seeded Role; returns what is wrong, or null.</param>
    private sealed record Sample(
        Func<HttpClient, string, CancellationToken, Task<HttpResponseMessage>> Send,
        HttpStatusCode Allowed,
        Func<HttpResponseMessage, SiteRole, CancellationToken, Task<string?>>? CheckAllowed = null);
}
