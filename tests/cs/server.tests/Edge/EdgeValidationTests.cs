using Coldframe.Contracts.Sites;
using Coldframe.Server.Edge;

namespace Coldframe.Server.Tests.Edge;

/// <summary>
/// The <c>Idempotency-Key</c> is required and 1–200 printable ASCII characters; a Site name is trimmed and
/// 1–100 characters.
/// </summary>
public sealed class EdgeValidationTests
{
    [Fact]
    public void ANoOrEmptyKeyIsMissing()
    {
        Assert.Equal(EdgeValidation.KeyCheck.Missing, EdgeValidation.CheckIdempotencyKey([]));
        Assert.Equal(EdgeValidation.KeyCheck.Missing, EdgeValidation.CheckIdempotencyKey([string.Empty]));
        Assert.Equal(EdgeValidation.KeyCheck.Missing, EdgeValidation.CheckIdempotencyKey([null]));
    }

    [Theory]
    [InlineData("k1")]
    [InlineData("0192f3a4-7c1e-7d2b-9a51-3f7e2c9b1d00")]
    [InlineData(" !\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~")]
    public void PrintableAsciiKeysAreValid(string key)
    {
        Assert.Equal(EdgeValidation.KeyCheck.Valid, EdgeValidation.CheckIdempotencyKey([key]));
    }

    [Fact]
    public void AKeyOfExactly200CharactersIsValidAndOneMoreIsNot()
    {
        Assert.Equal(EdgeValidation.KeyCheck.Valid, EdgeValidation.CheckIdempotencyKey([new string('k', 200)]));
        Assert.Equal(EdgeValidation.KeyCheck.Invalid, EdgeValidation.CheckIdempotencyKey([new string('k', 201)]));
    }

    [Theory]
    [InlineData("tab\there")]
    [InlineData("new\nline")]
    [InlineData("délai")]
    [InlineData("\u007f")]
    public void KeysOutsidePrintableAsciiAreInvalid(string key)
    {
        Assert.Equal(EdgeValidation.KeyCheck.Invalid, EdgeValidation.CheckIdempotencyKey([key]));
    }

    [Fact]
    public void TwoKeysAreInvalid()
    {
        Assert.Equal(EdgeValidation.KeyCheck.Invalid, EdgeValidation.CheckIdempotencyKey(["a", "b"]));
    }

    [Theory]
    [InlineData("Home", "Home")]
    [InlineData("  Home \t", "Home")]
    [InlineData("Garden shed", "Garden shed")]
    public void NamesAreTrimmed(string name, string expected)
    {
        Assert.Equal(expected, EdgeValidation.NormalizeSiteName(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void AMissingOrBlankNameIsInvalid(string? name)
    {
        Assert.Null(EdgeValidation.NormalizeSiteName(name));
    }

    [Theory]
    [InlineData("Ho\u0000me")]
    [InlineData("Home\u0000")]
    [InlineData("Ho\u0007me")]
    [InlineData("Ho\u001bme")]
    [InlineData("Ho\u007fme")]
    [InlineData("Ho\u0085me")]
    [InlineData("Ho\nme")]
    public void ANameWithAControlCharacterIsInvalid(string name)
    {
        Assert.Null(EdgeValidation.NormalizeSiteName(name));
    }

    [Fact]
    public void ANameWithAnUnpairedSurrogateIsInvalid()
    {
        // Not theory data: a lone surrogate does not survive the serialization of test arguments.
        Assert.Null(EdgeValidation.NormalizeSiteName("Ho\ud800me"));
        Assert.Null(EdgeValidation.NormalizeSiteName("Ho\udc00me"));
        Assert.Null(EdgeValidation.NormalizeSiteName("Home \ud83c"));
    }

    [Fact]
    public void ANameWithAPairedSurrogateSuchAsAnEmojiIsValid()
    {
        Assert.Equal("Home \ud83c\udf45", EdgeValidation.NormalizeSiteName("Home \ud83c\udf45"));
    }

    [Fact]
    public void ANameCountsCodePointsSoOneHundredEmojiAreValidAndOneMoreIsNot()
    {
        var hundred = string.Concat(Enumerable.Repeat("\ud83c\udf45", 100));

        Assert.Equal(hundred, EdgeValidation.NormalizeSiteName(hundred));
        Assert.Null(EdgeValidation.NormalizeSiteName(hundred + "\ud83c\udf45"));
    }

    [Fact]
    public void ANameOf100CharactersAfterTrimmingIsValidAndOneMoreIsNot()
    {
        Assert.Equal(new string('n', 100), EdgeValidation.NormalizeSiteName($"  {new string('n', 100)}  "));
        Assert.Null(EdgeValidation.NormalizeSiteName(new string('n', 101)));
    }

    [Theory]
    [InlineData("00:00", 0)]
    [InlineData("07:00", 420)]
    [InlineData("06:30", 390)]
    [InlineData("22:00", 1320)]
    [InlineData("23:59", 1439)]
    public void ATimeOfDayIsHHmmAndRoundTrips(string time, int minutes)
    {
        Assert.Equal(minutes, EdgeValidation.NormalizeTimeOfDay(time));
        Assert.Equal(time, EdgeValidation.TimeOfDayName(minutes));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("7:00")]
    [InlineData("24:00")]
    [InlineData("23:60")]
    [InlineData("07.00")]
    [InlineData("07:00:00")]
    [InlineData(" 7:00")]
    [InlineData("0७:00")]
    [InlineData("-1:00")]
    [InlineData("7 am")]
    public void AnythingElseIsNoTimeOfDay(string? time)
    {
        Assert.Null(EdgeValidation.NormalizeTimeOfDay(time));
    }

    [Fact]
    public void AWindowNamingOnlyItsStartClosesAt2200()
    {
        Assert.Equal(new NotificationWindow(390, 1320), EdgeValidation.NormalizeNotificationWindow("06:30", null));
        Assert.Equal(new NotificationWindow(420, 1260), EdgeValidation.NormalizeNotificationWindow("07:00", "21:00"));
        Assert.Equal(new NotificationWindow(0, 1439), EdgeValidation.NormalizeNotificationWindow("00:00", "23:59"));
    }

    [Theory]
    [InlineData("07:00", "07:00")]
    [InlineData("22:00", "07:00")]
    [InlineData("22:00", null)]
    [InlineData("23:00", null)]
    [InlineData("7:00", "22:00")]
    [InlineData("07:00", "24:00")]
    [InlineData(null, "22:00")]
    [InlineData("07:00", "")]
    public void AWindowNeedsFromBeforeToWithinOneDay(string? from, string? to)
    {
        Assert.Null(EdgeValidation.NormalizeNotificationWindow(from, to));
    }

    [Fact]
    public void AReminderCadenceIsDailyOrEvery2DaysAndRoundTrips()
    {
        Assert.Equal(ReminderCadence.Daily, EdgeValidation.NormalizeReminderCadence("daily"));
        Assert.Equal(ReminderCadence.Every2Days, EdgeValidation.NormalizeReminderCadence("every2Days"));
        Assert.Equal("daily", EdgeValidation.ReminderCadenceName(ReminderCadence.Daily));
        Assert.Equal("every2Days", EdgeValidation.ReminderCadenceName(ReminderCadence.Every2Days));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Daily")]
    [InlineData("every2days")]
    [InlineData("never")]
    [InlineData("weekly")]
    public void ThereIsNoOtherReminderCadence(string? cadence)
    {
        Assert.Null(EdgeValidation.NormalizeReminderCadence(cadence));
    }
}
