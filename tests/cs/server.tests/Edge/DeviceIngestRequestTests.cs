using System.Text;
using System.Text.Json;
using Coldframe.Contracts.Devices;
using Coldframe.Crypto;
using Coldframe.DeviceSimulator;
using Coldframe.Protocol.Device.V1;
using Coldframe.Server.Edge;
using Coldframe.Server.Tests.Devices;
using Google.Protobuf;
using Microsoft.AspNetCore.Http;
using ProtocolKind = Coldframe.Protocol.Setup.V1.DeviceKind;

namespace Coldframe.Server.Tests.Edge;

/// <summary>
/// What <c>POST /device/ingest</c> checks before a grain sees a request (headers → 401
/// <c>device-unauthorized</c>, envelope → 400 <c>validation</c>), how it decodes a frame, and how it answers
/// the grains (AD-9). Requests are built with the Device simulator only.
/// </summary>
public sealed class DeviceIngestRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 34, 56, 789, TimeSpan.Zero);

    private static readonly string ContractPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "coldframe.openapi.json");

    private static SimulatedDevice VectorHub() => SimulatedDevice.Create(Vectors.Heartbeat(0).Bytes("rootKey"));

    [Fact]
    public async Task ASignedEnvelopeReadsIntoTheHubsGrainRequestAndItsFrames()
    {
        var hub = VectorHub();
        var node = SimulatedDevice.Create(ProtocolKind.Node);
        var first = node.SealFrame(node.Wake(Now));
        var second = node.SealFrame(node.Wake(Now));
        var body = SimulatedDevice.IngestBody(first, second);
        var nonce = Enumerable.Range(0, CryptoSpec.HeartbeatNonceLength).Select(index => (byte)index).ToArray();
        using var message = hub.IngestRequest(Now.ToUnixTimeMilliseconds(), nonce, body);

        var read = await EdgeApi.ReadIngestAsync(await ContextAsync(message));

        Assert.Null(read.Problem);
        Assert.Equal(hub.DeviceId, read.HubId);
        var request = Assert.IsType<DeviceRelayAuthentication>(read.Request);
        Assert.Equal(("POST", "/device/ingest"), (request.Method, request.Path));
        Assert.Equal(body, request.Body);
        Assert.Equal(Now.ToUnixTimeMilliseconds(), request.TimestampMs);
        Assert.Equal(nonce, request.Nonce);
        Assert.True(Heartbeat.Verify(hub.Keys.HubAuthKey, request.Method, request.Path, request.Body, request.TimestampMs, request.Nonce, request.Signature));
        Assert.Equal([SimulatedDevice.EncodeFrame(first), SimulatedDevice.EncodeFrame(second)], read.Frames);
    }

    [Theory]
    [InlineData("X-Coldframe-Device")]
    [InlineData("X-Coldframe-Timestamp")]
    [InlineData("X-Coldframe-Nonce")]
    [InlineData("X-Coldframe-Signature")]
    public async Task AMissingHeaderIsDeviceUnauthorizedEvenWithABadEnvelope(string name)
    {
        using var message = VectorHub().IngestRequest(Now, Encoding.UTF8.GetBytes("not json"));
        message.Headers.Remove(name);

        var read = await EdgeApi.ReadIngestAsync(await ContextAsync(message));

        await AssertProblemAsync(read, StatusCodes.Status401Unauthorized, EdgeProblems.DeviceUnauthorized);
    }

    public static TheoryData<string> BadEnvelopes() => new()
    {
        "",
        "not json",
        "[]",
        "{}",
        "{\"frames\":null}",
        "{\"frames\":\"AAAA\"}",
        "{\"frames\":{}}",
        "{\"frames\":[1]}",
        "{\"frames\":[null]}",
        "{\"frames\":[\"AAAA\",[\"AAAA\"]]}",
        "{\"frames\":[]} trailing",
        "{\"Frames\":[]}",
        "{\"frames\":[\"\\uD800\"]}",
        "{\"frames\":[\"AAAA\",\"\\uDC00AAA\"]}",
    };

    [Theory]
    [MemberData(nameof(BadEnvelopes))]
    public async Task ABodyThatIsNotAnEnvelopeIsValidation(string body)
    {
        using var message = VectorHub().IngestRequest(Now, Encoding.UTF8.GetBytes(body));

        var read = await EdgeApi.ReadIngestAsync(await ContextAsync(message));

        await AssertProblemAsync(read, StatusCodes.Status400BadRequest, EdgeProblems.Validation);
    }

    [Fact]
    public async Task ThirtyTwoFramesAreReadAndOneMoreIsValidation()
    {
        var most = SimulatedDevice.IngestBody(Enumerable.Repeat("AAAA", EdgeValidation.MaxIngestFrames));
        using var fits = VectorHub().IngestRequest(Now, most);
        var read = await EdgeApi.ReadIngestAsync(await ContextAsync(fits));
        Assert.Null(read.Problem);
        Assert.Equal(EdgeValidation.MaxIngestFrames, read.Frames!.Count);

        var tooMany = SimulatedDevice.IngestBody(Enumerable.Repeat("AAAA", EdgeValidation.MaxIngestFrames + 1));
        using var message = VectorHub().IngestRequest(Now, tooMany);
        await AssertProblemAsync(await EdgeApi.ReadIngestAsync(await ContextAsync(message)), StatusCodes.Status400BadRequest, EdgeProblems.Validation);
    }

    [Fact]
    public async Task ABodyOver16KiBOrAFrameOver1024CharactersIsValidation()
    {
        Assert.Equal(16_384, EdgeValidation.MaxIngestBodyLength);
        var padded = Encoding.UTF8.GetBytes("{\"frames\":[]}" + new string(' ', EdgeValidation.MaxIngestBodyLength));
        using var tooLarge = VectorHub().IngestRequest(Now, padded);
        await AssertProblemAsync(await EdgeApi.ReadIngestAsync(await ContextAsync(tooLarge)), StatusCodes.Status400BadRequest, EdgeProblems.Validation);

        // Without a Content-Length, the reader stops at the cap all the same.
        using var chunked = VectorHub().IngestRequest(Now, padded);
        var context = await ContextAsync(chunked);
        context.Request.ContentLength = null;
        await AssertProblemAsync(await EdgeApi.ReadIngestAsync(context), StatusCodes.Status400BadRequest, EdgeProblems.Validation);

        var exact = Encoding.UTF8.GetBytes("{\"frames\":[]}" + new string(' ', EdgeValidation.MaxIngestBodyLength - 13));
        Assert.Equal(EdgeValidation.MaxIngestBodyLength, exact.Length);
        using var fits = VectorHub().IngestRequest(Now, exact);
        Assert.Null((await EdgeApi.ReadIngestAsync(await ContextAsync(fits))).Problem);

        using var longest = VectorHub().IngestRequest(Now, SimulatedDevice.IngestBody([new string('A', EdgeValidation.MaxIngestFrameLength)]));
        Assert.Null((await EdgeApi.ReadIngestAsync(await ContextAsync(longest))).Problem);
        using var tooLong = VectorHub().IngestRequest(Now, SimulatedDevice.IngestBody([new string('A', EdgeValidation.MaxIngestFrameLength + 4)]));
        await AssertProblemAsync(await EdgeApi.ReadIngestAsync(await ContextAsync(tooLong)), StatusCodes.Status400BadRequest, EdgeProblems.Validation);
    }

    [Fact]
    public async Task UnknownPropertiesAndAnEmptyEnvelopeAreFine()
    {
        using var message = VectorHub().IngestRequest(Now, Encoding.UTF8.GetBytes("{\"later\":{\"a\":[1]},\"frames\":[]}"));

        var read = await EdgeApi.ReadIngestAsync(await ContextAsync(message));

        Assert.Null(read.Problem);
        Assert.Empty(read.Frames!);
    }

    [Fact]
    public void AFrameDecodesIntoItsNodesGrainRequest()
    {
        var node = SimulatedDevice.Create(ProtocolKind.Node);
        var envelope = node.SealFrame(41, node.Wake(Now));

        var decoded = EdgeApi.DecodeFrame(SimulatedDevice.EncodeFrame(envelope), "92064422c012f481");

        Assert.NotNull(decoded);
        Assert.Equal(node.DeviceId.ToString(), decoded.Value.DeviceId);
        Assert.Equal(CryptoSpec.ProtocolMajor, decoded.Value.Request.ProtocolVersion);
        Assert.Equal(41UL, decoded.Value.Request.Counter);
        Assert.Equal(envelope.Ciphertext.ToByteArray(), decoded.Value.Request.Ciphertext);
        Assert.Equal("92064422c012f481", decoded.Value.Request.RelayHubId);
    }

    [Fact]
    public void WhatIsNotASealedEnvelopeOfADeviceDecodesToNothing()
    {
        var node = SimulatedDevice.Create(ProtocolKind.Node);
        var envelope = node.SealFrame(node.Wake(Now));
        var encoded = SimulatedDevice.EncodeFrame(envelope);

        Assert.Null(EdgeApi.DecodeFrame("not base64!", "hub"));
        Assert.Null(EdgeApi.DecodeFrame("-_-_", "hub"));
        Assert.Null(EdgeApi.DecodeFrame(" " + encoded, "hub"));
        Assert.Null(EdgeApi.DecodeFrame(encoded[..^1], "hub"));
        Assert.Null(EdgeApi.DecodeFrame(Convert.ToBase64String([0xFF, 0xFF, 0xFF, 0xFF]), "hub"));
        Assert.Null(EdgeApi.DecodeFrame(string.Empty, "hub"));

        var shortId = envelope.Clone();
        shortId.DeviceId = ByteString.CopyFrom(node.DeviceId.ToBytes().AsSpan(0, 7));
        Assert.Null(EdgeApi.DecodeFrame(SimulatedDevice.EncodeFrame(shortId), "hub"));
        Assert.Null(EdgeApi.DecodeFrame(SimulatedDevice.EncodeFrame(new SealedEnvelope()), "hub"));
    }

    [Fact]
    public async Task ResultsAreAnsweredInOrderWithADownlinkOnlyOnStoredAndDuplicate()
    {
        byte[] downlink = [1, 2, 3, 250];
        DeviceIngestResult[] results =
        [
            new(DeviceIngestStatus.Stored, downlink),
            new(DeviceIngestStatus.RejectedAuth),
            new(DeviceIngestStatus.Duplicate, downlink),
            new(DeviceIngestStatus.RejectedReplay),
            new(DeviceIngestStatus.RejectedTime),
            new(DeviceIngestStatus.UnknownDevice),
            new(DeviceIngestStatus.Retry, downlink),
        ];

        var (context, body) = await ExecuteAsync(EdgeApi.ToHttpResult(results));

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.StartsWith("application/json", context.Response.ContentType, StringComparison.Ordinal);
        Assert.Equal(["results"], body.RootElement.EnumerateObject().Select(property => property.Name));
        var answered = body.RootElement.GetProperty("results").EnumerateArray().ToList();
        Assert.Equal(
            ["stored", "rejected_auth", "duplicate", "rejected_replay", "rejected_time", "unknown_device", "retry"],
            answered.Select(result => result.GetProperty("status").GetString()));

        // An absent downlink is omitted, never null; a status that carries none never shows one.
        Assert.Equal(
            [true, false, true, false, false, false, false],
            answered.Select(result => result.TryGetProperty("downlink", out _)));
        Assert.Equal(downlink, answered[0].GetProperty("downlink").GetBytesFromBase64());
        Assert.All(answered, result => Assert.All(result.EnumerateObject(), property => Assert.True(property.Name is "status" or "downlink", property.Name)));
    }

    [Fact]
    public async Task ANodeGrainThatThrowsIsRetryForItsFrameOnly()
    {
        var first = SimulatedDevice.Create(ProtocolKind.Node);
        var failing = SimulatedDevice.Create(ProtocolKind.Node);
        var third = SimulatedDevice.Create(ProtocolKind.Node);
        string[] frames =
        [
            SimulatedDevice.EncodeFrame(first.SealFrame(first.Wake(Now))),
            SimulatedDevice.EncodeFrame(failing.SealFrame(failing.Wake(Now))),
            SimulatedDevice.EncodeFrame(third.SealFrame(third.Wake(Now))),
        ];
        var grains = new StubGrainFactory(failing.DeviceId.ToString());

        var results = await EdgeApi.IngestFramesAsync(grains, frames, "92064422c012f481", Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        var (context, body) = await ExecuteAsync(EdgeApi.ToHttpResult(results));

        // Every grain was asked, in request order, and the failure stayed with its frame.
        Assert.Equal([first.DeviceId.ToString(), failing.DeviceId.ToString(), third.DeviceId.ToString()], grains.Asked);

        // A Hub that hangs up cannot cancel a frame: no grain call carries a token that can be cancelled.
        Assert.Equal([false, false, false], grains.Cancellable);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        var answered = body.RootElement.GetProperty("results").EnumerateArray().ToList();
        Assert.Equal(["stored", "retry", "stored"], answered.Select(result => result.GetProperty("status").GetString()));
        Assert.Equal(first.DeviceId.ToBytes(), answered[0].GetProperty("downlink").GetBytesFromBase64());
        Assert.False(answered[1].TryGetProperty("downlink", out _));
        Assert.Equal(third.DeviceId.ToBytes(), answered[2].GetProperty("downlink").GetBytesFromBase64());
    }

    [Fact]
    public async Task AnEmptyEnvelopeAnswersAnEmptyResultList()
    {
        var (context, body) = await ExecuteAsync(EdgeApi.ToHttpResult([]));

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(0, body.RootElement.GetProperty("results").GetArrayLength());
    }

    [Fact]
    public async Task OnlyWhenEveryFrameIsRetryTheAnswerIsIngestUnavailable()
    {
        var (unavailable, problem) = await ExecuteAsync(EdgeApi.ToHttpResult(
            [new DeviceIngestResult(DeviceIngestStatus.Retry), new DeviceIngestResult(DeviceIngestStatus.Retry)]));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, unavailable.Response.StatusCode);
        Assert.StartsWith(EdgeProblems.ContentType, unavailable.Response.ContentType, StringComparison.Ordinal);
        Assert.Equal(EdgeProblems.IngestUnavailable, problem.RootElement.GetProperty("type").GetString());

        var (mixed, _) = await ExecuteAsync(EdgeApi.ToHttpResult(
            [new DeviceIngestResult(DeviceIngestStatus.Retry), new DeviceIngestResult(DeviceIngestStatus.RejectedReplay)]));
        Assert.Equal(StatusCodes.Status200OK, mixed.Response.StatusCode);
    }

    [Fact]
    public void EveryFrameStatusHasExactlyTheContractsName()
    {
        using var contract = JsonDocument.Parse(File.ReadAllText(ContractPath));
        var statuses = contract.RootElement
            .GetProperty("components").GetProperty("schemas").GetProperty("IngestFrameStatus").GetProperty("enum")
            .EnumerateArray().Select(status => status.GetString()).ToList();

        Assert.Equal(statuses, Enum.GetValues<DeviceIngestStatus>().Select(EdgeValidation.IngestStatusName));

        var frames = contract.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("IngestRequest").GetProperty("properties").GetProperty("frames");
        Assert.Equal(EdgeValidation.MaxIngestFrames, frames.GetProperty("maxItems").GetInt32());
        Assert.Equal(
            EdgeValidation.MaxIngestFrameLength,
            contract.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("SealedFrame").GetProperty("maxLength").GetInt32());
    }

    [Fact]
    public void TheIngestIsAnonymousToJwtAndDeclaresTheDeviceRule()
    {
        var ingest = Assert.Single(EdgeEndpointCatalog.Describe(), operation => operation.Key == "POST /device/ingest");

        Assert.Equal(EdgeAccessRule.DeviceCaller, ingest.Rule);
        Assert.True(ingest.Rule.IsDevice);
        Assert.Null(ingest.Rule.MinimumRole);
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

    private static async Task AssertProblemAsync(IngestRead read, int status, string type)
    {
        Assert.Null(read.Request);
        Assert.Null(read.Frames);
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

    // A Node grain that answers stored with its own Device ID as the "downlink", or throws.
    private sealed class StubDeviceGrain(string deviceId, bool fails, List<string> asked, List<bool> cancellable) : IDeviceGrain
    {
        public Task<DeviceIngestResult> Ingest(DeviceIngest request, CancellationToken cancellationToken = default)
        {
            asked.Add(deviceId);
            cancellable.Add(cancellationToken.CanBeCanceled);
            return fails
                ? throw new TimeoutException("The grain did not answer.")
                : Task.FromResult(new DeviceIngestResult(DeviceIngestStatus.Stored, Convert.FromHexString(deviceId)));
        }

        public Task<DeviceEnrolmentResult> Enrol(EnrolDevice request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DeviceHeartbeatResult> Heartbeat(DeviceHeartbeat request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DeviceRelayAuthenticationResult> AuthenticateRelay(DeviceRelayAuthentication request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DeviceAssignmentResult> Move(string siteId, string lotId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DeviceAssignmentResult> Unassign(string siteId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DeviceSummary?> Describe(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SetCalibration(SetCalibration request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    // Only what the ingest handler uses: a Device grain by its string key.
    private sealed class StubGrainFactory(string failingDeviceId) : IGrainFactory
    {
        public List<string> Asked { get; } = [];

        public List<bool> Cancellable { get; } = [];

        public TGrainInterface GetGrain<TGrainInterface>(string primaryKey, string? grainClassNamePrefix = null)
            where TGrainInterface : IGrainWithStringKey =>
            (TGrainInterface)(object)new StubDeviceGrain(primaryKey, primaryKey == failingDeviceId, Asked, Cancellable);

        public TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string? grainClassNamePrefix = null)
            where TGrainInterface : IGrainWithGuidKey => throw new NotSupportedException();

        public TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string? grainClassNamePrefix = null)
            where TGrainInterface : IGrainWithIntegerKey => throw new NotSupportedException();

        public TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string keyExtension, string? grainClassNamePrefix = null)
            where TGrainInterface : IGrainWithGuidCompoundKey => throw new NotSupportedException();

        public TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string keyExtension, string? grainClassNamePrefix = null)
            where TGrainInterface : IGrainWithIntegerCompoundKey => throw new NotSupportedException();

        public TGrainObserverInterface CreateObjectReference<TGrainObserverInterface>(IGrainObserver obj)
            where TGrainObserverInterface : IGrainObserver => throw new NotSupportedException();

        public void DeleteObjectReference<TGrainObserverInterface>(IGrainObserver obj)
            where TGrainObserverInterface : IGrainObserver => throw new NotSupportedException();

        public IGrain GetGrain(Type grainInterfaceType, Guid grainPrimaryKey) => throw new NotSupportedException();

        public IGrain GetGrain(Type grainInterfaceType, long grainPrimaryKey) => throw new NotSupportedException();

        public IGrain GetGrain(Type grainInterfaceType, string grainPrimaryKey) => throw new NotSupportedException();

        public IGrain GetGrain(Type grainInterfaceType, Guid grainPrimaryKey, string keyExtension) => throw new NotSupportedException();

        public IGrain GetGrain(Type grainInterfaceType, long grainPrimaryKey, string keyExtension) => throw new NotSupportedException();

        public TGrainInterface GetGrain<TGrainInterface>(GrainId grainId)
            where TGrainInterface : IAddressable => throw new NotSupportedException();

        public IAddressable GetGrain(GrainId grainId) => throw new NotSupportedException();

        public IAddressable GetGrain(GrainId grainId, GrainInterfaceType interfaceType) => throw new NotSupportedException();

        public IAddressable GetGrain(Type interfaceType, IdSpan grainKey) => throw new NotSupportedException();

        public IAddressable GetGrain(Type interfaceType, IdSpan grainKey, string grainClassNamePrefix) => throw new NotSupportedException();
    }
}
