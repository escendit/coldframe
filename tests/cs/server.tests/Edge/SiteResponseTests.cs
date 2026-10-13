using System.Text.Json;
using Coldframe.Contracts.Sites;
using Coldframe.Server.Edge;
using Coldframe.Server.Identity;
using Microsoft.AspNetCore.Http;

namespace Coldframe.Server.Tests.Edge;

/// <summary>
/// How a Site read from the identity projection is written (Story 8.2, DW-112): <c>GET /sites</c>,
/// <c>GET /sites/{siteId}</c> and the answer to a rename say whether the Site is paused and until when, through
/// the Server's own JSON configuration.
/// </summary>
public sealed class SiteResponseTests
{
    private const string SiteId = "0192f3a4-7c1e-7d2b-9a51-3f7e2c9b1d00";

    [Fact]
    public async Task APausedSiteWithAnEndCarriesPausedAndPausedUntilInUtc()
    {
        var until = new DateTimeOffset(2027, 3, 1, 0, 0, 0, TimeSpan.FromHours(1));

        using var body = await SerializeAsync(EdgeApi.ToSiteResponse(SiteId, "Home garden", SiteRole.Administrator, paused: true, until));

        Assert.Equal(
            ["id", "name", "paused", "pausedUntil", "role"],
            body.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.True(body.RootElement.GetProperty("paused").GetBoolean());
        Assert.Equal("2027-02-28T23:00:00.000Z", body.RootElement.GetProperty("pausedUntil").GetString());
        Assert.Equal("Administrator", body.RootElement.GetProperty("role").GetString());
    }

    [Fact]
    public async Task APauseWithoutAnEndOmitsPausedUntil()
    {
        using var body = await SerializeAsync(EdgeApi.ToSiteResponse(SiteId, "Home garden", SiteRole.Owner, paused: true, pausedUntil: null));

        Assert.True(body.RootElement.GetProperty("paused").GetBoolean());
        Assert.False(body.RootElement.TryGetProperty("pausedUntil", out _));
    }

    // A Site that is not paused says so, and never an end, whatever the row still holds.
    [Fact]
    public async Task ASiteThatIsNotPausedSaysPausedFalseAndNoEnd()
    {
        using var body = await SerializeAsync(EdgeApi.ToSiteResponse(
            SiteId,
            "Home garden",
            SiteRole.Member,
            paused: false,
            new DateTimeOffset(2027, 3, 1, 0, 0, 0, TimeSpan.Zero)));

        Assert.Equal(
            ["id", "name", "paused", "role"],
            body.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.False(body.RootElement.GetProperty("paused").GetBoolean());
    }

    // The answer to Create Site comes from the grains, which say nothing about a Pause: the member is omitted.
    [Fact]
    public async Task ASiteResponseWithoutPauseMembersOmitsThem()
    {
        using var body = await SerializeAsync(new SiteResponse(SiteId, "Home garden", SiteRole.Owner));

        Assert.Equal(
            ["id", "name", "role"],
            body.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheViewsOfTheIdentityProjectionAreNotPausedUnlessToldSo()
    {
        Assert.False(new SiteAccessView(SiteId, "Home garden", SiteLifecycle.Active, SiteRole.Owner).Paused);
        Assert.False(new MemberSiteView(SiteId, "Home garden", SiteRole.Owner).Paused);
    }

    private static async Task<JsonDocument> SerializeAsync(SiteResponse response)
    {
        await using var app = EdgeTestHost.Build();
        using var stream = new MemoryStream();
        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Response.Body = stream;

        await TypedResults.Ok(response).ExecuteAsync(context);

        stream.Position = 0;
        return await JsonDocument.ParseAsync(stream, cancellationToken: TestContext.Current.CancellationToken);
    }
}
