using Coldframe.Server.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Coldframe.Server.Tests.Identity;

/// <summary>
/// <c>Keycloak:InvitationRedirectUri</c> (Story 9.1) is optional, and absolute when set.
/// </summary>
public sealed class KeycloakOptionsTests
{
    [Fact]
    public void WithoutAnInvitationRedirectTheOptionsAreValid()
    {
        using var host = Build(Complete());

        Assert.Null(host.Services.GetRequiredService<IOptions<KeycloakOptions>>().Value.InvitationRedirectUri);
    }

    [Fact]
    public void AnAbsoluteInvitationRedirectIsKept()
    {
        var settings = Complete();
        settings[$"{KeycloakOptions.SectionName}:InvitationRedirectUri"] = "https://coldframe.example.org/garden";
        using var host = Build(settings);

        Assert.Equal(
            new Uri("https://coldframe.example.org/garden"),
            host.Services.GetRequiredService<IOptions<KeycloakOptions>>().Value.InvitationRedirectUri);
    }

    [Fact]
    public void ARelativeInvitationRedirectFailsValidation()
    {
        var settings = Complete();
        settings[$"{KeycloakOptions.SectionName}:InvitationRedirectUri"] = "/garden";
        using var host = Build(settings);

        var failure = Assert.Throws<OptionsValidationException>(
            () => host.Services.GetRequiredService<IOptions<KeycloakOptions>>().Value);

        Assert.Contains("Keycloak:InvitationRedirectUri must be an absolute URL when set.", failure.Failures);
    }

    private static Dictionary<string, string?> Complete() => new(StringComparer.Ordinal)
    {
        [$"{KeycloakOptions.SectionName}:BaseUrl"] = "http://keycloak.test.invalid",
        [$"{KeycloakOptions.SectionName}:ClientSecret"] = "secret",
    };

    private static IHost Build(Dictionary<string, string?> settings)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddSiteIdentity();
        return builder.Build();
    }
}
