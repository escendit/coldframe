using Orleans.Configuration;

namespace Coldframe.Server.Hosting;

/// <summary>
/// Hosts the Orleans silo inside the ASP.NET Core host.
/// </summary>
internal static class SiloExtensions
{
    private const string EndpointsSection = "Orleans:Endpoints";

    public static WebApplicationBuilder AddSilo(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var endpoints = builder.Configuration.GetSection(EndpointsSection);
        var siloPort = endpoints.GetValue(nameof(EndpointOptions.SiloPort), EndpointOptions.DEFAULT_SILO_PORT);
        var gatewayPort = endpoints.GetValue(nameof(EndpointOptions.GatewayPort), EndpointOptions.DEFAULT_GATEWAY_PORT);

        builder.UseOrleans(silo =>
        {
            // Single-silo localhost clustering until Story 1.2 adds the cluster schema.
            silo.UseLocalhostClustering(siloPort, gatewayPort);
            silo.AddActivityPropagation();
        });

        return builder;
    }
}
