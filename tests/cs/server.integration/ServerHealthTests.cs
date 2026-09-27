using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Aspire.Hosting.Testing;

namespace Coldframe.Server.IntegrationTests;

public sealed class ServerHealthTests(AppHostFixture fixture)
{
    private const string ServerResource = "server";
    private const string HealthPath = "/.well-known/healthz";

    [Fact]
    public async Task HealthEndpointReportsAHealthySilo()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(AppHostFixture.ResourceTimeout);

        using var response = await GetHealthAsync(timeout.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var report = await JsonDocument.ParseAsync(body, cancellationToken: timeout.Token);

        Assert.Equal("Healthy", report.RootElement.GetProperty("status").GetString());

        var silo = report.RootElement.GetProperty("services").GetProperty("silo");
        Assert.Equal("Healthy", silo.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("silo")]
    [InlineData("gateway")]
    public async Task SiloListensOnThePortTheAppHostAllocated(string endpointName)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(AppHostFixture.ResourceTimeout);

        // The silo listens before the host answers its first request.
        using var response = await GetHealthAsync(timeout.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var endpoint = fixture.App.GetEndpoint(ServerResource, endpointName);

        using var client = new TcpClient();
        await client.ConnectAsync(endpoint.Host, endpoint.Port, timeout.Token);

        Assert.True(client.Connected);
    }

    private async Task<HttpResponseMessage> GetHealthAsync(CancellationToken cancellationToken)
    {
        // Running, not healthy: a missing health endpoint must fail the request below
        // instead of waiting for a state the resource can never reach.
        await fixture.WaitForRunningAsync(ServerResource, cancellationToken);

        using var client = fixture.App.CreateHttpClient(ServerResource, "http");

        return await client.GetAsync(new Uri(HealthPath, UriKind.Relative), cancellationToken);
    }
}
