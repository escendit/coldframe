using Coldframe.Server.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace Coldframe.Server.Tests.Hosting;

/// <summary>
/// The Server registers <see cref="TimeProvider.System"/> only when no other provider is registered,
/// so any host can substitute <see cref="FakeTimeProvider"/> (AD-6).
/// </summary>
public sealed class TimeProviderTests
{
    private static readonly DateTimeOffset Start = new(2026, 5, 1, 6, 30, 0, TimeSpan.Zero);

    [Fact]
    public void AFakeTimeProviderRegisteredFirstIsWhatTheServerRegistrationsResolve()
    {
        var fake = new FakeTimeProvider(Start);
        var builder = CreateBuilder();
        builder.Services.AddSingleton<TimeProvider>(fake);

        builder.AddJournal();

        using var host = builder.Build();
        var time = host.Services.GetRequiredService<TimeProvider>();

        Assert.Same(fake, time);
        Assert.Equal(Start, time.GetUtcNow());
    }

    [Fact]
    public void WithoutASubstituteTheServerRegistrationsResolveTheSystemClock()
    {
        var builder = CreateBuilder();

        builder.AddJournal();

        using var host = builder.Build();

        Assert.Same(TimeProvider.System, host.Services.GetRequiredService<TimeProvider>());
    }

    private static HostApplicationBuilder CreateBuilder()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            // Nothing connects: the data source opens connections lazily.
            ["ConnectionStrings:coldframe"] = "Host=localhost;Database=coldframe",
        });

        return builder;
    }
}
