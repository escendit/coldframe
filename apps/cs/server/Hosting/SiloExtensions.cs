using Coldframe.Contracts.Devices;
using Coldframe.Server.Devices;
using Coldframe.Server.Journal;
using Orleans.Configuration;
using Orleans.Streaming.NATS.Hosting;
using Orleans.Streams;

namespace Coldframe.Server.Hosting;

/// <summary>
/// Hosts the Orleans silo inside the ASP.NET Core host.
/// </summary>
internal static partial class SiloExtensions
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

            // Every enrolled Device is activated once the silo runs (Story 7.1).
            silo.AddStartupTask(ActivateDevicesAsync);
        });

        return builder;
    }

    // Activates every enrolled Device, so that each applies the Server start to its silence clock and holds its
    // silence reminder: a Device enrolled before Story 7.1 has none until its grain is activated. The Devices
    // come from the devices read model, which only the host reads here, never a grain. A failure only logs: the
    // Device is activated by its next report or reminder all the same, and the silo starts. The pass runs
    // beside the start, not inside it: each activation replays a Device's journal, and the silo must accept
    // heartbeats and frames meanwhile. It ends when the host stops.
    private static Task ActivateDevicesAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var stopping = services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
        _ = Task.Run(() => ActivateEachDeviceAsync(services, stopping), CancellationToken.None);
        return Task.CompletedTask;
    }

    private static async Task ActivateEachDeviceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(SiloExtensions).FullName!);
        IReadOnlyList<string> deviceIds;

        try
        {
            deviceIds = await services.GetRequiredService<DevicesReadModel>().ListDeviceIdsAsync(cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Whatever kept the Devices from being read, the silo starts.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogDevicesNotRead(logger, exception);
            return;
        }

        var grains = services.GetRequiredService<IGrainFactory>();

        foreach (var deviceId in deviceIds)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await grains.GetGrain<IDeviceGrain>(deviceId).Silence(cancellationToken).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // One Device that cannot be activated delays no other.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                LogDeviceNotActivated(logger, deviceId, exception);
            }
        }
    }

    [LoggerMessage(EventId = 1, EventName = "DevicesNotRead", Level = LogLevel.Warning, Message = "The enrolled Devices could not be read at start; each is activated by its next report or reminder.")]
    private static partial void LogDevicesNotRead(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2, EventName = "DeviceNotActivated", Level = LogLevel.Warning, Message = "Device {DeviceId} could not be activated at start; it is activated by its next report or reminder.")]
    private static partial void LogDeviceNotActivated(ILogger logger, string deviceId, Exception exception);

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
