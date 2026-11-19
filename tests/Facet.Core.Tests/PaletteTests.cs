using Facet.Core.Palette;
using Facet.Core.Widgets;

namespace Facet.Core.Tests;

public class FuzzyMatcherTests
{
    [Fact]
    public void EmptyQueryMatchesWithZero() => Assert.Equal(0, FuzzyMatcher.Score("", "Chrome"));

    [Fact]
    public void NonSubsequenceReturnsNull() => Assert.Null(FuzzyMatcher.Score("xyz", "Chrome"));

    [Fact]
    public void IsCaseInsensitive() => Assert.NotNull(FuzzyMatcher.Score("CHR", "google chrome"));

    [Fact]
    public void PrefixBeatsMiddle() =>
        Assert.True(FuzzyMatcher.Score("code", "Code Editor") > FuzzyMatcher.Score("code", "Visual Studio Code"));

    [Fact]
    public void WordStartsBeatScattered() =>
        Assert.True(FuzzyMatcher.Score("vsc", "Visual Studio Code") > FuzzyMatcher.Score("vsc", "Avsxc"));

    [Fact]
    public void ContiguousBeatsGappy() =>
        Assert.True(FuzzyMatcher.Score("note", "Notepad") > FuzzyMatcher.Score("note", "No Tea Exists"));

    [Fact]
    public void FindsBestAlignmentNotJustFirst() =>
        // greedy-first would match the 's' in "Discord"; the best alignment uses "Studio"
        Assert.True(FuzzyMatcher.Score("studio", "Discord Studio") > FuzzyMatcher.Score("studio", "S t u d i o x"));
}

public class MathEvaluatorTests
{
    [Theory]
    [InlineData("1+2", 3)]
    [InlineData("2*3+4", 10)]
    [InlineData("2*(3+4)", 14)]
    [InlineData("-3+5", 2)]
    [InlineData("2^10", 1024)]
    [InlineData("2^3^2", 512)]
    [InlineData("-2^2", -4)]
    [InlineData("10/4", 2.5)]
    [InlineData("10 % 3", 1)]
    [InlineData("sqrt(16)", 4)]
    [InlineData("pi*2", 6.283185307179586)]
    [InlineData("abs(-7.5)", 7.5)]
    [InlineData(" 1.5e3 / 3 ", 500)]
    public void Evaluates(string expr, double expected) =>
        Assert.Equal(expected, MathEvaluator.Evaluate(expr), 9);

    [Theory]
    [InlineData("2+")]
    [InlineData("hello")]
    [InlineData("(1+2")]
    [InlineData("1/0")]
    public void TryEvaluateRejectsInvalid(string expr) => Assert.False(MathEvaluator.TryEvaluate(expr, out _));

    [Theory]
    [InlineData("12*3", true)]
    [InlineData("sqrt(2)", true)]
    [InlineData("(4+4)/2", true)]
    [InlineData("chrome", false)]
    [InlineData("42", false)]
    [InlineData("", false)]
    public void LooksLikeMath(string text, bool expected) => Assert.Equal(expected, MathEvaluator.LooksLikeMath(text));
}

public class JsonPathLiteTests
{
    private const string Json = """{"a":{"b":[{"c":42},{"c":"x"}]},"price":12.5,"ok":true,"name":null}""";

    [Theory]
    [InlineData("a.b[0].c", "42")]
    [InlineData("$.a.b[1].c", "x")]
    [InlineData("price", "12.5")]
    [InlineData("ok", "true")]
    [InlineData("a.b.0.c", "42")]
    public void Extracts(string path, string expected) => Assert.Equal(expected, JsonPathLite.Extract(Json, path));

    [Fact]
    public void MissingPathReturnsNull() => Assert.Null(JsonPathLite.Extract(Json, "a.zzz"));

    [Fact]
    public void NullValueReturnsNull() => Assert.Null(JsonPathLite.Extract(Json, "name"));

    [Fact]
    public void RootArrayIndex() => Assert.Equal("2", JsonPathLite.Extract("[1,2,3]", "[1]"));

    [Fact]
    public void InvalidJsonReturnsNull() => Assert.Null(JsonPathLite.Extract("not json", "a"));
}
