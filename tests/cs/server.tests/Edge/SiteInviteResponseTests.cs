using System.Text.Json;
using Coldframe.Contracts.Sites;
using Coldframe.Server.Edge;
using Coldframe.Server.Identity;
using Microsoft.AspNetCore.Http;

namespace Coldframe.Server.Tests.Edge;

/// <summary>
/// How <c>POST /sites/{siteId}/invitations</c> answers each outcome of the Site grain, and how
/// <c>GET /sites/{siteId}/members</c> joins the projection's Memberships with Keycloak's profiles (Story 9.1),
/// written through the Server's own JSON and Problem Details configuration.
/// </summary>
public sealed class SiteInviteResponseTests
{
    [Fact]
    public async Task ASentInvitationAnswers202WithNoBody()
    {
        var (context, body) = await ExecuteAsync(EdgeApi.ToHttpResult(SiteInviteOutcome.Sent));

        Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
        Assert.Empty(body);
    }

    [Theory]
    [InlineData(SiteInviteOutcome.AlreadyInvited, StatusCodes.Status409Conflict, EdgeProblems.AlreadyInvited, "That person is already invited or a member of this Site.", null)]
    [InlineData(SiteInviteOutcome.IdentityProviderUnavailable, StatusCodes.Status503ServiceUnavailable, EdgeProblems.IdentityProviderUnavailable, "The sign-in service is unavailable.", "The invitation was not sent. Try again.")]
    [InlineData(SiteInviteOutcome.NotFound, StatusCodes.Status404NotFound, EdgeProblems.SiteNotFound, "The Site does not exist.", null)]
    public async Task RefusalsAnswerProblemDetailsOfTheirType(SiteInviteOutcome outcome, int status, string type, string title, string? detail)
    {
        var (context, text) = await ExecuteAsync(EdgeApi.ToHttpResult(outcome));
        using var body = JsonDocument.Parse(text);

        Assert.Equal(status, context.Response.StatusCode);
        Assert.StartsWith(EdgeProblems.ContentType, context.Response.ContentType, StringComparison.Ordinal);
        Assert.Equal(type, body.RootElement.GetProperty("type").GetString());
        Assert.Equal(title, body.RootElement.GetProperty("title").GetString());
        Assert.Equal(detail, body.RootElement.TryGetProperty("detail", out var value) ? value.GetString() : null);
    }

    [Fact]
    public async Task MembersListOwnersFirstThenByNameWithTheRoleOfTheProjection()
    {
        var members = EdgeApi.ToMemberList(
            [
                new SiteMembershipView("u-ana", SiteRole.Member),
                new SiteMembershipView("u-simon", SiteRole.Owner),
                new SiteMembershipView("u-bea", SiteRole.Administrator),
                new SiteMembershipView("u-new", SiteRole.Member),
            ],
            [
                new PhaseTwoMemberProfile("u-simon", "Simon Novak", "simon@example.com"),
                new PhaseTwoMemberProfile("u-ana", "ana", "ana@example.com"),
                new PhaseTwoMemberProfile("u-bea", "Bea", null),
                new PhaseTwoMemberProfile("u-gone", "Not a member", "gone@example.com"),
            ]);

        Assert.Equal(["u-simon", "u-new", "u-ana", "u-bea"], members.Members.Select(member => member.UserId));
        Assert.Equal(SiteRole.Owner, members.Members[0].Role);
        Assert.Equal(string.Empty, members.Members[1].Name);

        var (_, text) = await ExecuteAsync(TypedResults.Ok(members));
        using var body = JsonDocument.Parse(text);
        var first = body.RootElement.GetProperty("members")[0];

        Assert.Equal("u-simon", first.GetProperty("userId").GetString());
        Assert.Equal("Simon Novak", first.GetProperty("name").GetString());
        Assert.Equal("simon@example.com", first.GetProperty("email").GetString());
        Assert.Equal("Owner", first.GetProperty("role").GetString());
        Assert.False(body.RootElement.GetProperty("members")[3].TryGetProperty("email", out _));
    }

    [Fact]
    public async Task MembersAnswer503WhenKeycloakCannotBeReadForTheProfiles()
    {
        var result = await EdgeApi.MembersResultAsync(
            new ProfilesStub(() => throw new IdentityProviderUnavailableException("Keycloak is down.")),
            "site",
            [new SiteMembershipView("u-simon", SiteRole.Owner)],
            TestContext.Current.CancellationToken);

        var (context, text) = await ExecuteAsync(result);
        using var body = JsonDocument.Parse(text);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.StartsWith(EdgeProblems.ContentType, context.Response.ContentType, StringComparison.Ordinal);
        Assert.Equal(EdgeProblems.IdentityProviderUnavailable, body.RootElement.GetProperty("type").GetString());
        Assert.Equal("The names and emails of the Members could not be read. Try again.", body.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task MembersOfAnOrganizationKeycloakDoesNotKnowHaveEmptyNames()
    {
        var result = await EdgeApi.MembersResultAsync(
            new ProfilesStub(() => null),
            "site",
            [new SiteMembershipView("u-simon", SiteRole.Owner)],
            TestContext.Current.CancellationToken);

        var (context, text) = await ExecuteAsync(result);
        using var body = JsonDocument.Parse(text);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        var member = Assert.Single(body.RootElement.GetProperty("members").EnumerateArray());
        Assert.Equal(string.Empty, member.GetProperty("name").GetString());
    }

    private static async Task<(DefaultHttpContext Context, string Body)> ExecuteAsync(IResult result)
    {
        await using var app = EdgeTestHost.Build();
        using var stream = new MemoryStream();
        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Response.Body = stream;

        await result.ExecuteAsync(context);

        stream.Position = 0;
        using var reader = new StreamReader(stream);
        return (context, await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
    }

    // Only the profile read is used by the Members answer.
    private sealed class ProfilesStub(Func<IReadOnlyList<PhaseTwoMemberProfile>?> profiles) : IPhaseTwoOrganizations
    {
        public Task<IReadOnlyList<PhaseTwoMemberProfile>?> GetMemberProfilesAsync(string organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(profiles());

        public Task<IReadOnlyList<PhaseTwoOrganization>> FindByAttributeAsync(string attribute, string value, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PhaseTwoOrganization?> GetAsync(string id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> CreateAsync(PhaseTwoOrganization organization, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task EnsureRoleAsync(string organizationId, string role, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task AddMemberAsync(string organizationId, string userId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task GrantRoleAsync(string organizationId, string role, string userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RevokeRoleAsync(string organizationId, string role, string userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RemoveMemberAsync(string organizationId, string userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpdateDisplayNameAsync(string organizationId, string displayName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PhaseTwoRoster?> GetRosterAsync(string organizationId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PhaseTwoInviteOutcome> InviteAsync(string organizationId, string email, string role, Uri? redirectUri, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
