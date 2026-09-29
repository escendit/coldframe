using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Sites;
using Coldframe.Crypto;
using Coldframe.DeviceSimulator;
using Coldframe.Server.Identity;

namespace Coldframe.Server.IntegrationTests.Identity;

/// <summary>
/// The Site's Device roster and the Device grain on a TestCluster (Story 3.3; AD-8, AD-18): the Site owns
/// the roster and the Idempotency-Key rule, the Device owns its Site, and a refusal persists nothing.
/// </summary>
public sealed class SiteDeviceRegistrationTests(IdentityCluster identity) : IClassFixture<IdentityCluster>
{
    [Fact]
    public async Task ARegisteredDeviceIsOnTheReplayedRosterAndTheSiteIsNotPaused()
    {
        var (userId, siteId) = await SeedSiteAsync();
        var deviceId = NewDeviceId();

        var result = await identity.Site(siteId).RegisterDevice(deviceId, DeviceKind.Hub, $"{userId}:k1", Ct);

        Assert.Equal(DeviceRegistrationOutcome.Registered, result.Outcome);
        Assert.Equal(SitePause.NotPaused, result.Pause);
        Assert.False(result.Pause!.Paused);
        Assert.Null(result.Pause.EndsAt);
        Assert.Equal(["site.created", "site.membership-granted", "site.device-registered"], await identity.AliasesAsync($"site/{siteId}"));

        var roster = await ReplaySiteAsync(siteId);
        Assert.Equal(DeviceKind.Hub, roster.Devices[deviceId]);
    }

