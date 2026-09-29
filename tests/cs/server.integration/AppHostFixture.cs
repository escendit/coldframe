using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

[assembly: AssemblyFixture(typeof(Coldframe.Server.IntegrationTests.AppHostFixture))]

namespace Coldframe.Server.IntegrationTests;

/// <summary>
/// Starts the same AppHost a developer runs locally, once for all tests in the assembly.
/// </summary>
public sealed class AppHostFixture : IAsyncLifetime
{
    /// <summary>
    /// Upper bound for one resource to become ready. The first run builds the Keycloak image,
    /// which downloads the extension's build dependencies.
    /// </summary>
    public static readonly TimeSpan ResourceTimeout = TimeSpan.FromMinutes(15);

    private DistributedApplication? _app;

    public DistributedApplication App =>
        _app ?? throw new InvalidOperationException("The AppHost has not been started.");

    public async ValueTask InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(ResourceTimeout);

        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Coldframe_AppHost>(timeout.Token);

        // Retries connection failures while a resource that is already running starts listening.
        builder.Services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());

        _app = await builder.BuildAsync(timeout.Token);
        await _app.StartAsync(timeout.Token);
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    /// <summary>
    /// Waits until the resource runs. Fails at once when the AppHost does not define it
    /// or when it ends in a state it cannot leave, instead of waiting for the timeout.
    /// </summary>
    public async Task WaitForRunningAsync(string name, CancellationToken cancellationToken)
    {
        GetResource(name);

        string[] states =
        [
            KnownResourceStates.Running,
            KnownResourceStates.FailedToStart,
            KnownResourceStates.RuntimeUnhealthy,
            KnownResourceStates.Exited,
            KnownResourceStates.Finished,
        ];

        var state = await App.ResourceNotifications.WaitForResourceAsync(name, states, cancellationToken);

        Assert.Equal(KnownResourceStates.Running, state);
    }

    /// <summary>
    /// Waits until the resource is healthy. Fails at once when the AppHost does not define it
    /// or when it becomes unavailable, instead of waiting for the timeout.
    /// </summary>
    public async Task WaitForHealthyAsync(string name, CancellationToken cancellationToken)
    {
        GetResource(name);

        await App.ResourceNotifications.WaitForResourceHealthyAsync(
            name,
            WaitBehavior.StopOnResourceUnavailable,
            cancellationToken);
    }

    /// <summary>
    /// Returns the named resource, or fails the test when the AppHost does not define it.
    /// </summary>
    public IResource GetResource(string name)
    {
        var model = App.Services.GetRequiredService<DistributedApplicationModel>();
        var resource = model.Resources.SingleOrDefault(
            candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));

        Assert.True(resource is not null, $"The AppHost defines no resource named '{name}'.");
        return resource;
    }
}
