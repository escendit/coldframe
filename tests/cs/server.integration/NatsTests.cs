using Aspire.Hosting.Testing;
using NATS.Client.Core;
using NATS.Client.JetStream;

namespace Coldframe.Server.IntegrationTests;

public sealed class NatsTests(AppHostFixture fixture)
{
    private const string NatsResource = "nats";

    [Fact]
    public async Task JetStreamIsEnabled()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(AppHostFixture.ResourceTimeout);

        await fixture.WaitForHealthyAsync(NatsResource, timeout.Token);

        var connectionString = await fixture.App.GetConnectionStringAsync(NatsResource, timeout.Token);
        Assert.False(string.IsNullOrEmpty(connectionString), "The AppHost gives 'nats' no connection string.");

        await using var connection = new NatsConnection(NatsOpts.Default with { Url = connectionString });
        var jetStream = new NatsJSContext(connection);

        // The account-info request fails on a server that runs without JetStream.
        var account = await jetStream.GetAccountInfoAsync(timeout.Token);

        Assert.NotNull(account);
    }
}
