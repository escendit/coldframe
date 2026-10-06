using System.Text.Json;
using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Lots;
using Coldframe.Contracts.Sites;
using Coldframe.Server.Devices;
using Coldframe.Server.Identity;
using Coldframe.Server.Journal;
using Coldframe.Server.Lots;
using Coldframe.Server.Tests.Samples;

namespace Coldframe.Server.Tests.Journal;

/// <summary>
/// Replays <c>Fixtures/journal.json</c> through the same registry and serializer the Server uses (AD-21, AD-24).
/// </summary>
/// <remarks>
/// A new event contract or schema version fails <see cref="EveryRegisteredAliasAndSchemaVersionAppearsInTheFixture"/>
/// until the fixture holds a row for it. An event must never become unreadable, so rows are only ever added.
/// </remarks>
public sealed class FixtureJournalReplayTests
{
    private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "journal.json");

    // Each alias prefix names the state its events apply to. A new aggregate adds its state here.
    private static readonly Dictionary<string, Func<object>> States = new(StringComparer.Ordinal)
    {
        ["device"] = () => new DeviceState(),
        ["lot"] = () => new LotState(),
        ["sample"] = () => new SampleState(),
        ["site"] = () => new SiteState(),
        ["user"] = () => new UserState(),
    };

    [Fact]
    public void EveryRowDeserializesAndAppliesToItsState()
    {
        var serializer = new JournalSerializer(CreateRegistry());
        var states = new Dictionary<string, object>(StringComparer.Ordinal);

        foreach (var row in ReadFixture())
        {
            var @event = serializer.Deserialize(row.Alias, row.SchemaVersion, row.Payload.GetRawText());

            if (!states.TryGetValue(row.StreamId, out var state))
            {
                var prefix = row.Alias.Split('.')[0];
                Assert.True(
                    States.TryGetValue(prefix, out var create),
                    $"The replay test has no state for alias prefix '{prefix}' (row {row.StreamId} v{row.Version}).");
                state = create();
                states.Add(row.StreamId, state);
            }

            // Journaled grains apply events the same way: through a public Apply overload.
            ((dynamic)state).Apply((dynamic)@event);
        }

        var sample = Assert.IsType<SampleState>(states["sample/fixture-a"]);
        Assert.Equal("Fixture A", sample.Name);
        Assert.Equal(2, sample.NoteCount);
        Assert.Equal(SampleNotedUpcaster.ImplicitWeight + 4, sample.TotalWeight);

        var site = Assert.IsType<SiteState>(states["site/0192f3a4-7c1e-7d2b-9a51-3f7e2c9b1d00"]);
        Assert.Equal(SiteLifecycle.Active, site.Lifecycle);
        Assert.Equal("Home", site.Name);
        Assert.Equal(["5b0c7c1e-2a4d-4f1b-8e3a-9d7f6c5b4a31"], site.Owners);
        Assert.Equal(SiteRole.Member, site.Members["8f1e2d3c-4b5a-4968-8776-5a4b3c2d1e0f"]);
        var lotCreation = site.LotCreations["5b0c7c1e-2a4d-4f1b-8e3a-9d7f6c5b4a31:lk1"];
        Assert.Equal(("0192f3a4-8a00-7c3d-8e4f-5a6b7c8d9e01", "Tomatoes", true), (lotCreation.LotId, lotCreation.Name, lotCreation.Completed));

        // A Hub joined the roster, then enrolled with its key wrapped.
        Assert.Equal(DeviceKind.Hub, site.Devices["92064422c012f481"]);
        Assert.Equal("92064422c012f481", site.DeviceRegistrations["5b0c7c1e-2a4d-4f1b-8e3a-9d7f6c5b4a31:dk1"].DeviceId);

        var device = Assert.IsType<DeviceState>(states["device/92064422c012f481"]);
        Assert.Equal(("0192f3a4-7c1e-7d2b-9a51-3f7e2c9b1d00", DeviceKind.Hub), (device.SiteId, device.Kind));
        Assert.Equal("0123456789abcdef", device.WrappedKey?.KekId);
        Assert.Equal(12, device.WrappedKey?.Nonce.Length);
        Assert.Equal(48, device.WrappedKey?.Sealed.Length);

        // Two heartbeats, the second without an uptime: last seen at the second.
        Assert.Equal(new DateTimeOffset(2026, 9, 29, 8, 1, 15, 500, TimeSpan.Zero), device.LastSeenAt);
        Assert.Equal(1_790_668_875_100, device.LastHeartbeatTimestampMs);

        // A Node enrolled and assigned to a Lot in one step.
        var node = Assert.IsType<DeviceState>(states["device/5a4b3c2d1e0f7c20"]);
        Assert.Equal(("0192f3a4-7c1e-7d2b-9a51-3f7e2c9b1d00", DeviceKind.Node), (node.SiteId, node.Kind));
        Assert.Equal("0192f3a4-8a00-7c3d-8e4f-5a6b7c8d9e02", node.LotId);
        Assert.Null(device.LotId);

        // Relayed by the Hub; paused by its Site and by itself, then the Site resumed: still paused.
        Assert.Equal("92064422c012f481", node.LastRelayHubId);
        Assert.True(node.IsPaused);
        Assert.Equal([DevicePauseSource.Device], node.PausedBy.Keys);
        Assert.Null(node.PausedBy[DevicePauseSource.Device]);
        Assert.False(device.IsPaused);
        Assert.Null(device.LastRelayHubId);

        // Created, renamed, claimed and released by a Node, then removed: the tombstone keeps name and Site.
        var removed = Assert.IsType<LotState>(states["lot/0192f3a4-8a00-7c3d-8e4f-5a6b7c8d9e01"]);
        Assert.Equal(LotLifecycle.Removed, removed.Lifecycle);
        Assert.Equal("Tomatoes east", removed.Name);
        Assert.Equal("0192f3a4-7c1e-7d2b-9a51-3f7e2c9b1d00", removed.SiteId);
        Assert.Null(removed.ClaimedBy);

        var claimed = Assert.IsType<LotState>(states["lot/0192f3a4-8a00-7c3d-8e4f-5a6b7c8d9e02"]);
        Assert.Equal(LotLifecycle.Active, claimed.Lifecycle);
        Assert.Equal("7C20", claimed.ClaimedBy);

        // Reconciled from Keycloak: renamed, an ownerless episode refused and resolved, a member revoked,
        // then deleted. Deletion keeps the Members.
        var reconciled = Assert.IsType<SiteState>(states["site/0192f3a4-7c1e-7d2b-9a51-3f7e2c9b1d01"]);
        Assert.Equal(SiteLifecycle.Deleted, reconciled.Lifecycle);
        Assert.Equal("Allotment 12", reconciled.Name);
        Assert.False(reconciled.OwnerlessEditRefused);
        Assert.Equal(["5b0c7c1e-2a4d-4f1b-8e3a-9d7f6c5b4a31"], reconciled.Members.Keys);
        Assert.Equal(["8f1e2d3c-4b5a-4968-8776-5a4b3c2d1e0f"], reconciled.FormerMembers);

        var user = Assert.IsType<UserState>(states["user/5b0c7c1e-2a4d-4f1b-8e3a-9d7f6c5b4a31"]);
        Assert.True(user.SiteCreations["k1"].Completed);
        Assert.Equal(SiteRole.Owner, user.Sites["0192f3a4-7c1e-7d2b-9a51-3f7e2c9b1d00"]);

        var former = Assert.IsType<UserState>(states["user/8f1e2d3c-4b5a-4968-8776-5a4b3c2d1e0f"]);
        Assert.Empty(former.Sites);
    }

    [Fact]
    public void EveryRegisteredAliasAndSchemaVersionAppearsInTheFixture()
    {
        var registry = CreateRegistry();
        var covered = ReadFixture().Select(row => (row.Alias, row.SchemaVersion)).ToHashSet();

        var missing = registry.EventTypes
            .Where(type => !covered.Contains((type.Alias, type.SchemaVersion)))
            .Select(type => $"{type.Alias} v{type.SchemaVersion}")
            .ToList();

        Assert.True(missing.Count == 0, $"The fixture journal has no row for: {string.Join(", ", missing)}.");
    }

    [Fact]
    public void TheRegistryCoversTheServerContracts()
    {
        var options = new JournalOptions();

        Assert.Contains(typeof(Coldframe.Contracts.Events.EventTypeAttribute).Assembly, options.EventAssemblies);
    }

    private static EventTypeRegistry CreateRegistry()
    {
        // The Server's own configuration plus the test-only sample events.
        var options = new JournalOptions();
        options.EventAssemblies.Add(typeof(SampleCreated).Assembly);

        return new EventTypeRegistry(options.EventAssemblies);
    }

    private static List<FixtureRow> ReadFixture()
    {
        using var stream = File.OpenRead(FixturePath);
        var rows = JsonSerializer.Deserialize<List<FixtureRow>>(stream, JsonSerializerOptions.Web);

        Assert.NotNull(rows);
        Assert.NotEmpty(rows);
        return rows;
    }

    private sealed record FixtureRow(string StreamId, int Version, string Alias, int SchemaVersion, JsonElement Payload);
}
