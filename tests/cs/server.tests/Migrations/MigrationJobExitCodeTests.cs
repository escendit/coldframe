using System.Diagnostics;
using Coldframe.Migrations;

namespace Coldframe.Server.Tests.Migrations;

/// <summary>
/// The migration job ends with a non-zero exit code whenever it cannot apply the migrations, so the
/// AppHost (and later the Kubernetes Job) never starts the Server on an unmigrated database.
/// </summary>
public sealed class MigrationJobExitCodeTests
{
    private const string ConnectionStringVariable = "ConnectionStrings__coldframe";

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task WithoutAConnectionStringTheJobExitsWithOne()
    {
        Assert.Equal(1, await RunJobAsync(connectionString: null));
    }

    [Fact]
    public async Task WithAnUnreachableDatabaseTheJobExitsWithOne()
    {
        // Nothing listens on port 1; the connection is refused at once.
        Assert.Equal(
            1,
            await RunJobAsync("Host=127.0.0.1;Port=1;Database=coldframe;Username=coldframe;Password=unused;Timeout=5"));
    }

    [Theory]
    [InlineData("migrate")]
    [InlineData("partitions", "--months-ahead", "1")]
    [InlineData("partitions", "--months-ahead")]
    [InlineData("partitions", "--weeks-ahead", "3")]
    [InlineData("advance-replay", "--uplink-margin", "0")]
    [InlineData("advance-replay", "extra")]
    public async Task WithArgumentsItDoesNotKnowTheJobExitsWithTwoBeforeItTouchesAnything(params string[] arguments)
    {
        // Even with a database it could not reach: the arguments are refused first.
        Assert.Equal(
            2,
            await RunJobAsync("Host=127.0.0.1;Port=1;Database=coldframe;Username=coldframe;Password=unused;Timeout=5", arguments));
        Assert.Equal(2, await RunJobAsync(connectionString: null, arguments));
    }

    [Theory]
    [InlineData("partitions")]
    [InlineData("advance-replay")]
    public async Task AKnownCommandWithAnUnreachableDatabaseExitsWithOne(string command)
    {
        Assert.Equal(
            1,
            await RunJobAsync("Host=127.0.0.1;Port=1;Database=coldframe;Username=coldframe;Password=unused;Timeout=5", command));
    }

    private static async Task<int> RunJobAsync(string? connectionString, params string[] arguments)
    {
        // The job is built next to the tests, because this project references it.
        var job = typeof(MigrationRunnerRegistration).Assembly.Location;

        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(job);
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment.Remove(ConnectionStringVariable);
        start.Environment["DOTNET_ENVIRONMENT"] = "Production";
        start.Environment.Remove("OTEL_EXPORTER_OTLP_ENDPOINT");
        if (connectionString is not null)
        {
            start.Environment[ConnectionStringVariable] = connectionString;
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("The migration job did not start.");
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(Timeout);

        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"The migration job did not exit within {Timeout.TotalSeconds:0} s.");
        }

        await Task.WhenAll(output, error);
        TestContext.Current.TestOutputHelper?.WriteLine(await output + await error);

        return process.ExitCode;
    }
}
