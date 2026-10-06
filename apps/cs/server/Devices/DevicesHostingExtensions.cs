using Coldframe.Server.Journal;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Coldframe.Server.Devices;

/// <summary>
/// Registers the Devices: the enrolment key and the Device key vault (configuration section
/// <see cref="EnrolmentOptions.SectionName"/>), checked at start, and the devices projector with its read
/// model, and the ingestion store of the Device grain. The Device grain is found with the other grains.
/// </summary>
public static class DevicesHostingExtensions
{
    /// <summary>
    /// Adds <see cref="EnrolmentKeyring"/> and <see cref="DeviceKeyVault"/>, with <see cref="EnrolmentOptions"/>
    /// validated at start, and the devices projector with <see cref="DevicesReadModel"/>.
    /// </summary>
    public static OptionsBuilder<EnrolmentOptions> AddDevices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<EnrolmentKeyring>();
        services.AddSingleton<DeviceKeyVault>();
        services.AddSingleton<DevicesReadModel>();

        // The ingestion tables, written only through the Device grain; a test host registers its own first.
        services.TryAddSingleton<DeviceIngestionStore>();
        services.AddProjector<DevicesProjector>();

        return services
            .AddOptions<EnrolmentOptions>()
            .Validate(
                options => EnrolmentKeyring.IsValidPrivateKeyPem(options.PrivateKeyPem),
                "Enrolment:PrivateKeyPem must be a PKCS#8 X25519 private key in PEM ('openssl genpkey -algorithm X25519').")
            .Validate(
                options => options.DeviceKeyEncryptionKey is { Length: >= EnrolmentOptions.MinDeviceKeyEncryptionKeyLength },
                $"Enrolment:DeviceKeyEncryptionKey must have at least {EnrolmentOptions.MinDeviceKeyEncryptionKeyLength} characters.")
            .ValidateOnStart();
    }

    /// <summary>
    /// Adds Device enrolment, bound to the configuration section <see cref="EnrolmentOptions.SectionName"/>.
    /// </summary>
    public static TBuilder AddDevices<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddDevices().Bind(builder.Configuration.GetSection(EnrolmentOptions.SectionName));

        return builder;
    }
}
