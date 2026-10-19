using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coldframe.Contracts.Sites;

namespace Coldframe.Server.IntegrationTests.Edge;

/// <summary>
/// Story 9.2 on the AppHost with Keycloak and Phase Two: an Owner changes a Role and removes a Membership through
/// <c>PUT</c> and <c>DELETE /sites/{siteId}/members/{userId}</c>. Phase Two holds the change, the person's next
/// request is authorized with the new Role or rejected, without waiting for the token to expire, and a removed
/// person's User grain drops the Site.
/// </summary>
public sealed class MembershipChangeTests(EdgeApiFixture edge) : IClassFixture<EdgeApiFixture>
{
    // Membership events arrive asynchronously (AD-3): Keycloak → Temporal → the Server's worker.
    private static readonly TimeSpan ArrivalTimeout = TimeSpan.FromSeconds(90);

    [Fact]
    public async Task AnOwnerChangesARoleAndRemovesAMemberAndTheNextRequestFollowsAtOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var owner = await edge.CreateUserAsync("changer", cancellationToken);
        var neighbour = await edge.CreateUserAsync("changed", cancellationToken);
        using var server = edge.CreateServerClient(owner.AccessToken);
        using var asNeighbour = edge.CreateServerClient(neighbour.AccessToken);
        var siteId = await CreateSiteAsync(server, cancellationToken);
        await JoinAsync(server, siteId, neighbour, cancellationToken);

