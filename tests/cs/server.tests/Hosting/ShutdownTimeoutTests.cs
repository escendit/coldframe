using Coldframe.Server.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Coldframe.Server.Tests.Hosting;

/// <summary>
/// The server chart sets <c>DOTNET_SHUTDOWNTIMEOUTSECONDS</c> so the silo can drain before
/// Kubernetes kills the pod (AD-22). The Server's host must read it from its environment.
/// </summary>
public sealed class ShutdownTimeoutTests
{
    private const string ShutdownTimeoutVariable = "DOTNET_SHUTDOWNTIMEOUTSECONDS";

    [Fact]
    public void TheShutdownTimeoutComesFromTheEnvironment()
    {
        var previous = Environment.GetEnvironmentVariable(ShutdownTimeoutVariable);
        Environment.SetEnvironmentVariable(ShutdownTimeoutVariable, "45");
        try
        {
            // Built as Program.cs builds the Server's host, without starting Orleans.
            var builder = WebApplication.CreateBuilder();
            builder.AddServiceDefaults();

            using var app = builder.Build();

            var options = app.Services.GetRequiredService<IOptions<HostOptions>>().Value;
            Assert.Equal(TimeSpan.FromSeconds(45), options.ShutdownTimeout);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ShutdownTimeoutVariable, previous);
        }
    }
}
