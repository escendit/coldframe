using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting.Testing;

namespace Coldframe.Server.IntegrationTests.Edge;

/// <summary>
/// Story 9.1 on the AppHost with Keycloak, Phase Two, Temporal and Mailpit: an Owner invites by email, Phase Two
/// emails the invitation through the Coldframe email theme, the invitee accepts on Keycloak's invitation page,
/// and the accepted invitation's user event brings the new Membership to the Server.
/// </summary>
public sealed class InvitationTests(EdgeApiFixture edge) : IClassFixture<EdgeApiFixture>
{
    private const string MailpitResource = "mailpit";

    // Membership events arrive asynchronously (AD-3): Keycloak → Temporal → the Server's worker.
    private static readonly TimeSpan ArrivalTimeout = TimeSpan.FromSeconds(90);

    [Fact]
    public async Task AnInvitationIsEmailedAndOnceAcceptedTheInviteeIsAMemberWithTheInvitedRole()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var owner = await edge.CreateUserAsync("inviter", cancellationToken);
        var invitee = await edge.CreateUserAsync("neighbour", cancellationToken);
        using var server = edge.CreateServerClient(owner.AccessToken);
        var siteId = await CreateSiteAsync(server, "Home", cancellationToken);

        using (var invited = await InviteAsync(server, siteId, invitee.Email, "Member", cancellationToken))
        {
            Assert.Equal(HttpStatusCode.Accepted, invited.StatusCode);
            Assert.Empty(await invited.Content.ReadAsByteArrayAsync(cancellationToken));
        }

        // Mailpit has the email of the Coldframe theme: it says to accept on the home network and links to the web app.
        var message = await WaitForEmailAsync(invitee.Email, cancellationToken);
        Assert.Equal("Invitation to Home in Coldframe", message.GetProperty("Subject").GetString());
        var text = message.GetProperty("Text").GetString()!;
        Assert.Contains("Open the link at home, on the garden's Wi-Fi: accepting needs the home network.", text, StringComparison.Ordinal);
        Assert.Contains("You are invited to the Site Home in Coldframe.", text, StringComparison.Ordinal);
        Assert.Contains("http://localhost:5173/garden", text, StringComparison.Ordinal);

        // The Server keeps nothing of the invitation: no event, and the Members list is unchanged.
        Assert.Equal(["site.created", "site.membership-granted"], await edge.AliasesAsync($"site/{siteId}", cancellationToken));
        Assert.Equal([(owner.UserId, "Owner")], await MembersAsync(server, siteId, cancellationToken));

        // The same email again: Phase Two holds the open invitation.
        using (var again = await InviteAsync(server, siteId, invitee.Email, "Administrator", cancellationToken))
        {
            await EdgeApiTests.AssertProblemAsync(again, HttpStatusCode.Conflict, "urn:coldframe:problem:already-invited", cancellationToken);
        }

        await edge.AcceptInvitationAsync(invitee, siteId, cancellationToken);