        // Raised to Administrator: Phase Two holds administrator alone, and the very next request is an Administrator's.
        using (var changed = await ChangeRoleAsync(server, siteId, neighbour.UserId, "Administrator", cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
            Assert.Empty(await changed.Content.ReadAsByteArrayAsync(cancellationToken));
        }

        // The Edge synced the User grain before it answered: no waiting for the echo reconciliation.
        await AssertUserGrainSyncedAsync(neighbour.UserId, siteId, SiteRole.Administrator, cancellationToken);

        Assert.Equal("Administrator", await RoleOnSiteAsync(asNeighbour, siteId, cancellationToken));
        Assert.Equal(["administrator"], await OrganizationRolesAsync(siteId, neighbour.UserId, cancellationToken));
        await WaitForUserGrainRoleAsync(neighbour.UserId, siteId, SiteRole.Administrator, cancellationToken);

        // The same Role again saves nothing.
        var aliases = await edge.AliasesAsync($"site/{siteId}", cancellationToken);
        using (var same = await ChangeRoleAsync(server, siteId, neighbour.UserId, "Administrator", cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, same.StatusCode);
        }

        Assert.Equal(aliases, await edge.AliasesAsync($"site/{siteId}", cancellationToken));

        // Lowered to Member: an Administrator's change is refused on the next request, with the same token.
        using (var lowered = await ChangeRoleAsync(server, siteId, neighbour.UserId, "Member", cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, lowered.StatusCode);
        }

        Assert.Equal("Member", await RoleOnSiteAsync(asNeighbour, siteId, cancellationToken));
        using (var refused = await asNeighbour.PutAsJsonAsync(new Uri($"/sites/{siteId}/pause", UriKind.Relative), new { }, cancellationToken))
        {
            await EdgeApiTests.AssertProblemAsync(refused, HttpStatusCode.Forbidden, "urn:coldframe:problem:forbidden", cancellationToken);
        }

        // A Member changes no Role.
        using (var notOwner = await ChangeRoleAsync(asNeighbour, siteId, owner.UserId, "Member", cancellationToken))
        {
            await EdgeApiTests.AssertProblemAsync(notOwner, HttpStatusCode.Forbidden, "urn:coldframe:problem:forbidden", cancellationToken);
        }

        // Removed: Phase Two no longer lists them, the next request is 403, and their User grain drops the Site.
        using (var removed = await server.DeleteAsync(new Uri($"/sites/{siteId}/members/{neighbour.UserId}", UriKind.Relative), cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        }

        await AssertUserGrainSyncedAsync(neighbour.UserId, siteId, null, cancellationToken);

        using (var gone = await asNeighbour.GetAsync(new Uri($"/sites/{siteId}", UriKind.Relative), cancellationToken))
        {
            await EdgeApiTests.AssertProblemAsync(gone, HttpStatusCode.Forbidden, "urn:coldframe:problem:forbidden", cancellationToken);
        }

        Assert.Empty(await OrganizationRolesAsync(siteId, neighbour.UserId, cancellationToken));
        Assert.DoesNotContain(neighbour.UserId, await OrganizationMembersAsync(siteId, cancellationToken));
        await WaitForUserGrainRoleAsync(neighbour.UserId, siteId, null, cancellationToken);

        using (var sites = await asNeighbour.GetAsync(new Uri("/sites", UriKind.Relative), cancellationToken))
        using (var body = await EdgeApiFixture.ReadJsonAsync(sites, cancellationToken))
        {
            Assert.DoesNotContain(body.RootElement.GetProperty("sites").EnumerateArray(), site => site.GetProperty("id").GetString() == siteId);
        }

        // No longer a member: 404 member-not-found, for a change as for a removal.
        using (var notMember = await ChangeRoleAsync(server, siteId, neighbour.UserId, "Owner", cancellationToken))
        {
            await EdgeApiTests.AssertProblemAsync(notMember, HttpStatusCode.NotFound, "urn:coldframe:problem:member-not-found", cancellationToken);
        }

        using (var notMember = await server.DeleteAsync(new Uri($"/sites/{siteId}/members/{neighbour.UserId}", UriKind.Relative), cancellationToken))
        {
            await EdgeApiTests.AssertProblemAsync(notMember, HttpStatusCode.NotFound, "urn:coldframe:problem:member-not-found", cancellationToken);
        }
    }

    [Fact]
    public async Task TheLastOwnerIsNeitherLoweredNorRemovedAndABadRoleIsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var owner = await edge.CreateUserAsync("last-owner", cancellationToken);
        using var server = edge.CreateServerClient(owner.AccessToken);
        var siteId = await CreateSiteAsync(server, cancellationToken);
        var aliases = await edge.AliasesAsync($"site/{siteId}", cancellationToken);

        using (var lowered = await ChangeRoleAsync(server, siteId, owner.UserId, "Member", cancellationToken))
        {
            await EdgeApiTests.AssertProblemAsync(lowered, HttpStatusCode.Conflict, "urn:coldframe:problem:last-owner", cancellationToken);
        }

        using (var removed = await server.DeleteAsync(new Uri($"/sites/{siteId}/members/{owner.UserId}", UriKind.Relative), cancellationToken))
        {
            await EdgeApiTests.AssertProblemAsync(removed, HttpStatusCode.Conflict, "urn:coldframe:problem:last-owner", cancellationToken);
        }

        foreach (var body in new object[] { new { role = "Gardener" }, new { role = "owner" }, new { } })
        {
            using var refused = await server.PutAsJsonAsync(new Uri($"/sites/{siteId}/members/{owner.UserId}", UriKind.Relative), body, cancellationToken);
            await EdgeApiTests.AssertProblemAsync(refused, HttpStatusCode.BadRequest, "urn:coldframe:problem:validation", cancellationToken);
        }

        using (var malformed = await server.PutAsync(
            new Uri($"/sites/{siteId}/members/{owner.UserId}", UriKind.Relative),
            new StringContent("{", System.Text.Encoding.UTF8, "application/json"),
            cancellationToken))
        {
            await EdgeApiTests.AssertProblemAsync(malformed, HttpStatusCode.BadRequest, "urn:coldframe:problem:validation", cancellationToken);
        }

        Assert.Equal(aliases, await edge.AliasesAsync($"site/{siteId}", cancellationToken));
        Assert.Equal("Owner", await RoleOnSiteAsync(server, siteId, cancellationToken));
        Assert.Equal(["owner"], await OrganizationRolesAsync(siteId, owner.UserId, cancellationToken));
    }

    private static Task<HttpResponseMessage> ChangeRoleAsync(HttpClient server, string siteId, string userId, string role, CancellationToken cancellationToken) =>
        server.PutAsJsonAsync(new Uri($"/sites/{siteId}/members/{userId}", UriKind.Relative), new { role }, cancellationToken);

