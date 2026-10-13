using System.Text.Json;
using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Sites;
using Coldframe.Server.Edge;
using Microsoft.AspNetCore.Http;

namespace Coldframe.Server.Tests.Edge;

/// <summary>
/// How the four pause endpoints (Story 8.1) read <c>until</c> and answer each outcome of the Device grain and of
/// the Site grain.
/// </summary>
public sealed class PauseResponseTests
{
    private static readonly DateTimeOffset Until = new(2026, 10, 31, 23, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"until": null}""")]
    public void NoValueIsAPauseWithoutAnEnd(string body)
    {
        Assert.True(EdgeApi.TryReadPauseDate(Member(body), out var date));
        Assert.Null(date);
    }

    [Theory]
    [InlineData("""{"until": "2026-11-01"}""", 2026, 11, 1)]
    [InlineData("""{"until": "2028-02-29"}""", 2028, 2, 29)]
    public void ADateIsTheEndDate(string body, int year, int month, int day)
    {
        Assert.True(EdgeApi.TryReadPauseDate(Member(body), out var date));
        Assert.Equal(new DateOnly(year, month, day), date);
    }

    [Theory]
    [InlineData("""{"until": "tomorrow"}""")]
    [InlineData("""{"until": ""}""")]
    [InlineData("""{"until": "2026-11-1"}""")]
    [InlineData("""{"until": "2027-02-29"}""")]
    [InlineData("""{"until": "2026-11-01T00:00:00Z"}""")]
    [InlineData("""{"until": 20261101}""")]
    [InlineData("""{"until": true}""")]
    [InlineData("""{"until": ["2026-11-01"]}""")]
    [InlineData("""{"until": {"date": "2026-11-01"}}""")]
    public void AnythingThatIsNoDateIsRefused(string body)
    {
        Assert.False(EdgeApi.TryReadPauseDate(Member(body), out var date));
        Assert.Null(date);
    }

    [Theory]
    [InlineData(DevicePauseOutcome.Changed)]
    [InlineData(DevicePauseOutcome.Unchanged)]
    public async Task ADevicesPauseAnswers200WithItsSourcesAndTheirEnd(DevicePauseOutcome outcome)
    {
        var both = new DevicePauseResult(outcome, [DevicePauseSource.Device, DevicePauseSource.Site], Until);

        var (context, body) = await ExecuteAsync(EdgeApi.ToHttpResult(both));

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(["pausedBy", "pausedUntil"], body.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(["device", "site"], body.RootElement.GetProperty("pausedBy").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal("2026-10-31T23:00:00.000Z", body.RootElement.GetProperty("pausedUntil").GetString());

        // Not paused: an empty list, and no end is no member.
        var (_, none) = await ExecuteAsync(EdgeApi.ToHttpResult(new DevicePauseResult(outcome, [])));

        Assert.Equal(["pausedBy"], none.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(0, none.RootElement.GetProperty("pausedBy").GetArrayLength());
    }

    [Theory]
    [InlineData(DevicePauseOutcome.NotFound, StatusCodes.Status404NotFound, EdgeProblems.DeviceNotFound)]
    [InlineData(DevicePauseOutcome.InvalidEnd, StatusCodes.Status400BadRequest, EdgeProblems.Validation)]
    public async Task ADevicesRefusalAnswersProblemDetailsOfItsType(DevicePauseOutcome outcome, int status, string type)
    {
        var (context, body) = await ExecuteAsync(EdgeApi.ToHttpResult(new DevicePauseResult(outcome, [])));

        Assert.Equal(status, context.Response.StatusCode);
        Assert.StartsWith(EdgeProblems.ContentType, context.Response.ContentType, StringComparison.Ordinal);
        Assert.Equal(type, body.RootElement.GetProperty("type").GetString());
    }

    // A Hub is paused too: the 404 does not say "Node".
    [Fact]
    public async Task TheNotFoundTitleFitsAHubToo()
    {
        var (_, body) = await ExecuteAsync(EdgeApi.ToHttpResult(new DevicePauseResult(DevicePauseOutcome.NotFound, [])));

        Assert.Equal("The Site has no such Device.", body.RootElement.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData(SitePauseOutcome.Changed)]
    [InlineData(SitePauseOutcome.Unchanged)]
    public async Task ASitesPauseAnswers200WithItsEndOnlyWhileItIsPausedWithOne(SitePauseOutcome outcome)
    {
        var (context, until) = await ExecuteAsync(EdgeApi.ToHttpResult(new SitePauseResult(outcome, new SitePause(true, Until, 1))));

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(["paused", "pausedUntil"], until.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.True(until.RootElement.GetProperty("paused").GetBoolean());
        Assert.Equal("2026-10-31T23:00:00.000Z", until.RootElement.GetProperty("pausedUntil").GetString());

        var (_, open) = await ExecuteAsync(EdgeApi.ToHttpResult(new SitePauseResult(outcome, new SitePause(true, null, 2))));
        Assert.Equal(["paused"], open.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.True(open.RootElement.GetProperty("paused").GetBoolean());

        var (_, resumed) = await ExecuteAsync(EdgeApi.ToHttpResult(new SitePauseResult(outcome, new SitePause(false, null, 3))));
        Assert.Equal(["paused"], resumed.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.False(resumed.RootElement.GetProperty("paused").GetBoolean());
    }

    [Theory]
    [InlineData(SitePauseOutcome.NotFound, StatusCodes.Status404NotFound, EdgeProblems.SiteNotFound)]
    [InlineData(SitePauseOutcome.InvalidEnd, StatusCodes.Status400BadRequest, EdgeProblems.Validation)]
    public async Task ASitesRefusalAnswersProblemDetailsOfItsType(SitePauseOutcome outcome, int status, string type)
    {
        var (context, body) = await ExecuteAsync(EdgeApi.ToHttpResult(new SitePauseResult(outcome, SitePause.NotPaused)));

        Assert.Equal(status, context.Response.StatusCode);
        Assert.StartsWith(EdgeProblems.ContentType, context.Response.ContentType, StringComparison.Ordinal);
        Assert.Equal(type, body.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public void AnOutcomeTheMappersDoNotKnowIsAServerBug()
    {
        Assert.Throws<InvalidOperationException>(() => EdgeApi.ToHttpResult(new DevicePauseResult((DevicePauseOutcome)99, [])));
        Assert.Throws<InvalidOperationException>(() => EdgeApi.ToHttpResult(new SitePauseResult((SitePauseOutcome)99, SitePause.NotPaused)));
    }

    private static JsonElement? Member(string body)
    {
        using var document = JsonDocument.Parse(body);

        return document.RootElement.TryGetProperty("until", out var member) ? member.Clone() : null;
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
