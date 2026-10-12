using System.Text.Json;
using Coldframe.Contracts.Devices;
using Coldframe.Server.Edge;
using Microsoft.AspNetCore.Http;

namespace Coldframe.Server.Tests.Edge;

/// <summary>
/// How <c>PUT /sites/{siteId}/devices/{deviceId}/silence-window</c> (Story 7.2) reads its body and answers each
/// outcome of the Device grain.
/// </summary>
public sealed class DeviceSilenceWindowResponseTests
{
    [Theory]
    [InlineData("""{"windowSeconds": 28800}""", 28800)]
    [InlineData("""{"windowSeconds": 60}""", 60)]
    [InlineData("""{"windowSeconds": 2592000}""", 2592000)]
    public void AWholeNumberOfSecondsIsTheDevicesOwnWindow(string body, int seconds)
    {
        Assert.True(EdgeApi.TryReadSilenceWindow(Member(body), out var window));
        Assert.Equal(TimeSpan.FromSeconds(seconds), window);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"windowSeconds": null}""")]
    public void NoValueMeansUseTheDefault(string body)
    {
        Assert.True(EdgeApi.TryReadSilenceWindow(Member(body), out var window));
        Assert.Null(window);
    }

    [Theory]
    [InlineData("""{"windowSeconds": 90.5}""")]
    [InlineData("""{"windowSeconds": "28800"}""")]
    [InlineData("""{"windowSeconds": true}""")]
    [InlineData("""{"windowSeconds": [60]}""")]
    [InlineData("""{"windowSeconds": 1e40}""")]
    public void AFractionAStringOrAnyOtherJsonIsRefused(string body)
    {
        Assert.False(EdgeApi.TryReadSilenceWindow(Member(body), out _));
    }

    // The range is the grain's to check: a number outside it is read, as a window the grain refuses.
    [Theory]
    [InlineData("""{"windowSeconds": 59}""")]
    [InlineData("""{"windowSeconds": 2592001}""")]
    [InlineData("""{"windowSeconds": 0}""")]
    [InlineData("""{"windowSeconds": -60}""")]
    [InlineData("""{"windowSeconds": 9223372036854775807}""")]
    public void ANumberOutOfRangeIsReadAsAWindowTheGrainRefuses(string body)
    {
        Assert.True(EdgeApi.TryReadSilenceWindow(Member(body), out var window));
        Assert.NotNull(window);
        Assert.False(Coldframe.Server.Devices.SilenceRule.IsValid(window.Value));
    }

    [Theory]
    [InlineData(DeviceSilenceWindowOutcome.Set, 28800, true)]
    [InlineData(DeviceSilenceWindowOutcome.Unchanged, 28800, true)]
    [InlineData(DeviceSilenceWindowOutcome.Set, 21600, false)]
    [InlineData(DeviceSilenceWindowOutcome.Unchanged, 300, false)]
    public async Task ASetOrUnchangedWindowAnswers200WithTheWindowInForce(DeviceSilenceWindowOutcome outcome, int seconds, bool overridden)
    {
        var result = new DeviceSilenceWindowResult(outcome, TimeSpan.FromSeconds(seconds));

        var (context, body) = await ExecuteAsync(EdgeApi.ToHttpResult(result, overridden));

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(["overridden", "windowSeconds"], body.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal((seconds, overridden), (body.RootElement.GetProperty("windowSeconds").GetInt32(), body.RootElement.GetProperty("overridden").GetBoolean()));
    }

    [Theory]
    [InlineData(DeviceSilenceWindowOutcome.NotFound, StatusCodes.Status404NotFound, EdgeProblems.DeviceNotFound)]
    [InlineData(DeviceSilenceWindowOutcome.OutOfRange, StatusCodes.Status400BadRequest, EdgeProblems.Validation)]
    public async Task RefusalsAnswerProblemDetailsOfTheirType(DeviceSilenceWindowOutcome outcome, int status, string type)
    {
        var (context, body) = await ExecuteAsync(EdgeApi.ToHttpResult(new DeviceSilenceWindowResult(outcome, TimeSpan.FromHours(6)), overridden: true));

        Assert.Equal(status, context.Response.StatusCode);
        Assert.StartsWith(EdgeProblems.ContentType, context.Response.ContentType, StringComparison.Ordinal);
        Assert.Equal(type, body.RootElement.GetProperty("type").GetString());
    }

    // A Hub has a Silence Window too: the 404 does not say "Node".
    [Fact]
    public async Task TheNotFoundTitleFitsAHubToo()
    {
        var (_, body) = await ExecuteAsync(EdgeApi.ToHttpResult(new DeviceSilenceWindowResult(DeviceSilenceWindowOutcome.NotFound), overridden: false));

        Assert.Equal("The Site has no such Device.", body.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public void ASuccessWithoutAWindowIsAServerBug()
    {
        Assert.Throws<InvalidOperationException>(() => EdgeApi.ToHttpResult(new DeviceSilenceWindowResult(DeviceSilenceWindowOutcome.Set), overridden: true));
    }

    private static JsonElement? Member(string body)
    {
        using var document = JsonDocument.Parse(body);

        return document.RootElement.TryGetProperty("windowSeconds", out var member) ? member.Clone() : null;
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
