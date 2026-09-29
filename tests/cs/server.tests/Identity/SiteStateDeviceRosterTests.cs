using Coldframe.Contracts.Devices;
using Coldframe.Contracts.Sites;
using Coldframe.Server.Identity;

namespace Coldframe.Server.Tests.Identity;

/// <summary>
/// The Site's Device roster and its registration keys: caller-scoped, 24 h after the registration.
/// </summary>
public sealed class SiteStateDeviceRosterTests
{
    private static readonly DateTimeOffset RegisteredAt = new(2026, 9, 29, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ARegistrationAddsTheDeviceAndHoldsItsKeyForJustUnder24Hours()
    {
        var state = new SiteState();
        state.Apply(new DeviceRegistered("92064422c012f481", DeviceKind.Hub, "u:k1", RegisteredAt));

        Assert.Equal(DeviceKind.Hub, state.Devices["92064422c012f481"]);
        Assert.Equal("92064422c012f481", state.FindLiveDeviceRegistration("u:k1", RegisteredAt + TimeSpan.FromHours(24) - TimeSpan.FromTicks(1))?.DeviceId);
        Assert.Null(state.FindLiveDeviceRegistration("u:k1", RegisteredAt + TimeSpan.FromHours(24)));

        // The Device stays on the roster when its key expires.
        Assert.True(state.Devices.ContainsKey("92064422c012f481"));
    }

    [Fact]
    public void KeysAreScopedToTheirCaller()
    {
        var state = new SiteState();
        state.Apply(new DeviceRegistered("92064422c012f481", DeviceKind.Node, "alice:k1", RegisteredAt));

        Assert.Null(state.FindLiveDeviceRegistration("bob:k1", RegisteredAt));
    }
}
