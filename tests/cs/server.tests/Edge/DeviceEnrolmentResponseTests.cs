using System.Text.Json;
using Coldframe.Contracts.Devices;
using Coldframe.Server.Edge;
using Microsoft.AspNetCore.Http;

namespace Coldframe.Server.Tests.Edge;

/// <summary>
/// How <c>POST /sites/{siteId}/devices</c> answers each outcome of the Device grain, and the request rules
/// it checks before it opens anything.
/// </summary>
public sealed class DeviceEnrolmentResponseTests
{
    private const string SiteId = "0192f3a4-7c1e-7d2b-9a51-3f7e2c9b1d00";

    [Fact]
    public async Task EnrolledAnswers201WithTheDevice()
    {
        var result = new DeviceEnrolmentResult(DeviceEnrolmentOutcome.Enrolled, new DeviceSummary("92064422c012f481", DeviceKind.Node, SiteId));

        var (context, body) = await ExecuteAsync(EdgeApi.ToHttpResult(result));

        Assert.Equal(StatusCodes.Status201Created, context.Response.StatusCode);
        Assert.StartsWith("application/json", context.Response.ContentType, StringComparison.Ordinal);
        Assert.Equal(["id", "kind", "siteId"], body.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal("92064422c012f481", body.RootElement.GetProperty("id").GetString());
        Assert.Equal("node", body.RootElement.GetProperty("kind").GetString());
        Assert.Equal(SiteId, body.RootElement.GetProperty("siteId").GetString());
    }

    [Theory]
    [InlineData(DeviceEnrolmentOutcome.SiteNotFound, StatusCodes.Status404NotFound, EdgeProblems.SiteNotFound)]
    [InlineData(DeviceEnrolmentOutcome.OnAnotherSite, StatusCodes.Status409Conflict, EdgeProblems.DeviceOnAnotherSite)]
    [InlineData(DeviceEnrolmentOutcome.IdempotencyKeyReused, StatusCodes.Status422UnprocessableEntity, EdgeProblems.IdempotencyKeyReused)]
    public async Task RefusalsAnswerProblemDetailsOfTheirType(DeviceEnrolmentOutcome outcome, int status, string type)
    {
        var (context, body) = await ExecuteAsync(EdgeApi.ToHttpResult(new DeviceEnrolmentResult(outcome)));

        Assert.Equal(status, context.Response.StatusCode);
        Assert.StartsWith(EdgeProblems.ContentType, context.Response.ContentType, StringComparison.Ordinal);
        Assert.Equal(type, body.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public void EnrolledWithoutADeviceIsAServerBug()
    {
        Assert.Throws<InvalidOperationException>(() => EdgeApi.ToHttpResult(new DeviceEnrolmentResult(DeviceEnrolmentOutcome.Enrolled)));
    }

    [Theory]
    [InlineData("92064422c012f481", true)]
    [InlineData("92064422C012F481", false)]
    [InlineData("92064422c012f48", false)]
    [InlineData("92064422c012f4811", false)]
    [InlineData("92064422c012f48g", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void DeviceIdsAre16LowercaseHexDigits(string? deviceId, bool valid)
    {
        Assert.Equal(valid, EdgeValidation.NormalizeDeviceId(deviceId) is not null);
    }

    [Theory]
    [InlineData("hub", DeviceKind.Hub)]
    [InlineData("node", DeviceKind.Node)]
    [InlineData("Hub", null)]
    [InlineData("sensor", null)]
    [InlineData(null, null)]
    public void KindsAreTheContractNames(string? kind, DeviceKind? expected)
    {
        Assert.Equal(expected, EdgeValidation.NormalizeDeviceKind(kind));

        if (expected is { } parsed)
        {
            Assert.Equal(kind, EdgeValidation.DeviceKindName(parsed));
        }
    }

    [Fact]
    public void Base64UrlMustBeUnpaddedAndExactlyTheLength()
    {
        var bytes = Enumerable.Range(0, 32).Select(value => (byte)(value * 7)).ToArray();
        var text = System.Buffers.Text.Base64Url.EncodeToString(bytes);

        Assert.Equal(43, text.Length);
        Assert.Equal(bytes, EdgeValidation.DecodeBase64Url(text, 32));
        Assert.Null(EdgeValidation.DecodeBase64Url(text + "=", 32));
        Assert.Null(EdgeValidation.DecodeBase64Url(Convert.ToBase64String(bytes), 32));
        Assert.Null(EdgeValidation.DecodeBase64Url(text[..42], 32));
        Assert.Null(EdgeValidation.DecodeBase64Url(text, 48));
        Assert.Null(EdgeValidation.DecodeBase64Url(text[..42] + "!", 32));
        Assert.Null(EdgeValidation.DecodeBase64Url(null, 32));
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
