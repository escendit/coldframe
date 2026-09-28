using Coldframe.Server.Journal;
using Microsoft.Extensions.Options;

namespace Coldframe.Server.Identity;

/// <summary>
/// Registers the identity pipeline: the Phase Two client, the identity projector and its read model.
/// </summary>
public static class IdentityHostingExtensions
{
    /// <summary>
    /// Adds the Keycloak service account and Organizations client (configuration section
    /// <see cref="KeycloakOptions.SectionName"/>), the identity projector and its read model.
    /// </summary>
    public static TBuilder AddSiteIdentity<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services
            .AddOptions<KeycloakOptions>()
            .Bind(builder.Configuration.GetSection(KeycloakOptions.SectionName))
            .Validate(options => options.BaseUrl is { IsAbsoluteUri: true }, "Keycloak:BaseUrl must be an absolute URL.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Realm), "Keycloak:Realm is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.ClientId), "Keycloak:ClientId is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.ClientSecret), "Keycloak:ClientSecret is required.")
            .Validate(
                options => options.RequestTimeout > TimeSpan.Zero && options.RequestTimeout < TimeSpan.FromSeconds(20),
                "Keycloak:RequestTimeout must be positive and under 20 s.")
            .Validate(
                options => options.OperationBudget > TimeSpan.Zero && options.OperationBudget < TimeSpan.FromSeconds(30),
                "Keycloak:OperationBudget must stay under the Orleans call timeout of 30 s.")
            .ValidateOnStart();

        builder.Services
            .AddHttpClient(KeycloakServiceAccount.HttpClientName)
            .ConfigureHttpClient((provider, client) =>
                client.Timeout = provider.GetRequiredService<IOptions<KeycloakOptions>>().Value.RequestTimeout);

        builder.Services.AddSingleton<KeycloakServiceAccount>();
        builder.Services.AddSingleton<IPhaseTwoOrganizations, PhaseTwoOrganizations>();
        builder.Services.AddSingleton<IdentityReadModel>();
        builder.Services.AddProjector<IdentityProjector>();

        return builder;
    }
}
