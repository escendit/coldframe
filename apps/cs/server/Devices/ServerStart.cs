namespace Coldframe.Server.Devices;

/// <summary>
/// When this Server started (Story 7.1): read once from the <see cref="TimeProvider"/> as the host starts. While
/// the Server was down it accepted nothing, and that is no silence of a Device, so every Device grain restarts its
/// silence clock here. It is a hosted service only so that the host creates it at start and not at the first
/// Device grain that asks.
/// </summary>
public sealed class ServerStart(TimeProvider clock) : IHostedService
{
    /// <summary>
    /// When the host created this instance.
    /// </summary>
    public DateTimeOffset At { get; } = (clock ?? throw new ArgumentNullException(nameof(clock))).GetUtcNow();

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
