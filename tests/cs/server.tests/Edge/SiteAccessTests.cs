using Coldframe.Contracts.Sites;
using Coldframe.Server.Edge;

namespace Coldframe.Server.Tests.Edge;

/// <summary>
/// The one rule behind every Site-scoped endpoint: 404 for a Site that does not exist, 403 for a missing
/// or too low Role, allowed otherwise (AD-4).
/// </summary>
public sealed class SiteAccessTests
{
    public static TheoryData<SiteRole, bool, SiteRole?, SiteAccessDecision> Table()
    {
        var table = new TheoryData<SiteRole, bool, SiteRole?, SiteAccessDecision>();
        SiteRole?[] callerRoles = [null, SiteRole.Member, SiteRole.Administrator, SiteRole.Owner];

        foreach (var minimum in Enum.GetValues<SiteRole>())
        {
            foreach (var callerRole in callerRoles)
            {
                table.Add(minimum, false, callerRole, SiteAccessDecision.NotFound);
                table.Add(
                    minimum,
                    true,
                    callerRole,
                    callerRole is { } role && role >= minimum ? SiteAccessDecision.Allow : SiteAccessDecision.Forbidden);
            }
        }

        return table;
    }

    [Theory]
    [MemberData(nameof(Table))]
    public void DecidesFromTheMinimumTheSiteAndTheCallersRole(SiteRole minimum, bool siteExists, SiteRole? callerRole, SiteAccessDecision expected)
    {
        Assert.Equal(expected, SiteAccess.Decide(minimum, siteExists, callerRole));
    }

    [Fact]
    public void AMemberOfNoSiteIsForbiddenEvenTheLowestMinimum()
    {
        Assert.Equal(SiteAccessDecision.Forbidden, SiteAccess.Decide(SiteRole.Member, siteExists: true, callerRole: null));
    }

    [Fact]
    public void RolesAreOrderedOwnerAboveAdministratorAboveMember()
    {
        Assert.True(SiteRole.Owner > SiteRole.Administrator);
        Assert.True(SiteRole.Administrator > SiteRole.Member);
        Assert.Equal([SiteRole.Member, SiteRole.Administrator, SiteRole.Owner], Enum.GetValues<SiteRole>().Order());
    }

    [Fact]
    public void RolesSerializeAsTheirNames()
    {
        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        };

        Assert.Equal("\"Owner\"", System.Text.Json.JsonSerializer.Serialize(SiteRole.Owner, options));
        Assert.Equal("\"Administrator\"", System.Text.Json.JsonSerializer.Serialize(SiteRole.Administrator, options));
        Assert.Equal("\"Member\"", System.Text.Json.JsonSerializer.Serialize(SiteRole.Member, options));
    }
}
