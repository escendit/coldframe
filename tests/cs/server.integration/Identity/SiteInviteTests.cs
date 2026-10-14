using Coldframe.Contracts.Sites;
using Coldframe.Server.Identity;

namespace Coldframe.Server.IntegrationTests.Identity;

/// <summary>
/// <see cref="ISiteGrain.Invite"/> on a TestCluster with a fake Phase Two (Story 9.1; AD-3): the Site grain is
/// the only caller of Phase Two's invitations, and it journals and stores nothing for an invitation.
/// </summary>
public sealed class SiteInviteTests(IdentityCluster identity) : IClassFixture<IdentityCluster>
{
    [Theory]
    [InlineData(SiteRole.Member, "member")]
    [InlineData(SiteRole.Administrator, "administrator")]
    [InlineData(SiteRole.Owner, "owner")]
    public async Task AnInvitationAsksPhaseTwoWithTheRoleAndJournalsNothing(SiteRole role, string organizationRole)
    {
        var siteId = await CreateSiteAsync();

        var result = await identity.Site(siteId).Invite("ana@example.com", role, Ct);

        Assert.Equal(new SiteInviteResult(SiteInviteOutcome.Sent), result);
        var invitation = Assert.Single(identity.PhaseTwo.Invitations(siteId));
        Assert.Equal("ana@example.com", invitation.Email);
        Assert.Equal(organizationRole, invitation.Role);
        Assert.Equal(["site.created", "site.membership-granted"], await identity.AliasesAsync($"site/{siteId}"));
    }

    [Fact]
    public async Task AnEmailInvitedAlreadyIsAConflictAndNothingIsSentAgain()
    {
        var siteId = await CreateSiteAsync();
        await identity.Site(siteId).Invite("ana@example.com", SiteRole.Member, Ct);

        var again = await identity.Site(siteId).Invite("ana@example.com", SiteRole.Administrator, Ct);

        Assert.Equal(SiteInviteOutcome.AlreadyInvited, again.Outcome);
        Assert.Single(identity.PhaseTwo.Invitations(siteId));
    }

    [Fact]
    public async Task WithKeycloakDownTheInvitationIsNotSent()
    {
        var siteId = await CreateSiteAsync();
        identity.PhaseTwo.FailOnceOn(SiteWrite.Invite);

        var result = await identity.Site(siteId).Invite("ana@example.com", SiteRole.Member, Ct);

        Assert.Equal(SiteInviteOutcome.IdentityProviderUnavailable, result.Outcome);
        Assert.Empty(identity.PhaseTwo.Invitations(siteId));
        Assert.Equal(["site.created", "site.membership-granted"], await identity.AliasesAsync($"site/{siteId}"));

        // Trying again sends it.
        Assert.Equal(SiteInviteOutcome.Sent, (await identity.Site(siteId).Invite("ana@example.com", SiteRole.Member, Ct)).Outcome);
    }

    [Fact]
    public async Task AKeycloakThatHangsEndsWithinTheBudgetAsUnavailable()
    {
        var siteId = await CreateSiteAsync();
        var stalled = identity.PhaseTwo.StallOnce(SiteWrite.Invite);

        var invite = identity.Site(siteId).Invite("ana@example.com", SiteRole.Member, Ct);
        await stalled.WaitAsync(Ct);
        identity.Time.Advance(new KeycloakOptions().OperationBudget);

        Assert.Equal(SiteInviteOutcome.IdentityProviderUnavailable, (await invite.WaitAsync(Ct)).Outcome);
        Assert.Empty(identity.PhaseTwo.Invitations(siteId));
    }

    [Fact]
    public async Task AnUncreatedSiteInvitesNoOne()
    {
        var siteId = Guid.CreateVersion7().ToString();

        Assert.Equal(SiteInviteOutcome.NotFound, (await identity.Site(siteId).Invite("ana@example.com", SiteRole.Member, Ct)).Outcome);
        Assert.Empty(identity.PhaseTwo.Invitations(siteId));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> CreateSiteAsync()
    {
        var created = await identity.User(Guid.NewGuid().ToString()).CreateSite(Guid.NewGuid().ToString(), "Home", Ct);
        Assert.Equal(SiteCreationOutcome.Created, created.Outcome);
        return created.Site!.Id;
    }
}
