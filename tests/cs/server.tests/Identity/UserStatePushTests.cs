using Coldframe.Contracts.Notifications;
using Coldframe.Contracts.Sites;
using Coldframe.Server.Identity;

namespace Coldframe.Server.Tests.Identity;

/// <summary>
/// The devices a User registered for push, as the User grain's events leave them (Story 6.5): one registration
/// per installation, replaced by a newer one, gone when removed.
/// </summary>
public sealed class UserStatePushTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WithoutAnyEventTheUserHasNoDevice()
    {
        var state = new UserState();

        Assert.Empty(state.PushRegistrations);
        Assert.Empty(state.OrderedPushRegistrations());
    }

    [Fact]
    public void ARegisteredDeviceIsHeldByItsInstallation()
    {
        var state = new UserState();

        state.Apply(new PushDeviceRegistered("iphone", PushPlatform.Apns, "token-1", ApnsEnvironment.Sandbox, Noon));

        Assert.Equal(
            new PushRegistration("iphone", PushPlatform.Apns, "token-1", ApnsEnvironment.Sandbox, Noon),
            state.PushRegistrations["iphone"]);
    }

    [Fact]
    public void RegisteringTheSameInstallationAgainReplacesItsToken()
    {
        var state = new UserState();
        state.Apply(new PushDeviceRegistered("android", PushPlatform.Fcm, "token-1", null, Noon));

        state.Apply(new PushDeviceRegistered("android", PushPlatform.Fcm, "token-2", null, Noon.AddDays(1)));

        var registration = Assert.Single(state.PushRegistrations.Values);
        Assert.Equal(("token-2", Noon.AddDays(1)), (registration.Token, registration.RegisteredAt));
    }

    [Fact]
    public void ARemovedDeviceIsGoneAndTheOthersStay()
    {
        var state = new UserState();
        state.Apply(new PushDeviceRegistered("iphone", PushPlatform.Apns, "token-1", ApnsEnvironment.Production, Noon));
        state.Apply(new PushDeviceRegistered("android", PushPlatform.Fcm, "token-2", null, Noon.AddMinutes(5)));

        state.Apply(new PushDeviceRemoved("iphone", PushDeviceRemovalReason.Invalid, Noon.AddDays(1)));
        state.Apply(new PushDeviceRemoved("never-registered", PushDeviceRemovalReason.Requested, Noon.AddDays(1)));

        Assert.Equal(["android"], state.PushRegistrations.Keys);
    }

    [Fact]
    public void TheRegistrationsAreHandedOverOldestFirst()
    {
        var state = new UserState();
        state.Apply(new PushDeviceRegistered("b", PushPlatform.Fcm, "token-b", null, Noon.AddMinutes(5)));
        state.Apply(new PushDeviceRegistered("c", PushPlatform.Fcm, "token-c", null, Noon));
        state.Apply(new PushDeviceRegistered("a", PushPlatform.Fcm, "token-a", null, Noon));

        Assert.Equal(["a", "c", "b"], state.OrderedPushRegistrations().Select(registration => registration.InstallationId));
    }

    [Fact]
    public void ARegistrationOutlivesAMembershipThatEnds()
    {
        // A device belongs to the User, not to a Site.
        var state = new UserState();
        state.Apply(new SiteMembershipChanged("site-1", SiteRole.Member));
        state.Apply(new PushDeviceRegistered("iphone", PushPlatform.Apns, "token-1", ApnsEnvironment.Production, Noon));

        state.Apply(new SiteMembershipChanged("site-1", null));

        Assert.Single(state.PushRegistrations);
    }

    [Theory]
    [InlineData("a", true)]
    [InlineData("0199c1f0-5a00-7000-8000-000000000001", true)]
    [InlineData("A.b_c-9", true)]
    [InlineData("", false)]
    [InlineData("with space", false)]
    [InlineData("slash/id", false)]
    [InlineData("ünïcode", false)]
    public void AnInstallationIdIsShortAndUrlSafe(string installationId, bool valid) =>
        Assert.Equal(valid, PushRegistrationLimits.IsInstallationId(installationId));

    [Fact]
    public void AnInstallationIdAndATokenHaveALongestLength()
    {
        Assert.True(PushRegistrationLimits.IsInstallationId(new string('a', PushRegistrationLimits.MaxInstallationIdLength)));
        Assert.False(PushRegistrationLimits.IsInstallationId(new string('a', PushRegistrationLimits.MaxInstallationIdLength + 1)));
        Assert.True(PushRegistrationLimits.IsToken(new string('a', PushRegistrationLimits.MaxTokenLength)));
        Assert.False(PushRegistrationLimits.IsToken(new string('a', PushRegistrationLimits.MaxTokenLength + 1)));
        Assert.False(PushRegistrationLimits.IsToken(string.Empty));
        Assert.False(PushRegistrationLimits.IsToken(null));
        Assert.False(PushRegistrationLimits.IsToken("with space"));
        Assert.True(PushRegistrationLimits.IsToken("dQw4w9WgXcQ:APA91b-_Example"));
    }
}
