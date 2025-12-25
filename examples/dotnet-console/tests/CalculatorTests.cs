using Xunit;
using DotNetConsole;

namespace DotNetConsole.Tests;

public class CalculatorTests
{
    private readonly Calculator _calculator = new();

    [Theory]
    [InlineData(2, 3, 5)]
    [InlineData(-1, -2, -3)]
    [InlineData(0, 0, 0)]
    [InlineData(100, -50, 50)]
    public void Add_ReturnsCorrectSum(int a, int b, int expected)
    {
        var result = _calculator.Add(a, b);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(10, 4, 6)]
    [InlineData(5, 5, 0)]
    [InlineData(3, 7, -4)]
    public void Subtract_ReturnsCorrectDifference(int a, int b, int expected)
    {
        var result = _calculator.Subtract(a, b);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(6, 7, 42)]
    [InlineData(5, 0, 0)]
    [InlineData(-3, 4, -12)]
    [InlineData(-2, -3, 6)]
    public void Multiply_ReturnsCorrectProduct(int a, int b, int expected)
    {
        var result = _calculator.Multiply(a, b);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(20, 4, 5.0)]
    [InlineData(7, 2, 3.5)]
    [InlineData(10, 3, 3.333)]
    public void Divide_ReturnsCorrectQuotient(int a, int b, double expected)
    {
        var result = _calculator.Divide(a, b);
        Assert.Equal(expected, result, 2);
    }

    [Fact]
    public void Divide_ByZero_ThrowsException()
    {
        Assert.Throws<DivideByZeroException>(() => _calculator.Divide(10, 0));
    }
}
