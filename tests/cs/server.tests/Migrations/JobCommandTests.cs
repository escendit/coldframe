using Coldframe.Migrations;

namespace Coldframe.Server.Tests.Migrations;

/// <summary>
/// The command line of the migration job: no argument migrates and ensures partitions, <c>partitions</c> and
/// <c>advance-replay</c> take their options, and anything else is refused (AD-15, AD-22).
/// </summary>
public sealed class JobCommandTests
{
    [Fact]
    public void WithoutArgumentsTheJobMigratesAndKeepsThreeMonthsAhead()
    {
        var command = JobCommand.Parse([], out var error);

        Assert.Null(error);
        Assert.Equal(new JobCommand(JobCommandKind.Migrate, MonthsAhead: 3, UplinkMargin: 64, DownlinkMargin: 1_048_576), command);
    }

    [Fact]
    public void PartitionsTakesTheMonthsAheadFromTwoUpwards()
    {
        Assert.Equal(new JobCommand(JobCommandKind.Partitions, 3), JobCommand.Parse(["partitions"], out _));
        Assert.Equal(new JobCommand(JobCommandKind.Partitions, 2), JobCommand.Parse(["partitions", "--months-ahead", "2"], out _));
        Assert.Equal(new JobCommand(JobCommandKind.Partitions, 12), JobCommand.Parse(["partitions", "--months-ahead", "12"], out _));
    }

    [Fact]
    public void AdvanceReplayTakesItsMargins()
    {
        Assert.Equal(new JobCommand(JobCommandKind.AdvanceReplay), JobCommand.Parse(["advance-replay"], out _));
        Assert.Equal(
            new JobCommand(JobCommandKind.AdvanceReplay, UplinkMargin: 128, DownlinkMargin: 5),
            JobCommand.Parse(["advance-replay", "--downlink-margin", "5", "--uplink-margin", "128"], out _));
        Assert.Equal(
            new JobCommand(JobCommandKind.AdvanceReplay, UplinkMargin: 4_294_967_296, DownlinkMargin: 1),
            JobCommand.Parse(["advance-replay", "--downlink-margin", "1", "--uplink-margin", "4294967296"], out _));
        Assert.Equal(4_294_967_296UL, ReadingsMaintenance.MaxMargin);
        Assert.Contains("4294967296", JobCommand.Usage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("migrate")]
    [InlineData("Partitions")]
    [InlineData("--months-ahead", "3")]
    [InlineData("partitions", "--months-ahead")]
    [InlineData("partitions", "--months-ahead", "1")]
    [InlineData("partitions", "--months-ahead", "121")]
    [InlineData("partitions", "--months-ahead", "three")]
    [InlineData("partitions", "--months-ahead", "+3")]
    [InlineData("partitions", "--months-ahead", "3", "--months-ahead", "4")]
    [InlineData("partitions", "--uplink-margin", "64")]
    [InlineData("partitions", "3")]
    [InlineData("advance-replay", "--uplink-margin", "0")]
    [InlineData("advance-replay", "--downlink-margin", "-1")]
    [InlineData("advance-replay", "--downlink-margin", "18446744073709551616")]
    [InlineData("advance-replay", "--downlink-margin", "18446744073709551615")]
    [InlineData("advance-replay", "--downlink-margin", "4294967297")]
    [InlineData("advance-replay", "--uplink-margin", "4294967297")]
    [InlineData("advance-replay", "--months-ahead", "3")]
    [InlineData("advance-replay", "now")]
    public void AnythingElseIsRefusedWithAReason(params string[] arguments)
    {
        Assert.Null(JobCommand.Parse(arguments, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void PartitionsAreNamedByTableYearAndMonth()
    {
        Assert.Equal("readings_y2026m10", ReadingsMaintenance.PartitionName("readings", 2026, 10));
        Assert.Equal("device_reports_y2027m01", ReadingsMaintenance.PartitionName("device_reports", 2027, 1));
        Assert.Equal(["readings", "device_reports"], ReadingsMaintenance.PartitionedTables);
    }
}
