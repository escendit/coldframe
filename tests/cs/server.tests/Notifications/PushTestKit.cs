using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Notifications;
using Coldframe.Contracts.Sites;
using Coldframe.Server.Notifications.Push;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Coldframe.Server.Tests.Notifications;

/// <summary>
/// One request a stub push provider received.
/// </summary>
internal sealed record ProviderRequest(HttpMethod Method, Uri Uri, Version Version, IReadOnlyDictionary<string, string> Headers, string Body)
{
    public JsonNode? Json => JsonNode.Parse(Body);
}

/// <summary>
/// A push provider stub: records every request and answers with what the test says.
/// </summary>
internal sealed class StubProvider(Func<ProviderRequest, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler, IHttpClientFactory
{
    public StubProvider(Func<ProviderRequest, HttpResponseMessage> respond)
        : this((request, _) => Task.FromResult(respond(request)))
    {
    }

    /// <summary>
    /// A provider that never answers: the request ends only when it is cancelled.
    /// </summary>
    public static StubProvider Silent() => new(async (_, cancellationToken) =>
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("Unreachable.");
    });

    private readonly ConcurrentQueue<ProviderRequest> _requests = new();

    public IReadOnlyList<ProviderRequest> Requests => [.. _requests];

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers
            .Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            .ToDictionary(header => header.Key.ToLowerInvariant(), header => string.Join(",", header.Value), StringComparer.Ordinal);
        var recorded = new ProviderRequest(request.Method, request.RequestUri!, request.Version, headers, body);
        _requests.Enqueue(recorded);

        return await respond(recorded, cancellationToken);
    }
}

/// <summary>
/// The names and values a test says the Server knows.
/// </summary>
internal sealed class FakeLookups : IPushLookups
{
    public string? SiteName { get; set; } = "Home garden";

    public Dictionary<string, string> Lots { get; } = new(StringComparer.Ordinal);

    public Dictionary<Guid, PushReading> Readings { get; } = [];

    public Dictionary<(Guid Sensor, ThresholdSide Side), PushValue> Thresholds { get; } = [];

    public bool Fail { get; set; }

    /// <summary>
    /// Only the Lot names cannot be read, as when the database stalls.
    /// </summary>
    public bool FailLots { get; set; }

    /// <summary>
    /// The Threshold lookup never answers.
    /// </summary>
    public bool HangThresholds { get; set; }

    public int Calls { get; private set; }

    public Task<string?> SiteNameAsync(string siteId, string userId, CancellationToken cancellationToken) => Answer(SiteName);

    public Task<string?> LotNameAsync(string siteId, string lotId, CancellationToken cancellationToken) =>
        FailLots ? Task.FromException<string?>(new InvalidOperationException("The lots read model is down.")) : Answer(Lots.GetValueOrDefault(lotId));

    public Task<PushReading?> NewestReadingAsync(Guid sensorId, string quantity, DateTimeOffset since, CancellationToken cancellationToken) =>
        Answer(Readings.GetValueOrDefault(sensorId) is { } reading && reading.MeasuredAt >= since ? reading : null);

    public Task<PushValue?> ThresholdAsync(Guid sensorId, ThresholdSide side, CancellationToken cancellationToken) =>
        HangThresholds ? new TaskCompletionSource<PushValue?>().Task : Answer(Thresholds.GetValueOrDefault((sensorId, side)));

    private Task<T?> Answer<T>(T? value)
        where T : class
    {
        Calls++;

        return Fail ? Task.FromException<T?>(new InvalidOperationException("The lookup is down.")) : Task.FromResult(value);
    }
}

/// <summary>
/// The notifications, lookups and fixtures the push tests share. The notifications are the ones the examples of
/// <c>packages/asyncapi/fixtures</c> were written for.
/// </summary>
internal static class PushTestKit
{
    public const string User = "5b0c7c1e-2a4d-4f1b-8e3a-9d7f6c5b4a31";

    public const string Site = "0199c1f0-5a00-7000-8000-000000000001";

    public const string TomatoesLot = "0199c1f0-5a00-7000-8000-0000000000a1";

    public const string ApnsToken = "0011223300112233001122330011223300112233001122330011223300112233";

    public const string FcmToken = "dQw4w9WgXcQ:APA91bExampleRegistrationToken";

    public static readonly Guid TomatoesAlert = Guid.Parse("5b0c0d6e-3f1a-5c2b-9d4e-7a8b9c0d1e2f");

    public static readonly Guid TomatoesSoil = Guid.Parse("dac4e7fe-93b1-56fc-a363-51235a586394");

    public static readonly DateTimeOffset AlertDueAt = new(2026, 10, 9, 5, 45, 0, TimeSpan.Zero);

    public static readonly PushRegistration IPhone = new("iphone", PushPlatform.Apns, ApnsToken, ApnsEnvironment.Production, AlertDueAt.AddDays(-3));

    public static readonly PushRegistration Android = new("android", PushPlatform.Fcm, FcmToken, null, AlertDueAt.AddDays(-2));

    public static NotificationEntry Entry(
        string lotId,
        string quantity = "soil_moisture",
        ThresholdSide side = ThresholdSide.Low,
        Guid? alertId = null,
        Guid? sensorId = null) =>
        new(alertId ?? Guid.NewGuid(), AlertKind.Threshold, side, lotId, sensorId ?? Guid.NewGuid(), "5a4b3c2d1e0f7c20", quantity, AlertDueAt.AddMinutes(-30));