    [Fact]
    public async Task TheSameKeyAndDeviceAnswersTheSameAndAnotherDeviceIsRefused()
    {
        var (userId, siteId) = await SeedSiteAsync();
        var site = identity.Site(siteId);
        var deviceId = NewDeviceId();
        var key = $"{userId}:k1";

        Assert.Equal(DeviceRegistrationOutcome.Registered, (await site.RegisterDevice(deviceId, DeviceKind.Node, key, Ct)).Outcome);
        Assert.Equal(DeviceRegistrationOutcome.Registered, (await site.RegisterDevice(deviceId, DeviceKind.Node, key, Ct)).Outcome);
        Assert.Equal(DeviceRegistrationOutcome.IdempotencyKeyReused, (await site.RegisterDevice(NewDeviceId(), DeviceKind.Node, key, Ct)).Outcome);

        // The same Device under a new key is already on the roster; another caller's key is its own.
        Assert.Equal(DeviceRegistrationOutcome.Registered, (await site.RegisterDevice(deviceId, DeviceKind.Node, $"{userId}:k2", Ct)).Outcome);
        var other = NewDeviceId();
        Assert.Equal(DeviceRegistrationOutcome.Registered, (await site.RegisterDevice(other, DeviceKind.Node, $"{Guid.NewGuid()}:k1", Ct)).Outcome);

        Assert.Equal(
            ["site.created", "site.membership-granted", "site.device-registered", "site.device-registered"],
            await identity.AliasesAsync($"site/{siteId}"));
        Assert.Equal(
            new[] { deviceId, other }.Order(StringComparer.Ordinal),
            (await ReplaySiteAsync(siteId)).Devices.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task AKeyExpires24HoursAfterItsRegistration()
    {
        var (userId, siteId) = await SeedSiteAsync();
        var site = identity.Site(siteId);
        var key = $"{userId}:k1";

        Assert.Equal(DeviceRegistrationOutcome.Registered, (await site.RegisterDevice(NewDeviceId(), DeviceKind.Hub, key, Ct)).Outcome);

        identity.Time.Advance(TimeSpan.FromHours(24));

        Assert.Equal(DeviceRegistrationOutcome.Registered, (await site.RegisterDevice(NewDeviceId(), DeviceKind.Hub, key, Ct)).Outcome);
    }

    [Fact]
    public async Task AnUncreatedOrDeletedSiteRegistersNothing()
    {
        var uncreated = Guid.CreateVersion7().ToString();
        Assert.Equal(DeviceRegistrationOutcome.NotFound, (await identity.Site(uncreated).RegisterDevice(NewDeviceId(), DeviceKind.Hub, "u:k1", Ct)).Outcome);

        var (userId, deleted) = await SeedSiteAsync(new SiteDeleted());
        Assert.Equal(DeviceRegistrationOutcome.NotFound, (await identity.Site(deleted).RegisterDevice(NewDeviceId(), DeviceKind.Hub, $"{userId}:k1", Ct)).Outcome);

        Assert.Empty(await identity.AliasesAsync($"site/{uncreated}"));
        Assert.Equal(["site.created", "site.membership-granted", "site.deleted"], await identity.AliasesAsync($"site/{deleted}"));
    }

    [Fact]
    public async Task ADeviceEnrolsOnceAndStaysOnItsSite()
    {
        var (userId, siteA) = await SeedSiteAsync();
        var (otherUserId, siteB) = await SeedSiteAsync();
        var device = SimulatedDevice.Create(Coldframe.Protocol.Setup.V1.DeviceKind.Hub);
        var deviceId = device.DeviceId.ToString();
        var wrapped = identity.Vault.Wrap(device.DeviceId, device.Keys.DeviceKey);
        var now = identity.Time.GetUtcNow();

        var enrolled = await identity.Device(deviceId).Enrol(new EnrolDevice(siteA, DeviceKind.Hub, wrapped, userId, "k1"), Ct);
        var again = await identity.Device(deviceId).Enrol(new EnrolDevice(siteA, DeviceKind.Hub, wrapped, userId, "k2"), Ct);
        var elsewhere = await identity.Device(deviceId).Enrol(new EnrolDevice(siteB, DeviceKind.Hub, wrapped, otherUserId, "k1"), Ct);

        var expected = new DeviceEnrolmentResult(DeviceEnrolmentOutcome.Enrolled, new DeviceSummary(deviceId, DeviceKind.Hub, siteA));
        Assert.Equal(expected, enrolled);
        Assert.Equal(expected, again);
        Assert.Equal(new DeviceEnrolmentResult(DeviceEnrolmentOutcome.OnAnotherSite), elsewhere);

        Assert.Equal(["device.enrolled"], await identity.AliasesAsync($"device/{deviceId}"));
        Assert.Equal(["site.created", "site.membership-granted", "site.device-registered"], await identity.AliasesAsync($"site/{siteA}"));
        Assert.Equal(["site.created", "site.membership-granted"], await identity.AliasesAsync($"site/{siteB}"));

        // The journal holds the wrapped key only; the Server's vault unwraps it to K_dev.
        var journaled = Assert.IsType<DeviceEnrolled>(Assert.Single(await identity.Store.ReadStreamAsync($"device/{deviceId}", Ct)).Data);
        Assert.Equal((siteA, DeviceKind.Hub, now), (journaled.SiteId, journaled.Kind, journaled.EnrolledAt));
        Assert.Equal(device.Keys.DeviceKey.ToArray(), identity.Vault.Unwrap(device.DeviceId, journaled.WrappedKey));
    }

    [Fact]
    public async Task ARefusedRegistrationEnrolsNothing()
    {
        var (userId, siteId) = await SeedSiteAsync();
        var first = SimulatedDevice.Create();
        var second = SimulatedDevice.Create();

        Assert.Equal(
            DeviceEnrolmentOutcome.Enrolled,
            (await identity.Device(first.DeviceId.ToString()).Enrol(Request(siteId, first, userId, "k1"), Ct)).Outcome);
        Assert.Equal(
            DeviceEnrolmentOutcome.IdempotencyKeyReused,
            (await identity.Device(second.DeviceId.ToString()).Enrol(Request(siteId, second, userId, "k1"), Ct)).Outcome);
        Assert.Equal(
            DeviceEnrolmentOutcome.SiteNotFound,
            (await identity.Device(second.DeviceId.ToString()).Enrol(Request(Guid.CreateVersion7().ToString(), second, userId, "k1"), Ct)).Outcome);

        Assert.Empty(await identity.AliasesAsync($"device/{second.DeviceId}"));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewDeviceId() => new DeviceId(BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8))).ToString();

    private EnrolDevice Request(string siteId, SimulatedDevice device, string callerId, string key) =>
        new(siteId, DeviceKind.Hub, identity.Vault.Wrap(device.DeviceId, device.Keys.DeviceKey), callerId, key);

    private async Task<(string UserId, string SiteId)> SeedSiteAsync(params object[] more)
    {
        var userId = Guid.NewGuid().ToString();
        var siteId = Guid.CreateVersion7().ToString();

        Assert.True(await identity.Store.AppendAsync(
            $"site/{siteId}",
            0,
            [new SiteCreated("Home", userId), new MembershipGranted(userId, SiteRole.Owner), .. more],
            Ct));

        return (userId, siteId);
    }

    private async Task<SiteState> ReplaySiteAsync(string siteId)
    {
        var state = new SiteState();

        foreach (var @event in await identity.Store.ReadStreamAsync($"site/{siteId}", Ct))
        {
            ((dynamic)state).Apply((dynamic)@event.Data);
        }

        return state;
    }
}
