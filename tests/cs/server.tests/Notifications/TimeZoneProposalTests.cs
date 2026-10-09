using System.Net;
using Coldframe.Server.Notifications;

namespace Coldframe.Server.Tests.Notifications;

/// <summary>
/// Time-zone detection only proposes (Story 6.3): the device or browser zone, then the detected zone the
/// Server holds, then the IP lookup, then none. A zone is an IANA ID the Server knows.
/// </summary>
public sealed class TimeZoneProposalTests
{
    private static readonly IPAddress Address = IPAddress.Parse("203.0.113.7");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("Europe/Zurich")]
    [InlineData("America/New_York")]
    [InlineData("America/Argentina/Buenos_Aires")]
    [InlineData("Asia/Kolkata")]
    [InlineData("Etc/GMT+5")]
    [InlineData("UTC")]
    public void AnIanaZoneOfTheServersDatabaseIsKnown(string timeZone)
    {
        Assert.True(TimeZoneProposal.IsKnown(timeZone));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Mars/Olympus")]
    [InlineData("W. Europe Standard Time")]
    [InlineData("Central European Standard Time")]
    [InlineData("europe/zurich")]
    [InlineData("Europe/Zurich ")]
    [InlineData("../etc/passwd")]
    [InlineData("posix/Europe/Zurich")]
    [InlineData("right/Europe/Zurich")]
    [InlineData("posixrules")]
    [InlineData("Factory")]
    [InlineData("+02:00")]
    [InlineData("Europe/Zürich")]
    public void AnythingElseIsNoTimeZone(string? timeZone)
    {
        Assert.False(TimeZoneProposal.IsKnown(timeZone));
    }

    [Fact]
    public void AZoneLongerThan64CharactersIsNoTimeZone()
    {
        Assert.False(TimeZoneProposal.IsKnown("Europe/" + new string('Z', 58)));
    }

    [Fact]
    public async Task TheDeviceZoneComesFirstAndTheLookupIsNotAsked()
    {
        var lookup = new FakeLookup("Asia/Tokyo");

        var proposed = await TimeZoneProposal.ResolveAsync("Europe/Zurich", "Europe/Vienna", lookup, Address, Ct);

        Assert.Equal("Europe/Zurich", proposed);
        Assert.Equal(0, lookup.Calls);
    }

    [Fact]
    public async Task WithoutADeviceZoneTheStoredDetectedZoneIsProposed()
    {
        var lookup = new FakeLookup("Asia/Tokyo");

        Assert.Equal("Europe/Vienna", await TimeZoneProposal.ResolveAsync(null, "Europe/Vienna", lookup, Address, Ct));
        Assert.Equal("Europe/Vienna", await TimeZoneProposal.ResolveAsync("Mars/Olympus", "Europe/Vienna", lookup, Address, Ct));
        Assert.Equal(0, lookup.Calls);
    }

    [Fact]
    public async Task WithNeitherTheIpLookupIsAskedWithTheCallersAddress()
    {
        var lookup = new FakeLookup("Asia/Tokyo");

        Assert.Equal("Asia/Tokyo", await TimeZoneProposal.ResolveAsync(null, null, lookup, Address, Ct));
        Assert.Equal(1, lookup.Calls);
        Assert.Equal(Address, lookup.LastAddress);
    }

    [Fact]
    public async Task ALookupThatKnowsNoZoneOrAnUnknownOneProposesNone()
    {
        Assert.Null(await TimeZoneProposal.ResolveAsync(null, null, new FakeLookup(null), Address, Ct));
        Assert.Null(await TimeZoneProposal.ResolveAsync(null, null, new FakeLookup("Mars/Olympus"), null, Ct));
    }

    [Fact]
    public async Task TheServersOwnLookupKnowsNoZone()
    {
        var lookup = new NoIpTimeZoneLookup();

        Assert.Null(await lookup.FindAsync(Address, Ct));
        Assert.Null(await lookup.FindAsync(null, Ct));
        Assert.Null(await TimeZoneProposal.ResolveAsync(null, null, lookup, Address, Ct));
    }

    private sealed class FakeLookup(string? timeZone) : IIpTimeZoneLookup
    {
        public int Calls { get; private set; }

        public IPAddress? LastAddress { get; private set; }

        public ValueTask<string?> FindAsync(IPAddress? address, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastAddress = address;
            return ValueTask.FromResult(timeZone);
        }
    }
}
