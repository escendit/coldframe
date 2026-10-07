using System.Text.Json;
using Coldframe.Contracts.Sensors;
using Coldframe.Server.Edge;
using Microsoft.AspNetCore.Http;

namespace Coldframe.Server.Tests.Edge;

/// <summary>
/// How the calibration endpoint (Story 5.1) answers each outcome of the Sensor grain.
/// </summary>
public sealed class CalibrationResponseTests
{
    private static readonly Guid CalibrationId = Guid.Parse("0192f3a4-9000-7000-8000-000000000001");

    private static readonly SensorCalibration InForce = new(CalibrationId, 1, 3000, 1200, new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task ACalibratedSensorAnswers200WithTheCalibrationIdAndBothRawValues()
    {
        var (context, body) = await ExecuteAsync(EdgeApi.ToHttpResult(new SensorCalibrationResult(SensorCalibrationOutcome.Calibrated, InForce)));

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(["calibrated", "calibrationId", "dry", "wet"], Names(body));
        Assert.True(body.RootElement.GetProperty("calibrated").GetBoolean());
        Assert.Equal(CalibrationId.ToString(), body.RootElement.GetProperty("calibrationId").GetString());
        Assert.Equal(3000, body.RootElement.GetProperty("dry").GetProperty("rawValue").GetInt64());
        Assert.Equal(1200, body.RootElement.GetProperty("wet").GetProperty("rawValue").GetInt64());
    }

    [Fact]
    public async Task APointKeptWhileUncalibratedAnswers200WithOnlyThePendingPoint()
    {
        var (context, body) = await ExecuteAsync(EdgeApi.ToHttpResult(new SensorCalibrationResult(SensorCalibrationOutcome.PointRecorded, PendingDryRaw: 3000)));

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(["calibrated", "pendingDry"], Names(body));
        Assert.False(body.RootElement.GetProperty("calibrated").GetBoolean());
        Assert.Equal(3000, body.RootElement.GetProperty("pendingDry").GetProperty("rawValue").GetInt64());
    }

    [Fact]
    public async Task APointKeptWhileRecalibratingKeepsTheCalibrationInForce()
    {
        var (_, body) = await ExecuteAsync(EdgeApi.ToHttpResult(new SensorCalibrationResult(SensorCalibrationOutcome.PointRecorded, InForce, PendingWetRaw: 1100)));

        Assert.Equal(["calibrated", "calibrationId", "dry", "pendingWet", "wet"], Names(body));
    }

    [Theory]
    [InlineData(SensorCalibrationOutcome.NotCalibratable, StatusCodes.Status400BadRequest, EdgeProblems.Validation)]
    [InlineData(SensorCalibrationOutcome.NoPoint, StatusCodes.Status400BadRequest, EdgeProblems.Validation)]
    [InlineData(SensorCalibrationOutcome.UnknownReading, StatusCodes.Status400BadRequest, EdgeProblems.Validation)]
    [InlineData(SensorCalibrationOutcome.IndistinctPoints, StatusCodes.Status400BadRequest, EdgeProblems.Validation)]
    [InlineData(SensorCalibrationOutcome.NotDelivered, StatusCodes.Status503ServiceUnavailable, EdgeProblems.CalibrationNotDelivered)]
    public async Task RefusalsAndAPendingDeliveryAnswerProblemDetailsOfTheirType(SensorCalibrationOutcome outcome, int status, string type)
    {
        var (context, body) = await ExecuteAsync(EdgeApi.ToHttpResult(new SensorCalibrationResult(outcome, outcome == SensorCalibrationOutcome.NotDelivered ? InForce : null)));

        Assert.Equal(status, context.Response.StatusCode);
        Assert.StartsWith(EdgeProblems.ContentType, context.Response.ContentType, StringComparison.Ordinal);
        Assert.Equal(type, body.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task AnIndistinctPairNamesTheMinimumSpan()
    {
        var (_, body) = await ExecuteAsync(EdgeApi.ToHttpResult(new SensorCalibrationResult(SensorCalibrationOutcome.IndistinctPoints)));

        Assert.Contains("16", body.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    private static string[] Names(JsonDocument body) =>
        [.. body.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)];

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
