using System.Diagnostics.Metrics;
using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Notifications;
using Coldframe.Server.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Coldframe.Server.Tests.Notifications;

/// <summary>
/// The Notifier seam (Story 6.4) only sends and records: it stamps <c>sentAt</c> from the clock, calls every
/// channel, and puts <c>dueAt</c> and <c>sentAt</c> in one structured log entry and on the notifications meter.
/// It never filters, delays or schedules, and a channel that fails fails the send.
/// </summary>
public sealed class NotifierTests
{
    private static readonly DateTimeOffset DueAt = new(2026, 10, 9, 7, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset SentAt = DueAt.AddSeconds(42);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ANotificationReachesEveryChannelWithTheClocksSentAt()
    {
        using var host = new NotifierHost();
        var (first, second) = (new Channel(), new Channel());
        var notifier = host.Create(first, second);
        var notification = Summary();

        var sentAt = await notifier.SendAsync(notification, Ct);

        Assert.Equal(SentAt, sentAt);
        Assert.Equal((notification, SentAt), Assert.Single(first.Sent));
        Assert.Equal((notification, SentAt), Assert.Single(second.Sent));
    }

    [Fact]
    public async Task OneLogEntryCarriesDueAtAndSentAtAsStructuredFields()
    {
        using var host = new NotifierHost();

        await host.Create().SendAsync(Summary(), Ct);

        var entry = Assert.Single(host.Logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(DueAt, entry.Fields["DueAt"]);
        Assert.Equal(SentAt, entry.Fields["SentAt"]);
        Assert.Equal("summary", entry.Fields["Kind"]);
        Assert.Equal("user-1", entry.Fields["UserId"]);
        Assert.Equal("site-1", entry.Fields["SiteId"]);
        Assert.Equal(2, entry.Fields["Entries"]);
        Assert.Contains("2026-10-09T07:00:00.0000000+00:00", entry.Message, StringComparison.Ordinal);
        Assert.Contains("2026-10-09T07:00:42.0000000+00:00", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheNotificationsMeterCountsTheNotificationAndRecordsHowLongAfterDueAtItWasSent()
    {
        using var host = new NotifierHost();
        var counted = new List<(long Value, string? Kind)>();
        var delays = new List<(double Value, string? Kind)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meters) =>
        {
            // Only this test's meter: other tests create theirs from their own factory.
            if (instrument.Meter.Name == Notifier.MeterName && ReferenceEquals(instrument.Meter.Scope, host.Meters))
            {
                meters.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            Assert.Equal(Notifier.SentCounterName, instrument.Name);
            counted.Add((value, KindOf(tags)));
        });
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
        {
            Assert.Equal(Notifier.DelayHistogramName, instrument.Name);
            delays.Add((value, KindOf(tags)));
        });
        var notifier = host.Create();
        listener.Start();

        await notifier.SendAsync(Summary(), Ct);
        await notifier.SendAsync(Summary() with { Kind = NotificationKind.Reminder, DueAt = SentAt, HeldFrom = null }, Ct);

        Assert.Equal([(1L, "summary"), (1L, "reminder")], counted);
        Assert.Equal([(42d, "summary"), (0d, "reminder")], delays);
    }

    [Fact]
    public async Task AChannelThatFailsIsLoggedAndTheOthersStillDeliver()
    {
        using var host = new NotifierHost();
        var first = new Channel();
        var last = new Channel();
        var notifier = host.Create(first, new Channel { Failure = new InvalidOperationException("The push provider is down.") }, last);

        // Sent: a retry would hand the channels that delivered the same notification again.
        Assert.Equal(SentAt, await notifier.SendAsync(Summary(), Ct));

        Assert.Single(first.Sent);
        Assert.Single(last.Sent);
        Assert.Equal([LogLevel.Warning, LogLevel.Information], host.Logger.Entries.Select(entry => entry.Level));
        Assert.Equal("Channel", host.Logger.Entries[0].Fields["Channel"]);
    }

    [Fact]
    public async Task WhenNoChannelDeliversTheSendFailsAndNothingIsRecordedAsSent()
    {
        using var host = new NotifierHost();
        var failure = new InvalidOperationException("The push provider is down.");
        var notifier = host.Create(new Channel { Failure = failure }, new Channel { Failure = failure });

        var thrown = await Assert.ThrowsAsync<AggregateException>(() => notifier.SendAsync(Summary(), Ct));

        Assert.Equal([failure, failure], thrown.InnerExceptions);
        Assert.DoesNotContain(host.Logger.Entries, entry => entry.Level == LogLevel.Information);
    }

    [Fact]
    public async Task WithoutAChannelTheNotificationIsStillRecorded()
    {
        using var host = new NotifierHost();

        var sentAt = await host.Create().SendAsync(Summary() with { Kind = NotificationKind.Alert, HeldFrom = null }, Ct);

        Assert.Equal(SentAt, sentAt);
        Assert.Equal("alert", Assert.Single(host.Logger.Entries).Fields["Kind"]);
    }

    [Fact]
    public void TheHostRegistersTheNotifierOnce()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(SentAt));

        services.AddNotifications();
        services.AddNotifications();

        using var provider = services.BuildServiceProvider();
        Assert.IsType<Notifier>(Assert.Single(provider.GetServices<INotifier>()));
        Assert.IsType<FakeTimeProvider>(provider.GetRequiredService<TimeProvider>());
    }

    private static string? KindOf(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        foreach (var tag in tags)
        {
            if (tag.Key == Notifier.KindTag)
            {
                return tag.Value as string;
            }
        }

        return null;
    }

    private static Notification Summary() =>
        new(
            "user-1",
            "site-1",
            NotificationKind.Summary,
            DueAt,
            DueAt.AddHours(-7.5),
            [Entry(ThresholdSide.Low, "lot-1"), Entry(ThresholdSide.High, "lot-2")]);

    private static NotificationEntry Entry(ThresholdSide side, string lotId) =>
        new(Guid.NewGuid(), AlertKind.Threshold, side, lotId, Guid.NewGuid(), "5a4b3c2d1e0f7c20", "soil_moisture", DueAt.AddHours(-8));

    private sealed class NotifierHost : IDisposable
    {
        private readonly ServiceProvider _provider = new ServiceCollection().AddMetrics().BuildServiceProvider();

        public IMeterFactory Meters => _provider.GetRequiredService<IMeterFactory>();

        public RecordingLogger Logger { get; } = new();

        public Notifier Create(params INotificationChannel[] channels) =>
            new(channels, Meters, new FakeTimeProvider(SentAt), Logger);

        public void Dispose() => _provider.Dispose();
    }

    private sealed class Channel : INotificationChannel
    {
        public List<(Notification Notification, DateTimeOffset SentAt)> Sent { get; } = [];

        public Exception? Failure { get; init; }

        public Task SendAsync(Notification notification, DateTimeOffset sentAt, CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Sent.Add((notification, sentAt));
            return Task.CompletedTask;
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message, IReadOnlyDictionary<string, object?> Fields);

    private sealed class RecordingLogger : ILogger<Notifier>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            var fields = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                : [];
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), fields));
        }
    }
}
