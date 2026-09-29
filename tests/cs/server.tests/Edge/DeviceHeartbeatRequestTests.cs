using System.Text;
using System.Text.Json;
using Coldframe.Contracts.Devices;
using Coldframe.DeviceSimulator;
using Coldframe.Server.Edge;
using Coldframe.Server.Tests.Devices;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Time.Testing;

namespace Coldframe.Server.Tests.Edge;

/// <summary>
/// What <c>POST /device/heartbeat</c> checks before the Device grain sees a request (headers → 401
/// <c>device-unauthorized</c>, body → 400 <c>validation</c>), and how it answers the grain. Requests are
/// built with the Device simulator only.
/// </summary>
public sealed class DeviceHeartbeatRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 34, 56, 789, TimeSpan.Zero);

    private static SimulatedDevice VectorHub() => SimulatedDevice.Create(Vectors.Heartbeat(0).Bytes("rootKey"));

    [Fact]
    public async Task ASignedHeartbeatReadsIntoTheGrainRequest()
    {
        var vector = Vectors.Heartbeat(0);
        var hub = VectorHub();
        var timestamp = long.Parse(vector.Text("timestampMs"), System.Globalization.CultureInfo.InvariantCulture);
        using var message = hub.HeartbeatRequest(timestamp, vector.Bytes("nonce"), vector.Bytes("body"));

        var read = await EdgeApi.ReadHeartbeatAsync(await ContextAsync(message));

        Assert.Null(read.Problem);
        Assert.Equal(vector.Text("deviceId"), read.DeviceId.ToString());
        var request = Assert.IsType<DeviceHeartbeat>(read.Request);
        Assert.Equal(("POST", "/device/heartbeat"), (request.Method, request.Path));
        Assert.Equal(vector.Bytes("body"), request.Body);
        Assert.Equal(timestamp, request.TimestampMs);
        Assert.Equal(vector.Bytes("nonce"), request.Nonce);
        Assert.Equal(vector.Bytes("signature"), request.Signature);
        Assert.Equal(61_000, request.UptimeMs);

        // The simulator's request is exactly the vector's.
        foreach (var header in vector.GetProperty("headers").EnumerateObject())
        {
            Assert.Equal(header.Value.GetString(), message.Headers.GetValues(header.Name).Single());
        }
    }

    public static TheoryData<string, string?> BadHeaders() => new()
    {
        { "X-Coldframe-Device", null },
        { "X-Coldframe-Device", "92064422C012F481" },
        { "X-Coldframe-Device", "92064422c012f48" },
        { "X-Coldframe-Timestamp", null },
        { "X-Coldframe-Timestamp", "" },
        { "X-Coldframe-Timestamp", "-1790000000000" },
        { "X-Coldframe-Timestamp", "+1790000000000" },
        { "X-Coldframe-Timestamp", "1790000000000.5" },
        { "X-Coldframe-Timestamp", "12345678901234567890" },
        { "X-Coldframe-Timestamp", "9223372036854775808" },
        { "X-Coldframe-Nonce", null },
        { "X-Coldframe-Nonce", "00112233445566778899AABBCCDDEEFF" },
        { "X-Coldframe-Nonce", "00112233445566778899aabbccddee" },
        { "X-Coldframe-Signature", null },
        { "X-Coldframe-Signature", "b49601aa" },
        { "X-Coldframe-Signature", "g49601aa6737918be5dda0bf39eccc7570b951f1e8ea2de19b4226b396c84724" },
    };

    [Theory]
    [MemberData(nameof(BadHeaders))]
    public async Task AMissingOrMalformedHeaderIsDeviceUnauthorized(string name, string? value)
    {
        using var message = VectorHub().HeartbeatRequest(Now);
        message.Headers.Remove(name);
        if (value is not null)
        {
            message.Headers.TryAddWithoutValidation(name, value);
        }

        var read = await EdgeApi.ReadHeartbeatAsync(await ContextAsync(message));

        await AssertProblemAsync(read, StatusCodes.Status401Unauthorized, EdgeProblems.DeviceUnauthorized);
    }

    [Fact]
    public async Task ARepeatedHeaderIsDeviceUnauthorized()
    {
        using var message = VectorHub().HeartbeatRequest(Now);
        message.Headers.TryAddWithoutValidation("X-Coldframe-Nonce", "00112233445566778899aabbccddeeff");

        var read = await EdgeApi.ReadHeartbeatAsync(await ContextAsync(message));

        await AssertProblemAsync(read, StatusCodes.Status401Unauthorized, EdgeProblems.DeviceUnauthorized);
    }

    [Fact]
    public async Task ATimestampAtTheTopOfLongIsWellFormed()
    {
        using var message = VectorHub().HeartbeatRequest(long.MaxValue, new byte[16]);

        var read = await EdgeApi.ReadHeartbeatAsync(await ContextAsync(message));

        Assert.Null(read.Problem);
        Assert.Equal(long.MaxValue, read.Request!.TimestampMs);
    }

    public static TheoryData<string> BadBodies() => new()
    {
        "",
        "not json",
        "[]",
        "\"protocolVersion\"",
        "{}",
        "{\"uptimeMs\":1}",
        "{\"protocolVersion\":0}",
        "{\"protocolVersion\":2}",
        "{\"protocolVersion\":\"1\"}",
        "{\"protocolVersion\":1.5}",
        "{\"protocolVersion\":1,\"uptimeMs\":-1}",
        "{\"protocolVersion\":1,\"uptimeMs\":\"61000\"}",
        "{\"protocolVersion\":1,\"uptimeMs\":null}",
        "{\"protocolVersion\":1} trailing",
    };

    [Theory]
    [MemberData(nameof(BadBodies))]
    public async Task ABodyThatIsNotAHeartbeatIsValidation(string body)
    {
        using var message = VectorHub().HeartbeatRequest(Now, Encoding.UTF8.GetBytes(body));

        var read = await EdgeApi.ReadHeartbeatAsync(await ContextAsync(message));

        await AssertProblemAsync(read, StatusCodes.Status400BadRequest, EdgeProblems.Validation);
    }

    [Fact]
    public async Task ABodyOver4KiBIsValidation()
    {
        var padding = new string(' ', EdgeValidation.MaxHeartbeatBodyLength);
        var tooLarge = Encoding.UTF8.GetBytes("{\"protocolVersion\":1}" + padding);
        using var message = VectorHub().HeartbeatRequest(Now, tooLarge);

        var read = await EdgeApi.ReadHeartbeatAsync(await ContextAsync(message));
        await AssertProblemAsync(read, StatusCodes.Status400BadRequest, EdgeProblems.Validation);

        // Without a Content-Length, the reader stops at the cap all the same.
        using var chunked = VectorHub().HeartbeatRequest(Now, tooLarge);
        var context = await ContextAsync(chunked);
        context.Request.ContentLength = null;
        await AssertProblemAsync(await EdgeApi.ReadHeartbeatAsync(context), StatusCodes.Status400BadRequest, EdgeProblems.Validation);

        // Exactly 4 KiB is read.
        var exact = Encoding.UTF8.GetBytes("{\"protocolVersion\":1}" + padding[..(EdgeValidation.MaxHeartbeatBodyLength - 21)]);
        Assert.Equal(EdgeValidation.MaxHeartbeatBodyLength, exact.Length);
        using var fits = VectorHub().HeartbeatRequest(Now, exact);
        Assert.Null((await EdgeApi.ReadHeartbeatAsync(await ContextAsync(fits))).Problem);
    }

    [Fact]
    public async Task UnknownFieldsAndAMissingUptimeAreFine()
    {
        using var message = VectorHub().HeartbeatRequest(Now, Encoding.UTF8.GetBytes("{\"later\":{\"a\":[1]},\"protocolVersion\":1}"));

        var read = await EdgeApi.ReadHeartbeatAsync(await ContextAsync(message));

        Assert.Null(read.Problem);
        Assert.Null(read.Request!.UptimeMs);
    }

    [Fact]
    public async Task HeadersAreCheckedBeforeTheBody()
    {
        using var message = VectorHub().HeartbeatRequest(Now, Encoding.UTF8.GetBytes("not json"));
        message.Headers.Remove("X-Coldframe-Signature");

        var read = await EdgeApi.ReadHeartbeatAsync(await ContextAsync(message));

        await AssertProblemAsync(read, StatusCodes.Status401Unauthorized, EdgeProblems.DeviceUnauthorized);
    }

    [Fact]
    public async Task AnAcceptedHeartbeatAnswersTheServerTime()
    {
        var time = new FakeTimeProvider(Now);

        var (context, body) = await ExecuteAsync(EdgeApi.ToHttpResult(new DeviceHeartbeatResult(DeviceHeartbeatOutcome.Accepted), time));

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.StartsWith("application/json", context.Response.ContentType, StringComparison.Ordinal);
        Assert.Equal(["serverTime"], body.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal("2026-09-29T12:34:56.789Z", body.RootElement.GetProperty("serverTime").GetString());

        // Whole seconds still carry the milliseconds.
        time.SetUtcNow(new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero));
        var (_, midnight) = await ExecuteAsync(EdgeApi.ToHttpResult(new DeviceHeartbeatResult(DeviceHeartbeatOutcome.Accepted), time));
        Assert.Equal("2026-09-30T00:00:00.000Z", midnight.RootElement.GetProperty("serverTime").GetString());
    }

    [Fact]
    public async Task ARefusedHeartbeatIsDeviceUnauthorizedWithoutAReason()
    {
        var (context, body) = await ExecuteAsync(EdgeApi.ToHttpResult(
            new DeviceHeartbeatResult(DeviceHeartbeatOutcome.Unauthorized),
            new FakeTimeProvider(Now)));

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.StartsWith(EdgeProblems.ContentType, context.Response.ContentType, StringComparison.Ordinal);
        Assert.Equal(EdgeProblems.DeviceUnauthorized, body.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public void TheHeartbeatIsAnonymousToJwtAndDeclaresTheDeviceRule()
    {
        var heartbeat = Assert.Single(EdgeEndpointCatalog.Describe(), operation => operation.Key == "POST /device/heartbeat");

        Assert.Equal(EdgeAccessRule.DeviceCaller, heartbeat.Rule);
        Assert.Equal("Device", heartbeat.Rule.ToString());
        Assert.True(heartbeat.Rule.IsDevice);
        Assert.Null(heartbeat.Rule.MinimumRole);
    }

    private static async Task<DefaultHttpContext> ContextAsync(HttpRequestMessage message)
    {
        var body = message.Content is null ? [] : await message.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        var context = new DefaultHttpContext();
        context.Request.Method = message.Method.Method;
        context.Request.Path = message.RequestUri!.OriginalString;
        foreach (var header in message.Headers)
        {
            context.Request.Headers[header.Key] = header.Value.ToArray();
        }

        context.Request.ContentType = message.Content?.Headers.ContentType?.ToString();
        context.Request.ContentLength = body.Length;
        context.Request.Body = new MemoryStream(body);
        return context;
    }

    private static async Task AssertProblemAsync(HeartbeatRead read, int status, string type)
    {
        Assert.Null(read.Request);
        var (context, body) = await ExecuteAsync(Assert.IsAssignableFrom<IResult>(read.Problem));

        Assert.Equal(status, context.Response.StatusCode);
        Assert.Equal(type, body.RootElement.GetProperty("type").GetString());
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
