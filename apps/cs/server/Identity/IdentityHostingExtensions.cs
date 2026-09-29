using Coldframe.Server.Identity.Reconciliation;
using Coldframe.Server.Journal;
using Microsoft.Extensions.Options;
using Temporalio.Client;
using Temporalio.Extensions.Hosting;

namespace Coldframe.Server.Identity;

/// <summary>
/// Registers the identity pipeline: the Phase Two client, the identity projector and its read model, and
/// the Keycloak event pipeline.
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

    /// <summary>
    /// Adds the Temporal workers of the Keycloak event pipeline (configuration section
    /// <see cref="KeycloakEventOptions.SectionName"/>, AD-3, AD-5): one for the <c>IdentityAdminEvent</c>
    /// workflow, which reconciles Sites and Users, and one for the <c>IdentityUserEvent</c> workflow, which
    /// only completes. Temporal carries nothing else.
    /// </summary>
    public static TBuilder AddKeycloakEventPipeline<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        var section = builder.Configuration.GetSection(KeycloakEventOptions.SectionName);

        builder.Services
            .AddOptions<KeycloakEventOptions>()
            .Bind(section)
            .Validate(options => !string.IsNullOrWhiteSpace(options.TargetHost), "KeycloakEvents:TargetHost is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Namespace), "KeycloakEvents:Namespace is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.RealmId), "KeycloakEvents:RealmId is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.AdminTaskQueue), "KeycloakEvents:AdminTaskQueue must not be empty.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.UserTaskQueue), "KeycloakEvents:UserTaskQueue must not be empty.")
            .ValidateOnStart();

        // One lazy client for both workers; it connects when the first worker starts.
        builder.Services
            .AddTemporalClient()
            .Configure<IOptions<KeycloakEventOptions>>((client, events) =>
            {
                client.TargetHost = events.Value.TargetHost;
                client.Namespace = events.Value.Namespace!;
            });

        // Task queues are fixed when the workers are registered.
        var queues = section.Get<KeycloakEventOptions>() ?? new KeycloakEventOptions();

        builder.Services
            .AddHostedTemporalWorker(queues.AdminTaskQueue)
            .AddScopedActivities<IdentityReconciliationActivities>()
            .AddWorkflow<IdentityAdminEventWorkflow>();

        builder.Services
            .AddHostedTemporalWorker(queues.UserTaskQueue)
            .AddWorkflow<IdentityUserEventWorkflow>();

        return builder;
    }
}
