using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coldframe.DeviceSimulator;
using Coldframe.Protocol.Device.V1;
using Microsoft.Playwright;
using ProtocolKind = Coldframe.Protocol.Setup.V1.DeviceKind;

namespace Coldframe.Web.E2ETests;

/// <summary>
/// What every journey on the real stack does (Stories 6.6 and 7.2): the Edge API as the apps call it, the Device
/// simulator as a Hub and its Nodes, and the Server's test-only clock. The journeys share one stack
/// (<see cref="MorningNudgeFixture"/>) and one clock that only moves forward, so they run one after another
/// (<see cref="Collection"/>), and each takes its times from the Server's clock as it finds it.
/// </summary>
/// <param name="stack">The running stack.</param>
public abstract class JourneyTests(MorningNudgeFixture stack)
{
    /// <summary>
    /// The test collection of every journey: the clock is one, so no two journeys run at once.
    /// </summary>
    public const string Collection = "journeys";

    // window.Notification replaced by a recorder. The permission starts as "default", is granted when the page
    // asks, and is kept across page loads like a browser keeps it; every request and every notification is listed.
    protected const string RecordNotifications =
        """
        (() => {
          const read = (key) => JSON.parse(window.localStorage.getItem(key) ?? '[]');
          const add = (key, value) => window.localStorage.setItem(key, JSON.stringify([...read(key), value]));
          class RecordingNotification extends EventTarget {
            static get permission() { return window.localStorage.getItem('__cf_permission') ?? 'default'; }
            static requestPermission() {
              add('__cf_permission_requests', window.location.pathname);
              window.localStorage.setItem('__cf_permission', 'granted');
              return Promise.resolve('granted');
            }
            constructor(title, options = {}) {
              super();
              this.onclick = null;
              add('__cf_notifications', { title, body: options.body ?? '', tag: options.tag ?? '' });
            }
            close() {}
          }
          Object.defineProperty(window, 'Notification', { configurable: true, writable: true, value: RecordingNotification });
        })();
        """;

    /// <summary>
    /// How long a journey waits for what the Server does on its own timers.
    /// </summary>
    protected static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The running stack.
    /// </summary>
    protected MorningNudgeFixture Stack => stack;