        // The accepted invitation's user event reaches the Site and the projection.
        using var arrival = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        arrival.CancelAfter(ArrivalTimeout);
        IReadOnlyList<(string UserId, string Role)> members;
        do
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), arrival.Token);
            members = await MembersAsync(server, siteId, cancellationToken);
        }
        while (members.Count < 2);

        Assert.Equal([(owner.UserId, "Owner"), (invitee.UserId, "Member")], members);

        using (var list = await server.GetAsync(new Uri($"/sites/{siteId}/members", UriKind.Relative), cancellationToken))
        using (var body = await EdgeApiFixture.ReadJsonAsync(list, cancellationToken))
        {
            var member = body.RootElement.GetProperty("members")[1];
            Assert.Equal("neighbour Test", member.GetProperty("name").GetString());
            Assert.Equal(invitee.Email, member.GetProperty("email").GetString());
        }

        // The new Member reads the Site with the invited Role, from the projection.
        using var asInvitee = edge.CreateServerClient(invitee.AccessToken);
        using var site = await asInvitee.GetAsync(new Uri($"/sites/{siteId}", UriKind.Relative), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, site.StatusCode);
        using (var siteBody = await EdgeApiFixture.ReadJsonAsync(site, cancellationToken))
        {
            Assert.Equal("Member", siteBody.RootElement.GetProperty("role").GetString());
        }

        // A Member cannot invite or list Members.
        using (var refused = await InviteAsync(asInvitee, siteId, "someone@example.org", "Member", cancellationToken))
        {
            await EdgeApiTests.AssertProblemAsync(refused, HttpStatusCode.Forbidden, "urn:coldframe:problem:forbidden", cancellationToken);
        }

        // A member is invited no more.
        using (var member = await InviteAsync(server, siteId, invitee.Email, "Member", cancellationToken))
        {
            await EdgeApiTests.AssertProblemAsync(member, HttpStatusCode.Conflict, "urn:coldframe:problem:already-invited", cancellationToken);
        }
    }

    [Fact]
    public async Task AnInvalidEmailOrRoleIsRefusedAndNothingIsSent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var owner = await edge.CreateUserAsync("inviter-invalid", cancellationToken);
        using var server = edge.CreateServerClient(owner.AccessToken);
        var siteId = await CreateSiteAsync(server, "Home", cancellationToken);
        var email = $"never-{Guid.NewGuid():N}@example.org";

        foreach (var body in new object[]
        {
            new { email = "   ", role = "Member" },
            new { email = "ana", role = "Member" },
            new { email = "Ana <ana@example.org>", role = "Member" },
            new { email, role = "Gardener" },
            new { email, role = "member" },
            new { email },
        })
        {
            using var refused = await server.PostAsJsonAsync(new Uri($"/sites/{siteId}/invitations", UriKind.Relative), body, cancellationToken);
            await EdgeApiTests.AssertProblemAsync(refused, HttpStatusCode.BadRequest, "urn:coldframe:problem:validation", cancellationToken);
        }

        using var mailpit = edge.AppHost.App.CreateHttpClient(MailpitResource, "http");
        Assert.Empty(await SearchAsync(mailpit, email, cancellationToken));
    }

    private static Task<HttpResponseMessage> InviteAsync(HttpClient server, string siteId, string email, string role, CancellationToken cancellationToken) =>
        server.PostAsJsonAsync(new Uri($"/sites/{siteId}/invitations", UriKind.Relative), new { email, role }, cancellationToken);

    private static async Task<IReadOnlyList<(string UserId, string Role)>> MembersAsync(HttpClient server, string siteId, CancellationToken cancellationToken)
    {
        using var response = await server.GetAsync(new Uri($"/sites/{siteId}/members", UriKind.Relative), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await EdgeApiFixture.ReadJsonAsync(response, cancellationToken);

        return
        [
            .. body.RootElement.GetProperty("members").EnumerateArray()
                .Select(member => (member.GetProperty("userId").GetString()!, member.GetProperty("role").GetString()!)),
        ];
    }

    private async Task<JsonElement> WaitForEmailAsync(string to, CancellationToken cancellationToken)
    {
        using var mailpit = edge.AppHost.App.CreateHttpClient(MailpitResource, "http");
        using var arrival = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        arrival.CancelAfter(TimeSpan.FromSeconds(30));

        while (true)
        {
            var found = await SearchAsync(mailpit, to, cancellationToken);

            if (found.Count > 0)
            {
                using var message = await mailpit.GetAsync(new Uri($"/api/v1/message/{found[0]}", UriKind.Relative), cancellationToken);
                message.EnsureSuccessStatusCode();
                using var body = await EdgeApiFixture.ReadJsonAsync(message, cancellationToken);
                return body.RootElement.Clone();
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), arrival.Token);
        }
    }

    private static async Task<IReadOnlyList<string>> SearchAsync(HttpClient mailpit, string to, CancellationToken cancellationToken)
    {
        using var response = await mailpit.GetAsync(
            new Uri($"/api/v1/search?query={Uri.EscapeDataString($"to:\"{to}\"")}", UriKind.Relative),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        using var body = await EdgeApiFixture.ReadJsonAsync(response, cancellationToken);

        return [.. body.RootElement.GetProperty("messages").EnumerateArray().Select(message => message.GetProperty("ID").GetString()!)];
    }

    private static async Task<string> CreateSiteAsync(HttpClient server, string name, CancellationToken cancellationToken)
    {
        using var created = await EdgeApiTests.PostSiteAsync(server, Guid.NewGuid().ToString(), new { name }, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var body = await EdgeApiFixture.ReadJsonAsync(created, cancellationToken);
        return body.RootElement.GetProperty("id").GetString()!;
    }
}
