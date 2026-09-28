using System.Text.Json;
using Coldframe.Contracts.Sites;
using Coldframe.Server.Edge;
using Microsoft.AspNetCore.Http;

namespace Coldframe.Server.Tests.Edge;

/// <summary>
/// How <c>PATCH /sites/{siteId}</c> answers each outcome of the Site grain, written through the Server's own
/// JSON and Problem Details configuration.
/// </summary>
public sealed class SiteRenameResponseTests
{
    private const string SiteId = "0192f3a4-7c1e-7d2b-9a51-3f7e2c9b1d00";

    [Theory]
    [InlineData(SiteRenameOutcome.Renamed)]
    [InlineData(SiteRenameOutcome.Unchanged)]
    public async Task RenamedOrUnchangedAnswers200WithTheSite(SiteRenameOutcome outcome)
    {
        var site = new SiteResponse(SiteId, "Home garden", SiteRole.Owner);

        var (context, body) = await ExecuteAsync(EdgeApi.ToHttpResult(outcome, site));

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.StartsWith("application/json", context.Response.ContentType, StringComparison.Ordinal);
        Assert.Equal(SiteId, body.RootElement.GetProperty("id").GetString());
        Assert.Equal("Home garden", body.RootElement.GetProperty("name").GetString());
        Assert.Equal("Owner", body.RootElement.GetProperty("role").GetString());
    }

    [Theory]
    [InlineData(SiteRenameOutcome.IdentityProviderUnavailable, StatusCodes.Status503ServiceUnavailable, EdgeProblems.IdentityProviderUnavailable)]
    [InlineData(SiteRenameOutcome.NotFound, StatusCodes.Status404NotFound, EdgeProblems.SiteNotFound)]
    [InlineData(SiteRenameOutcome.Renamed, StatusCodes.Status404NotFound, EdgeProblems.SiteNotFound)]
    [InlineData(SiteRenameOutcome.Unchanged, StatusCodes.Status404NotFound, EdgeProblems.SiteNotFound)]
    public async Task RefusalsAndAVanishedSiteAnswerProblemDetailsOfTheirType(SiteRenameOutcome outcome, int status, string type)
    {
        var (context, body) = await ExecuteAsync(EdgeApi.ToHttpResult(outcome, site: null));

        Assert.Equal(status, context.Response.StatusCode);
        Assert.StartsWith(EdgeProblems.ContentType, context.Response.ContentType, StringComparison.Ordinal);
        Assert.Equal(type, body.RootElement.GetProperty("type").GetString());
        Assert.Equal(status, body.RootElement.GetProperty("status").GetInt32());
    }

    private static async Task<(DefaultHttpContext Context, JsonDocument Body)> ExecuteAsync(IResult result)
    {
        await using var app = EdgeTestHost.Build();
        using var stream = new MemoryStream();
        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Response.Body = stream;

        await result.ExecuteAsync(context);

        stream.Position = 0;
        return (context, await JsonDocument.ParseAsync(stream, cancellationToken: TestContext.Current.CancellationToken));
    }
}
