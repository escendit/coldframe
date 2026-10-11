using Coldframe.Contracts.Sites;
using Coldframe.Server.Edge;
using Coldframe.Server.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Coldframe.Server.Testing;

/// <summary>
/// The body of <c>POST /testing/clock</c>.
/// </summary>
/// <param name="Now">The time the Server's clock shows from now on; it must not be behind the clock.</param>
public sealed record SetClockRequest(DateTimeOffset? Now);

/// <summary>
/// The answer of <c>GET</c> and <c>POST /testing/clock</c>.
/// </summary>
/// <param name="Now">What the Server's clock shows.</param>
/// <param name="OffsetSeconds">How far that is ahead of the real time.</param>
public sealed record ClockState(DateTimeOffset Now, double OffsetSeconds);

/// <summary>
/// Test-only clock control for a Server that runs as its own process (Story 6.6), as under the Aspire AppHost in
/// the end-to-end test of the morning-nudge journey.
/// </summary>
/// <remarks>
/// It is on only when the host environment is Development <em>and</em> <c>Testing:ManualClock</c> is
/// <c>true</c>. In any other host, with or without the setting, no clock is replaced and no endpoint is mapped:
/// <c>/testing/*</c> answers 404 and time is the system clock. The chart sets neither condition.
/// </remarks>
public static class TestingHostingExtensions
{
    /// <summary>
    /// The setting that, in a Development host, turns the manual clock on.
    /// </summary>
    public const string ManualClockKey = "Testing:ManualClock";

    /// <summary>
    /// The clock endpoint.
    /// </summary>
    public const string ClockPath = "/testing/clock";

    /// <summary>
    /// Whether this host runs on the manual clock: Development and the setting, both.
    /// </summary>
    public static bool UsesManualClock(IHostEnvironment environment, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(configuration);

        return environment.IsDevelopment() && configuration.GetValue(ManualClockKey, defaultValue: false);
    }

    /// <summary>
    /// Registers <see cref="ManualClock"/> as the Server's <see cref="TimeProvider"/> when
    /// <see cref="UsesManualClock"/> holds, otherwise nothing. Call it before anything that registers
    /// <see cref="TimeProvider.System"/> with <c>TryAdd</c>.
    /// </summary>
    public static TBuilder AddTestingClock<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (UsesManualClock(builder.Environment, builder.Configuration))
        {
            var clock = new ManualClock(TimeProvider.System);
            builder.Services.AddSingleton(clock);
            builder.Services.AddSingleton<TimeProvider>(clock);
        }

        return builder;
    }

    /// <summary>
    /// Maps the test-only endpoints when <see cref="UsesManualClock"/> holds, otherwise nothing:
    /// <c>GET /testing/clock</c>, <c>POST /testing/clock</c> (<c>{"now": "…"}</c>, forward only) and
    /// <c>POST /testing/users/{userId}/wake</c>, which wakes that User's grain as its reminder does. They need an
    /// access token like every other endpoint.
    /// </summary>
    public static WebApplication MapTestingClock(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (!UsesManualClock(app.Environment, app.Configuration))
        {
            return app;
        }

        app.MapGet(ClockPath, (ManualClock clock) => Results.Ok(new ClockState(clock.GetUtcNow(), clock.Offset.TotalSeconds)));

        app.MapPost(ClockPath, ([FromBody] SetClockRequest request, ManualClock clock) =>
        {
            if (request.Now is not { } now)
            {
                return EdgeProblems.Result(StatusCodes.Status400BadRequest, EdgeProblems.Validation, "The clock was not set.", "now is required.");
            }

            if (now < clock.GetUtcNow())
            {
                return EdgeProblems.Result(StatusCodes.Status400BadRequest, EdgeProblems.Validation, "The clock was not set.", "The clock only moves forward.");
            }

            clock.AdvanceTo(now);

            return Results.Ok(new ClockState(clock.GetUtcNow(), clock.Offset.TotalSeconds));
        });

        app.MapPost("/testing/users/{userId}/wake", async (string userId, IGrainFactory grains) =>
        {
            await grains
                .GetGrain<IUserGrain>(userId)
                .AsReference<IRemindable>()
                .ReceiveReminder(UserGrain.WakeReminderName, default)
                .ConfigureAwait(false);

            return Results.NoContent();
        });

        return app;
    }
}
