using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Notifications;
using Coldframe.Server.Hosting;
using Coldframe.Server.Notifications;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace Coldframe.Server.Tests.Hosting;

/// <summary>
/// The Server's telemetry setup exports the Coldframe notifications meter (Story 6.4): what the Notifier records
/// for a notification leaves through the OpenTelemetry meter provider the service defaults configure.
/// </summary>
public sealed class NotificationsTelemetryTests
{
    private static readonly DateTimeOffset SentAt = new(2026, 10, 9, 7, 0, 30, TimeSpan.Zero);

    [Fact]
    public void TheNotificationsMeterIsAmongTheExportedMeters()
    {
        Assert.Contains(Notifier.MeterName, ServiceDefaultsExtensions.ExportedMeters);
        Assert.Contains("Microsoft.Orleans", ServiceDefaultsExtensions.ExportedMeters);
    }

    [Fact]
    public async Task ASentNotificationIsExportedThroughTheServersMeterProvider()
    {
        var exported = new CollectingExporter();
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(SentAt));
        builder.AddServiceDefaults().AddNotifications();
        using var reader = new BaseExportingMetricReader(exported);
        builder.Services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddReader(reader));
        await using var app = builder.Build();
        var provider = app.Services.GetRequiredService<MeterProvider>();

        await app.Services.GetRequiredService<INotifier>().SendAsync(
            new Notification(
                "user-1",
                "site-1",
                NotificationKind.Alert,
                SentAt.AddSeconds(-30),
                null,
                [new NotificationEntry(Guid.NewGuid(), AlertKind.Threshold, ThresholdSide.Low, "lot-1", Guid.NewGuid(), "5a4b3c2d1e0f7c20", "soil_moisture", SentAt)]),
            TestContext.Current.CancellationToken);

        Assert.True(provider.ForceFlush());
        Assert.Contains((Notifier.MeterName, Notifier.SentCounterName), exported.Metrics);
        Assert.Contains((Notifier.MeterName, Notifier.DelayHistogramName), exported.Metrics);
    }

    private sealed class CollectingExporter : BaseExporter<Metric>
    {
        public HashSet<(string Meter, string Name)> Metrics { get; } = [];

        public override ExportResult Export(in Batch<Metric> batch)
        {
            foreach (var metric in batch)
            {
                Metrics.Add((metric.MeterName, metric.Name));
            }

            return ExportResult.Success;
        }
    }
}