    private static async Task<string?> RoleOnSiteAsync(HttpClient server, string siteId, CancellationToken cancellationToken)
    {
        using var response = await server.GetAsync(new Uri($"/sites/{siteId}", UriKind.Relative), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await EdgeApiFixture.ReadJsonAsync(response, cancellationToken);
        return body.RootElement.GetProperty("role").GetString();
    }

    private static async Task<string> CreateSiteAsync(HttpClient server, CancellationToken cancellationToken)
    {
        using var created = await EdgeApiTests.PostSiteAsync(server, Guid.NewGuid().ToString(), new { name = "Home" }, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var body = await EdgeApiFixture.ReadJsonAsync(created, cancellationToken);
        return body.RootElement.GetProperty("id").GetString()!;
    }

    // Invites as Member and accepts on the Phase Two page, then waits for the Membership to reach the Server.
    private async Task JoinAsync(HttpClient server, string siteId, TestUser invitee, CancellationToken cancellationToken)
    {
        using (var invited = await server.PostAsJsonAsync(
            new Uri($"/sites/{siteId}/invitations", UriKind.Relative),
            new { email = invitee.Email, role = "Member" },
            cancellationToken))
        {
            Assert.Equal(HttpStatusCode.Accepted, invited.StatusCode);
        }

        await edge.AcceptInvitationAsync(invitee, siteId, cancellationToken);

        using var arrival = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        arrival.CancelAfter(ArrivalTimeout);

        while (true)
        {
            using var members = await server.GetAsync(new Uri($"/sites/{siteId}/members", UriKind.Relative), cancellationToken);
            using var body = await EdgeApiFixture.ReadJsonAsync(members, cancellationToken);

            if (body.RootElement.GetProperty("members").EnumerateArray().Any(member => member.GetProperty("userId").GetString() == invitee.UserId))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), arrival.Token);
        }
    }

    private async Task<IReadOnlyList<string>> OrganizationRolesAsync(string siteId, string userId, CancellationToken cancellationToken)
    {
        using var organizations = await edge.CreateOrganizationsClientAsync(cancellationToken);
        var held = new List<string>();

        foreach (var role in new[] { "owner", "administrator", "member" })
        {
            using var response = await organizations.GetAsync(
                new Uri($"/realms/{EdgeApiFixture.Realm}/orgs/{siteId}/roles/{role}/users", UriKind.Relative),
                cancellationToken);
            response.EnsureSuccessStatusCode();
            using var body = await EdgeApiFixture.ReadJsonAsync(response, cancellationToken);

            if (body.RootElement.EnumerateArray().Any(user => user.GetProperty("id").GetString() == userId))
            {
                held.Add(role);
            }
        }

        return held;
    }

    private async Task<IReadOnlyList<string>> OrganizationMembersAsync(string siteId, CancellationToken cancellationToken)
    {
        using var organizations = await edge.CreateOrganizationsClientAsync(cancellationToken);
        using var response = await organizations.GetAsync(new Uri($"/realms/{EdgeApiFixture.Realm}/orgs/{siteId}/members", UriKind.Relative), cancellationToken);
        response.EnsureSuccessStatusCode();
        using var body = await EdgeApiFixture.ReadJsonAsync(response, cancellationToken);
        return [.. body.RootElement.EnumerateArray().Select(user => user.GetProperty("id").GetString()!)];
    }

    // Read once, without polling: the User grain already journaled the Role for the Site (null: it dropped the Site).
    private async Task AssertUserGrainSyncedAsync(string userId, string siteId, SiteRole? expected, CancellationToken cancellationToken)
    {
        var changes = (await edge.ReadStreamAsync($"user/{userId}", cancellationToken))
            .Select(record => record.Data)
            .OfType<SiteMembershipChanged>();

        Assert.Contains(new SiteMembershipChanged(siteId, expected), changes);
    }

    // The Role the User grain journaled last for the Site: what it holds, null once it dropped the Site. The Edge
    // syncs it before it answers; an older reconciliation of the accepted invitation may still sync it after that,
    // and the echo of the change syncs it again, so the last entry is awaited.
    private async Task WaitForUserGrainRoleAsync(string userId, string siteId, SiteRole? expected, CancellationToken cancellationToken)
    {
        using var arrival = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        arrival.CancelAfter(ArrivalTimeout);

        while (true)
        {
            var changes = (await edge.ReadStreamAsync($"user/{userId}", cancellationToken))
                .Select(record => record.Data)
                .OfType<SiteMembershipChanged>()
                .Where(change => change.SiteId == siteId)
                .ToList();

            if (changes.Count > 0 && changes[^1].Role == expected)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), arrival.Token);
        }
    }
}
