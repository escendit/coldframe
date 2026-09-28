using System.Net;
using Coldframe.Contracts.Sites;
using Coldframe.Server.Tests.Edge;

namespace Coldframe.Server.IntegrationTests.Edge;

/// <summary>
/// The generated authorization matrix (AD-4, AD-24): every Edge API endpoint × every Role × own Site and
/// other Site, with the expected outcome derived only from the endpoint's declared access rule. A new
/// endpoint joins the matrix on its own and fails it until it has a sample request below.
/// </summary>
/// <remarks>
/// Only Owners can be created through the API before Epic 9, so Site A (Owner, Administrator, Member) and
/// Site B (another Owner) are seeded as <c>site.*</c> events on fresh streams of the Server's journal.
/// The projection, and with it the policy, sees exactly what real events produce. The seeded Sites have
/// no Organization, which is harmless: the policy reads only the projection.
/// </remarks>
public sealed class AuthorizationMatrixTests(EdgeApiFixture edge) : IClassFixture<EdgeApiFixture>
{
    private static readonly SiteRole[] Roles = [SiteRole.Owner, SiteRole.Administrator, SiteRole.Member];

    /// <summary>
    /// One request per endpoint, keyed like <see cref="EdgeOperation.Key"/>: the request for a Site, and the
    /// status it answers when the caller is allowed.
    /// </summary>
    private static readonly Dictionary<string, Sample> Samples = new(StringComparer.Ordinal)
    {
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
    };

    [Fact]
    public void EveryEndpointHasASampleRequest()
    {
        var missing = EdgeEndpointCatalog.Describe()
            .Select(operation => operation.Key)
            .Where(key => !Samples.ContainsKey(key))
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
            users[role] = await edge.CreateUserAsync(role.ToString().ToLowerInvariant(), cancellationToken);
        }

        var otherOwner = await edge.CreateUserAsync("other-owner", cancellationToken);

        var siteA = Guid.CreateVersion7().ToString();
        var siteB = Guid.CreateVersion7().ToString();

        await edge.AppendAsync(
            $"site/{siteA}",
            [
                new SiteCreated("Matrix A", users[SiteRole.Owner].UserId),
                .. Roles.Select(role => new MembershipGranted(users[role].UserId, role)),
            ],
            cancellationToken);
        var last = await edge.AppendAsync(
            $"site/{siteB}",
            [new SiteCreated("Matrix B", otherOwner.UserId), new MembershipGranted(otherOwner.UserId, SiteRole.Owner)],
            cancellationToken);

        await edge.WaitForIdentityCheckpointAsync(last);

        var failures = new List<string>();

        foreach (var operation in operations)
        {
            var sample = Samples[operation.Key];
            var minimum = operation.Rule.MinimumRole;

            foreach (var role in Roles)
            {
                using var server = edge.CreateServerClient(users[role].AccessToken);

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
        using var server = edge.CreateServerClient();

        foreach (var operation in EdgeEndpointCatalog.Describe())
        {
            using var response = await Samples[operation.Key].Send(server, Guid.CreateVersion7().ToString(), cancellationToken);

            await EdgeApiTests.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "urn:coldframe:problem:unauthorized", cancellationToken);
        }
    }

    /// <param name="Send">Calls the endpoint for a Site.</param>
    /// <param name="Allowed">The status the endpoint answers an allowed caller.</param>
    /// <param name="CheckAllowed">Checks an allowed answer against the caller's seeded Role; returns what is wrong, or null.</param>
    private sealed record Sample(
        Func<HttpClient, string, CancellationToken, Task<HttpResponseMessage>> Send,
        HttpStatusCode Allowed,
        Func<HttpResponseMessage, SiteRole, CancellationToken, Task<string?>>? CheckAllowed = null);
}
