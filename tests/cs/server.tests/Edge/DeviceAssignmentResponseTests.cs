using System.Text.Json;
using Coldframe.Contracts.Devices;
using Coldframe.Server.Edge;
using Microsoft.AspNetCore.Http;

namespace Coldframe.Server.Tests.Edge;

/// <summary>
/// How the move and unassign endpoints (Story 4.9) answer each outcome of the Device grain.
/// </summary>
public sealed class DeviceAssignmentResponseTests
{
    private const string SiteId = "0192f3a4-7c1e-7d2b-9a51-3f7e2c9b1d00";
    private const string LotId = "0192f3a4-8a00-7c3d-8e4f-5a6b7c8d9e02";

    [Theory]
    [InlineData(DeviceAssignmentOutcome.Moved, LotId)]
    [InlineData(DeviceAssignmentOutcome.Unchanged, LotId)]
    [InlineData(DeviceAssignmentOutcome.Unassigned, null)]
    public async Task SuccessAnswers200WithTheNodeAndItsLotOnlyWhenItHasOne(DeviceAssignmentOutcome outcome, string? lotId)
    {
        var result = new DeviceAssignmentResult(outcome, new DeviceSummary("92064422c012f481", DeviceKind.Node, SiteId, lotId));

        var (context, body) = await ExecuteAsync(EdgeApi.ToHttpResult(result));

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(
            lotId is null ? ["id", "kind", "siteId"] : ["id", "kind", "lotId", "siteId"],
            body.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(DeviceAssignmentOutcome.NotFound, StatusCodes.Status404NotFound, EdgeProblems.DeviceNotFound)]
    [InlineData(DeviceAssignmentOutcome.LotNotFound, StatusCodes.Status404NotFound, EdgeProblems.LotNotFound)]
    [InlineData(DeviceAssignmentOutcome.LotOccupied, StatusCodes.Status409Conflict, EdgeProblems.LotClaimed)]
    public async Task RefusalsAnswerProblemDetailsOfTheirType(DeviceAssignmentOutcome outcome, int status, string type)
    {
        var (context, body) = await ExecuteAsync(EdgeApi.ToHttpResult(new DeviceAssignmentResult(outcome)));

        Assert.Equal(status, context.Response.StatusCode);
        Assert.StartsWith(EdgeProblems.ContentType, context.Response.ContentType, StringComparison.Ordinal);
        Assert.Equal(type, body.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public void ASuccessWithoutADeviceIsAServerBug()
    {
        Assert.Throws<InvalidOperationException>(() => EdgeApi.ToHttpResult(new DeviceAssignmentResult(DeviceAssignmentOutcome.Moved)));
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