    /// <summary>
    /// Cancels with the running test.
    /// </summary>
    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);

    // The next time the wall clock of the zone shows the given time, strictly after the given instant.
    protected static DateTimeOffset NextLocal(TimeZoneInfo zone, DateTimeOffset after, TimeOnly time)
    {
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(after, zone).DateTime);

        while (true)
        {
            var local = day.ToDateTime(time, DateTimeKind.Unspecified);

            if (!zone.IsInvalidTime(local))
            {
                var instant = new DateTimeOffset(local, zone.GetUtcOffset(local));

                if (instant > after)
                {
                    return instant;
                }
            }

            day = day.AddDays(1);
        }
    }

    protected static async Task<IReadOnlyList<JsonElement>> ListAsync(IPage page, string key)
    {
        var json = await page.EvaluateAsync<string>("key => window.localStorage.getItem(key) ?? '[]'", key);
        using var document = JsonDocument.Parse(json);

        return [.. document.RootElement.EnumerateArray().Select(element => element.Clone())];
    }

    // Waits for what the Server does on its own timers, and wakes the User's grain meanwhile as its reminder would.
    protected async Task UntilAsync<T>(JourneyUser user, Func<Task<T>> read, Func<T, bool> done, string what)
    {
        var deadline = TimeProvider.System.GetUtcNow() + Patience;

        while (true)
        {
            await WakeUserAsync(user);

            if (done(await read()))
            {
                return;
            }

            Assert.True(TimeProvider.System.GetUtcNow() < deadline, $"Waited {Patience.TotalSeconds} s and it did not happen: {what}.");
            await Task.Delay(TimeSpan.FromMilliseconds(500), Ct);
        }
    }

    protected async Task<int> JournalCountAsync(JourneyUser user, string alias)
    {
        await using var command = Stack.Database.CreateCommand(
            "SELECT COUNT(*) FROM journal_events WHERE stream_id = @stream_id AND type_alias = @alias");
        command.Parameters.AddWithValue("stream_id", $"user/{user.UserId}");
        command.Parameters.AddWithValue("alias", alias);

        return (int)(long)(await command.ExecuteScalarAsync(Ct))!;
    }

    protected async Task<DateTimeOffset> ServerNowAsync(JourneyUser user)
    {
        using var server = await Stack.CreateServerClientAsync(user, Ct);
        using var response = await server.GetAsync(new Uri("/testing/clock", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));

        // To the millisecond, as a frame carries it.
        return DateTimeOffset.FromUnixTimeMilliseconds(body.RootElement.GetProperty("now").GetDateTimeOffset().ToUnixTimeMilliseconds());
    }

    protected async Task SetClockAsync(JourneyUser user, DateTimeOffset now)
    {
        using var response = await SendAsync(user, HttpMethod.Post, "/testing/clock", new { now });
    }

    protected async Task WakeUserAsync(JourneyUser user)
    {
        using var server = await Stack.CreateServerClientAsync(user, Ct);
        using var response = await server.PostAsync(new Uri($"/testing/users/{user.UserId}/wake", UriKind.Relative), content: null, Ct);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    protected async Task<string> PostAsync(JourneyUser user, string path, object body, bool idempotent)
    {
        using var response = await SendAsync(user, HttpMethod.Post, path, body, idempotent);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));

        return json.RootElement.GetProperty("id").GetString()!;
    }

    protected async Task<HttpResponseMessage> SendAsync(JourneyUser user, HttpMethod method, string path, object body, bool idempotent = false)
    {
        using var server = await Stack.CreateServerClientAsync(user, Ct);
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = JsonContent.Create(body) };

        if (idempotent)
        {
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        }

        var response = await server.SendAsync(request, Ct);
        Assert.True(response.IsSuccessStatusCode, $"{method} {path} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");

        return response;
    }

    protected async Task<SimulatedDevice> EnrolAsync(JourneyUser user, string siteId, ProtocolKind kind, string? lotId)
    {
        using var server = await Stack.CreateServerClientAsync(user, Ct);
        using var key = await server.GetAsync(new Uri("/enrolment-key", UriKind.Relative), Ct);
        using var keyBody = JsonDocument.Parse(await key.Content.ReadAsStringAsync(Ct));
        var publicKey = Base64Url.DecodeFromChars(keyBody.RootElement.GetProperty("publicKey").GetString());

        var device = SimulatedDevice.Create(kind);
        var enrolment = device.SealEnrolment(publicKey, siteId);
        var body = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["deviceId"] = enrolment.DeviceId,
            ["kind"] = enrolment.Kind,
            ["enc"] = enrolment.Enc,
            ["ciphertext"] = enrolment.Ciphertext,
        };

        if (lotId is not null)
        {
            body["lotId"] = lotId;
        }

        using var enrolled = await SendAsync(user, HttpMethod.Post, $"/sites/{siteId}/devices", body, idempotent: true);

        return device;
    }

    // One heartbeat of the Hub, stamped from the Server's clock: the Hub is alive, whatever the clock jumped over.
    protected async Task HeartbeatAsync(JourneyUser user, SimulatedDevice hub)
    {
        using var server = Stack.CreateDeviceClient();
        using var request = hub.HeartbeatRequest(await ServerNowAsync(user));
        using var response = await server.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // One wake of the Node, relayed by the Hub and stamped from the Server's clock. The Node follows the
    // downlink, so it attaches its Specification set when the Server asks. Returns the soil Reading's reading_seq.
    protected async Task<ulong> WakeAsync(
        JourneyUser user,
        SimulatedDevice hub,
        SimulatedDevice node,
        long soilRaw,
        DateTimeOffset measuredAt,
        uint? batteryPercent = 87,
        ChargeStatus charging = ChargeStatus.Charging)
    {
        var readings = SimulatedDevice.DefaultReadings
            .Select(reading => reading.Quantity == Quantity.SoilMoisture ? reading with { Value = soilRaw } : reading)
            .ToList();
        var frame = node.Wake(measuredAt, batteryPercent, charging, readings);

        using var server = Stack.CreateDeviceClient();
        using var request = hub.IngestRequest(await ServerNowAsync(user), SimulatedDevice.IngestBody(node.SealFrame(frame)));
        using var response = await server.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = Assert.Single(SimulatedDevice.ReadIngestResponse(await response.Content.ReadAsByteArrayAsync(Ct)));
        Assert.Equal("stored", result.Status);
        node.OpenDownlink(result.Downlink!);

        return frame.Readings.Single(reading => reading.Quantity == Quantity.SoilMoisture).ReadingSeq;
    }
}
