using Coldframe.Server.Hosting;
using Coldframe.Server.Journal;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Coldframe.Server.Tests.Hosting;

/// <summary>
/// <c>Journal:HintStream:Enabled</c> switches the NATS hint stream off, so the Server runs without
/// NATS and projectors converge by polling alone (AD-21).
/// </summary>
public sealed class HintStreamSwitchTests
{
    [Fact]
    public void WithHintsOffTheServerNeedsNoNatsAndRunsNoOutboxDispatcher()
    {
        var builder = CreateBuilder(("Journal:HintStream:Enabled", "false"));

        builder.AddJournal().AddSilo();

        using var app = builder.Build();

        Assert.DoesNotContain(app.Services.GetServices<IHostedService>(), service => service is OutboxDispatcher);
    }

    [Fact]
    public void WithHintsOnTheServerRunsTheOutboxDispatcher()
    {
        var builder = CreateBuilder(("ConnectionStrings:nats", "nats://localhost:4222"));

        builder.AddJournal().AddSilo();

        using var app = builder.Build();

        Assert.Contains(app.Services.GetServices<IHostedService>(), service => service is OutboxDispatcher);
    }

    [Fact]
    public void WithHintsOnAndNoNatsConnectionStringTheServerRefusesToStart()
    {
        var builder = CreateBuilder();

        var error = Assert.Throws<InvalidOperationException>(() => builder.AddJournal().AddSilo());

        Assert.Contains("Journal:HintStream:Enabled", error.Message, StringComparison.Ordinal);
    }

    private static WebApplicationBuilder CreateBuilder(params (string Key, string Value)[] settings)
    {
        var builder = WebApplication.CreateSlimBuilder();
        var configuration = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            // Nothing connects: building the host opens no connection.
            ["ConnectionStrings:coldframe"] = "Host=localhost;Database=coldframe",
        };

        foreach (var (key, value) in settings)
        {
            configuration[key] = value;
        }

        builder.Configuration.AddInMemoryCollection(configuration);

        return builder;
    }
}
