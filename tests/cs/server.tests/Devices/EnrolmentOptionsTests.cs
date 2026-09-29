using Coldframe.Server.Devices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Coldframe.Server.Tests.Devices;

/// <summary>
/// Both enrolment secrets are required and checked at start; there is no fallback.
/// </summary>
public sealed class EnrolmentOptionsTests
{
    private static readonly string ValidPem = EnrolmentKeyring.ToPrivateKeyPem(Curve25519Key());

    public static TheoryData<string?, string?> Invalid() => new()
    {
        { null, null },
        { null, new string('k', 32) },
        { ValidPem, null },
        { ValidPem, new string('k', 31) },
        { "not a key", new string('k', 32) },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public async Task AMissingOrInvalidSecretStopsTheStart(string? pem, string? kek)
    {
        using var host = Build(pem, kek);

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync(TestContext.Current.CancellationToken));

        // The message names the setting, never its value.
        Assert.Contains("Enrolment:", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ValidPem, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidSecretsStart()
    {
        using var host = Build(ValidPem, new string('k', 32));

        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    private static IHost Build(string? pem, string? kek)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Enrolment:PrivateKeyPem"] = pem,
            ["Enrolment:DeviceKeyEncryptionKey"] = kek,
        });
        builder.AddDevices();

        return builder.Build();
    }

    private static byte[] Curve25519Key() => System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
}
