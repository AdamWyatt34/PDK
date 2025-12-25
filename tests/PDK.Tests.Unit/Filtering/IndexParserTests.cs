using PDK.Core.Filtering;

namespace PDK.Tests.Unit.Filtering;

public class IndexParserTests
{
    [Theory]
    [InlineData("1", new[] { 1 })]
    [InlineData("5", new[] { 5 })]
    [InlineData("10", new[] { 10 })]
    public void Parse_SingleIndex_ReturnsSingleValue(string input, int[] expected)
    {
        var result = IndexParser.Parse(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("1,2,3", new[] { 1, 2, 3 })]
    [InlineData("1,3,5", new[] { 1, 3, 5 })]
    [InlineData("10,20,30", new[] { 10, 20, 30 })]
    public void Parse_CommaSeparatedList_ReturnsAllValues(string input, int[] expected)
    {
        var result = IndexParser.Parse(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("1-3", new[] { 1, 2, 3 })]
    [InlineData("2-5", new[] { 2, 3, 4, 5 })]
    [InlineData("10-12", new[] { 10, 11, 12 })]
    public void Parse_Range_ReturnsExpandedValues(string input, int[] expected)
    {
        var result = IndexParser.Parse(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("1,3-5,7", new[] { 1, 3, 4, 5, 7 })]
    [InlineData("1-2,5,8-10", new[] { 1, 2, 5, 8, 9, 10 })]
    public void Parse_MixedSyntax_ReturnsAllValues(string input, int[] expected)
    {
        var result = IndexParser.Parse(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("1,1,2,2,3", new[] { 1, 2, 3 })]
    [InlineData("1-3,2-4", new[] { 1, 2, 3, 4 })]
    public void Parse_WithDuplicates_ReturnsDeduplicated(string input, int[] expected)
    {
        var result = IndexParser.Parse(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(" 1 , 2 , 3 ", new[] { 1, 2, 3 })]
    [InlineData(" 1 - 3 ", new[] { 1, 2, 3 })]
    public void Parse_WithWhitespace_ParsesCorrectly(string input, int[] expected)
    {
        var result = IndexParser.Parse(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_EmptyOrWhitespace_ReturnsEmpty(string input)
    {
        var result = IndexParser.Parse(input);
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_NullInput_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => IndexParser.Parse(null!));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1-")]
    [InlineData("-1")]
    [InlineData("1-2-3")]
    public void Parse_InvalidFormat_ThrowsFormatException(string input)
    {
        Assert.Throws<FormatException>(() => IndexParser.Parse(input));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0-5")]
    public void Parse_ZeroIndex_ThrowsArgumentOutOfRangeException(string input)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => IndexParser.Parse(input));
    }

    [Fact]
    public void Parse_NegativeRange_ThrowsFormatException()
    {
        // -1 is parsed as a range, which fails
        Assert.Throws<FormatException>(() => IndexParser.Parse("-1"));
    }

    [Theory]
    [InlineData("5-3")]
    [InlineData("10-5")]
    public void Parse_ReverseRange_ThrowsFormatException(string input)
    {
        Assert.Throws<FormatException>(() => IndexParser.Parse(input));
    }

    [Fact]
    public void Parse_SingleValueRange_ReturnsSingleValue()
    {
        var result = IndexParser.Parse("5-5");
        Assert.Equal([5], result);
    }

    [Fact]
    public void Parse_LargeNumbers_ParsesCorrectly()
    {
        var result = IndexParser.Parse("100,200,300");
        Assert.Equal([100, 200, 300], result);
    }

    [Fact]
    public void Parse_ComplexMixed_ParsesCorrectly()
    {
        var result = IndexParser.Parse("1,3-5,7,10-12,15");
        Assert.Equal([1, 3, 4, 5, 7, 10, 11, 12, 15], result);
    }

    [Fact]
    public void Parse_ResultIsOrdered()
    {
        var result = IndexParser.Parse("5,1,3,2,4");
        Assert.True(result.SequenceEqual([1, 2, 3, 4, 5]));
    }
}
