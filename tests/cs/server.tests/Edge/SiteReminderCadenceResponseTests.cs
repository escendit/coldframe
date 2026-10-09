using System.Text.Json;
using Coldframe.Contracts.Sites;
using Coldframe.Server.Edge;
using Coldframe.Server.Notifications;
using Microsoft.AspNetCore.Http;

namespace Coldframe.Server.Tests.Edge;

/// <summary>
/// How <c>PUT /sites/{siteId}/reminder-cadence</c> answers each way the write and its hand-over to the members
/// ended (Story 6.3), written through the Server's own JSON and Problem Details configuration.
/// </summary>
public sealed class SiteReminderCadenceResponseTests
{
    [Theory]
    [InlineData(ReminderCadence.Daily, "daily")]
    [InlineData(ReminderCadence.Every2Days, "every2Days")]
    public async Task DeliveredAnswers200WithTheCadenceInForce(ReminderCadence cadence, string name)
    {
        var (context, body) = await ExecuteAsync(EdgeApi.ToHttpResult(SiteReminderCadenceDelivery.Delivered, cadence));

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.StartsWith("application/json", context.Response.ContentType, StringComparison.Ordinal);
        Assert.Equal(name, body.RootElement.GetProperty("cadence").GetString());
    }

    [Theory]
    [InlineData(SiteReminderCadenceDelivery.NotDelivered, StatusCodes.Status503ServiceUnavailable, EdgeProblems.ReminderCadenceNotDelivered)]
    [InlineData(SiteReminderCadenceDelivery.SiteNotFound, StatusCodes.Status404NotFound, EdgeProblems.SiteNotFound)]
    public async Task AMemberNotReachedAndAMissingSiteAnswerProblemDetailsOfTheirType(SiteReminderCadenceDelivery delivery, int status, string type)
    {
        var (context, body) = await ExecuteAsync(EdgeApi.ToHttpResult(delivery, ReminderCadence.Every2Days));

        Assert.Equal(status, context.Response.StatusCode);
        Assert.StartsWith(EdgeProblems.ContentType, context.Response.ContentType, StringComparison.Ordinal);
        Assert.Equal(type, body.RootElement.GetProperty("type").GetString());
        Assert.Equal(status, body.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public void TheNotDeliveredTypeIsTheContractsSlug()
    {
        Assert.Equal("urn:coldframe:problem:reminder-cadence-not-delivered", EdgeProblems.ReminderCadenceNotDelivered);
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
