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
}
