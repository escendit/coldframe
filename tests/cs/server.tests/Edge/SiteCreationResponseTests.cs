using System.Text.Json;
using Coldframe.Contracts.Sites;
using Coldframe.Server.Edge;
using Microsoft.AspNetCore.Http;

namespace Coldframe.Server.Tests.Edge;

/// <summary>
/// How <c>POST /sites</c> answers each outcome of the User grain, written through the Server's own JSON
/// and Problem Details configuration.
/// </summary>
public sealed class SiteCreationResponseTests
{
    private const string SiteId = "0192f3a4-7c1e-7d2b-9a51-3f7e2c9b1d00";

    [Fact]
    public async Task CreatedAnswers201WithTheLocationAndTheSite()
    {
        var result = new SiteCreationResult(SiteCreationOutcome.Created, new SiteSummary(SiteId, "Home", SiteRole.Owner));

        var (context, body) = await ExecuteAsync(EdgeApi.ToHttpResult(result));

        Assert.Equal(StatusCodes.Status201Created, context.Response.StatusCode);
        Assert.Equal($"/sites/{SiteId}", context.Response.Headers.Location.ToString());
        Assert.StartsWith("application/json", context.Response.ContentType, StringComparison.Ordinal);
        Assert.Equal(
            ["id", "name", "role"],
            body.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(SiteId, body.RootElement.GetProperty("id").GetString());
        Assert.Equal("Home", body.RootElement.GetProperty("name").GetString());
        Assert.Equal("Owner", body.RootElement.GetProperty("role").GetString());
    }

    [Theory]
    [InlineData(SiteCreationOutcome.IdempotencyKeyReused, StatusCodes.Status422UnprocessableEntity, EdgeProblems.IdempotencyKeyReused)]
    [InlineData(SiteCreationOutcome.IdentityProviderUnavailable, StatusCodes.Status503ServiceUnavailable, EdgeProblems.IdentityProviderUnavailable)]
    public async Task RefusalsAnswerProblemDetailsOfTheirType(SiteCreationOutcome outcome, int status, string type)
    {
        var (context, body) = await ExecuteAsync(EdgeApi.ToHttpResult(new SiteCreationResult(outcome)));

        Assert.Equal(status, context.Response.StatusCode);
        Assert.StartsWith(EdgeProblems.ContentType, context.Response.ContentType, StringComparison.Ordinal);
        Assert.Equal(type, body.RootElement.GetProperty("type").GetString());
        Assert.Equal(status, body.RootElement.GetProperty("status").GetInt32());
        Assert.False(context.Response.Headers.ContainsKey("Location"));
    }

    [Fact]
    public void CreatedWithoutASiteIsAServerBug()
    {
        Assert.Throws<InvalidOperationException>(() => EdgeApi.ToHttpResult(new SiteCreationResult(SiteCreationOutcome.Created)));
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
