using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Coldframe.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Coldframe.Server.Tests.Live;

/// <summary>
/// The test-only clock (Story 6.6): on only in a Development host that sets <c>Testing:ManualClock</c>. Any
/// other host runs on the system clock and has no <c>/testing</c> endpoint, whatever the setting says.
/// </summary>
public sealed class ManualClockTests
{
    private const string User = "5b0c7c1e-2a4d-4f1b-8e3a-9d7f6c5b4a31";

    private static readonly Uri Clock = new("/testing/clock", UriKind.Relative);

    private static readonly Uri Wake = new($"/testing/users/{User}/wake", UriKind.Relative);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("Production", true)]
    [InlineData("Production", false)]
    [InlineData("Production", null)]
    [InlineData("Staging", true)]
    [InlineData("Development", false)]
    [InlineData("Development", null)]
    public async Task OutsideDevelopmentOrWithoutTheSettingTheClockIsTheSystemClockAndTestingAnswers404(string environment, bool? setting)
    {
        await using var host = await LiveTestHost.StartAsync(environment, setting, Ct);
        using var anonymous = host.CreateClient();
        using var signedIn = host.CreateClient(host.Token(User));

        Assert.Same(TimeProvider.System, host.Services.GetRequiredService<TimeProvider>());
        Assert.Null(host.Services.GetService<ManualClock>());

        using var read = await signedIn.GetAsync(Clock, Ct);
        using var set = await signedIn.PostAsJsonAsync(Clock, new { now = "2099-01-01T00:00:00Z" }, Ct);
        using var wake = await signedIn.PostAsync(Wake, content: null, Ct);

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, set.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, wake.StatusCode);

        // Without a token the Server answers every path it does not map alike (the fallback policy): nothing
        // tells /testing apart from a path that never existed.
        using var unmapped = await anonymous.GetAsync(new Uri("/no-such-path", UriKind.Relative), Ct);
        using var anonymousRead = await anonymous.GetAsync(Clock, Ct);
        using var anonymousSet = await anonymous.PostAsJsonAsync(Clock, new { now = "2099-01-01T00:00:00Z" }, Ct);
        using var anonymousWake = await anonymous.PostAsync(Wake, content: null, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, unmapped.StatusCode);
        Assert.Equal(
            (unmapped.StatusCode, unmapped.StatusCode, unmapped.StatusCode),
            (anonymousRead.StatusCode, anonymousSet.StatusCode, anonymousWake.StatusCode));
    }

    [Fact]
    public async Task InDevelopmentWithTheSettingTheClockMovesForwardOnlyAndNeedsAToken()
    {
        await using var host = await LiveTestHost.StartAsync("Development", manualClock: true, Ct);
        using var anonymous = host.CreateClient();
        using var client = host.CreateClient(host.Token(User));
        var clock = Assert.IsType<ManualClock>(host.Services.GetRequiredService<TimeProvider>());

        using var refused = await anonymous.GetAsync(Clock, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);

        using var read = await client.GetAsync(Clock, Ct);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.InRange(JsonNode.Parse(await read.Content.ReadAsStringAsync(Ct))!["offsetSeconds"]!.GetValue<double>(), 0, 0);

        var target = TimeProvider.System.GetUtcNow().AddHours(5);
        using var set = await client.PostAsJsonAsync(Clock, new { now = target }, Ct);
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.InRange(clock.GetUtcNow(), target, target.AddSeconds(30));
        Assert.InRange(JsonNode.Parse(await set.Content.ReadAsStringAsync(Ct))!["now"]!.GetValue<DateTimeOffset>(), target, target.AddSeconds(30));

        // Back to the real time, or anywhere behind the clock: refused, and the clock stays.
        using var back = await client.PostAsJsonAsync(Clock, new { now = TimeProvider.System.GetUtcNow() }, Ct);
        using var empty = await client.PostAsJsonAsync(Clock, new { }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, back.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.InRange(clock.GetUtcNow(), target, target.AddSeconds(30));
    }

    [Fact]
    public async Task AccessTokensKeepTheRealClockWhileTheManualClockRunsAhead()
    {
        await using var host = await LiveTestHost.StartAsync("Development", manualClock: true, Ct);
        var token = host.Token(User, TimeSpan.FromMinutes(5));
        using var client = host.CreateClient(token);

        // A day ahead on the Server's clock: a five-minute token is still good, and a hub connection stays open.
        using var set = await client.PostAsJsonAsync(Clock, new { now = TimeProvider.System.GetUtcNow().AddDays(1) }, Ct);
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);

        using var read = await client.GetAsync(Clock, Ct);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        await using var connection = await host.ConnectAsync(User, Ct, accessToken: token);
        await Task.Delay(TimeSpan.FromMilliseconds(300), Ct);
        Assert.Equal(Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected, connection.Connection.State);
    }

    [Fact]
    public void TheManualClockIsRealTimePlusAnOffsetThatOnlyGrows()
    {
        var real = new FakeTimeProvider(new DateTimeOffset(2026, 10, 10, 0, 40, 0, TimeSpan.Zero));
        var clock = new ManualClock(real);

        Assert.Equal(real.GetUtcNow(), clock.GetUtcNow());

        Assert.True(clock.AdvanceTo(real.GetUtcNow().AddHours(4)));
        Assert.Equal(TimeSpan.FromHours(4), clock.Offset);

        // Real time keeps running underneath.
        real.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(new DateTimeOffset(2026, 10, 10, 4, 41, 0, TimeSpan.Zero), clock.GetUtcNow());

        Assert.False(clock.AdvanceTo(real.GetUtcNow().AddHours(1)));
        Assert.False(clock.AdvanceTo(clock.GetUtcNow()));
        Assert.Equal(TimeSpan.FromHours(4), clock.Offset);
    }
}