    /// <summary>
    /// The Alert of <c>push.alert.json</c>: Tomatoes at ~20 %, low 30 %, for a User in Zurich with both phones.
    /// </summary>
    public static Notification Alert(params PushRegistration[] registrations) =>
        new(
            User,
            Site,
            NotificationKind.Alert,
            AlertDueAt,
            HeldFrom: null,
            [Entry(TomatoesLot, alertId: TomatoesAlert, sensorId: TomatoesSoil)],
            registrations,
            "Europe/Zurich",
            NotificationWindow.Default);

    /// <summary>
    /// The Reminder of <c>push.reminder.json</c>: the same Alert one day later.
    /// </summary>
    public static Notification Reminder(params PushRegistration[] registrations) =>
        Alert(registrations) with { Kind = NotificationKind.Reminder, DueAt = AlertDueAt.AddDays(1) };

    /// <summary>
    /// The summary of <c>push.summary.json</c>: five Alerts held overnight, in the User grain's order (oldest
    /// first), two of them "needs water".
    /// </summary>
    public static Notification Summary(FakeLookups lookups, params PushRegistration[] registrations)
    {
        var entries = new[]
        {
            Named(lookups, "Herbs", Entry("lot-herbs", side: ThresholdSide.High)),
            Named(lookups, "Tomatoes", Entry(TomatoesLot)),
            Named(lookups, "Beans", Entry("lot-beans", "air_temperature")),
            Named(lookups, "Peppers", Entry("lot-peppers")),
            Named(lookups, "Lettuce", Entry("lot-lettuce", "relative_humidity", ThresholdSide.High)),
        };
        lookups.Thresholds[(entries[2].SensorId, ThresholdSide.Low)] = new PushValue(5, "°C");
        lookups.Thresholds[(entries[4].SensorId, ThresholdSide.High)] = new PushValue(90, "%");

        return new Notification(
            User,
            Site,
            NotificationKind.Summary,
            new DateTimeOffset(2026, 10, 9, 5, 0, 0, TimeSpan.Zero),
            AlertDueAt.AddHours(-6),
            entries,
            registrations,
            "Europe/Zurich",
            NotificationWindow.Default);
    }

    /// <summary>
    /// What the Server knows for <see cref="Alert"/>: the Site, the Lot, the Reading and the low Threshold.
    /// </summary>
    public static FakeLookups TomatoesLookups()
    {
        var lookups = new FakeLookups();
        lookups.Lots[TomatoesLot] = "Tomatoes";
        lookups.Readings[TomatoesSoil] = new PushReading(new PushValue(20, "%"), AlertDueAt.AddMinutes(-30));
        lookups.Thresholds[(TomatoesSoil, ThresholdSide.Low)] = new PushValue(30, "%");

        return lookups;
    }

    public static PushContentBuilder Builder(IPushLookups lookups, PushOptions? options = null, TimeProvider? clock = null) =>
        new(lookups, Options.Create(options ?? TestOptions()), clock ?? new FakeTimeProvider(AlertDueAt), NullLogger<PushContentBuilder>.Instance);

    /// <summary>
    /// The defaults with no pause between tries: a fake clock never ends a delay.
    /// </summary>
    public static PushOptions TestOptions() => new() { RetryDelay = TimeSpan.Zero };

    public static JsonNode Fixture(string name) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "asyncapi", name)))
        ?? throw new InvalidOperationException($"The fixture {name} is empty.");

    public static DateTimeOffset SentAt(JsonNode fixture) =>
        fixture["notification"]!["sentAt"]!.GetValue<DateTimeOffset>();

    /// <summary>
    /// Moves <paramref name="clock"/> on, a second at a time, until <paramref name="task"/> ends: whatever
    /// waits on the fake clock (a budget) runs out. Fails when a minute of fake time does not end it.
    /// </summary>
    public static async Task AdvanceUntilDoneAsync(Microsoft.Extensions.Time.Testing.FakeTimeProvider clock, Task task)
    {
        for (var second = 0; second < 60 && !task.IsCompleted; second++)
        {
            await Task.WhenAny(task, Task.Delay(TimeSpan.FromMilliseconds(20), TestContext.Current.CancellationToken));
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.True(task.IsCompleted, "A minute of fake time did not end the wait.");
    }

    public static void AssertJsonEqual(JsonNode? expected, JsonNode? actual) =>
        Assert.True(
            JsonNode.DeepEquals(expected, actual),
            $"Expected:\n{expected?.ToJsonString(Indented)}\nActual:\n{actual?.ToJsonString(Indented)}");

    public static string NewP256Pem()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return key.ExportPkcs8PrivateKeyPem();
    }

    public static (string Json, RSA PublicKey) NewServiceAccount(string? tokenUri = null)
    {
        using var key = RSA.Create(2048);
        var publicKey = RSA.Create();
        publicKey.ImportRSAPublicKey(key.ExportRSAPublicKey(), out _);
        var account = new JsonObject
        {
            ["type"] = "service_account",
            ["project_id"] = "coldframe-test",
            ["private_key_id"] = "key-1",
            ["private_key"] = key.ExportPkcs8PrivateKeyPem(),
            ["client_email"] = "push@coldframe-test.iam.gserviceaccount.com",
        };

        if (tokenUri is not null)
        {
            account["token_uri"] = tokenUri;
        }

        return (account.ToJsonString(), publicKey);
    }

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static NotificationEntry Named(FakeLookups lookups, string name, NotificationEntry entry)
    {
        lookups.Lots[entry.LotId] = name;
        return entry;
    }
}

/// <summary>
/// Records what a channel logs.
/// </summary>
internal sealed class ChannelLog<T> : Microsoft.Extensions.Logging.ILogger<T>
{
    public List<(Microsoft.Extensions.Logging.LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public void Log<TState>(
        Microsoft.Extensions.Logging.LogLevel logLevel,
        Microsoft.Extensions.Logging.EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        Entries.Add((logLevel, formatter(state, exception)));
    }
}
