using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Coldframe.Server.Hosting;

/// <summary>
/// Applies the Escendit service defaults to the ASP.NET Core host.
/// </summary>
/// <remarks>
/// <c>AddServiceDefaults()</c> and <c>AddOrleansServerRuntime()</c> in
/// <c>Escendit.Extensions.Hosting.*</c> extend the concrete <see cref="HostApplicationBuilder"/>,
/// which <see cref="WebApplicationBuilder"/> does not derive from. This applies the same defaults
/// through the building blocks the package exposes, and adds ASP.NET Core request instrumentation.
/// Health checks come from <c>Escendit.AspNetCore.Diagnostics.HealthChecks</c>.
/// </remarks>
internal static class ServiceDefaultsExtensions
{
    private const string OrleansActivitySource = "Microsoft.Orleans.Application";
    private const string OrleansMeter = "Microsoft.Orleans";
    private const string OtlpEndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";

    public static WebApplicationBuilder AddServiceDefaults(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services
            .AddOpenTelemetry()
            .WithDefaultMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddMeter(OrleansMeter))
            .WithDefaultTracing(builder.Environment, tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddSource(OrleansActivitySource));

        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddServiceDiscovery();
            http.AddStandardResilienceHandler();
        });

        if (!string.IsNullOrWhiteSpace(builder.Configuration[OtlpEndpointKey]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }
}
