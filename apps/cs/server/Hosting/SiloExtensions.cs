using Coldframe.Server.Journal;
using Orleans.Configuration;
using Orleans.Streaming.NATS.Hosting;
using Orleans.Streams;

namespace Coldframe.Server.Hosting;

/// <summary>
/// Hosts the Orleans silo inside the ASP.NET Core host.
/// </summary>
internal static class SiloExtensions
{
    private const string EndpointsSection = "Orleans:Endpoints";
    private const string AdoNetInvariant = "Npgsql";
    private const string ClusterName = "coldframe";
    private const string NatsConnectionStringName = "nats";
    private const string NatsStreamName = "coldframe-journal-hints";

    /// <summary>
    /// Switches the NATS hint stream on or off. Off, projectors converge by polling alone (AD-21).
    /// </summary>
    private const string HintStreamEnabledKey = "Journal:HintStream:Enabled";

    public static WebApplicationBuilder AddSilo(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var endpoints = builder.Configuration.GetSection(EndpointsSection);
        var siloPort = endpoints.GetValue(nameof(EndpointOptions.SiloPort), EndpointOptions.DEFAULT_SILO_PORT);
        var gatewayPort = endpoints.GetValue(nameof(EndpointOptions.GatewayPort), EndpointOptions.DEFAULT_GATEWAY_PORT);
        var connectionString = JournalHostingExtensions.GetConnectionString(builder.Configuration);
        var hintStream = CreateHintStream(builder.Configuration);

        builder.UseOrleans(silo =>
        {
            // Clustering and reminders use the tables the migration job creates (AD-22).
            silo.Configure<ClusterOptions>(options =>
            {
                options.ClusterId = ClusterName;
                options.ServiceId = ClusterName;
            });
            silo.ConfigureEndpoints(siloPort, gatewayPort, listenOnAnyHostAddress: true);
            silo.UseAdoNetClustering(options =>
            {
                options.Invariant = AdoNetInvariant;
                options.ConnectionString = connectionString;
            });
            silo.UseAdoNetReminderService(options =>
            {
                options.Invariant = AdoNetInvariant;
                options.ConnectionString = connectionString;
            });
            silo.AddActivityPropagation();
            silo.AddJournalGrains(hintStream);
        });

        return builder;
    }

    // The one seam for the hint stream (AD-5): NATS JetStream, or nothing.
    private static Action<ISiloBuilder>? CreateHintStream(IConfiguration configuration)
    {
        if (!configuration.GetValue(HintStreamEnabledKey, defaultValue: true))
        {
            return null;
        }

        var natsUrl = configuration.GetConnectionString(NatsConnectionStringName);

        if (string.IsNullOrWhiteSpace(natsUrl))
        {
            throw new InvalidOperationException(
                $"The hint stream needs the connection string '{NatsConnectionStringName}'. " +
                $"Set {HintStreamEnabledKey} to false to run without it.");
        }

        return silo => silo.AddNatsStreams(JournalHints.StreamProvider, streams =>
        {
            streams.ConfigureNats(options => options.Configure(nats =>
            {
                nats.StreamName = NatsStreamName;
                nats.NatsClientOptions = NATS.Client.Core.NatsOpts.Default with { Url = natsUrl };
            }));
            streams.ConfigureStreamPubSub(StreamPubSubType.ImplicitOnly);
        });
    }
}
