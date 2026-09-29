using Coldframe.Server.Identity;
using Coldframe.Server.Identity.Reconciliation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Coldframe.Server.Tests.Identity.Reconciliation;

/// <summary>
/// The Keycloak event pipeline refuses to start without its Temporal target, namespace and realm ID.
/// </summary>
public sealed class KeycloakEventOptionsTests
{
    public static TheoryData<string> RequiredKeys() => ["TargetHost", "Namespace", "RealmId"];

    [Fact]
    public void ACompleteConfigurationIsAcceptedWithTheDefaultTaskQueues()
    {
        using var host = Build(Complete());

        var options = host.Services.GetRequiredService<IOptions<KeycloakEventOptions>>().Value;

        Assert.Equal("localhost:7233", options.TargetHost);
        Assert.Equal("coldframe", options.Namespace);
        Assert.Equal("coldframe", options.RealmId);
        Assert.Equal("keycloak-admin-queue", options.AdminTaskQueue);
        Assert.Equal("keycloak-user-queue", options.UserTaskQueue);
    }

    [Theory]
    [MemberData(nameof(RequiredKeys))]
    public void AMissingRequiredSettingFailsValidation(string key)
    {
        var settings = Complete();
        settings.Remove($"{KeycloakEventOptions.SectionName}:{key}");
        using var host = Build(settings);

        var failure = Assert.Throws<OptionsValidationException>(
            () => host.Services.GetRequiredService<IOptions<KeycloakEventOptions>>().Value);

        Assert.Contains($"{KeycloakEventOptions.SectionName}:{key} is required.", failure.Failures);
    }

    [Fact]
    public async Task TheHostDoesNotStartWithoutTheSettings()
    {
        using var host = Build([]);

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync(TestContext.Current.CancellationToken));
    }

    private static Dictionary<string, string?> Complete() => new(StringComparer.Ordinal)
    {
        [$"{KeycloakEventOptions.SectionName}:TargetHost"] = "localhost:7233",
        [$"{KeycloakEventOptions.SectionName}:Namespace"] = "coldframe",
        [$"{KeycloakEventOptions.SectionName}:RealmId"] = "coldframe",
    };

    private static IHost Build(Dictionary<string, string?> settings)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddKeycloakEventPipeline();
        return builder.Build();
    }
}
